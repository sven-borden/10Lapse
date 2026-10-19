using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using Windows.Graphics.Imaging;

namespace TenLapse.Media.Preview;

/// <summary>
/// The filmstrip's thumbnails (#20), cheapest first: the picture the camera embedded in the photo
/// (<see cref="PhotoThumbnails"/>), else the preview's proxy when #19 has built it (<see cref="ProxyCache"/>, decoded
/// small), else the photo decoded small. No second proxy pipeline: the proxies are only read here, never built. Every
/// thumbnail is upright and at least covers the size asked for.
/// </summary>
public sealed class ThumbnailSource : IThumbnailSource<SoftwareBitmap>
{
    private readonly ProxyCache _proxies;

    /// <summary>Reads the proxies of <paramref name="proxies"/> when they exist.</summary>
    public ThumbnailSource(ProxyCache proxies)
    {
        _proxies = proxies ?? throw new ArgumentNullException(nameof(proxies));
    }

    /// <inheritdoc/>
    public async Task<ThumbnailResult<SoftwareBitmap>> LoadAsync(Frame frame, ThumbnailSize size, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var cover = new PixelSize(size.Width, size.Height);
        if (await PhotoThumbnails.EmbeddedAsync(frame.SourcePath, cover, cancellationToken).ConfigureAwait(false) is { } embedded)
        {
            return Loaded(embedded, ThumbnailOrigin.Embedded);
        }

        // The proxy as the import recorded the photo; one the preview has not built yet is not waited for.
        var key = _proxies.KeyOf(frame);
        if (_proxies.Contains(key, mayTouchDisk: false))
        {
            var proxy = await PhotoDecoder.DecodeAsync(_proxies.PathOf(key), PhotoDecodeOptions.Cover(cover), cancellationToken).ConfigureAwait(false);
            if (proxy.Succeeded)
            {
                return Loaded(proxy.Value, ThumbnailOrigin.Proxy);
            }
        }

        var photo = await PhotoDecoder.DecodeAsync(frame.SourcePath, PhotoDecodeOptions.Cover(cover), cancellationToken).ConfigureAwait(false);
        return photo.Succeeded
            ? Loaded(photo.Value, ThumbnailOrigin.Photo)
            : ThumbnailResult<SoftwareBitmap>.Failed(new FrameLoadFailure(photo.Failure.ToSkippedFile(), photo.Failure.Exception));
    }

    // The decoded photo's pixels go to the cache, which disposes them.
    private static ThumbnailResult<SoftwareBitmap> Loaded(DecodedPhoto photo, ThumbnailOrigin origin) =>
        ThumbnailResult<SoftwareBitmap>.Loaded(photo.Pixels, (long)photo.Size.Width * photo.Size.Height * 4, origin);
}
