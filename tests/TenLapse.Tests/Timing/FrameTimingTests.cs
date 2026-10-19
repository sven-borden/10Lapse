using TenLapse.Core.Timing;

namespace TenLapse.Tests.Timing;

public sealed class FrameTimingTests
{
    private const int FrameCount = 100_000;

    public static TheoryData<int, int> Rates => new()
    {
        { 24, 1 },
        { 25, 1 },
        { 30000, 1001 }, // 29.97
        { 30, 1 },
        { 50, 1 },
        { 60, 1 },
        { 60000, 1001 }, // 59.94
        { 24000, 1001 }, // 23.976
        { 2, 1 },        // 0.5 s per photo
        { 1, 2 },        // 2 s per photo
        { 1, 5 },        // 5 s per photo
        { 1, 10 },
        { 120, 1 },
    };

    [Theory]
    [MemberData(nameof(Rates))]
    public void NoDriftOver100000FramesAndTheRoundingRuleHolds(int num, int den)
    {
        var rate = new Fraction(num, den);
        var previous = FrameTiming.FrameStart(0, rate);
        var shortest = long.MaxValue;
        var longest = long.MinValue;

        Assert.Equal(0, previous);
        for (long n = 1; n <= FrameCount; n++)
        {
            var start = FrameTiming.FrameStart(n, rate);

            // Floor of the exact rational n * 10^7 * den / num: start <= exact < start + 1.
            var exactTimesNum = (Int128)n * FrameTiming.TicksPerSecond * rate.Denominator;
            Assert.True((Int128)start * rate.Numerator <= exactTimesNum, $"frame {n} starts after its exact time");
            Assert.True(exactTimesNum < ((Int128)start + 1) * rate.Numerator, $"frame {n} starts a whole tick early");

            Assert.True(start > previous, $"frame {n} does not start after frame {n - 1}");
            var duration = start - previous;
            shortest = Math.Min(shortest, duration);
            longest = Math.Max(longest, duration);
            previous = start;
        }

        Assert.True(longest - shortest <= 1, $"frame durations span {shortest}..{longest} ticks");
    }

    [Fact]
    public void ThirtyThousandOver1001KeepsItsExactValueAfter100000Frames()
    {
        // 100,000 * 1001 / 30,000 s = 3,336.666... s = 33,366,666,666.67 ticks, floored.
        Assert.Equal(33_366_666_666, FrameTiming.FrameStart(FrameCount, FrameRates.Ntsc30));
        Assert.Equal(33_366_666_666, FrameTiming.Duration(FrameCount, FrameRates.Ntsc30));
    }

    [Fact]
    public void FrameStartIsComputedFromTheIndexNotAccumulated()
    {
        var rate = new Fraction(60, 1);

        // 10,000,000 / 60 = 166,666.67: the starts alternate the rounding, they do not compound it.
        Assert.Equal(0, FrameTiming.FrameStart(0, rate));
        Assert.Equal(166_666, FrameTiming.FrameStart(1, rate));
        Assert.Equal(333_333, FrameTiming.FrameStart(2, rate));
        Assert.Equal(500_000, FrameTiming.FrameStart(3, rate));
        Assert.Equal(10_000_000, FrameTiming.FrameStart(60, rate));
        // Literals worked out by hand: floor(n * 10^7 * den / num).
        Assert.Equal(205_761_166_666, FrameTiming.FrameStart(1_234_567, rate));                  // 1,234,567 / 60 s
        Assert.Equal(833_333_333_333, FrameTiming.FrameStart(10_000_000, new Fraction(120, 1))); // 10^7 / 120 s
        Assert.Equal(123_456_700_000_000, FrameTiming.FrameStart(1_234_567, new Fraction(1, 10))); // 10 s per photo
    }

    [Fact]
    public void FrameDurationIsOneOfTwoNeighbouringTickValues()
    {
        var rate = new Fraction(60, 1);

        for (var n = 0; n < 1000; n++)
        {
            Assert.InRange(FrameTiming.FrameDuration(n, rate), 166_666, 166_667);
        }
    }

    [Fact]
    public void Legacy_SixtyFpsIsNotSixtyTwoPointFive()
    {
        // The legacy app cut 16.67 ms to 16 ms, so 60 frames lasted 0.96 s (62.5 fps).
        Assert.Equal(FrameTiming.TicksPerSecond, FrameTiming.Duration(60, new Fraction(60, 1)));
    }

    [Fact]
    public void Legacy_600PhotosAt30FpsLastExactly20Seconds()
    {
        // The legacy app rounded 33.33 ms down to 33 ms and got 19.8 s.
        Assert.Equal(20 * FrameTiming.TicksPerSecond, FrameTiming.Duration(600, new Fraction(30, 1)));
    }

    [Theory]
    [InlineData(2, 1, 5_000_000)]        // 0.5 s per photo
    [InlineData(1, 2, 20_000_000)]       // 2 s per photo
    [InlineData(1, 5, 50_000_000)]       // 5 s per photo
    [InlineData(1, 10, 100_000_000)]
    public void SlowRatesGiveWholeFrameLengths(int num, int den, long frameTicks)
    {
        var rate = new Fraction(num, den);

        Assert.Equal(frameTicks, FrameTiming.FrameStart(1, rate));
        Assert.Equal(frameTicks, FrameTiming.FrameDuration(7, rate));
        Assert.Equal(10 * frameTicks, FrameTiming.Duration(10, rate));
    }

    [Fact]
    public void ZeroFramesLastZero()
    {
        Assert.Equal(0, FrameTiming.Duration(0, new Fraction(30, 1)));
    }

    [Fact]
    public void InvalidInputThrows()
    {
        var rate = new Fraction(30, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => FrameTiming.FrameStart(-1, rate));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameTiming.FrameStart(1, default));
        // Above 10 million fps a frame is shorter than one tick and starts would repeat.
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameTiming.FrameStart(1, new Fraction(int.MaxValue, 1)));
    }

    [Fact]
    public void FrameDurationOfTheLastIndexThrowsOverflowNotAMisleadingNegativeIndexError()
    {
        var rate = new Fraction(30, 1);

        // index + 1 used to wrap to long.MinValue and report "A frame index cannot be negative".
        var ex = Assert.Throws<OverflowException>(() => FrameTiming.FrameDuration(long.MaxValue, rate));
        Assert.DoesNotContain("negative", ex.Message);
        Assert.Throws<OverflowException>(() => FrameTiming.FrameDuration(long.MaxValue - 1, new Fraction(1, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameTiming.FrameDuration(-1, rate));
    }

    [Fact]
    public void HugeIndexesThrowInsteadOfWrapping()
    {
        Assert.Throws<OverflowException>(() => FrameTiming.FrameStart(long.MaxValue, new Fraction(1, 10)));
    }

    [Theory]
    [MemberData(nameof(Rates))]
    public void FrameAtIsTheExactInverseOfFrameStart(int num, int den)
    {
        var rate = new Fraction(num, den);
        for (long n = 0; n < 20_000; n++)
        {
            var start = FrameTiming.FrameStart(n, rate);
            var next = FrameTiming.FrameStart(n + 1, rate);

            Assert.Equal(n, FrameTiming.FrameAt(start, rate));
            Assert.Equal(n, FrameTiming.FrameAt(next - 1, rate));
            Assert.Equal(n, FrameTiming.FrameAt(start + ((next - start) / 2), rate));
        }
    }

    [Fact]
    public void FrameAtAnHourIntoA2997ClipIsFrame107892()
    {
        var rate = new Fraction(30000, 1001);

        // 3600 s * 30000 / 1001 = 107,892.1...: the clock lands on the frame the index math says, an hour later.
        Assert.Equal(107_892, FrameTiming.FrameAt(3600 * FrameTiming.TicksPerSecond, rate));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameTiming.FrameAt(-1, rate));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameTiming.FrameAt(0, default));
    }
}
