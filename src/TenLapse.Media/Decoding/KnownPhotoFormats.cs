namespace TenLapse.Media.Decoding;

/// <summary>A free Windows extension from the Microsoft Store that adds a WIC decoder (#14 offers it).</summary>
/// <param name="Name">The Store name, as the user will find it ("HEIF Image Extensions").</param>
/// <param name="StoreProductId">
/// The Store product ID for <c>ms-windows-store://pdp/?ProductId=…</c>, or null where the issues gave none to
/// verify (#14 checks the two it lists before use).
/// </param>
public sealed record PhotoCodecExtension(string Name, string? StoreProductId);

/// <summary>A photo format 10Lapse knows by name, whether or not this PC can read it.</summary>
/// <param name="Name">The friendly name ("HEIF", "Camera RAW").</param>
/// <param name="Extensions">
/// Its file extensions, lower case with the dot (".heic"), the primary one first: the format counts as available
/// when a decoder reads that one (<see cref="KnownPhotoFormats.Missing"/>).
/// </param>
/// <param name="Extension">The Store extension that adds its decoder, or null when Windows has it built in.</param>
public sealed record KnownPhotoFormat(string Name, IReadOnlyList<string> Extensions, PhotoCodecExtension? Extension)
{
    /// <summary>True when <paramref name="extension"/> (with or without the dot, any case) is one of this format's.</summary>
    public bool HasExtension(string extension) =>
        Extensions.Contains(PhotoCodecs.NormalizeExtension(extension), StringComparer.Ordinal);
}

/// <summary>
/// The formats the issue names (#13) with their friendly names and, for those Windows does not read out of the
/// box, the Store extension that adds them. What this PC can actually read comes from <see cref="PhotoCodecs"/>,
/// never from this table: it only names formats and their fix.
/// </summary>
public static class KnownPhotoFormats
{
    /// <summary>HEIF Image Extensions: HEIC and HEIF photos (the iPhone default). Decoding HEVC-coded images also needs an HEVC decoder.</summary>
    public static readonly PhotoCodecExtension HeifExtension = new("HEIF Image Extensions", "9PMMSR1CGPWG");

    /// <summary>Raw Image Extension: camera RAW files.</summary>
    public static readonly PhotoCodecExtension RawExtension = new("Raw Image Extension", "9NCTDW2W1BH8");

    /// <summary>The HEIF entry (also in <see cref="All"/>).</summary>
    public static readonly KnownPhotoFormat Heif = new("HEIF", [".heic", ".heif", ".hif"], HeifExtension);

    /// <summary>The camera RAW entry (also in <see cref="All"/>).</summary>
    public static readonly KnownPhotoFormat Raw = new(
        "Camera RAW",
        [
            ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng", ".orf", ".ori", ".rw2", ".rwl",
            ".raf", ".pef", ".srw", ".3fr", ".erf", ".kdc", ".dcr", ".mrw", ".mef", ".mos", ".iiq", ".x3f",
        ],
        RawExtension);

    /// <summary>Every known format, the built-in ones first.</summary>
    public static IReadOnlyList<KnownPhotoFormat> All { get; } =
    [
        new("JPEG", [".jpg", ".jpeg", ".jpe", ".jfif"], null),
        new("PNG", [".png"], null),
        new("TIFF", [".tif", ".tiff"], null),
        new("BMP", [".bmp", ".dib"], null),
        new("GIF", [".gif"], null),
        new("JPEG XR", [".jxr", ".wdp", ".hdp"], null),
        new("WebP", [".webp"], new PhotoCodecExtension("Webp Image Extensions", null)),
        Heif,
        new("AVIF", [".avif"], new PhotoCodecExtension("AV1 Video Extension", null)),
        new("JPEG XL", [".jxl"], new PhotoCodecExtension("JPEG XL Image Extension", null)),
        Raw,
    ];

    /// <summary>The known format with this extension (with or without the dot, any case), or null.</summary>
    public static KnownPhotoFormat? Find(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var normalized = PhotoCodecs.NormalizeExtension(extension);
        return All.FirstOrDefault(format => format.Extensions.Contains(normalized, StringComparer.Ordinal));
    }

    /// <summary>The known format of a file, by its extension, or null.</summary>
    public static KnownPhotoFormat? ForPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Find(Path.GetExtension(path));
    }

    /// <summary>
    /// The known formats whose primary extension (the first) <paramref name="readable"/> does not contain: on this
    /// PC, the ones #14 explains how to add (<see cref="PhotoCodecs.MissingFormats"/> passes the installed
    /// decoders' list). The primary one, not any: Windows reads DNG on its own, so ".dng" says nothing about the
    /// Raw Image Extension, and its JPEG XR decoder does not register ".hdp".
    /// </summary>
    public static IReadOnlyList<KnownPhotoFormat> Missing(IReadOnlySet<string> readable)
    {
        ArgumentNullException.ThrowIfNull(readable);
        return [.. All.Where(format => !readable.Contains(format.Extensions[0]))];
    }
}
