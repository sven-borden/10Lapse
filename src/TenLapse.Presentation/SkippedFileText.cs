using TenLapse.Core.Projects;
using TenLapse.Core.Text;

namespace TenLapse.Presentation;

/// <summary>
/// The words for photos that could not be read (#13): the InfoBar sentence and one short reason per file, as the
/// prototype's list shows them ("IMG_4402.HEIC — Damaged file: it ends early"). Same rules as
/// <see cref="ProjectText"/>: whole strings, a plural family for the count, read from <c>Strings/SkippedFileText.resx</c> (#39).
/// </summary>
public static class SkippedFileText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.SkippedFileText", typeof(SkippedFileText).Assembly);

    /// <summary>The InfoBar for skipped photos. {0} the count ("3", "1,204").</summary>
    public static PluralText Skipped => _strings.Plural();

    /// <summary><see cref="SkipReason.UnsupportedFormat"/>.</summary>
    public static string UnsupportedFormat => _strings.Get();

    /// <summary><see cref="SkipReason.DecodeFailed"/>.</summary>
    public static string DecodeFailed => _strings.Get();

    /// <summary><see cref="SkipReason.AccessDenied"/>.</summary>
    public static string AccessDenied => _strings.Get();

    /// <summary><see cref="SkipReason.FileNotFound"/>.</summary>
    public static string FileNotFound => _strings.Get();

    /// <summary><see cref="SkipReason.Other"/>.</summary>
    public static string Other => _strings.Get();

    /// <summary><see cref="SkipReason.EmptyFile"/>, the prototype's words.</summary>
    public static string EmptyFile => _strings.Get();

    /// <summary><see cref="SkipReason.Truncated"/>, the prototype's words.</summary>
    public static string Truncated => _strings.Get();

    /// <summary><see cref="SkipReason.NeedsExtension"/>; #14's own InfoBar names the extension and opens the Store.</summary>
    public static string NeedsExtension => _strings.Get();

    /// <summary><see cref="SkipReason.FileInUse"/>.</summary>
    public static string FileInUse => _strings.Get();

    /// <summary><see cref="SkipReason.ImageTooLarge"/>.</summary>
    public static string ImageTooLarge => _strings.Get();

    /// <summary><see cref="SkipReason.CloudFileOffline"/>.</summary>
    public static string CloudFileOffline => _strings.Get();

    /// <summary><see cref="SkipReason.CloudOnly"/>: the user chose not to download it.</summary>
    public static string CloudOnly => _strings.Get();

    /// <summary>The short reason shown beside the file name.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value <see cref="SkipReason"/> does not define.</exception>
    public static string Reason(SkipReason reason) => reason switch
    {
        SkipReason.UnsupportedFormat => UnsupportedFormat,
        SkipReason.DecodeFailed => DecodeFailed,
        SkipReason.AccessDenied => AccessDenied,
        SkipReason.FileNotFound => FileNotFound,
        SkipReason.Other => Other,
        SkipReason.EmptyFile => EmptyFile,
        SkipReason.Truncated => Truncated,
        SkipReason.NeedsExtension => NeedsExtension,
        SkipReason.FileInUse => FileInUse,
        SkipReason.ImageTooLarge => ImageTooLarge,
        SkipReason.CloudFileOffline => CloudFileOffline,
        SkipReason.CloudOnly => CloudOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown skip reason."),
    };
}
