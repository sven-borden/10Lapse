using TenLapse.Core.Text;

namespace TenLapse.Core.Timing;

/// <summary>
/// Every user-visible word and format the timing formatters print, read from <c>Strings/CoreText.resx</c> (#39, #114):
/// whole format strings ("{0} min {1} s"), so a translation can place the number and the unit as its language does.
/// <see cref="TimingFormat"/> fills them with numbers written in the culture it is given.
/// </summary>
public static class TimingWords
{
    /// <summary>
    /// The symbol for seconds alone ("s"): the unit after the Timing box in Length mode, and the unit the box accepts when
    /// the user types it (<c>TimingInput</c> also accepts the English "s").
    /// </summary>
    public static string Second => CoreText.Strings.Get();

    /// <summary>
    /// The abbreviation for frames per second alone ("fps"): the unit after the Timing box in Per second mode, and the unit
    /// the box accepts when the user types it (<c>TimingInput</c> also accepts the English "fps").
    /// </summary>
    public static string FramesPerSecond => CoreText.Strings.Get();

    /// <summary>A number of seconds, "{0} s": {0} the number with its decimal separator ("24.7").</summary>
    public static string SecondsFormat => CoreText.Strings.Get();

    /// <summary>A length from a minute, "{0} min {1} s": {0} the minutes, {1} the seconds.</summary>
    public static string MinutesSecondsFormat => CoreText.Strings.Get();

    /// <summary>A length from an hour, "{0} h {1} min": {0} the hours, {1} the minutes, two digits.</summary>
    public static string HoursMinutesFormat => CoreText.Strings.Get();

    /// <summary>A frame rate, "{0} fps": {0} the rate with its decimal separator.</summary>
    public static string FramesPerSecondFormat => CoreText.Strings.Get();

    /// <summary>The time each photo stays, "{0} s per photo": {0} the seconds with its decimal separator.</summary>
    public static string SecondsPerPhotoFormat => CoreText.Strings.Get();
}
