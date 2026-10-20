using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Text;
using TenLapse.Core.Timing;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible word of the Timing panel (#17), as whole format strings read from <c>Strings/TimingText.resx</c>
/// (#39); the log contexts stay here in English. The panel's numbers
/// (the preset labels, the box and the sentences about what was typed) are written in the user's culture, so a
/// German or French user reads and types a decimal comma, and so does the readout, which
/// <see cref="ProjectText.TimingReadoutParts"/> writes in the panel's culture.
/// </summary>
public static class TimingText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.TimingText", typeof(TimingText).Assembly);

    /// <summary>The section's heading.</summary>
    public static string Heading => _strings.Get();

    /// <summary>Name of the segmented control that picks the mode.</summary>
    public static string ModesName => _strings.Get();

    /// <summary>The first mode: frames per second.</summary>
    public static string ModePerSecond => _strings.Get();

    /// <summary>The second mode: a target length.</summary>
    public static string ModeLength => _strings.Get();

    /// <summary>The third mode: seconds for each photo.</summary>
    public static string ModePerPhoto => _strings.Get();

    /// <summary>Name of the presets in Per second mode.</summary>
    public static string PresetsFramesPerSecond => _strings.Get();

    /// <summary>Name of the presets in Length mode.</summary>
    public static string PresetsLength => _strings.Get();

    /// <summary>Name of the presets in Per photo mode.</summary>
    public static string PresetsSecondsPerPhoto => _strings.Get();

    /// <summary>The caption above the box in Per second mode (it is also the box's name for a screen reader).</summary>
    public static string CustomFramesPerSecond => _strings.Get();

    /// <summary>The caption above the box in Length mode.</summary>
    public static string CustomLength => _strings.Get();

    /// <summary>The caption above the box in Per photo mode.</summary>
    public static string CustomSecondsPerPhoto => _strings.Get();

    /// <summary>
    /// The unit after the box in Per photo mode ("s per photo"). The other two modes show Core's
    /// <see cref="TimingWords.FramesPerSecond"/> and <see cref="TimingWords.Second"/>, the units the box also accepts, so
    /// the label and what may be typed cannot disagree.
    /// </summary>
    public static string UnitSecondsPerPhoto => _strings.Get();

    /// <summary>The Per second box was left with something that is not a number. {0}: the lowest rate, {1}: the highest, {2}: the NTSC example ("29.97").</summary>
    public static string NotANumberFramesPerSecond => _strings.Get();

    /// <summary>The Length box was left with something that is not a length. {0}: the shortest length ("0.1 s"), {1}: the longest.</summary>
    public static string NotANumberLength => _strings.Get();

    /// <summary>The Per photo box was left with something that is not a number. {0}: the shortest time, {1}: the longest.</summary>
    public static string NotANumberSecondsPerPhoto => _strings.Get();

    /// <summary>A frame rate outside the range was typed. {0}: what was typed, {1}: the value now set ("120 fps"), {2}: the lowest, {3}: the highest.</summary>
    public static string ClampedFramesPerSecond => _strings.Get();

    /// <summary>A length outside the range was typed. {0}: what was typed, {1}: the value now set ("0.1 s"), {2}: the shortest, {3}: the longest.</summary>
    public static string ClampedLength => _strings.Get();

    /// <summary>A time per photo outside the range was typed. {0}: what was typed, {1}: the value now set, {2}: the shortest, {3}: the longest.</summary>
    public static string ClampedSecondsPerPhoto => _strings.Get();

    /// <summary>A frame rate typed in digits became the exact NTSC rate. {0}: the rate ("29.97 fps"), {1}: its numerator, {2}: its denominator.</summary>
    public static string NtscRate => _strings.Get();

    /// <summary>One separator followed by exactly three digits was typed ("1,000"); it is refused rather than guessed. {0}: one and a half in the user's culture ("1.5").</summary>
    public static string ThousandsSeparator => _strings.Get();

    /// <summary>More decimals were typed than the box keeps. {0}: the value kept ("37.48 fps").</summary>
    public static string Rounded => _strings.Get();

    /// <summary>
    /// Length mode, the photos cannot play in the target even at the fastest speed. {0}: the target ("30 s"), {1}: the fastest
    /// speed ("120 fps"), {2}: the length the video has instead.
    /// </summary>
    public static string LengthTooShort => _strings.Get();

    /// <summary>
    /// Length mode, the photos cannot fill the target even at the slowest speed. {0}: the target, {1}: the slowest speed
    /// ("10 s per photo"), {2}: the length the video has instead.
    /// </summary>
    public static string LengthTooLong => _strings.Get();

    /// <summary>Log context: a speed could not be written to the project.</summary>
    public static string ChangeFailed => "Changing the speed failed.";

    /// <summary>Log context: the panel could not follow a change of the project.</summary>
    public static string RefreshFailed => "The timing panel could not update.";

    /// <summary>Fills a format string in <paramref name="culture"/>.</summary>
    internal static string Format(CultureInfo culture, string format, params object[] values) =>
        string.Format(culture, format, values);
}
