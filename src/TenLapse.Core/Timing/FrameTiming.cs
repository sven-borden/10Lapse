namespace TenLapse.Core.Timing;

/// <summary>
/// Where each frame starts and how long a clip lasts, in 100 ns ticks (the unit of
/// <see cref="TimeSpan"/> and of Media Foundation sample times).
/// </summary>
/// <remarks>
/// Every value is computed from the frame index with exact integer math and is never
/// accumulated, so there is no drift: <c>FrameStart(n) = floor(n * 10_000_000 * den / num)</c>.
/// Rounding is toward zero (floor), chosen so that frame 0 is exactly 0, the clip duration is
/// exactly <c>FrameStart(count)</c>, and the error is below one tick (100 ns). Because the exact
/// frame length is at least one tick, starts are strictly increasing, and two frame durations
/// differ by at most one tick.
/// </remarks>
public static class FrameTiming
{
    /// <summary>Ticks in one second.</summary>
    public const long TicksPerSecond = 10_000_000;

    /// <summary>The start of frame <paramref name="index"/> (0-based) at <paramref name="rate"/> frames per second.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is negative, or the rate is zero or above 10 million fps.</exception>
    /// <exception cref="OverflowException">The result does not fit in a <see cref="long"/> (more than about 29,000 years).</exception>
    public static long FrameStart(long index, Fraction rate)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "A frame index cannot be negative.");
        }

        RequireUsable(rate);
        var ticks = (Int128)index * TicksPerSecond * rate.Denominator / rate.Numerator;
        return checked((long)ticks);
    }

    /// <summary>The length of frame <paramref name="index"/>: the next frame's start minus its own. One of two neighbouring tick values.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is negative, or the rate is zero or above 10 million fps.</exception>
    /// <exception cref="OverflowException">The next frame's start does not fit in a <see cref="long"/>, which includes the index <see cref="long.MaxValue"/>.</exception>
    public static long FrameDuration(long index, Fraction rate)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "A frame index cannot be negative.");
        }

        if (index == long.MaxValue)
        {
            throw new OverflowException("The frame index is too large: the next frame has no index.");
        }

        return FrameStart(index + 1, rate) - FrameStart(index, rate);
    }

    /// <summary>The length of a clip of <paramref name="frameCount"/> frames, which is where frame number <paramref name="frameCount"/> would start.</summary>
    public static long Duration(long frameCount, Fraction rate) => FrameStart(frameCount, rate);

    /// <summary>
    /// The frame on screen at <paramref name="ticks"/> into a clip: the last frame whose <see cref="FrameStart"/> is at
    /// or before it, the exact inverse of <see cref="FrameStart"/> (so <c>FrameAt(FrameStart(n)) == n</c> and
    /// <c>FrameAt(FrameStart(n + 1) - 1) == n</c>). Playback (#19) asks this of the wall clock instead of counting frames,
    /// so it never drifts.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The time is negative, or the rate is zero or above 10 million fps.</exception>
    public static long FrameAt(long ticks, Fraction rate)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "A clip time cannot be negative.");
        }

        RequireUsable(rate);

        // FrameStart(n) <= t  <=>  n * T * den < (t + 1) * num  <=>  n <= ((t + 1) * num - 1) / (T * den).
        var index = (((Int128)ticks + 1) * rate.Numerator - 1) / ((Int128)TicksPerSecond * rate.Denominator);
        return (long)index;
    }

    private static void RequireUsable(Fraction rate)
    {
        if (rate.Numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "The frame rate must be positive.");
        }

        if (rate.Numerator > (long)TicksPerSecond * rate.Denominator)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "A frame must last at least one 100 ns tick.");
        }
    }
}
