using TenLapse.Core.Projects;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TenLapse.Media.Decoding;

/// <summary>What <see cref="PhotoDecoder.ReadInfoAsync"/> learns from a photo's header, without decoding pixels.</summary>
/// <param name="Size">The size as the photo is meant to be seen, EXIF orientation applied (what <see cref="Frame.PixelSize"/> holds).</param>
/// <param name="StoredSize">The size of the pixels as stored, before orientation.</param>
/// <param name="Orientation">
/// The EXIF orientation, 1 to 8 (1: as stored; 3: upside down; 6 and 8: turned a quarter, so width and height
/// swap). 1 when the file has none or the format cannot carry one.
/// </param>
/// <param name="FrameCount">Frames in the file: more than 1 for an animated GIF, a multi-page TIFF or an image sequence. Decoding uses the first.</param>
/// <param name="Decoder">The installed decoder that reads it.</param>
public sealed record PhotoInfo(PixelSize Size, PixelSize StoredSize, int Orientation, int FrameCount, PhotoDecoderInfo Decoder);

/// <summary>How <see cref="PhotoDecoder.DecodeAsync"/> sizes its output. Never upscales.</summary>
public sealed record PhotoDecodeOptions
{
    /// <summary>Decodes at full size.</summary>
    public static PhotoDecodeOptions FullSize { get; } = new();

    /// <summary>
    /// Shrinks during decode to the smallest size that still covers this box (a centred cover crop of it fills
    /// the box exactly, as <see cref="Export.FrameFill"/> draws it): pass the output size. Null for full size.
    /// </summary>
    public PixelSize? CoverSize { get; init; }

    /// <summary>
    /// The longest side the output may have, such as the device's largest bitmap or a proxy's 1280 px; wins over
    /// <see cref="CoverSize"/>. Null for no limit.
    /// </summary>
    public int? MaxSide { get; init; }

    /// <summary>The cover size for an output, as the export uses it.</summary>
    public static PhotoDecodeOptions Cover(PixelSize outputSize) => new() { CoverSize = outputSize };
}

/// <summary>
/// A decoded photo: BGRA8 premultiplied pixels, EXIF orientation applied, colour converted to sRGB. Dispose it.
/// </summary>
public sealed class DecodedPhoto : IDisposable
{
    internal DecodedPhoto(SoftwareBitmap pixels, PhotoInfo info)
    {
        Pixels = pixels;
        Info = info;
    }

    /// <summary>The pixels, <see cref="BitmapPixelFormat.Bgra8"/> with <see cref="BitmapAlphaMode.Premultiplied"/>.</summary>
    public SoftwareBitmap Pixels { get; }

    /// <summary>The size of <see cref="Pixels"/>.</summary>
    public PixelSize Size => new(Pixels.PixelWidth, Pixels.PixelHeight);

    /// <summary>What the header said, <see cref="PhotoInfo.Size"/> being the full size before any scaling.</summary>
    public PhotoInfo Info { get; }

    /// <inheritdoc/>
    public void Dispose() => Pixels.Dispose();
}

/// <summary>
/// Reads photos through WIC (<see cref="BitmapDecoder"/>), for import, preview and export alike. Any format an
/// installed decoder reads (<see cref="PhotoCodecs"/>); the first frame of a GIF, a multi-page TIFF or an image
/// sequence; CMYK, 16-bit, grayscale and palette images converted to BGRA8; embedded colour profiles converted to
/// sRGB. Every method is safe on any thread.
/// </summary>
/// <remarks>
/// <para>
/// Nothing escapes as an exception except cancellation (<see cref="OperationCanceledException"/>) and a null
/// path: every other failure comes back as a <see cref="PhotoFailure"/> with its <see cref="SkipReason"/> and the
/// original exception for the log.
/// </para>
/// <para>
/// Files are opened for reading only, letting other programs read, write, rename and delete them meanwhile (a
/// camera import still writing, an editor saving): 10Lapse never locks a photo and never writes to one.
/// </para>
/// </remarks>
public static class PhotoDecoder
{
    /// <summary>The most pixels a decode may produce (1 GiB of BGRA); a larger full-size decode is <see cref="SkipReason.ImageTooLarge"/>.</summary>
    public const long MaxDecodedPixels = 1L << 28;

    private const string OrientationProperty = "System.Photo.Orientation";

    /// <summary>Reads the size, orientation and frame count from the header, without decoding pixels.</summary>
    public static async Task<PhotoResult<PhotoInfo>> ReadInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        cancellationToken.ThrowIfCancellationRequested();
        var stage = PhotoFailures.Stage.Open;
        try
        {
            using var stream = await OpenAsync(path, cancellationToken).ConfigureAwait(false);
            if (stream.Size == 0)
            {
                return PhotoResult<PhotoInfo>.Failed(Empty(path));
            }

            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            stage = PhotoFailures.Stage.Decode;
            return await ReadInfoAsync(path, decoder, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
        {
            return PhotoResult<PhotoInfo>.Failed(PhotoFailures.Classify(exception, path, stage));
        }
    }

    /// <summary>
    /// Decodes the photo to BGRA8 premultiplied, orientation applied, scaled during decode as
    /// <paramref name="options"/> asks (Fant filtering), never upscaled.
    /// </summary>
    public static async Task<PhotoResult<DecodedPhoto>> DecodeAsync(
        string path, PhotoDecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        options ??= PhotoDecodeOptions.FullSize;
        cancellationToken.ThrowIfCancellationRequested();
        var stage = PhotoFailures.Stage.Open;
        try
        {
            using var stream = await OpenAsync(path, cancellationToken).ConfigureAwait(false);
            if (stream.Size == 0)
            {
                return PhotoResult<DecodedPhoto>.Failed(Empty(path));
            }

            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            stage = PhotoFailures.Stage.Decode;
            var read = await ReadInfoAsync(path, decoder, cancellationToken).ConfigureAwait(false);
            if (!read.Succeeded)
            {
                return PhotoResult<DecodedPhoto>.Failed(read.Failure);
            }

            var info = read.Value;
            var scale = Scale(info.Size, options);
            var width = Scaled(info.StoredSize.Width, scale);
            var height = Scaled(info.StoredSize.Height, scale);
            if ((long)width * height > MaxDecodedPixels)
            {
                return PhotoResult<DecodedPhoto>.Failed(PhotoFailures.Found(
                    path,
                    SkipReason.ImageTooLarge,
                    $"Decode: {width}x{height} would be more than {MaxDecodedPixels} pixels."));
            }

            // Scaling happens before the EXIF rotation, so the scaled size is in stored orientation.
            // A WinRT operation that has already completed ignores the token, so check it before the long part.
            cancellationToken.ThrowIfCancellationRequested();
            var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant };
            if (scale < 1)
            {
                transform.ScaledWidth = (uint)width;
                transform.ScaledHeight = (uint)height;
            }

            var pixels = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            if (info.Decoder.CodecId == BitmapDecoder.JpegDecoderId && !JpegStructure.EndsCompletely(path))
            {
                pixels.Dispose();
                return PhotoResult<DecodedPhoto>.Failed(PhotoFailures.Found(
                    path,
                    SkipReason.Truncated,
                    "Decode: the JPEG data ends before its end-of-image marker (the decoder filled the rest with gray)."));
            }

            return PhotoResult<DecodedPhoto>.Success(new DecodedPhoto(pixels, info));
        }
        catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
        {
            return PhotoResult<DecodedPhoto>.Failed(PhotoFailures.Classify(exception, path, stage));
        }
    }

    /// <summary>
    /// The factor a decode scales <paramref name="size"/> (oriented) by: the cover scale of
    /// <see cref="PhotoDecodeOptions.CoverSize"/>, then <see cref="PhotoDecodeOptions.MaxSide"/>, never above 1.
    /// </summary>
    public static double Scale(PixelSize size, PhotoDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var scale = 1.0;
        if (options.CoverSize is { } cover)
        {
            scale = Math.Min(scale, Math.Max((double)cover.Width / size.Width, (double)cover.Height / size.Height));
        }

        if (options.MaxSide is { } maxSide)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSide, nameof(options));
            scale = Math.Min(scale, (double)maxSide / Math.Max(size.Width, size.Height));
        }

        return scale;
    }

    /// <summary>A side scaled by <paramref name="scale"/>, rounded up so a cover never falls a pixel short (a tiny tolerance absorbs rounding).</summary>
    public static int Scaled(int side, double scale) =>
        scale >= 1 ? side : Math.Max(1, (int)Math.Ceiling((side * scale) - 1e-6));

    private static async Task<PhotoResult<PhotoInfo>> ReadInfoAsync(string path, BitmapDecoder decoder, CancellationToken cancellationToken)
    {
        var codec = decoder.DecoderInformation;
        var decoderInfo = PhotoCodecs.Decoders.FirstOrDefault(d => d.CodecId == codec.CodecId)
            ?? new PhotoDecoderInfo(codec.FriendlyName, codec.CodecId, [.. codec.FileExtensions], [.. codec.MimeTypes]);
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || decoder.FrameCount == 0)
        {
            return PhotoResult<PhotoInfo>.Failed(PhotoFailures.Found(
                path,
                SkipReason.DecodeFailed,
                $"Decode: {decoderInfo.Name} reports {decoder.PixelWidth}x{decoder.PixelHeight} and {decoder.FrameCount} frames."));
        }

        var stored = new PixelSize((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        var oriented = new PixelSize((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight);
        var orientation = await OrientationAsync(decoder, cancellationToken).ConfigureAwait(false);
        return PhotoResult<PhotoInfo>.Success(new PhotoInfo(oriented, stored, orientation, (int)decoder.FrameCount, decoderInfo));
    }

    // Best effort: PNG, BMP and GIF have no orientation property, and their decoders say so by throwing.
    private static async Task<int> OrientationAsync(BitmapDecoder decoder, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await decoder.BitmapProperties.GetPropertiesAsync([OrientationProperty]).AsTask(cancellationToken).ConfigureAwait(false);
            return properties.TryGetValue(OrientationProperty, out var value) && value.Value is ushort number && number is >= 1 and <= 8
                ? number
                : 1;
        }
        catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
        {
            return 1;
        }
    }

    /// <summary>
    /// Opens the file read-only, sharing it: other programs keep it open, write, rename or delete it meanwhile.
    /// </summary>
    /// <remarks>
    /// Windows' own file stream first: through it a 24 MP JPEG decodes in about 115 ms, through .NET's adapter over
    /// a <see cref="FileStream"/> in about 205 ms (every read becomes an async WinRT call). It refuses paths past
    /// MAX_PATH even with the <c>\\?\</c> prefix, and says "access denied" for a file another program has locked,
    /// so both cases go to the <see cref="FileStream"/>, which opens long paths and names a lock as a sharing
    /// violation (or throws the real access error, which is then classified).
    /// </remarks>
    private static async Task<IRandomAccessStream> OpenAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read, StorageOpenOptions.AllowReadersAndWriters, FileOpenDisposition.OpenExisting)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PathTooLongException or UnauthorizedAccessException)
        {
            var file = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite | FileShare.Delete,
                    Options = FileOptions.RandomAccess,
                    BufferSize = 64 * 1024,
                });
            return file.AsRandomAccessStream();
        }
    }

    private static PhotoFailure Empty(string path) => PhotoFailures.Found(path, SkipReason.EmptyFile, "Open: the file has 0 bytes.");

    private static bool IsCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
