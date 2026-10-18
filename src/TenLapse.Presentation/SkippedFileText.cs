using TenLapse.Core.Projects;

namespace TenLapse.Presentation;

/// <summary>
/// The words for photos that could not be read (#13): the InfoBar sentence and one short reason per file, as the
/// prototype's list shows them ("IMG_4402.HEIC — Damaged file: it ends early"). Same rules as
/// <see cref="ProjectText"/>: whole strings, singular and plural separate, for #39 to move to resource files.
/// </summary>
public static class SkippedFileText
{
    /// <summary>The InfoBar for one skipped photo.</summary>
    public static string SkippedOne => "1 photo couldn't be read and was skipped.";

    /// <summary>The InfoBar for several skipped photos. {0} the count ("3", "1,204").</summary>
    public static string SkippedMany => "{0} photos couldn't be read and were skipped.";

    /// <summary><see cref="SkipReason.UnsupportedFormat"/>.</summary>
    public static string UnsupportedFormat => "Not a photo format Windows can read";

    /// <summary><see cref="SkipReason.DecodeFailed"/>.</summary>
    public static string DecodeFailed => "Damaged file";

    /// <summary><see cref="SkipReason.AccessDenied"/>.</summary>
    public static string AccessDenied => "No permission to open it";

    /// <summary><see cref="SkipReason.FileNotFound"/>.</summary>
    public static string FileNotFound => "No longer there: moved or deleted";

    /// <summary><see cref="SkipReason.Other"/>.</summary>
    public static string Other => "Couldn't be read";

    /// <summary><see cref="SkipReason.EmptyFile"/>, the prototype's words.</summary>
    public static string EmptyFile => "Empty file (0 bytes)";

    /// <summary><see cref="SkipReason.Truncated"/>, the prototype's words.</summary>
    public static string Truncated => "Damaged file: it ends early";

    /// <summary><see cref="SkipReason.NeedsExtension"/>; #14's own InfoBar names the extension and opens the Store.</summary>
    public static string NeedsExtension => "Needs a free extension from the Microsoft Store";

    /// <summary><see cref="SkipReason.FileInUse"/>.</summary>
    public static string FileInUse => "In use by another app";

    /// <summary><see cref="SkipReason.ImageTooLarge"/>.</summary>
    public static string ImageTooLarge => "Too large to open";

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
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown skip reason."),
    };
}
