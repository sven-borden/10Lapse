using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Timing;

/// <summary>
/// GIF frame delays (#51): each delay is the difference of two rounded exact starts, so the delays add up to the
/// rounded length at every frame, however long the GIF runs, and each delay is the frame's exact length rounded down or
/// up, never more off.
/// </summary>
public sealed class GifTimingTests
{
    // Over 100,000 frames: 2 h 46 min at 10 fps, 55 min at 29.97 fps, past any GIF anyone would send.
    private const int LongRun = 100_000;

    public static TheoryData<long, long> Rates() => new()
    {
        { 10, 1 },
        { 25, 2 },
        { 15, 1 },
        { 24, 1 },
        { 25, 1 },
        { 30000, 1001 },
        { 30, 1 },
        { 20, 1 },
        { 50, 1 },
        { 7, 3 },
        { 1, 10 },
    };

    [Theory]
    [MemberData(nameof(Rates))]
    public void DelaysAddUpToTheRoundedLengthAtEveryFrame(long numerator, long denominator)
    {
        var rate = new Fraction(numerator, denominator);
        long total = 0;
        for (var i = 0; i < LongRun; i++)
        {
            total += GifTiming.Delay(i, rate);

            // The running sum is the next frame's start rounded once: never off by more than half a centisecond.
            Assert.Equal(GifTiming.ExactStart(i + 1, rate), total);
        }

        // And that is the exact length within half a centisecond: no drift after 100,000 frames.
        var exact = (double)LongRun * 100 * denominator / numerator;
        Assert.InRange(total - exact, -0.5, 0.5);
    }

    [Theory]
    [MemberData(nameof(Rates))]
    public void EachDelayIsTheFramesLengthRoundedDownOrUp(long numerator, long denominator)
    {
        var rate = new Fraction(numerator, denominator);
        var low = 100 * denominator / numerator;
        var high = (100 * denominator + numerator - 1) / numerator;
        for (var i = 0; i < LongRun; i++)
        {
            Assert.InRange(GifTiming.Delay(i, rate), low, high);
        }
    }

    [Theory]
    [MemberData(nameof(Rates))]
    public void TheTickStartRoundsToTheSameCentisecondAsTheExactStart(long numerator, long denominator)
    {
        // The encoder rounds the tick times the pipeline hands it; the rational start is the reference.
        var rate = new Fraction(numerator, denominator);
        for (var i = 0; i < LongRun; i++)
        {
            Assert.Equal(GifTiming.ExactStart(i, rate), GifTiming.Centiseconds(FrameTiming.FrameStart(i, rate)));
        }
    }

    [Theory]
    [InlineData(10, 1, new[] { 10, 10, 10, 10, 10, 10 })]
    [InlineData(25, 2, new[] { 8, 8, 8, 8, 8, 8 })]
    [InlineData(15, 1, new[] { 7, 6, 7, 7, 6, 7 })]
    [InlineData(24, 1, new[] { 4, 4, 5, 4, 4, 4 })]
    [InlineData(25, 1, new[] { 4, 4, 4, 4, 4, 4 })]
    [InlineData(30, 1, new[] { 3, 4, 3, 3, 4, 3 })]
    [InlineData(30000, 1001, new[] { 3, 4, 3, 3, 4, 3 })]
    public void TheFirstDelaysFollowTheRoundedStarts(long numerator, long denominator, int[] expected)
    {
        var rate = new Fraction(numerator, denominator);
        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(i => GifTiming.Delay(i, rate)));
    }

    [Fact]
    public void AtTheNtscRateTheDelaysCatchUpWhereThe1001Accumulates()
    {
        // 29.97 fps is 3.3367 cs a frame: after 1,000 frames the GIF is 3,336.67 cs long, so 3,337 cs, not 3,333 as
        // 30 fps would give and not 3,000 or 4,000 as a fixed rounded delay would.
        var rate = FrameRates.Ntsc30;
        Assert.Equal(3337, Enumerable.Range(0, 1000).Sum(i => GifTiming.Delay(i, rate)));
        Assert.Equal(3333, Enumerable.Range(0, 1000).Sum(i => GifTiming.Delay(i, new Fraction(30, 1))));
    }

    [Fact]
    public void EveryPresetKeepsDelaysThatBrowsersHonour()
    {
        // Browsers stretch a delay under 2 cs to 10 cs. The presets and anything slower stay at or above 2.
        foreach (var preset in Enum.GetValues<GifFrameRate>())
        {
            var rate = GifOutput.Rate(preset);
            Assert.True(rate <= GifOutput.MaxRate);
            Assert.All(Enumerable.Range(0, 1000), i => Assert.True(GifTiming.Delay(i, rate) >= 2));
        }
    }

    [Fact]
    public void CentisecondsRoundHalfUp()
    {
        Assert.Equal(0, GifTiming.Centiseconds(0));
        Assert.Equal(0, GifTiming.Centiseconds(49_999));
        Assert.Equal(1, GifTiming.Centiseconds(50_000));
        Assert.Equal(1, GifTiming.Centiseconds(149_999));
        Assert.Equal(2, GifTiming.Centiseconds(150_000));
    }

    [Fact]
    public void ADelayTooLongForAGifThrows()
    {
        Assert.Equal(GifTiming.MaxDelay, GifTiming.Delay(0, GifTiming.MaxDelay * GifTiming.TicksPerCentisecond));
        Assert.Throws<OverflowException>(() => GifTiming.Delay(0, (GifTiming.MaxDelay + 1) * GifTiming.TicksPerCentisecond));
        Assert.Throws<ArgumentOutOfRangeException>(() => GifTiming.Delay(10, 5));
    }
}
