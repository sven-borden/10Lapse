using TenLapse.Core.Text;

namespace TenLapse.Media.Decoding;

/// <summary>
/// The names of the Microsoft Store extensions that add photo formats (#14, #114), read from
/// <c>Strings/StoreExtensionText.resx</c>: the user searches the Store for them by name when its page does not open, so
/// each is the exact name the Store shows in the user's language. Their product IDs stay in
/// <see cref="KnownPhotoFormats"/>; they are the same everywhere.
/// </summary>
public static class StoreExtensionText
{
    private static readonly TextResources _strings = new("TenLapse.Media.Strings.StoreExtensionText", typeof(StoreExtensionText).Assembly);

    /// <summary>The HEIF decoder (product 9PMMSR1CGPWG): "HEIF Image Extension".</summary>
    public static string HeifImageExtensions => _strings.Get();

    /// <summary>The HEVC codec (product 9NMZLZ57R3T7), which the Store sells: "HEVC Video Extensions".</summary>
    public static string HevcVideoExtensions => _strings.Get();

    /// <summary>The AV1 codec (product 9MVZQVXJBQ9V): "AV1 Video Extension".</summary>
    public static string Av1VideoExtension => _strings.Get();

    /// <summary>The WebP decoder (product 9PG2DK419DRG): "WebP Image Extension".</summary>
    public static string WebpImageExtensions => _strings.Get();

    /// <summary>The JPEG XL decoder (product 9MZPRTH5C0TB): "JPEG XL Image Extension".</summary>
    public static string JpegXlImageExtension => _strings.Get();

    /// <summary>The camera RAW decoder (product 9NCTDW2W1BH8): "Raw Image Extension".</summary>
    public static string RawImageExtension => _strings.Get();
}
