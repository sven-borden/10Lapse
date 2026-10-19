using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Presentation.Services;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only stand-in for the PC's photo extensions (this folder is not built in Release), to look at #14's
/// InfoBars on a PC that has them all: <c>--missing-codec=heif,hevc,raw</c> raises the InfoBars an import of
/// 12 HEIC photos and 3 RAW photos would, as if those extensions were missing. "heif" alone leaves HEVC to what
/// this PC really has; "heif,hevc" says both are missing (one InfoBar, then a second after its button). The Store
/// button still opens the real Store page.
/// </summary>
internal sealed class DebugMissingCodecs(ICodecExtensions inner, IReadOnlySet<string> missing) : ICodecExtensions
{
    private static readonly string[] _names = ["heif", "hevc", "raw"];

    /// <inheritdoc />
    public void Refresh() => inner.Refresh();

    /// <inheritdoc />
    public bool IsInstalled(PhotoCodecExtension extension) => !missing.Contains(Name(extension)) && inner.IsInstalled(extension);

    /// <inheritdoc />
    public Task<bool> OpenStorePageAsync(PhotoCodecExtension extension) => inner.OpenStorePageAsync(extension);

    /// <summary>The names the switch asks for, or null when it is not given.</summary>
    public static IReadOnlySet<string>? FromSwitches(string[] args)
    {
        const string prefix = "--missing-codec=";
        var value = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
        if (value is null)
        {
            return null;
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.ToLowerInvariant())
            .Where(_names.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The skipped files an import of 12 HEIC and 3 RAW photos would report with these extensions missing.</summary>
    public static IReadOnlyList<SkippedFile> SampleSkips(IReadOnlySet<string> missing)
    {
        List<SkippedFile> files = [];
        if (missing.Contains("heif"))
        {
            // A PC without the HEIF decoder: the file never reaches a decoder, so HEVC is asked of the PC.
            files.AddRange(Enumerable.Range(1, 12).Select(i => Skip($@"C:\10Lapse debug samples\Harbor sunset\IMG_{i:0000}.HEIC", KnownPhotoFormats.Heif, KnownPhotoFormats.HeifExtension)));
        }
        else if (missing.Contains("hevc"))
        {
            files.AddRange(Enumerable.Range(1, 12).Select(i => Skip($@"C:\10Lapse debug samples\Harbor sunset\IMG_{i:0000}.HEIC", KnownPhotoFormats.Heif, KnownPhotoFormats.HevcExtension)));
        }

        if (missing.Contains("raw"))
        {
            files.AddRange(Enumerable.Range(1, 3).Select(i => Skip($@"C:\10Lapse debug samples\Harbor sunset\DSC_{i:0000}.NEF", KnownPhotoFormats.Raw, KnownPhotoFormats.RawExtension)));
        }

        return files;
    }

    private static SkippedFile Skip(string path, KnownPhotoFormat format, PhotoCodecExtension missing)
    {
        PhotoCodecExtension[] needed = format.Codec is { } codec && missing == format.Container ? [missing, codec] : [missing];
        return new SkippedFile(path, SkipReason.NeedsExtension, "Debug: made up by --missing-codec.", needed);
    }

    private static string Name(PhotoCodecExtension extension) =>
        extension == KnownPhotoFormats.HeifExtension ? "heif"
        : extension == KnownPhotoFormats.HevcExtension ? "hevc"
        : extension == KnownPhotoFormats.RawExtension ? "raw"
        : string.Empty;
}
