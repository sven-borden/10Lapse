using System.Collections.Frozen;
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
/// The list is read once per process and kept: installing an extension while 10Lapse runs takes a restart to show.
/// A decoder can be listed and still fail on a file, such as a HEIC photo when the HEVC decoder it needs is missing;
/// <see cref="PhotoDecoder"/> reports that per file.
/// </remarks>
public static class PhotoCodecs
{
    private static readonly Lazy<Snapshot> _installed = new(Read);

    /// <summary>The installed decoders, in the order Windows lists them.</summary>
    public static IReadOnlyList<PhotoDecoderInfo> Decoders => _installed.Value.Decoders;

    /// <summary>Every file extension an installed decoder registers, lower case with the dot.</summary>
    public static IReadOnlySet<string> Extensions => _installed.Value.Extensions;

    /// <summary>The known formats (<see cref="KnownPhotoFormats.All"/>) no installed decoder reads, with the extension that adds each.</summary>
    public static IReadOnlyList<KnownPhotoFormat> MissingFormats => KnownPhotoFormats.Missing(Extensions);

    /// <summary>True when an installed decoder registers this file's extension (any case). A file with no extension is not.</summary>
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
        return new Snapshot(decoders, decoders.SelectMany(d => d.Extensions).ToFrozenSet(StringComparer.Ordinal));
    }

    private sealed record Snapshot(IReadOnlyList<PhotoDecoderInfo> Decoders, FrozenSet<string> Extensions);
}
