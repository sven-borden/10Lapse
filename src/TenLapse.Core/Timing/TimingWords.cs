using TenLapse.Core.Text;

namespace TenLapse.Core.Timing;

/// <summary>
/// Every user-visible word the timing formatters print, read from <c>Strings/CoreText.resx</c> (#39).
/// <see cref="TimingFormat"/> joins them to numbers with a space and writes numbers with the invariant culture
/// (a dot as decimal separator) unless it is given a culture.
/// </summary>
public static class TimingWords
{
    /// <summary>
    /// Seconds, as in "24.7 s". Also the unit the Timing box accepts when the user types a duration ("2 s",
    /// <c>TimingInput</c>), so keep it short and easy to type.
    /// </summary>
    public static string Second => CoreText.Strings.Get();

    /// <summary>Minutes, as in "1 min 12 s".</summary>
    public static string Minute => CoreText.Strings.Get();

    /// <summary>Hours, as in "1 h 02 min".</summary>
    public static string Hour => CoreText.Strings.Get();

    /// <summary>
    /// Frames per second, as in "30 fps". Also the unit the Timing box accepts when the user types a rate ("30 fps",
    /// <c>TimingInput</c>), so keep it short and easy to type.
    /// </summary>
    public static string FramesPerSecond => CoreText.Strings.Get();

    /// <summary>The per-photo suffix, as in "2 s per photo".</summary>
    public static string PerPhoto => CoreText.Strings.Get();
}
