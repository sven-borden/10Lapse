using Microsoft.Graphics.Canvas;
using TenLapse.Core.Diagnostics;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Media;
using TenLapse.Media.Decoding;
using TenLapse.Media.Preview;
using Windows.Graphics.Imaging;

namespace TenLapse.Tests.Media;

public sealed class ProxyCacheTests : IDisposable
{
    private static readonly DateTime _written = new(2026, 5, 1, 18, 30, 0, DateTimeKind.Utc);

    private readonly PhotoFixtures _photos = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", "proxies-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _photos.Dispose();
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
            else if (File.Exists(_folder))
            {
                File.Delete(_folder);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Windows cleans the temp folder eventually.
        }
    }

    [Fact]
    public void AKeyIsThePhotosPathSizeAndTime()
    {
        var cache = new ProxyCache(_folder);
        var key = cache.KeyOf(@"C:\Photos\Harbor\IMG_0001.JPG", 1_000, _written);

        Assert.Matches("^[0-9a-f]{32}$", key);
        Assert.Equal(key, cache.KeyOf(@"c:\photos\harbor\.\IMG_0001.jpg", 1_000, _written));
        Assert.Equal(key, cache.KeyOf(@"C:/Photos/Harbor/IMG_0001.JPG", 1_000, _written.ToLocalTime()));
        Assert.NotEqual(key, cache.KeyOf(@"C:\Photos\Harbor\IMG_0001.JPG", 1_001, _written));
        Assert.NotEqual(key, cache.KeyOf(@"C:\Photos\Harbor\IMG_0001.JPG", 1_000, _written.AddTicks(1)));
        Assert.NotEqual(key, cache.KeyOf(@"C:\Photos\Harbor\IMG_0002.JPG", 1_000, _written));
        Assert.NotEqual(key, new ProxyCache(_folder, new ProxyCacheOptions { MaxSide = 640 }).KeyOf(@"C:\Photos\Harbor\IMG_0001.JPG", 1_000, _written));
    }

    [Fact]
    public async Task BuildsAJpegNoLongerThanTheMaxSideWithTheOrientationApplied()
    {
        // 3000 x 2000 stored, EXIF orientation 6 (turn right): a 2000 x 3000 portrait.
        var path = await _photos.WriteAsync("portrait.jpg", BitmapEncoder.JpegEncoderId, 3000, 2000, orientation: 6);
        var cache = new ProxyCache(_folder);
        await cache.OpenAsync();
        var key = KeyOf(cache, path);

        var build = await cache.BuildAsync(path, key, TestContext.Current.CancellationToken);

        Assert.Null(build.Failure);
        Assert.Null(build.WriteFailure);
        using (var photo = build.Photo!)
        {
            Assert.Equal(1280, photo.Size.Height);
            Assert.InRange(photo.Size.Width, 853, 854);
        }

        Assert.True(cache.Contains(key, mayTouchDisk: false));
        var proxy = await PhotoDecoder.ReadInfoAsync(cache.PathOf(key), TestContext.Current.CancellationToken);
        Assert.True(proxy.Succeeded);
        Assert.Equal(1, proxy.Value.Orientation);
        Assert.Equal(1280, proxy.Value.Size.Height);
        Assert.Equal(1, cache.Size.Count);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public async Task APhotoThatCannotBeReadSaysWhyAndLeavesNoProxy()
    {
        var cache = new ProxyCache(_folder);
        var gone = Path.Combine(_photos.Directory, "gone.jpg");

        var build = await cache.BuildAsync(gone, cache.KeyOf(gone, 1, _written), TestContext.Current.CancellationToken);

        Assert.Null(build.Photo);
        Assert.Equal(SkipReason.FileNotFound, build.Failure!.Reason);
        Assert.Equal(0, cache.Size.Count);
    }

    [Fact]
    public async Task OpeningSweepsProxiesNoProjectUsedForAMonthAndUnfinishedWrites()
    {
        Directory.CreateDirectory(_folder);
        var used = Fake("a1", DateTime.UtcNow.AddDays(-2));
        var unused = Fake("b2", DateTime.UtcNow.AddDays(-31));
        var leftover = Fake(Key("c3") + ".1234abcd", DateTime.UtcNow.AddHours(-2), ".tmp");
        var writing = Fake(Key("d4") + ".5678abcd", DateTime.UtcNow, ".tmp");

        // Files that are not the cache's own stay, however old: a folder given by mistake loses nothing.
        var photo = Fake("IMG_0001", DateTime.UtcNow.AddDays(-400), ".jpg", keyName: false);
        var upper = Fake(Key("e5").ToUpperInvariant(), DateTime.UtcNow.AddDays(-400), ".jpg", keyName: false);
        var notes = Fake("draft", DateTime.UtcNow.AddDays(-400), ".tmp");
        var otherTemp = Fake(Key("f6") + ".tmp", DateTime.UtcNow.AddDays(-400), ".tmp");

        var cache = new ProxyCache(_folder);
        await cache.OpenAsync();

        Assert.True(File.Exists(used));
        Assert.False(File.Exists(unused));
        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(writing));
        Assert.True(File.Exists(photo));
        Assert.True(File.Exists(upper));
        Assert.True(File.Exists(notes));
        Assert.True(File.Exists(otherTemp));
        Assert.True(cache.Contains(Key("a1"), mayTouchDisk: false));
        Assert.False(cache.Contains(Key("b2"), mayTouchDisk: false));
        Assert.Equal((1, 100L), cache.Size);
    }

    [Fact]
    public async Task PastTheBoundOtherPhotosGoFirstAndTheProjectsOwnOnlyDownToTheBound()
    {
        Directory.CreateDirectory(_folder);
        var names = Enumerable.Range(0, 6).Select(i => "a" + i).ToList();
        for (var i = 0; i < names.Count; i++)
        {
            Fake(names[i], DateTime.UtcNow.AddMinutes(i - 10));
        }

        // 600 bytes on a 350-byte bound: the three the project does not use go (oldest first), and then it is at 300,
        // under the bound, so the project's own three stay although the trim aims at 80 % (280).
        var cache = new ProxyCache(_folder, new ProxyCacheOptions { MaxBytes = 350 });
        cache.Protect(names.Take(3).Select(Key));
        await cache.OpenAsync();

        Assert.Equal(["a0", "a1", "a2"], Remaining());
        Assert.Equal(300, cache.Size.Bytes);

        // When the project alone is past the bound, its least recently used go until it fits.
        var tight = new ProxyCache(_folder, new ProxyCacheOptions { MaxBytes = 150 });
        tight.Protect(names.Select(Key));
        await tight.OpenAsync();
        Assert.Equal(["a2"], Remaining());
    }

    [Fact]
    public void TheDefaultBoundIsTwoGigabytesOrATenthOfTheFreeSpace()
    {
        var bound = ProxyCache.DefaultMaxBytes(Path.GetTempPath());

        Assert.InRange(bound, 256L << 20, 2L << 30);
        Assert.Equal(2L << 30, ProxyCache.DefaultMaxBytes("::not a path::"));
    }

    [Fact]
    public async Task TheSourceBuildsAProxyOnceAndThenLoadsIt()
    {
        MediaSkips.SkipWithoutGraphicsDevice();
        var path = await _photos.WriteAsync("landscape.jpg", BitmapEncoder.JpegEncoderId, 3000, 2000);
        var frame = FrameOf(path);
        var cache = new ProxyCache(_folder);
        await cache.OpenAsync();
        var source = new PreviewImageSource(cache, () => GraphicsDevice.Shared);
        var token = TestContext.Current.CancellationToken;

        Assert.False(source.HasProxy(frame));
        using (var quick = await LoadAsync(source, frame, FrameImageQuality.Quick))
        {
            Assert.Equal(FrameImageQuality.Quick, quick.Quality);
            Assert.Equal(PreviewImageSource.QuickSide, (int)quick.Image.SizeInPixels.Width);
        }

        using (var proxy = await LoadAsync(source, frame, FrameImageQuality.Proxy))
        {
            Assert.Equal(FrameImageQuality.Proxy, proxy.Quality);
            Assert.Equal(1280u, proxy.Image.SizeInPixels.Width);
            Assert.InRange(proxy.Bytes, 1280L * 853 * 4, 1280L * 854 * 4);
        }

        Assert.True(source.HasProxy(frame));
        Assert.Equal(FrameProxyResult.Ready, await source.EnsureProxyAsync(frame, token));

        // With the proxy there, even a quick load takes it.
        using (var again = await LoadAsync(source, frame, FrameImageQuality.Quick))
        {
            Assert.Equal(FrameImageQuality.Proxy, again.Quality);
        }

        Assert.Equal(1, cache.Size.Count);
    }

    [Fact]
    public async Task APhotoChangedOnDiskGetsANewProxy()
    {
        MediaSkips.SkipWithoutGraphicsDevice();
        var path = await _photos.WriteAsync("changing.jpg", BitmapEncoder.JpegEncoderId, 3000, 2000);
        var frame = FrameOf(path);
        var cache = new ProxyCache(_folder);
        await cache.OpenAsync();
        var source = new PreviewImageSource(cache, () => GraphicsDevice.Shared);
        using (var before = await LoadAsync(source, frame, FrameImageQuality.Proxy))
        {
            Assert.Equal(1280u, before.Image.SizeInPixels.Width);
        }

        // Edited in another app: now a portrait, written later. The frame still carries what the import saw.
        await PhotoFixtures.WriteToAsync(path, BitmapEncoder.JpegEncoderId, 2000, 3000);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));

        using (var after = await LoadAsync(source, frame, FrameImageQuality.Proxy))
        {
            Assert.Equal(1280u, after.Image.SizeInPixels.Height);
        }

        Assert.Equal(2, Directory.GetFiles(_folder, "*.jpg").Length);
    }

    [Fact]
    public async Task APhotoThatIsGoneFailsWithTheReasonTheImportWouldGive()
    {
        MediaSkips.SkipWithoutGraphicsDevice();
        var path = await _photos.WriteAsync("gone.jpg", BitmapEncoder.JpegEncoderId, 64, 32);
        var frame = FrameOf(path);
        File.Delete(path);
        var source = new PreviewImageSource(new ProxyCache(_folder), () => GraphicsDevice.Shared);
        var token = TestContext.Current.CancellationToken;

        var loaded = await source.LoadAsync(frame, FrameImageQuality.Proxy, token);
        var built = await source.EnsureProxyAsync(frame, token);

        Assert.Null(loaded.Image);
        Assert.Equal(SkipReason.FileNotFound, loaded.Failure!.File.Reason);
        Assert.Equal(path, loaded.Failure.File.Path);
        Assert.Equal(SkipReason.FileNotFound, built.Failure!.File.Reason);
    }

    [Fact]
    public async Task AProxyThatCannotBeSavedStillGivesTheImage()
    {
        MediaSkips.SkipWithoutGraphicsDevice();
        var path = await _photos.WriteAsync("unsaved.jpg", BitmapEncoder.JpegEncoderId, 3000, 2000);
        var frame = FrameOf(path);

        // The cache's folder is a file, so nothing can be written there.
        Directory.CreateDirectory(Path.GetDirectoryName(_folder)!);
        await File.WriteAllTextAsync(_folder, "not a folder", TestContext.Current.CancellationToken);
        var source = new PreviewImageSource(new ProxyCache(_folder), () => GraphicsDevice.Shared);

        using var loaded = await LoadAsync(source, frame, FrameImageQuality.Proxy, expectCacheFailure: true);
        var built = await source.EnsureProxyAsync(frame, TestContext.Current.CancellationToken);

        Assert.Equal(FrameImageQuality.Proxy, loaded.Quality);
        Assert.NotNull(built.CacheFailure);
        Assert.Null(built.Failure);
    }

    [Fact]
    public async Task ADamagedProxyIsMadeAgainAndIsNoCacheFailure()
    {
        MediaSkips.SkipWithoutGraphicsDevice();
        var path = await _photos.WriteAsync("damaged.jpg", BitmapEncoder.JpegEncoderId, 3000, 2000);
        var frame = FrameOf(path);
        var cache = new ProxyCache(_folder);
        await cache.OpenAsync();
        var log = new ListLog();
        var source = new PreviewImageSource(cache, () => GraphicsDevice.Shared) { Log = log };
        using (await LoadAsync(source, frame, FrameImageQuality.Proxy))
        {
        }

        var proxyPath = cache.PathOf(KeyOf(cache, path));
        var whole = new FileInfo(proxyPath).Length;

        // A crash mid-copy, a disk error: the proxy ends a third of the way in.
        using (var file = new FileStream(proxyPath, FileMode.Open, FileAccess.Write))
        {
            file.SetLength(whole / 3);
        }

        // No cache failure (LoadAsync asserts it), the image is proxy size, and the proxy is whole again.
        using (var again = await LoadAsync(source, frame, FrameImageQuality.Proxy))
        {
            Assert.Equal(FrameImageQuality.Proxy, again.Quality);
            Assert.Equal(1280u, again.Image.SizeInPixels.Width);
        }

        Assert.Equal(whole, new FileInfo(proxyPath).Length);
        Assert.Single(log.Lines, line => line.Level == LogLevel.Warning && line.Message.Contains("damaged.jpg", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, line => line.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task ATransparentPartOfAPhotoIsBlackInItsProxyAsOverTheRenderersBlack()
    {
        // Left half transparent white, right half opaque white, as a PNG with alpha.
        const int Width = 64;
        const int Height = 32;
        var path = Path.Combine(_photos.Directory, "transparent.png");
        var pixels = new byte[Width * Height * 4];
        for (var i = 0; i < Width * Height; i++)
        {
            pixels.AsSpan(i * 4, 3).Fill(255);
            pixels[(i * 4) + 3] = (i % Width) < Width / 2 ? (byte)0 : (byte)255;
        }

        using (var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var stream = file.AsRandomAccessStream())
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, Width, Height, 96, 96, pixels);
            await encoder.FlushAsync();
        }

        var cache = new ProxyCache(_folder);
        await cache.OpenAsync();
        var key = KeyOf(cache, path);
        var build = await cache.BuildAsync(path, key, TestContext.Current.CancellationToken);
        build.Photo?.Dispose();
        Assert.Null(build.WriteFailure);

        using var proxyFile = File.OpenRead(cache.PathOf(key));
        var decoder = await BitmapDecoder.CreateAsync(proxyFile.AsRandomAccessStream());
        var data = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        var left = ((Height / 2 * Width) + 8) * 4;
        var right = ((Height / 2 * Width) + Width - 8) * 4;
        Assert.All(data.AsSpan(left, 3).ToArray(), value => Assert.InRange(value, 0, 16));
        Assert.All(data.AsSpan(right, 3).ToArray(), value => Assert.InRange(value, 239, 255));
    }

    private static Frame FrameOf(string path)
    {
        var info = new FileInfo(path);
        return new Frame(path, info.Length, info.LastWriteTimeUtc);
    }

    private static string KeyOf(ProxyCache cache, string path)
    {
        var info = new FileInfo(path);
        return cache.KeyOf(path, info.Length, info.LastWriteTimeUtc);
    }

    private static async Task<Loaded> LoadAsync(PreviewImageSource source, Frame frame, FrameImageQuality quality, bool expectCacheFailure = false)
    {
        var result = await source.LoadAsync(frame, quality, TestContext.Current.CancellationToken);
        Assert.Null(result.Failure);
        Assert.Equal(expectCacheFailure, result.CacheFailure is not null);
        return new Loaded(result.Image!, result.Quality, result.Bytes);
    }

    // A proxy file of 100 bytes named after a made-up key (or, with keyName false, the name as given), last used at the given time.
    private string Fake(string name, DateTime lastUsedUtc, string extension = ".jpg", bool keyName = true)
    {
        var path = Path.Combine(_folder, (extension == ".jpg" && keyName ? Key(name) : name) + extension);
        File.WriteAllBytes(path, new byte[100]);
        File.SetLastWriteTimeUtc(path, lastUsedUtc);
        return path;
    }

    // Hex, as a real key is: the cache touches no other names.
    private static string Key(string name) => name.PadRight(32, 'f');

    private List<string> Remaining() =>
        [.. Directory.GetFiles(_folder, "*.jpg").Select(path => Path.GetFileNameWithoutExtension(path).TrimEnd('f')).Order()];

    private sealed class ListLog : ILog
    {
        private readonly List<(LogLevel Level, string Message)> _lines = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public void Write(LogLevel level, string message, Exception? exception = null)
        {
            lock (_lines)
            {
                _lines.Add((level, message));
            }
        }

        public IReadOnlyList<string> RecentLines(int count) => [.. Lines.TakeLast(count).Select(line => line.Message)];
    }

    private sealed record Loaded(CanvasBitmap Image, FrameImageQuality Quality, long Bytes) : IDisposable
    {
        public void Dispose() => Image.Dispose();
    }
}
