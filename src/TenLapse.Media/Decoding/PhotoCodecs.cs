using System.Collections.Frozen;
using TenLapse.Core.Projects;
using TenLapse.Media.Encoding;
using Windows.Graphics.Imaging;

namespace TenLapse.Media.Decoding;

/// <summary>One WIC decoder installed on this PC.</summary>
/// <param name="Name">Its friendly name ("Microsoft HEIF Decoder").</param>
/// <param name="CodecId">Its WIC codec ID (compare with <see cref="BitmapDecoder.HeifDecoderId"/> and the others).</param>
/// <param name="Extensions">The file extensions it registers, lower case with the dot.</param>
/// <param name="MimeTypes">The MIME types it registers, lower case.</param>
public sealed record PhotoDecoderInfo(string Name, Guid CodecId, IReadOnlyList<string> Extensions, IReadOnlyList<string> MimeTypes);

/// <summary>
/// What this PC can read: the WIC decoders installed now, as Windows lists them. Built-in ones (JPEG, PNG, TIFF,
/// BMP, GIF, ICO, JPEG XR, DDS) and the ones Store extensions add (HEIF, WebP, RAW, JPEG XL). Import (#12) filters
/// by <see cref="IsReadable"/>; #14 explains <see cref="MissingFormats"/>.
/// </summary>
/// <remarks>
/// The list is read when first needed and kept until <see cref="Refresh"/>; an import refreshes it first (#14), so an
/// extension the user installs from the Store while 10Lapse runs is found by the next import, with no restart.
/// A decoder can be listed and still fail on a file, such as a HEIC photo when the HEVC decoder it needs is missing;
/// <see cref="PhotoDecoder"/> reports that per file.
/// </remarks>
public static class PhotoCodecs
{
    private static Lazy<Snapshot> _installed = new(Read);

    /// <summary>The installed decoders, in the order Windows lists them.</summary>
    public static IReadOnlyList<PhotoDecoderInfo> Decoders => Volatile.Read(ref _installed).Value.Decoders;

    /// <summary>
    /// Forgets the decoder list, so the next use asks Windows again. Called when the user retries an import: a
    /// "no decoder" answer is never kept for the session (#14).
    /// </summary>
    public static void Refresh() => Volatile.Write(ref _installed, new Lazy<Snapshot>(Read));

    /// <summary>
    /// True when this PC has what <paramref name="extension"/> adds, asked of Windows now (never remembered): for a
    /// decoder extension (HEIF, RAW, WebP, JPEG XL), a WIC decoder for its format's first extension; for the HEVC Video
    /// Extensions, a Media Foundation HEVC decoder. True for anything it cannot tell (the AV1 codec): a verdict of
    /// "missing" must be certain, because it sends the user to the Store.
    /// </summary>
    public static bool IsInstalled(PhotoCodecExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var format = KnownPhotoFormats.All.FirstOrDefault(known => known.Container == extension);
        if (format is not null)
        {
            return BitmapDecoder.GetDecoderInformationEnumerator()
                .Any(info => info.FileExtensions.Any(e => string.Equals(NormalizeExtension(e), format.Extensions[0], StringComparison.Ordinal)));
        }

        if (extension == KnownPhotoFormats.HevcExtension)
        {
            try
            {
                return EncoderCatalog.FindDecoders(VideoFormat.Mp4Hevc).Count > 0;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return true;
            }
        }

        return true;
    }

    /// <summary>
    /// The file extensions 10Lapse reads as photos on this PC, lower case with the dot: those of the known photo
    /// formats (<see cref="KnownPhotoFormats.All"/>) that an installed decoder registers. Not every extension a
    /// decoder registers: icons (.ico), cursors (.cur), textures (.dds) and RLE bitmaps (.rle) are no photos.
    /// </summary>
    public static IReadOnlySet<string> Extensions => Volatile.Read(ref _installed).Value.Extensions;

    /// <summary>The known formats (<see cref="KnownPhotoFormats.All"/>) no installed decoder reads, with the extension that adds each.</summary>
    public static IReadOnlyList<KnownPhotoFormat> MissingFormats => KnownPhotoFormats.Missing(Extensions);

    /// <summary>True when this file's extension (any case) is in <see cref="Extensions"/>: a known photo format an installed decoder reads. A file with no extension is not.</summary>
    public static bool IsReadable(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        return extension.Length > 1 && Extensions.Contains(NormalizeExtension(extension));
    }

    /// <summary>The installed decoder that registers this extension (with or without the dot, any case), or null.</summary>
    public static PhotoDecoderInfo? DecoderFor(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var normalized = NormalizeExtension(extension);
        return Decoders.FirstOrDefault(decoder => decoder.Extensions.Contains(normalized, StringComparer.Ordinal));
    }

    /// <summary>".JPG", "jpg" and ".jpg" all become ".jpg".</summary>
    public static string NormalizeExtension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var trimmed = extension.Trim().ToLowerInvariant();
        return trimmed.Length == 0 || trimmed[0] == '.' ? trimmed : "." + trimmed;
    }

    private static Snapshot Read()
    {
        var decoders = BitmapDecoder.GetDecoderInformationEnumerator()
            .Select(info => new PhotoDecoderInfo(
                info.FriendlyName,
                info.CodecId,
                [.. info.FileExtensions.Select(NormalizeExtension).Where(e => e.Length > 1).Distinct()],
                [.. info.MimeTypes.Select(m => m.Trim().ToLowerInvariant()).Where(m => m.Length > 0).Distinct()]))
            .ToArray();
        var known = KnownPhotoFormats.All.SelectMany(format => format.Extensions).ToHashSet(StringComparer.Ordinal);
        return new Snapshot(decoders, decoders.SelectMany(d => d.Extensions).Where(known.Contains).ToFrozenSet(StringComparer.Ordinal));
    }

    private sealed record Snapshot(IReadOnlyList<PhotoDecoderInfo> Decoders, FrozenSet<string> Extensions);
}
