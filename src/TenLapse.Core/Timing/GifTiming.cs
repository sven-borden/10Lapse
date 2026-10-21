namespace TenLapse.Core.Timing;

/// <summary>
/// GIF frame delays (#51). A GIF times each frame in hundredths of a second (centiseconds, cs). The delay of frame n is
/// the difference of two rounded starts, <c>cs(start of n + 1) − cs(start of n)</c>, each start rounded half up from its
/// exact time: never a rounded delay added up, so the GIF's length stays within half a centisecond of the exact one
/// however long it runs. At 15 fps (6⅔ cs a frame) the starts round to 0, 7, 13, 20, 27, 33, 40, so the delays go 7, 6,
/// 7, 7, 6, 7; at 29.97 fps mostly 3 and 4.
/// </summary>
public static class GifTiming
{
    /// <summary>100 ns ticks in a centisecond.</summary>
    public const long TicksPerCentisecond = FrameTiming.TicksPerSecond / 100;

    /// <summary>The largest delay a GIF can hold (an unsigned 16-bit number of centiseconds, about 655 s).</summary>
    public const int MaxDelay = ushort.MaxValue;

    /// <summary>
    /// A time in 100 ns ticks, rounded half up to centiseconds. For a frame start this equals the exact start rounded:
    /// <see cref="FrameTiming.FrameStart"/> is the exact time rounded down by less than a tick, and a half centisecond is
    /// a whole number of ticks, so rounding down never crosses one.
    /// </summary>
    public static long Centiseconds(long ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        return (ticks + (TicksPerCentisecond / 2)) / TicksPerCentisecond;
    }

    /// <summary>The delay in centiseconds of a frame from <paramref name="startTicks"/> to <paramref name="endTicks"/>: the rounded end minus the rounded start.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The end is before the start.</exception>
    /// <exception cref="OverflowException">The delay does not fit a GIF (over <see cref="MaxDelay"/>).</exception>
    public static int Delay(long startTicks, long endTicks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(endTicks, startTicks);
        var delay = Centiseconds(endTicks) - Centiseconds(startTicks);
        return delay <= MaxDelay ? (int)delay : throw new OverflowException($"A GIF frame cannot last {delay} cs; the most is {MaxDelay}.");
    }

    /// <summary>The delay of frame <paramref name="index"/> at <paramref name="rate"/>, from <see cref="FrameTiming.FrameStart"/> of it and the next.</summary>
    public static int Delay(long index, Fraction rate) =>
        Delay(FrameTiming.FrameStart(index, rate), FrameTiming.FrameStart(index + 1, rate));

    /// <summary>
    /// The start of frame <paramref name="index"/> in centiseconds straight from the rational rate, rounded half up:
    /// round(index × 100 × den / num). The same number as <see cref="Centiseconds"/> of the tick start; the tests hold
    /// one against the other.
    /// </summary>
    public static long ExactStart(long index, Fraction rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (rate.Numerator <= 0 || rate.Denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "The frame rate must be positive.");
        }

        // floor((2 × index × 100 × den + num) / (2 × num)) is round half up of index × 100 × den / num.
        var twice = 2 * (Int128)index * 100 * rate.Denominator;
        return (long)((twice + rate.Numerator) / (2 * (Int128)rate.Numerator));
    }
}
