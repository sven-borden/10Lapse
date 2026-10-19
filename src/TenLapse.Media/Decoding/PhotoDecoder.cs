using System.Buffers;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Win32.SafeHandles;
using TenLapse.Core.Projects;
using Windows.Graphics.Imaging;
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
    /// the box exactly, as <see cref="Rendering.FrameRenderer"/> draws Fill): pass the output size. Null for full size.
    /// </summary>
    public PixelSize? CoverSize { get; init; }

    /// <summary>
    /// Shrinks during decode to the largest size that fits inside this box (the whole photo, letterboxed, as
    /// <see cref="Rendering.FrameRenderer"/> draws Fit): pass the output size. Null for full size. Set this or
    /// <see cref="CoverSize"/>, not both.
    /// </summary>
    public PixelSize? ContainSize { get; init; }

    /// <summary>
    /// The longest side the output may have, such as the device's largest bitmap or a proxy's 1280 px; wins over
    /// <see cref="CoverSize"/>. Null for no limit.
    /// </summary>
    public int? MaxSide { get; init; }

    /// <summary>The cover size for an output, as the export uses it.</summary>
    public static PhotoDecodeOptions Cover(PixelSize outputSize) => new() { CoverSize = outputSize };

    /// <summary>The contain size for an output, as the export uses it for Fit.</summary>
    public static PhotoDecodeOptions Contain(PixelSize outputSize) => new() { ContainSize = outputSize };
}

/// <summary>
/// A decoded photo: BGRA8 premultiplied pixels, EXIF orientation applied, colour converted to sRGB. Dispose it.
/// </summary>
public sealed class DecodedPhoto : IDisposable
{
    internal DecodedPhoto(SoftwareBitmap pixels, PhotoInfo info, ColourConversion colour = ColourConversion.None)
    {
        Pixels = pixels;
        Info = info;
        Colour = colour;
    }

    /// <summary>The pixels, <see cref="BitmapPixelFormat.Bgra8"/> with <see cref="BitmapAlphaMode.Premultiplied"/>.</summary>
    public SoftwareBitmap Pixels { get; }

    /// <summary>The size of <see cref="Pixels"/>.</summary>
    public PixelSize Size => new(Pixels.PixelWidth, Pixels.PixelHeight);

    /// <summary>What the header said, <see cref="PhotoInfo.Size"/> being the full size before any scaling.</summary>
    public PhotoInfo Info { get; }

    /// <summary>How the colour came to sRGB (for tests and the log).</summary>
    internal ColourConversion Colour { get; }

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
/// camera import still writing, an editor saving), and closed before a method returns, with nothing mapped:
/// 10Lapse never locks a photo and never writes to one.
/// </para>
/// </remarks>
public static class PhotoDecoder
{
    /// <summary>The most pixels a decode may produce (1 GiB of BGRA); a larger full-size decode is <see cref="SkipReason.ImageTooLarge"/>.</summary>
    public const long MaxDecodedPixels = 1L << 28;

    /// <summary>The largest file <see cref="DecodeAsync"/> copies into memory before decoding (1 GiB); a larger one is read from disk.</summary>
    internal const long MaxInMemoryFileBytes = 1L << 30;

    private const string OrientationProperty = "System.Photo.Orientation";

    private const int FileBufferSize = 64 * 1024;

    private const int CopyBufferSize = 1024 * 1024;

    /// <summary>
    /// Reads the size, orientation and frame count from the header, without decoding pixels: cheap enough for
    /// every file of an import. A JPEG, PNG or GIF cut short is <see cref="SkipReason.Truncated"/> here already (its
    /// end marker is missing; checking the last bytes costs well under a millisecond, a JPEG with data after its
    /// image such as a motion photo is read through once); other formats show a cut when decoded.
    /// </summary>
    public static async Task<PhotoResult<PhotoInfo>> ReadInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        cancellationToken.ThrowIfCancellationRequested();
        var stage = PhotoFailures.Stage.Open;
        byte[]? header = null;
        try
        {
            using var file = OpenFile(path);
            var length = RandomAccess.GetLength(file);
            if (length == 0)
            {
                return PhotoResult<PhotoInfo>.Failed(Empty(path));
            }

            header = ReadHeader(file);
            if (!PhotoStructure.EndsCompletely(header, length, ReadAtHandle(file)))
            {
                return PhotoResult<PhotoInfo>.Failed(Truncated(path));
            }

            // .NET's stream adapter: slower per read than Windows' own file stream, which a header does not
            // notice, and it never maps the file, so nothing holds the file once this returns.
            using var stream = new FileStream(file, FileAccess.Read, FileBufferSize).AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            stage = PhotoFailures.Stage.Decode;
            return await ReadInfoAsync(path, decoder, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
        {
            return PhotoResult<PhotoInfo>.Failed(PhotoFailures.Classify(exception, path, stage, header: header));
        }
    }

    /// <summary>
    /// Decodes the photo to BGRA8 premultiplied, orientation applied, scaled during decode as
    /// <paramref name="options"/> asks (Fant filtering), never upscaled.
    /// </summary>
    /// <remarks>
    /// The file is copied whole into Windows' in-memory stream and closed before decoding. Windows' own file
    /// stream maps the file, and the mapping lasts until a garbage collection finalizes the decoder, all that
    /// time keeping other programs from rewriting the file in place; .NET's stream adapter makes every read an
    /// async call, about 90 ms more per 24 MP JPEG. The copy goes through a 1 MB buffer and costs a few
    /// milliseconds; reading the file into one pooled array instead kept a 16 MB array per thread in the pool and
    /// raised a 1,000-frame 4K export's peak working set from about 540 to 900 MB.
    /// </remarks>
    public static async Task<PhotoResult<DecodedPhoto>> DecodeAsync(
        string path, PhotoDecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        options ??= PhotoDecodeOptions.FullSize;
        cancellationToken.ThrowIfCancellationRequested();
        var stage = PhotoFailures.Stage.Open;
        byte[]? header = null;
        try
        {
            using var file = OpenFile(path);
            var length = RandomAccess.GetLength(file);
            if (length == 0)
            {
                return PhotoResult<DecodedPhoto>.Failed(Empty(path));
            }

            header = ReadHeader(file);
            IRandomAccessStream stream;
            var inMemory = length <= MaxInMemoryFileBytes;
            if (inMemory)
            {
                var (copy, failure) = await CopyToMemoryAsync(path, file, header, length, cancellationToken).ConfigureAwait(false);
                file.Dispose();
                if (failure is not null)
                {
                    return PhotoResult<DecodedPhoto>.Failed(failure);
                }

                stream = copy!;
            }
            else
            {
                // Too large to copy: read through .NET's adapter, which does not map the file either.
                if (!PhotoStructure.EndsCompletely(header, length, ReadAtHandle(file)))
                {
                    return PhotoResult<DecodedPhoto>.Failed(Truncated(path));
                }

                stream = new FileStream(file, FileAccess.Read, FileBufferSize).AsRandomAccessStream();
            }

            using (stream)
            {
                var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
                stage = PhotoFailures.Stage.Decode;
                return await DecodeAsync(path, decoder, inMemory ? stream : null, options, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
        {
            return PhotoResult<DecodedPhoto>.Failed(PhotoFailures.Classify(exception, path, stage, header: header));
        }
    }

    /// <summary>
    /// The factor a decode scales <paramref name="size"/> (oriented) by: the cover scale of
    /// <see cref="PhotoDecodeOptions.CoverSize"/> or the contain scale of <see cref="PhotoDecodeOptions.ContainSize"/>,
    /// then <see cref="PhotoDecodeOptions.MaxSide"/>, never above 1.
    /// </summary>
    public static double Scale(PixelSize size, PhotoDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var scale = 1.0;
        if (options.CoverSize is { } cover)
        {
            scale = Math.Min(scale, Math.Max((double)cover.Width / size.Width, (double)cover.Height / size.Height));
        }

        if (options.ContainSize is { } contain)
        {
            scale = Math.Min(scale, Math.Min((double)contain.Width / size.Width, (double)contain.Height / size.Height));
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

    /// <summary>
    /// A side scaled by <paramref name="scale"/> to cover <paramref name="coverSide"/>, the output's side it meets.
    /// The side that sets the cover scale comes out exactly <paramref name="coverSide"/>; the other is rounded to the
    /// nearest whole number with the parity of its cover side, never below it, so the centred crop
    /// (<see cref="Rendering.FrameRenderer"/>) starts on a whole pixel and copies the pixels one to one: an odd margin
    /// would start it half a pixel in, and the drawing would resample every pixel, softening the frame
    /// (4288x2848 covering 3840x2160: 2550 rows, cropped from row 195, rather than 2551 from 195.5).
    /// </summary>
    public static int Scaled(int side, double scale, int coverSide)
    {
        var exact = side * scale;
        if (scale >= 1 || exact < coverSide - 1e-6)
        {
            // Not scaled, or capped below the cover (MaxSide): the drawing scales it anyway.
            return Scaled(side, scale);
        }

        var rounded = coverSide + (2 * (int)Math.Round((exact - coverSide) / 2));
        return Math.Min(Math.Max(rounded, coverSide), side);
    }

    /// <summary>
    /// A side scaled by <paramref name="scale"/> to fit inside <paramref name="containSide"/>, the output's side it
    /// meets. The side that sets the contain scale comes out exactly <paramref name="containSide"/>; the other is
    /// the whole number within a pixel of the exact size that has the parity of its output side, never a whole pixel
    /// above it, so the letterbox bars split evenly and the drawing (<see cref="Rendering.FrameRenderer"/>) copies
    /// the pixels one to one.
    /// </summary>
    public static int ScaledToContain(int side, double scale, int containSide)
    {
        if (scale >= 1)
        {
            return side;
        }

        var exact = side * scale;
        if (Math.Abs(exact - containSide) < 1e-6)
        {
            return containSide;
        }

        var floor = (int)Math.Floor(exact + 1e-6);
        var sameParity = (floor - containSide) % 2 == 0;
        var result = Math.Abs(exact - floor) < 1e-6
            ? (sameParity ? floor : floor - 1) // whole already: keep it, or one less to match the parity
            : (sameParity ? floor : floor + 1); // between floor and floor + 1: one of them has the parity
        return Math.Clamp(result, 1, Math.Min(containSide, side));
    }

    // colourSource: the photo's bytes, for reading its embedded colour; null converts as WinRT does, at the source.
    private static async Task<PhotoResult<DecodedPhoto>> DecodeAsync(
        string path, BitmapDecoder decoder, IRandomAccessStream? colourSource, PhotoDecodeOptions options, CancellationToken cancellationToken)
    {
        var read = await ReadInfoAsync(path, decoder, cancellationToken).ConfigureAwait(false);
        if (!read.Succeeded)
        {
            return PhotoResult<DecodedPhoto>.Failed(read.Failure);
        }

        var info = read.Value;
        var scale = Scale(info.Size, options);
        var (width, height) = (Scaled(info.StoredSize.Width, scale), Scaled(info.StoredSize.Height, scale));
        if (options.CoverSize is { } cover)
        {
            // The stored sides meet the output's sides as the photo is turned (orientations 5 to 8 swap them).
            var turned = info.Orientation is >= 5 and <= 8;
            (width, height) = (
                Scaled(info.StoredSize.Width, scale, turned ? cover.Height : cover.Width),
                Scaled(info.StoredSize.Height, scale, turned ? cover.Width : cover.Height));
        }
        else if (options.ContainSize is { } contain)
        {
            var turned = info.Orientation is >= 5 and <= 8;
            (width, height) = (
                ScaledToContain(info.StoredSize.Width, scale, turned ? contain.Height : contain.Width),
                ScaledToContain(info.StoredSize.Height, scale, turned ? contain.Width : contain.Height));
        }

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

        // Colour: WinRT would convert any embedded profile at the stored size, before scaling. An sRGB profile
        // needs nothing; another RGB one is converted after scaling, on a fraction of the pixels.
        var colour = colourSource is null ? ColourPlan.AtSource : PhotoColour.Plan(colourSource);
        var pixels = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                colour.Conversion == ColourConversion.AtSource ? ColorManagementMode.ColorManageToSRgb : ColorManagementMode.DoNotColorManage)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (colour.Conversion == ColourConversion.AfterScaling)
            {
                PhotoColour.ConvertToSrgb(pixels, colour);
            }

            return PhotoResult<DecodedPhoto>.Success(new DecodedPhoto(pixels, info, colour.Conversion));
        }
        catch
        {
            pixels.Dispose();
            throw;
        }
    }

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
    internal static async Task<int> OrientationAsync(BitmapDecoder decoder, CancellationToken cancellationToken)
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
    /// Any form of path opens: long, <c>\\?\</c>, UNC, forward slashes, <c>.</c> and <c>..</c> segments, or
    /// relative to the current folder.
    /// </summary>
    internal static SafeFileHandle OpenFile(string path) =>
        File.OpenHandle(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);

    /// <summary>
    /// The whole file in Windows' in-memory stream, through a small buffer, then checked for a cut through the
    /// same handle up to the length copied; or why not. The file may change while it is read: a file that came
    /// out shorter than the length captured at open is cut.
    /// </summary>
    private static async Task<(IRandomAccessStream? Stream, PhotoFailure? Failure)> CopyToMemoryAsync(
        string path, SafeFileHandle file, byte[] header, long length, CancellationToken cancellationToken)
    {
        var stream = new InMemoryRandomAccessStream { Size = (ulong)length };
        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(CopyBufferSize, length));
        var kept = false;
        try
        {
            long count = 0;
            while (count < length)
            {
                var step = RandomAccess.Read(file, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - count)), count);
                if (step == 0)
                {
                    break;
                }

                await stream.WriteAsync(buffer.AsBuffer(0, step)).AsTask(cancellationToken).ConfigureAwait(false);
                count += step;
            }

            if (count == 0)
            {
                return (null, Empty(path));
            }

            if (count < length || !PhotoStructure.EndsCompletely(header, count, ReadAtHandle(file)))
            {
                return (null, Truncated(path));
            }

            stream.Seek(0);
            kept = true;
            return (stream, null);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (!kept)
            {
                stream.Dispose();
            }
        }
    }

    private static byte[] ReadHeader(SafeFileHandle file)
    {
        var header = new byte[PhotoStructure.HeaderLength];
        var count = RandomAccess.Read(file, header, 0);
        return header[..count];
    }

    private static ReadAt ReadAtHandle(SafeFileHandle file) => (offset, buffer) => RandomAccess.Read(file, buffer, offset);

    private static PhotoFailure Empty(string path) => PhotoFailures.Found(path, SkipReason.EmptyFile, "Open: the file has 0 bytes.");

    private static PhotoFailure Truncated(string path) =>
        PhotoFailures.Found(path, SkipReason.Truncated, "Open: the file ends before its image does (no end marker where its format puts one).");

    internal static bool IsCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
