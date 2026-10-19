using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;

namespace TenLapse.Media.Preview;

/// <summary>
/// The preview's frame images from the photos (#19): proxies from <see cref="ProxyCache"/>, built from the photo when
/// missing or stale, small decodes while a proxy is not built yet, uploaded to the graphics device as Win2D bitmaps
/// for <see cref="Rendering.FrameRenderer.Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize)"/>, the
/// same call the export draws with.
/// </summary>
/// <remarks>
/// Before each load the photo's size and last write time are read from the disk, so a photo edited since the import
/// gets a new proxy (the export would read the edited photo too). A photo that is gone or unreadable comes back as a
/// failure with the reason the import would give (<see cref="SkippedFile"/>).
/// </remarks>
public sealed class PreviewImageSource : IFrameImageSource<CanvasBitmap>
{
    /// <summary>The longest side of a small decode (<see cref="FrameImageQuality.Quick"/>).</summary>
    public const int QuickSide = 480;

    private readonly ProxyCache _cache;
    private readonly Func<CanvasDevice> _device;
    private readonly ConcurrentDictionary<Guid, string> _recordedKeys = new();

    /// <summary>Loads through <paramref name="cache"/> onto the device <paramref name="device"/> gives (asked at each upload, so a new device after a loss is used).</summary>
    public PreviewImageSource(ProxyCache cache, Func<CanvasDevice> device)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <inheritdoc/>
    public bool HasProxy(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return _cache.Contains(RecordedKey(frame), mayTouchDisk: false);
    }

    /// <inheritdoc/>
    public async Task<FrameImageResult<CanvasBitmap>> LoadAsync(Frame frame, FrameImageQuality quality, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var key = CurrentKey(frame);
        Exception? cacheFailure = null;
        if (key is not null && _cache.Contains(key))
        {
            var proxy = await PhotoDecoder.DecodeAsync(_cache.PathOf(key), PhotoDecodeOptions.FullSize, cancellationToken).ConfigureAwait(false);
            if (proxy.Succeeded)
            {
                _cache.Touch(key);
                return Upload(proxy.Value, FrameImageQuality.Proxy);
            }

            // Deleted under us (another instance trimmed it) or damaged: rebuild it from the photo.
            _cache.Forget(key);
            if (proxy.Failure.Reason is not SkipReason.FileNotFound)
            {
                cacheFailure = proxy.Failure.Exception ?? new IOException(proxy.Failure.Detail);
            }
        }

        if (quality == FrameImageQuality.Quick || key is null)
        {
            // A small decode, or the photo is gone (no key): the decoder gives the reason the import would.
            var small = await PhotoDecoder.DecodeAsync(frame.SourcePath, new PhotoDecodeOptions { MaxSide = QuickSide }, cancellationToken).ConfigureAwait(false);
            return small.Succeeded
                ? Upload(small.Value, FrameImageQuality.Quick, cacheFailure)
                : FrameImageResult<CanvasBitmap>.Failed(Failure(small.Failure));
        }

        var build = await _cache.BuildAsync(frame.SourcePath, key, cancellationToken).ConfigureAwait(false);
        if (build.Photo is null)
        {
            return FrameImageResult<CanvasBitmap>.Failed(Failure(build.Failure!));
        }

        return Upload(build.Photo, FrameImageQuality.Proxy, build.WriteFailure ?? cacheFailure);
    }

    /// <inheritdoc/>
    public async Task<FrameProxyResult> EnsureProxyAsync(Frame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var key = CurrentKey(frame);
        if (key is null)
        {
            // Gone: say why as the decoder would.
            var probe = await PhotoDecoder.ReadInfoAsync(frame.SourcePath, cancellationToken).ConfigureAwait(false);
            return probe.Succeeded ? FrameProxyResult.Ready : new FrameProxyResult(Failure(probe.Failure));
        }

        if (_cache.Contains(key))
        {
            _cache.Touch(key);
            return FrameProxyResult.Ready;
        }

        var build = await _cache.BuildAsync(frame.SourcePath, key, cancellationToken).ConfigureAwait(false);
        build.Photo?.Dispose();
        return build.Photo is null
            ? new FrameProxyResult(Failure(build.Failure!))
            : build.WriteFailure is null ? FrameProxyResult.Ready : new FrameProxyResult(CacheFailure: build.WriteFailure);
    }

    /// <inheritdoc/>
    public void FramesChanged(ImmutableArray<Frame> frames)
    {
        var ids = frames.Select(frame => frame.Id).ToHashSet();
        foreach (var id in _recordedKeys.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            _recordedKeys.TryRemove(id, out _);
        }

        _cache.Protect(frames.Select(RecordedKey));
    }

    private static FrameLoadFailure Failure(PhotoFailure failure) => new(failure.ToSkippedFile(), failure.Exception);

    // The key as the import saw the photo; the scheduler's hint and the protection from eviction use it.
    private string RecordedKey(Frame frame) => _recordedKeys.GetOrAdd(frame.Id, _ => _cache.KeyOf(frame));

    // The key of the photo as it is on disk now; null when it is not there.
    private string? CurrentKey(Frame frame)
    {
        try
        {
            var info = new FileInfo(frame.SourcePath);
            if (!info.Exists)
            {
                return null;
            }

            return info.Length == frame.FileSize && info.LastWriteTimeUtc == frame.LastWriteTimeUtc
                ? RecordedKey(frame)
                : _cache.KeyOf(frame.SourcePath, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private FrameImageResult<CanvasBitmap> Upload(DecodedPhoto photo, FrameImageQuality quality, Exception? cacheFailure = null)
    {
        using (photo)
        {
            var bitmap = CanvasBitmap.CreateFromSoftwareBitmap(_device(), photo.Pixels);
            var bytes = (long)photo.Size.Width * photo.Size.Height * 4;
            return FrameImageResult<CanvasBitmap>.Loaded(bitmap, quality, bytes, cacheFailure);
        }
    }
}
