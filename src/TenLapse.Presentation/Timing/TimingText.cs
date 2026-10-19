using System.Globalization;
using TenLapse.Core.Projects;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible word of the Timing panel (#17), as whole format strings for #39. The panel's numbers
/// (the preset labels, the box and the sentences about what was typed) are written in the user's culture, so a
/// German or French user reads and types a decimal comma, and so does the readout, which
/// <see cref="ProjectText.TimingReadoutParts"/> writes in the panel's culture.
/// </summary>
public static class TimingText
{
    /// <summary>The section's heading.</summary>
    public static string Heading => "Timing";

    /// <summary>Name of the segmented control that picks the mode.</summary>
    public static string ModesName => "Speed mode";

    /// <summary>The first mode: frames per second.</summary>
    public static string ModePerSecond => "Per second";

    /// <summary>The second mode: a target length.</summary>
    public static string ModeLength => "Length";

    /// <summary>The third mode: seconds for each photo.</summary>
    public static string ModePerPhoto => "Per photo";

    /// <summary>Name of the presets in Per second mode.</summary>
    public static string PresetsFramesPerSecond => "Frames per second";

    /// <summary>Name of the presets in Length mode.</summary>
    public static string PresetsLength => "Video length";

    /// <summary>Name of the presets in Per photo mode.</summary>
    public static string PresetsSecondsPerPhoto => "Seconds per photo";

    /// <summary>The caption above the box in Per second mode (it is also the box's name for a screen reader).</summary>
    public static string CustomFramesPerSecond => "Custom frames per second";

    /// <summary>The caption above the box in Length mode.</summary>
    public static string CustomLength => "Custom length in seconds";

    /// <summary>The caption above the box in Per photo mode.</summary>
    public static string CustomSecondsPerPhoto => "Custom seconds per photo";

    /// <summary>The unit after the box in Per second mode.</summary>
    public static string UnitFramesPerSecond => "fps";

    /// <summary>The unit after the box in Length mode.</summary>
    public static string UnitLength => "s";

    /// <summary>The unit after the box in Per photo mode.</summary>
    public static string UnitSecondsPerPhoto => "s per photo";

    /// <summary>A preset of the Length mode. {0}: the seconds ("30").</summary>
    public static string LengthPreset => "{0} s";

    /// <summary>The bar's left end: no time.</summary>
    public static string BarStart => "0 s";

    /// <summary>The bar's middle: half a minute.</summary>
    public static string BarMiddle => "30 s";

    /// <summary>The bar's right end: a minute, the longest clip it can show.</summary>
    public static string BarEnd => "60 s";

    /// <summary>The Per second box was left with something that is not a number. {0}: the lowest rate, {1}: the highest, {2}: the NTSC example ("29.97").</summary>
    public static string NotANumberFramesPerSecond => "Type a frame rate from {0} to {1} fps, like 24 or {2}.";

    /// <summary>The Length box was left with something that is not a length. {0}: the shortest length ("0.1 s"), {1}: the longest.</summary>
    public static string NotANumberLength => "Type a length in seconds, like 30, or as minutes and seconds, like 1:30. It can be from {0} to {1}.";

    /// <summary>The Per photo box was left with something that is not a number. {0}: the shortest time, {1}: the longest.</summary>
    public static string NotANumberSecondsPerPhoto => "Type how many seconds each photo stays, from {0} to {1}.";

    /// <summary>A frame rate outside the range was typed. {0}: what was typed, {1}: the value now set ("120 fps"), {2}: the lowest, {3}: the highest.</summary>
    public static string ClampedFramesPerSecond => "Frame rates run from {2} to {3} fps, so {0} is now {1}.";

    /// <summary>A length outside the range was typed. {0}: what was typed, {1}: the value now set ("0.1 s"), {2}: the shortest, {3}: the longest.</summary>
    public static string ClampedLength => "A video can be from {2} to {3} long, so {0} is now {1}.";

    /// <summary>A time per photo outside the range was typed. {0}: what was typed, {1}: the value now set, {2}: the shortest, {3}: the longest.</summary>
    public static string ClampedSecondsPerPhoto => "A photo can stay from {2} to {3}, so {0} is now {1}.";

    /// <summary>A frame rate typed in digits became the exact NTSC rate. {0}: the rate ("29.97 fps"), {1}: its numerator, {2}: its denominator.</summary>
    public static string NtscRate => "Using the NTSC rate {0} ({1}/{2}).";

    /// <summary>One separator followed by exactly three digits was typed ("1,000"); it is refused rather than guessed. {0}: one and a half in the user's culture ("1.5").</summary>
    public static string ThousandsSeparator => "Type 1000 without a separator, or {0} for one and a half.";

    /// <summary>More decimals were typed than the box keeps. {0}: the value kept ("37.48 fps").</summary>
    public static string Rounded => "Rounded to {0}.";

    /// <summary>
    /// Length mode, the photos cannot play in the target even at the fastest speed. {0}: the target ("30 s"), {1}: the fastest
    /// speed ("120 fps"), {2}: the length the video has instead.
    /// </summary>
    public static string LengthTooShort => "Your photos can't play in {0}: the fastest speed is {1}, so the video is {2} long.";

    /// <summary>
    /// Length mode, the photos cannot fill the target even at the slowest speed. {0}: the target, {1}: the slowest speed
    /// ("10 s per photo"), {2}: the length the video has instead.
    /// </summary>
    public static string LengthTooLong => "Your photos can't fill {0}: the slowest speed is {1}, so the video is {2} long.";

    /// <summary>Log context: a speed could not be written to the project.</summary>
    public static string ChangeFailed => "Changing the speed failed.";

    /// <summary>Log context: the panel could not follow a change of the project.</summary>
    public static string RefreshFailed => "The timing panel could not update.";

    /// <summary>Fills a format string in <paramref name="culture"/>.</summary>
    internal static string Format(CultureInfo culture, string format, params object[] values) =>
        string.Format(culture, format, values);
}
