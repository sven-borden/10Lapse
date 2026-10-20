using System.Globalization;

namespace TenLapse.Core.Timing;

/// <summary>
/// The text of the speed readout ("742 photos → 24.7 s at 30 fps"): durations and frame-rate labels. The words are
/// whole format strings from <see cref="TimingWords"/> (#114); the numbers are written with the decimal separator of the
/// culture each method is given, the invariant culture (a dot) when none is. Callers that show the text pass the
/// user's culture. <see cref="Timecode"/> alone is invariant on purpose.
/// </summary>
public static class TimingFormat
{
    private const long TicksPerTenth = FrameTiming.TicksPerSecond / 10;
    private const long TicksPerMinute = FrameTiming.TicksPerSecond * 60;

    /// <summary>
    /// A clip length. Under a minute one decimal ("24.7 s", "45.0 s"); from a minute whole
    /// seconds ("1 min 12 s"); from an hour whole minutes with two digits ("1 h 02 min").
    /// Each unit is rounded half up and carries into the next one, so 59.95 s reads "1 min 0 s",
    /// never "60.0 s", and 3599.5 s reads "1 h 00 min".
    /// </summary>
    /// <param name="ticks">The length in 100 ns ticks.</param>
    /// <param name="culture">The culture whose decimal separator is used; the invariant culture (a dot) when null.</param>
    public static string Duration(long ticks, CultureInfo? culture = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        var tenths = DivideRounded(ticks, TicksPerTenth);
        if (tenths < 600)
        {
            return Fill(culture, TimingWords.SecondsFormat, $"{Number(tenths / 10)}{Separator(culture)}{Number(tenths % 10)}");
        }

        var seconds = DivideRounded(ticks, FrameTiming.TicksPerSecond);
        if (seconds < 3600)
        {
            return Fill(culture, TimingWords.MinutesSecondsFormat, Number(seconds / 60), Number(seconds % 60));
        }

        var minutes = DivideRounded(ticks, TicksPerMinute);
        return Fill(culture, TimingWords.HoursMinutesFormat, Number(minutes / 60), Two(minutes % 60));
    }

    /// <summary>
    /// A number of seconds as a label: "30 s", "0.25 s" (up to two decimals, trailing zeros dropped, the decimal
    /// separator of <paramref name="culture"/>). The Length and Per photo presets, and a typed length or time per photo
    /// in the Timing panel's sentences.
    /// </summary>
    /// <param name="seconds">A positive number of seconds.</param>
    /// <param name="culture">The culture whose decimal separator is used; the invariant culture (a dot) when null.</param>
    public static string Seconds(Fraction seconds, CultureInfo? culture = null)
    {
        RequirePositive(seconds);
        return Fill(culture, TimingWords.SecondsFormat, Localized(Hundredths(seconds.Numerator, seconds.Denominator), culture));
    }

    /// <summary>Whole seconds as a label, zero included: "0 s", "60 s" (the ends of the length bar).</summary>
    /// <param name="seconds">Zero or more whole seconds.</param>
    /// <param name="culture">The culture of the digits; the invariant culture when null.</param>
    public static string WholeSeconds(long seconds, CultureInfo? culture = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        return Fill(culture, TimingWords.SecondsFormat, seconds);
    }

    /// <summary>
    /// A rate in frames per second whatever its size: "30 fps", "29.97 fps", "0.5 fps" (up to two decimals). The readout
    /// uses <see cref="FrameRate"/>, which says the per-photo form below 1 fps.
    /// </summary>
    /// <param name="rate">A positive rate.</param>
    /// <param name="culture">The culture whose decimal separator is used; the invariant culture (a dot) when null.</param>
    public static string FramesPerSecond(Fraction rate, CultureInfo? culture = null)
    {
        RequirePositive(rate);
        return Fill(culture, TimingWords.FramesPerSecondFormat, Localized(Hundredths(rate.Numerator, rate.Denominator), culture));
    }

    /// <summary>
    /// A time code: "00:05.26", minutes and seconds with hundredths, both two digits; from an hour the hours lead
    /// ("1:02:03.40"). The hundredths are cut, not rounded, so a frame's start never reads later than it is and
    /// nothing carries ("00:59.99", never "01:00.00" before the minute). Invariant on purpose: a time code is the same
    /// clock notation in every language, as in a video editor, and keeps its width (DESIGN.md, the Tabular Rule).
    /// </summary>
    public static string Timecode(long ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        var hundredths = ticks / (FrameTiming.TicksPerSecond / 100);
        var seconds = hundredths / 100;
        var clock = $"{Two(seconds / 60 % 60)}:{Two(seconds % 60)}.{Two(hundredths % 100)}";
        return seconds >= 3600 ? $"{Number(seconds / 3600)}:{clock}" : clock;
    }

    /// <summary>
    /// The frame-rate label for the readout: "30 fps", "29.97 fps" (up to two decimals, trailing
    /// zeros dropped) from 1 fps up, and the per-photo form "2 s per photo" below 1 fps.
    /// </summary>
    /// <param name="rate">A positive rate.</param>
    /// <param name="culture">The culture whose decimal separator is used; the invariant culture (a dot) when null.</param>
    public static string FrameRate(Fraction rate, CultureInfo? culture = null)
    {
        RequirePositive(rate);
        return rate >= new Fraction(1, 1) ? FramesPerSecond(rate, culture) : SecondsPerPhoto(rate, culture);
    }

    /// <summary>
    /// A rate or a number of seconds as a plain number for an input box: up to two decimals, trailing zeros
    /// dropped ("30", "29.97", "0.25"), rounded half up, with the decimal separator of <paramref name="culture"/>
    /// ("29,97" in German). The digits are the ones <see cref="FrameRate"/> prints, so a box and the readout never
    /// disagree.
    /// </summary>
    /// <param name="value">A positive value.</param>
    /// <param name="culture">The culture whose decimal separator is used; the invariant culture (a dot) when null.</param>
    public static string Decimal(Fraction value, CultureInfo? culture = null)
    {
        RequirePositive(value);
        return Localized(Hundredths(value.Numerator, value.Denominator), culture);
    }

    /// <summary>
    /// The per-photo label whatever the rate: "0.5 s per photo", "2 s per photo", "3.33 s per photo".
    /// From 0.1 s up two decimals; below 0.1 s three significant figures, so 30 fps reads
    /// "0.0333 s per photo" and 29.97 fps "0.0334 s per photo" instead of both "0.03".
    /// Trailing zeros are dropped.
    /// </summary>
    /// <param name="rate">A positive rate.</param>
    /// <param name="culture">The culture whose decimal separator is used; the invariant culture (a dot) when null.</param>
    public static string SecondsPerPhoto(Fraction rate, CultureInfo? culture = null)
    {
        RequirePositive(rate);
        var seconds = rate.Denominator * 10 >= rate.Numerator
            ? Hundredths(rate.Denominator, rate.Numerator)
            : ThreeSignificant(rate.Denominator, rate.Numerator);
        return Fill(culture, TimingWords.SecondsPerPhotoFormat, Localized(seconds, culture));
    }

    // A format of TimingWords filled with numbers already written as text.
    private static string Fill(CultureInfo? culture, string format, params object[] numbers) =>
        string.Format(culture ?? CultureInfo.InvariantCulture, format, numbers);

    /// <summary><c>numerator / denominator</c> (below 0.1) rounded half up to three significant figures, trailing zeros dropped.</summary>
    private static string ThreeSignificant(long numerator, long denominator)
    {
        // The smallest number of decimals that keeps three significant figures: value * 10^decimals >= 100.
        var decimals = 2;
        Int128 scale = 100;
        while ((Int128)numerator * scale < 100 * (Int128)denominator)
        {
            decimals++;
            scale *= 10;
        }

        var scaled = (((Int128)numerator * scale * 2) + denominator) / (2 * (Int128)denominator);
        // Invariant on purpose: digits only; Localized puts in the culture's decimal separator.
        var digits = ((long)scaled).ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
        var text = $"{digits[..^decimals]}.{digits[^decimals..]}".TrimEnd('0');
        return text.EndsWith('.') ? text[..^1] : text;
    }

    private static string Separator(CultureInfo? culture) => culture?.NumberFormat.NumberDecimalSeparator ?? ".";

    // Swaps the invariant dot of a formatted number for the culture's decimal separator.
    private static string Localized(string text, CultureInfo? culture)
    {
        var separator = Separator(culture);
        return separator == "." ? text : text.Replace(".", separator, StringComparison.Ordinal);
    }

    private static void RequirePositive(Fraction rate)
    {
        if (rate.Numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "The frame rate must be positive.");
        }
    }

    /// <summary><c>numerator / denominator</c> rounded half up to two decimals, trailing zeros dropped.</summary>
    private static string Hundredths(long numerator, long denominator)
    {
        var hundredths = (long)((((Int128)numerator * 200) + denominator) / (2 * (Int128)denominator));
        var whole = hundredths / 100;
        var fraction = hundredths % 100;
        return fraction switch
        {
            0 => Number(whole),
            _ when fraction % 10 == 0 => $"{Number(whole)}.{Number(fraction / 10)}",
            // Invariant on purpose: digits and a dot; Localized puts in the culture's decimal separator.
            _ => $"{Number(whole)}.{fraction.ToString("D2", CultureInfo.InvariantCulture)}",
        };
    }

    /// <summary>Rounded half up without overflowing near <see cref="long.MaxValue"/>.</summary>
    private static long DivideRounded(long value, long unit) =>
        (value / unit) + ((value % unit) * 2 >= unit ? 1 : 0);

    // Invariant on purpose: ASCII digits without grouping (no length or rate here reaches a thousand units of its kind).
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    // Invariant on purpose: two digits, as the minutes of "1 h 02 min" and a time code.
    private static string Two(long value) => value.ToString("D2", CultureInfo.InvariantCulture);
}
