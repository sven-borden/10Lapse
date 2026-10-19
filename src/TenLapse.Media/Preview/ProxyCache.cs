using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace TenLapse.Media.Preview;

/// <summary>How the proxy cache is bounded.</summary>
public sealed record ProxyCacheOptions
{
    /// <summary>A proxy's longest side, in pixels.</summary>
    public int MaxSide { get; init; } = ProxyCache.DefaultMaxSide;

    /// <summary>
    /// The most the proxies may take on disk together. Past it the least recently used proxies go, those of photos the
    /// current project does not use first. <see cref="ProxyCache.DefaultMaxBytes"/> picks it from the drive.
    /// </summary>
    public long MaxBytes { get; init; } = 2L << 30;

    /// <summary>A proxy no project has used for this long is deleted when the cache opens.</summary>
    public TimeSpan Unused { get; init; } = TimeSpan.FromDays(30);

    /// <summary>JPEG quality, 0 to 1.</summary>
    public double JpegQuality { get; init; } = 0.85;
}

/// <summary>A proxy build: the decoded photo at proxy size (the caller owns it), or why the photo cannot be read; and whether the proxy reached the disk.</summary>
/// <param name="Photo">The photo decoded at proxy size; null when it cannot be read. Dispose it.</param>
/// <param name="Failure">Why the photo cannot be read; null when it could.</param>
/// <param name="WriteFailure">Why the proxy could not be saved (the photo is still good); null when it was.</param>
public sealed record ProxyBuild(DecodedPhoto? Photo, PhotoFailure? Failure, Exception? WriteFailure);

/// <summary>
/// The preview's proxies on disk (#19): one JPEG per photo, its longest side at most 1280 px, EXIF orientation
/// applied and colour converted to sRGB (what <see cref="PhotoDecoder"/> gives), no metadata. Playback and scrubbing
/// decode these instead of the photos, which are often 20 MB and more.
/// </summary>
/// <remarks>
/// <para>
/// <b>Key and invalidation.</b> A proxy is named after a hash of the photo's normalized path, its size and its last
/// write time (and the proxy format), so a photo that changes on disk gets a new proxy and the old one is simply no
/// longer used. Nothing is ever looked up by path alone.
/// </para>
/// <para>
/// <b>Bound and clean-up.</b> When the proxies exceed <see cref="ProxyCacheOptions.MaxBytes"/>, the least recently used
/// go until they are under 80 % of it: first those of photos the current project does not use
/// (<see cref="Protect"/>), and only then, down to the bound itself, the project's own. A proxy is marked used once
/// per session (its last write time is set), and one unused for <see cref="ProxyCacheOptions.Unused"/> is deleted when
/// the cache opens (<see cref="OpenAsync"/>), with the leftovers of builds that never finished. Projects are not
/// saved, so "a project uses it" means the open project.
/// </para>
/// <para>
/// Thread-safe. Writes go to a temporary file renamed into place, so a proxy is either whole or absent. Two app
/// instances may share the folder; a proxy that vanishes under one is rebuilt.
/// </para>
/// </remarks>
public sealed partial class ProxyCache
{
    /// <summary>The proxy's longest side by default (DESIGN: about 1280 px).</summary>
    public const int DefaultMaxSide = 1280;

    /// <summary>Changes when the proxies' content changes, so old ones are never read as new.</summary>
    public const int FormatVersion = 1;

    private const string Extension = ".jpg";
    private const string TempExtension = ".tmp";
    private const double TrimTarget = 0.8;

    private readonly ConcurrentDictionary<string, Entry> _index = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _touched = new(StringComparer.Ordinal);
    private readonly Lock _trimGate = new();
    private HashSet<string> _protected = new(StringComparer.Ordinal);
    private long _bytes;
    private volatile bool _scanned;
    private int _trimming;

    /// <summary>A cache in <paramref name="folder"/> (created when needed).</summary>
    public ProxyCache(string folder, ProxyCacheOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = Path.GetFullPath(folder);
        Options = options ?? new ProxyCacheOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(Options.MaxSide, 16);
        ArgumentOutOfRangeException.ThrowIfNegative(Options.MaxBytes);
    }

    /// <summary>The folder.</summary>
    public string Folder { get; }

    /// <summary>The bounds.</summary>
    public ProxyCacheOptions Options { get; }

    /// <summary>The proxies known on disk, and their bytes (once <see cref="OpenAsync"/> has listed the folder).</summary>
    public (int Count, long Bytes) Size => (_index.Count, Interlocked.Read(ref _bytes));

    /// <summary>Called when a trim or the opening sweep fails (logged by the app; the cache goes on).</summary>
    public Action<Exception>? CleanupFailed { get; set; }

    /// <summary>
    /// The bound for a cache on <paramref name="folder"/>'s drive: 2 GB, or a tenth of the free space when that is less,
    /// but at least 256 MB.
    /// </summary>
    public static long DefaultMaxBytes(string folder)
    {
        const long Most = 2L << 30;
        const long Least = 256L << 20;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root))
            {
                return Most;
            }

            var free = new DriveInfo(root).AvailableFreeSpace;
            return Math.Clamp(free / 10, Least, Most);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Most;
        }
    }

    /// <summary>The key of a photo at <paramref name="path"/> with this size and last write time.</summary>
    public string KeyOf(string path, long size, DateTime lastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var identity = string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatVersion}|{Options.MaxSide}|{Frame.NormalizePath(path).ToUpperInvariant()}|{size}|{lastWriteTimeUtc.ToUniversalTime().Ticks}");
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    /// <summary>The key of <paramref name="frame"/> as the import recorded it (its size and time then).</summary>
    public string KeyOf(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return KeyOf(frame.SourcePath, frame.FileSize, frame.LastWriteTimeUtc);
    }

    /// <summary>The proxy file of <paramref name="key"/>.</summary>
    public string PathOf(string key) => Path.Combine(Folder, key + Extension);

    /// <summary>
    /// True when the proxy of <paramref name="key"/> is on disk. Answers from the list once <see cref="OpenAsync"/> has
    /// made it; before that it asks the disk when <paramref name="mayTouchDisk"/>, and says false otherwise.
    /// </summary>
    public bool Contains(string key, bool mayTouchDisk = true)
    {
        if (_index.ContainsKey(key))
        {
            return true;
        }

        return !_scanned && mayTouchDisk && File.Exists(PathOf(key));
    }

    /// <summary>
    /// Lists the folder, deletes what is left of builds that never finished and the proxies unused for
    /// <see cref="ProxyCacheOptions.Unused"/>, then trims to the bound. Never throws; a failure goes to
    /// <see cref="CleanupFailed"/>.
    /// </summary>
    public Task OpenAsync() => Task.Run(() =>
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var stale = DateTime.UtcNow - Options.Unused;
            foreach (var info in new DirectoryInfo(Folder).EnumerateFiles())
            {
                // Only the cache's own names are ever touched: a folder given by mistake (a Debug --cache-folder that
                // is a photo folder) keeps every file that is not a proxy or one of its unfinished writes.
                if (TempName().IsMatch(info.Name))
                {
                    // Another instance may be writing it right now; one older than an hour is a leftover.
                    if (info.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1))
                    {
                        TryDelete(info.FullName);
                    }

                    continue;
                }

                if (!ProxyName().IsMatch(info.Name))
                {
                    continue;
                }

                var key = Path.GetFileNameWithoutExtension(info.Name);
                if (info.LastWriteTimeUtc < stale)
                {
                    TryDelete(info.FullName);
                    continue;
                }

                Add(key, info.Length, info.LastWriteTimeUtc);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            CleanupFailed?.Invoke(exception);
        }
        finally
        {
            _scanned = true;
        }

        TrimNow();
    });

    /// <summary>
    /// The keys of the photos the current project uses: their proxies go last when the cache trims. Replaces the
    /// previous set (a new project's photos protect only themselves).
    /// </summary>
    public void Protect(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        Volatile.Write(ref _protected, new HashSet<string>(keys, StringComparer.Ordinal));
    }

    /// <summary>Marks the proxy of <paramref name="key"/> used: its last write time is set, once per session.</summary>
    public void Touch(string key)
    {
        if (_index.TryGetValue(key, out var entry))
        {
            entry.LastUsed = DateTime.UtcNow.Ticks;
        }

        if (_touched.TryAdd(key, true))
        {
            try
            {
                File.SetLastWriteTimeUtc(PathOf(key), DateTime.UtcNow);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Only the eviction order suffers.
            }
        }
    }

    /// <summary>The proxy of <paramref name="key"/> turned out missing or unreadable: forget it, so it is rebuilt.</summary>
    public void Forget(string key)
    {
        if (_index.TryRemove(key, out var entry))
        {
            Interlocked.Add(ref _bytes, -entry.Bytes);
        }

        TryDelete(PathOf(key));
    }

    /// <summary>
    /// Decodes the photo at <paramref name="sourcePath"/> at proxy size and saves it as the proxy of
    /// <paramref name="key"/>. Never throws but for cancellation: a photo that cannot be read is
    /// <see cref="ProxyBuild.Failure"/>, a proxy that cannot be saved <see cref="ProxyBuild.WriteFailure"/>.
    /// </summary>
    public async Task<ProxyBuild> BuildAsync(string sourcePath, string key, CancellationToken cancellationToken)
    {
        var result = await PhotoDecoder.DecodeAsync(sourcePath, new PhotoDecodeOptions { MaxSide = Options.MaxSide }, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return new ProxyBuild(null, result.Failure, null);
        }

        var photo = result.Value;
        try
        {
            await WriteAsync(photo, key, cancellationToken).ConfigureAwait(false);
            return new ProxyBuild(photo, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            photo.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            return new ProxyBuild(photo, null, exception);
        }
    }

    /// <summary>Trims to the bound now, on this thread (the cache does it by itself after builds; the tests call it).</summary>
    public void TrimNow()
    {
        if (!_scanned || Interlocked.Read(ref _bytes) <= Options.MaxBytes)
        {
            return;
        }

        lock (_trimGate)
        {
            try
            {
                var protectedKeys = Volatile.Read(ref _protected);
                var byAge = _index.Select(pair => (Key: pair.Key, pair.Value.LastUsed, Protected: protectedKeys.Contains(pair.Key)))
                    .OrderBy(entry => entry.Protected)
                    .ThenBy(entry => entry.LastUsed)
                    .ToList();
                var target = (long)(Options.MaxBytes * TrimTarget);
                foreach (var entry in byAge)
                {
                    var bytes = Interlocked.Read(ref _bytes);
                    if (bytes <= target || (entry.Protected && bytes <= Options.MaxBytes))
                    {
                        break;
                    }

                    Forget(entry.Key);
                }
            }
            catch (Exception exception)
            {
                CleanupFailed?.Invoke(exception);
            }
        }
    }

    private async Task WriteAsync(DecodedPhoto photo, string key, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Folder);
        var target = PathOf(key);
        var temporary = Path.Combine(Folder, key + "." + Guid.NewGuid().ToString("N")[..8] + TempExtension);
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var stream = file.AsRandomAccessStream())
            {
                var quality = new BitmapPropertySet
                {
                    ["ImageQuality"] = new BitmapTypedValue(Options.JpegQuality, PropertyType.Single),
                };
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, quality).AsTask(cancellationToken).ConfigureAwait(false);

                // Straight from the decoded bitmap, with no managed copy: a 4.4 MB array per proxy was garbage on the
                // large object heap, 0.3 to 0.5 GB a second during the background pass (review M1). JPEG has no alpha,
                // so WIC drops it from the premultiplied pixels: a transparent part reads as it does over the
                // renderer's black, as before.
                encoder.SetSoftwareBitmap(photo.Pixels);
                await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        var length = new FileInfo(target).Length;
        _touched.TryAdd(key, true);
        if (_index.TryGetValue(key, out var existing))
        {
            Interlocked.Add(ref _bytes, length - existing.Bytes);
            existing.Bytes = length;
            existing.LastUsed = DateTime.UtcNow.Ticks;
        }
        else
        {
            Add(key, length, DateTime.UtcNow);
        }

        TrimSoon();
    }

    private void Add(string key, long bytes, DateTime lastUsedUtc)
    {
        if (_index.TryAdd(key, new Entry { Bytes = bytes, LastUsed = lastUsedUtc.Ticks }))
        {
            Interlocked.Add(ref _bytes, bytes);
        }
    }

    // One trim at a time, off the building thread.
    private void TrimSoon()
    {
        if (Interlocked.Read(ref _bytes) <= Options.MaxBytes || Interlocked.Exchange(ref _trimming, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                TrimNow();
            }
            finally
            {
                Volatile.Write(ref _trimming, 0);
            }
        });
    }

    // A proxy: the key (16 bytes of hex) and .jpg, as PathOf names it.
    [GeneratedRegex(@"^[0-9a-f]{32}\.jpg$", RegexOptions.CultureInvariant)]
    private static partial Regex ProxyName();

    // An unfinished write: the key, 8 hex digits and .tmp, as WriteAsync names it.
    [GeneratedRegex(@"^[0-9a-f]{32}\.[0-9a-f]{8}\.tmp$", RegexOptions.CultureInvariant)]
    private static partial Regex TempName();

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // In use by another instance, or already gone: the next sweep tries again.
        }
    }

    private sealed class Entry
    {
        public long Bytes { get; set; }

        public long LastUsed { get; set; }
    }
}
