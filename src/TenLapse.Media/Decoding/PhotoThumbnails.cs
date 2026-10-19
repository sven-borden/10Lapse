using TenLapse.Core.Projects;
using Windows.Graphics.Imaging;

namespace TenLapse.Media.Decoding;

/// <summary>
/// The small picture a camera embeds in a photo (the EXIF thumbnail, or the preview a RAW or HEIC decoder offers), for
/// the filmstrip (#20): reading it costs the photo's first few kilobytes instead of a decode of the whole photo.
/// </summary>
/// <remarks>
/// <para>
/// The embedded picture is stored as the sensor saw it, like the photo's own pixels: the photo's EXIF orientation is
/// applied here by hand (WIC turns only the main image), so a portrait photo's thumbnail stands upright.
/// </para>
/// <para>
/// A picture is used only when it shows the same thing the photo does: its shape must match the photo's within 3 %
/// (many cameras embed a 160 × 120 picture of a 3:2 photo with black bars above and below), and it must cover the size
/// asked for without being enlarged. Otherwise, and for a photo without one, the answer is null and the caller decodes
/// the proxy or the photo instead.
/// </para>
/// </remarks>
public static class PhotoThumbnails
{
    /// <summary>How far the embedded picture's shape may differ from the photo's.</summary>
    public const double AspectTolerance = 0.03;

    private const int FileBufferSize = 64 * 1024;

    /// <summary>
    /// The photo's embedded picture, upright, at the smallest size that covers <paramref name="cover"/> (BGRA8
    /// premultiplied, like <see cref="PhotoDecoder.DecodeAsync"/>); null when it has none that will do. Never throws but
    /// for cancellation: a photo that cannot be opened is null too, and the full decode says why.
    /// </summary>
    public static async Task<DecodedPhoto?> EmbeddedAsync(string path, PixelSize cover, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // .NET's stream adapter reads only what the decoder asks for and never maps the file.
            using var file = PhotoDecoder.OpenFile(path);
            using var stream = new FileStream(file, FileAccess.Read, FileBufferSize).AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
            {
                return null;
            }

            using var embedded = await decoder.GetThumbnailAsync().AsTask(cancellationToken).ConfigureAwait(false);
            var picture = await BitmapDecoder.CreateAsync(embedded).AsTask(cancellationToken).ConfigureAwait(false);
            var (width, height) = ((int)picture.PixelWidth, (int)picture.PixelHeight);
            if (width == 0 || height == 0)
            {
                return null;
            }

            var photoAspect = (double)decoder.PixelWidth / decoder.PixelHeight;
            var pictureAspect = (double)width / height;
            if (Math.Abs((pictureAspect / photoAspect) - 1) > AspectTolerance)
            {
                return null;
            }

            var orientation = await PhotoDecoder.OrientationAsync(decoder, cancellationToken).ConfigureAwait(false);
            var turned = orientation is >= 5 and <= 8;
            var (uprightWidth, uprightHeight) = turned ? (height, width) : (width, height);
            var scale = Math.Max((double)cover.Width / uprightWidth, (double)cover.Height / uprightHeight);
            if (scale > 1)
            {
                return null;
            }

            var (rotation, flip) = Turn(orientation);
            var transform = new BitmapTransform
            {
                InterpolationMode = BitmapInterpolationMode.Fant,
                ScaledWidth = (uint)Math.Max(1, (int)Math.Round(width * scale)),
                ScaledHeight = (uint)Math.Max(1, (int)Math.Round(height * scale)),
                Rotation = rotation,
                Flip = flip,
            };

            cancellationToken.ThrowIfCancellationRequested();
            var pixels = await picture.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            var codec = decoder.DecoderInformation;
            var info = new PhotoInfo(
                new PixelSize((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight),
                new PixelSize((int)decoder.PixelWidth, (int)decoder.PixelHeight),
                orientation,
                (int)decoder.FrameCount,
                new PhotoDecoderInfo(codec.FriendlyName, codec.CodecId, [.. codec.FileExtensions], [.. codec.MimeTypes]));
            return new DecodedPhoto(pixels, info);
        }
        catch (Exception exception) when (!PhotoDecoder.IsCancellation(exception, cancellationToken))
        {
            // No embedded picture (WIC says so by throwing), or one that does not decode: the caller decodes the photo.
            return null;
        }
    }

    /// <summary>
    /// The turn that stands a picture stored with EXIF <paramref name="orientation"/> upright. WIC flips before it
    /// rotates, so the mirrored orientations (5, 7) flip horizontally and then turn.
    /// </summary>
    internal static (BitmapRotation Rotation, BitmapFlip Flip) Turn(int orientation) => orientation switch
    {
        2 => (BitmapRotation.None, BitmapFlip.Horizontal),
        3 => (BitmapRotation.Clockwise180Degrees, BitmapFlip.None),
        4 => (BitmapRotation.None, BitmapFlip.Vertical),
        5 => (BitmapRotation.Clockwise270Degrees, BitmapFlip.Horizontal),
        6 => (BitmapRotation.Clockwise90Degrees, BitmapFlip.None),
        7 => (BitmapRotation.Clockwise90Degrees, BitmapFlip.Horizontal),
        8 => (BitmapRotation.Clockwise270Degrees, BitmapFlip.None),
        _ => (BitmapRotation.None, BitmapFlip.None),
    };
}
