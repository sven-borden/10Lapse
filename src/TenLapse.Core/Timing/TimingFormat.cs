using System.Globalization;

namespace TenLapse.Core.Timing;

/// <summary>
/// The text of the speed readout ("742 photos → 24.7 s at 30 fps"): durations and frame-rate
/// labels. Words come from <see cref="TimingWords"/>; numbers use the invariant culture for now.
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
    public static string Duration(long ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        var tenths = DivideRounded(ticks, TicksPerTenth);
        if (tenths < 600)
        {
            return $"{Number(tenths / 10)}.{Number(tenths % 10)} {TimingWords.Second}";
        }

        var seconds = DivideRounded(ticks, FrameTiming.TicksPerSecond);
        if (seconds < 3600)
        {
            return $"{Number(seconds / 60)} {TimingWords.Minute} {Number(seconds % 60)} {TimingWords.Second}";
        }

        var minutes = DivideRounded(ticks, TicksPerMinute);
        return $"{Number(minutes / 60)} {TimingWords.Hour} {(minutes % 60).ToString("D2", CultureInfo.InvariantCulture)} {TimingWords.Minute}";
    }

    /// <summary>
    /// The frame-rate label for the readout: "30 fps", "29.97 fps" (up to two decimals, trailing
    /// zeros dropped) from 1 fps up, and the per-photo form "2 s per photo" below 1 fps.
    /// </summary>
    public static string FrameRate(Fraction rate)
    {
        RequirePositive(rate);
        return rate >= new Fraction(1, 1)
            ? $"{Hundredths(rate.Numerator, rate.Denominator)} {TimingWords.FramesPerSecond}"
            : SecondsPerPhoto(rate);
    }

    /// <summary>The per-photo label whatever the rate: "0.5 s per photo", "2 s per photo", "3.33 s per photo".</summary>
    public static string SecondsPerPhoto(Fraction rate)
    {
        RequirePositive(rate);
        return $"{Hundredths(rate.Denominator, rate.Numerator)} {TimingWords.Second} {TimingWords.PerPhoto}";
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
            _ => $"{Number(whole)}.{fraction.ToString("D2", CultureInfo.InvariantCulture)}",
        };
    }

    /// <summary>Rounded half up without overflowing near <see cref="long.MaxValue"/>.</summary>
    private static long DivideRounded(long value, long unit) =>
        (value / unit) + ((value % unit) * 2 >= unit ? 1 : 0);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
