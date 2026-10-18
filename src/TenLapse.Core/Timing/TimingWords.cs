namespace TenLapse.Core.Timing;

/// <summary>
/// Every user-visible word the timing formatters print, in one place so #39 can move them to
/// resource files. They are English for now. <see cref="TimingFormat"/> joins them to numbers
/// with a space and writes numbers with the invariant culture (a dot as decimal separator);
/// localization changes both there.
/// </summary>
public static class TimingWords
{
    /// <summary>Seconds, as in "24.7 s".</summary>
    public static string Second => "s";

    /// <summary>Minutes, as in "1 min 12 s".</summary>
    public static string Minute => "min";

    /// <summary>Hours, as in "1 h 02 min".</summary>
    public static string Hour => "h";

    /// <summary>Frames per second, as in "30 fps".</summary>
    public static string FramesPerSecond => "fps";

    /// <summary>The per-photo suffix, as in "2 s per photo".</summary>
    public static string PerPhoto => "per photo";
}
