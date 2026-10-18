namespace TenLapse.Media.Decoding;

/// <summary>A free Windows extension from the Microsoft Store that adds a WIC decoder or a codec one needs (#14 offers it).</summary>
/// <param name="Name">The Store name, as the user will find it ("HEIF Image Extensions").</param>
/// <param name="StoreProductId">The Store product ID for <c>ms-windows-store://pdp/?ProductId=…</c>.</param>
public sealed record PhotoCodecExtension(string Name, string StoreProductId);

/// <summary>A photo format 10Lapse knows by name, whether or not this PC can read it.</summary>
/// <param name="Name">The friendly name ("HEIF", "Camera RAW").</param>
/// <param name="Extensions">
/// Its file extensions, lower case with the dot (".heic"), the primary one first: the format counts as available
/// when a decoder reads that one (<see cref="KnownPhotoFormats.Missing"/>).
/// </param>
/// <param name="Container">The Store extension that adds its decoder (the container format), or null when Windows has it built in.</param>
/// <param name="Codec">
/// The Store extension that adds the codec its decoder needs for the image inside, or null when the decoder needs
/// none: HEIF photos are HEVC-coded and AVIF ones AV1-coded, and the HEIF decoder reads neither on its own.
/// </param>
public sealed record KnownPhotoFormat(string Name, IReadOnlyList<string> Extensions, PhotoCodecExtension? Container, PhotoCodecExtension? Codec = null)
{
    /// <summary>True when <paramref name="extension"/> (with or without the dot, any case) is one of this format's.</summary>
    public bool HasExtension(string extension) =>
        Extensions.Contains(PhotoCodecs.NormalizeExtension(extension), StringComparer.Ordinal);
}

/// <summary>
/// The formats the issue names (#13) with their friendly names and, for those Windows does not read out of the
/// box, the Store extensions that add them. What this PC can actually read comes from <see cref="PhotoCodecs"/>,
/// never from this table: it names formats, their extensions and their fix.
/// </summary>
public static class KnownPhotoFormats
{
    /// <summary>HEIF Image Extensions: the HEIF decoder, which reads HEIC, HEIF and AVIF files (the container).</summary>
    public static readonly PhotoCodecExtension HeifExtension = new("HEIF Image Extensions", "9PMMSR1CGPWG");

    /// <summary>HEVC Video Extensions: the codec of HEIC photos (the iPhone default). A fresh Windows 11 has the HEIF decoder but not this.</summary>
    public static readonly PhotoCodecExtension HevcExtension = new("HEVC Video Extensions", "9NMZLZ57R3T7");

    /// <summary>AV1 Video Extension: the codec of AVIF photos.</summary>
    public static readonly PhotoCodecExtension Av1Extension = new("AV1 Video Extension", "9MVZQVXJBQ9V");

    /// <summary>Webp Image Extensions.</summary>
    public static readonly PhotoCodecExtension WebpExtension = new("Webp Image Extensions", "9PG2DK419DRG");

    /// <summary>JPEG XL Image Extension.</summary>
    public static readonly PhotoCodecExtension JpegXlExtension = new("JPEG XL Image Extension", "9MZPRTH5C0TB");

    /// <summary>Raw Image Extension: camera RAW files.</summary>
    public static readonly PhotoCodecExtension RawExtension = new("Raw Image Extension", "9NCTDW2W1BH8");

    /// <summary>The HEIF entry (also in <see cref="All"/>).</summary>
    public static readonly KnownPhotoFormat Heif = new("HEIF", [".heic", ".heif", ".hif"], HeifExtension, HevcExtension);

    /// <summary>The AVIF entry (also in <see cref="All"/>).</summary>
    public static readonly KnownPhotoFormat Avif = new("AVIF", [".avif"], HeifExtension, Av1Extension);

    /// <summary>The camera RAW entry (also in <see cref="All"/>).</summary>
    public static readonly KnownPhotoFormat Raw = new(
        "Camera RAW",
        [
            ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng", ".orf", ".ori", ".rw2", ".rwl",
            ".raf", ".pef", ".srw", ".3fr", ".erf", ".kdc", ".dcr", ".mrw", ".mef", ".mos", ".iiq", ".x3f",
            ".ari", ".bay", ".cap", ".dcs", ".drf", ".eip", ".fff", ".k25", ".ptx", ".pxn", ".raw",
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
        new("WebP", [".webp"], WebpExtension),
        Heif,
        Avif,
        new("JPEG XL", [".jxl"], JpegXlExtension),
        Raw,
    ];

    // ISO base media file brands (the ftyp box) that say which codec the images inside use.
    private static readonly string[] _hevcBrands = ["heic", "heix", "heim", "heis", "hevc", "hevx", "hevm", "hevs"];
    private static readonly string[] _av1Brands = ["avif", "avis"];

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

    /// <summary>
    /// HEIF or AVIF by the file's first bytes (its ftyp box's brands), whatever its name; null for anything else,
    /// including a HEIF file whose brands do not name a codec.
    /// </summary>
    internal static KnownPhotoFormat? ByContent(ReadOnlySpan<byte> header)
    {
        if (header.Length < 16 || !header[4..8].SequenceEqual("ftyp"u8))
        {
            return null;
        }

        // The major brand at 8, a minor version at 12, then compatible brands up to the end of the box.
        var end = Math.Min(header.Length, (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
        for (var offset = 8; offset + 4 <= end; offset += offset == 8 ? 8 : 4)
        {
            var brand = System.Text.Encoding.ASCII.GetString(header.Slice(offset, 4));
            if (_hevcBrands.Contains(brand, StringComparer.Ordinal))
            {
                return Heif;
            }

            if (_av1Brands.Contains(brand, StringComparer.Ordinal))
            {
                return Avif;
            }
        }

        return null;
    }
}
