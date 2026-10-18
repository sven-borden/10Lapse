namespace TenLapse.Core.Timing;

/// <summary>
/// The usual frame rates, parsing of what a user types, and the conversions between the three
/// speed modes of the timing panel. The frame rate (a <see cref="Fraction"/>) is the single
/// source of truth; the other modes are views of it.
/// </summary>
/// <remarks>
/// The conversions are exact, so A to B to A returns the same <see cref="Fraction"/>, as long as
/// the value passed on is the exact one these methods returned and not a rounded display text.
/// Only <see cref="FromTotalLength"/> can round, and only when the exact rate needs a
/// denominator above <see cref="MaxDenominator"/>.
/// </remarks>
public static class FrameRates
{
    /// <summary>The largest denominator a rate computed or parsed from a decimal keeps; 30000/1001 fits.</summary>
    public const int MaxDenominator = 10_000;

    private static readonly Fraction[] _ntscFamily =
        [new(24000, 1001), new(30000, 1001), new(48000, 1001), new(60000, 1001), new(120000, 1001)];

    /// <summary>24000/1001, film on NTSC video (23.976 fps).</summary>
    public static Fraction Ntsc24 { get; } = new(24000, 1001);

    /// <summary>30000/1001 (29.97 fps).</summary>
    public static Fraction Ntsc30 { get; } = new(30000, 1001);

    /// <summary>60000/1001 (59.94 fps).</summary>
    public static Fraction Ntsc60 { get; } = new(60000, 1001);

    /// <summary>The slowest rate the app offers: 10 seconds per photo.</summary>
    public static Fraction Min { get; } = new(1, 10);

    /// <summary>The fastest rate the app offers: 120 fps.</summary>
    public static Fraction Max { get; } = new(120, 1);

    /// <summary>The frames-per-second presets of the timing panel, slowest first.</summary>
    public static IReadOnlyList<Fraction> Presets { get; } =
        [new(12, 1), new(24, 1), new(25, 1), new(30, 1), new(50, 1), new(60, 1)];

    /// <summary>True when <paramref name="rate"/> lies between <see cref="Min"/> and <see cref="Max"/>.</summary>
    public static bool IsInRange(Fraction rate) => rate >= Min && rate <= Max;

    /// <summary>
    /// Parses a frame rate typed by the user (see <see cref="Fraction.TryParse"/>). A decimal
    /// within 0.005 of an NTSC rate becomes that rate: 29.97 and 29.970 are 30000/1001, 59.94 is
    /// 60000/1001, 23.976 and 23.98 are 24000/1001 (also 47.95 and 119.88). Anything else is the
    /// nearest fraction with a denominator up to <see cref="MaxDenominator"/>: 25 is 25, 12.5 is 25/2.
    /// </summary>
    public static bool TryParse(string? text, out Fraction rate)
    {
        if (!Fraction.TryParse(text, out rate))
        {
            return false;
        }

        // A ratio like "30000/1001" is taken literally; only a typed decimal is snapped.
        if (text!.Contains('/'))
        {
            return true;
        }

        var typed = rate.ToDouble();
        foreach (var ntsc in _ntscFamily)
        {
            if (Math.Abs(typed - ntsc.ToDouble()) < 0.005)
            {
                rate = ntsc;
                break;
            }
        }

        return true;
    }

    /// <summary>Per-photo mode: <paramref name="secondsPerPhoto"/> seconds for each photo is a rate of 1 / seconds.</summary>
    public static Fraction FromSecondsPerPhoto(Fraction secondsPerPhoto) => secondsPerPhoto.Reciprocal();

    /// <summary>The seconds each photo stays on screen at <paramref name="rate"/>; the inverse of <see cref="FromSecondsPerPhoto"/>.</summary>
    public static Fraction ToSecondsPerPhoto(Fraction rate) => rate.Reciprocal();

    /// <summary>
    /// Length mode: the rate that makes <paramref name="frameCount"/> frames last
    /// <paramref name="lengthSeconds"/> seconds, <c>count / length</c>. Exact when the denominator
    /// is at most <see cref="MaxDenominator"/>, otherwise the closest such fraction (the clip
    /// then misses the target by a tiny fraction of a frame). Defined for any positive count and
    /// length, it never throws for them: a rate below <c>1 / MaxDenominator</c> comes back as
    /// <c>1 / MaxDenominator</c> and an absurdly large one as <see cref="int.MaxValue"/> fps. Both
    /// are outside the usable range, so the caller must check <see cref="IsInRange"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The frame count or the length is not positive.</exception>
    public static Fraction FromTotalLength(long frameCount, Fraction lengthSeconds)
    {
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "At least one frame is needed.");
        }

        if (lengthSeconds.Numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthSeconds), lengthSeconds, "The length must be positive.");
        }

        return Fraction.Nearest((Int128)frameCount * lengthSeconds.Denominator, lengthSeconds.Numerator, MaxDenominator);
    }

    /// <summary>The exact length in seconds of <paramref name="frameCount"/> frames at <paramref name="rate"/>; the inverse of <see cref="FromTotalLength"/>. For ticks use <see cref="FrameTiming.Duration"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The frame count is not positive.</exception>
    /// <exception cref="OverflowException">The reduced result does not fit in 32-bit parts.</exception>
    public static Fraction TotalLengthSeconds(long frameCount, Fraction rate)
    {
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "At least one frame is needed.");
        }

        return new Fraction(frameCount * rate.Denominator, rate.Numerator);
    }
}
