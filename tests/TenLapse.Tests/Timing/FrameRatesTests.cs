using TenLapse.Core.Timing;

namespace TenLapse.Tests.Timing;

public sealed class FrameRatesTests
{
    private const long Photos = 742;

    [Theory]
    [InlineData("29.97", 30000, 1001)]
    [InlineData("29,97", 30000, 1001)]
    [InlineData("29.970", 30000, 1001)]
    [InlineData("59.94", 60000, 1001)]
    [InlineData("23.976", 24000, 1001)]
    [InlineData("23.98", 24000, 1001)]
    [InlineData("47.95", 48000, 1001)]
    [InlineData("119.88", 120000, 1001)]
    [InlineData("24", 24, 1)]
    [InlineData("25", 25, 1)]
    [InlineData("30", 30, 1)]
    [InlineData("50", 50, 1)]
    [InlineData("60", 60, 1)]
    [InlineData("29.98", 1499, 50)]   // not NTSC: nearest small fraction
    [InlineData("12.5", 25, 2)]
    [InlineData("0.5", 1, 2)]
    [InlineData("30000/1001", 30000, 1001)]
    [InlineData("30/1", 30, 1)]
    public void TryParseMapsTypedRatesToTheirRationals(string text, int num, int den)
    {
        Assert.True(FrameRates.TryParse(text, out var rate));
        Assert.Equal(new Fraction(num, den), rate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fast")]
    [InlineData("0")]
    [InlineData("-30")]
    public void TryParseRejectsGarbage(string text)
    {
        Assert.False(FrameRates.TryParse(text, out _));
    }

    [Fact]
    public void TheNtscConstantsHaveTheirExactValues()
    {
        Assert.Equal(new Fraction(24000, 1001), FrameRates.Ntsc24);
        Assert.Equal(new Fraction(30000, 1001), FrameRates.Ntsc30);
        Assert.Equal(new Fraction(60000, 1001), FrameRates.Ntsc60);
    }

    [Fact]
    public void RangeIsOneTenthToOneHundredTwentyFps()
    {
        Assert.True(FrameRates.IsInRange(new Fraction(1, 10)));
        Assert.True(FrameRates.IsInRange(new Fraction(120, 1)));
        Assert.True(FrameRates.IsInRange(FrameRates.Ntsc60));
        Assert.False(FrameRates.IsInRange(new Fraction(1, 11)));
        Assert.False(FrameRates.IsInRange(new Fraction(121, 1)));
        Assert.All(FrameRates.Presets, rate => Assert.True(FrameRates.IsInRange(rate)));
    }

    public static TheoryData<int, int> AllModesRates => new()
    {
        { 12, 1 }, { 24, 1 }, { 25, 1 }, { 30, 1 }, { 50, 1 }, { 60, 1 },
        { 24000, 1001 }, { 30000, 1001 }, { 60000, 1001 },
        { 25, 2 }, { 1, 2 }, { 1, 5 }, { 1, 10 }, { 10, 3 }, { 1499, 50 },
    };

    [Theory]
    [MemberData(nameof(AllModesRates))]
    public void FramesPerSecondToLengthAndBackGivesTheSameRate(int num, int den)
    {
        var rate = new Fraction(num, den);

        var length = FrameRates.TotalLengthSeconds(Photos, rate);

        Assert.Equal(rate, FrameRates.FromTotalLength(Photos, length));
    }

    [Theory]
    [MemberData(nameof(AllModesRates))]
    public void FramesPerSecondToSecondsPerPhotoAndBackGivesTheSameRate(int num, int den)
    {
        var rate = new Fraction(num, den);

        var secondsPerPhoto = FrameRates.ToSecondsPerPhoto(rate);

        Assert.Equal(rate, FrameRates.FromSecondsPerPhoto(secondsPerPhoto));
    }

    [Fact]
    public void LengthToFramesPerSecondAndBackGivesTheSameLength()
    {
        Assert.True(Fraction.TryParse("24.7", out var length));

        var rate = FrameRates.FromTotalLength(Photos, length);

        Assert.Equal(length, FrameRates.TotalLengthSeconds(Photos, rate));
    }

    [Fact]
    public void SecondsPerPhotoToFramesPerSecondAndBackGivesTheSameSeconds()
    {
        foreach (var text in new[] { "0.1", "0.3", "0.5", "1", "2", "2.5", "5", "10" })
        {
            Assert.True(Fraction.TryParse(text, out var seconds));

            var rate = FrameRates.FromSecondsPerPhoto(seconds);

            Assert.Equal(seconds, FrameRates.ToSecondsPerPhoto(rate));
        }
    }

    [Fact]
    public void ARoundTripThroughLengthKeepsTheExactNtscClipDuration()
    {
        var length = FrameRates.TotalLengthSeconds(Photos, FrameRates.Ntsc30);
        var rate = FrameRates.FromTotalLength(Photos, length);

        Assert.Equal(FrameTiming.Duration(Photos, FrameRates.Ntsc30), FrameTiming.Duration(Photos, rate));
    }

    [Theory]
    [InlineData("0.1", 10, 1)]
    [InlineData("0.5", 2, 1)]
    [InlineData("1", 1, 1)]
    [InlineData("2", 1, 2)]
    [InlineData("2.5", 2, 5)]
    [InlineData("5", 1, 5)]
    [InlineData("10", 1, 10)]
    public void SecondsPerPhotoGivesTheRateOneOverSeconds(string seconds, int expectedNum, int expectedDen)
    {
        Assert.True(Fraction.TryParse(seconds, out var parsed));

        Assert.Equal(new Fraction(expectedNum, expectedDen), FrameRates.FromSecondsPerPhoto(parsed));
    }

    [Fact]
    public void LengthModeTurnsTargetLengthAndCountIntoARate()
    {
        Assert.True(Fraction.TryParse("20", out var twentySeconds));

        var rate = FrameRates.FromTotalLength(600, twentySeconds);

        Assert.Equal(new Fraction(30, 1), rate);
        Assert.Equal("30 fps", TimingFormat.FrameRate(rate));
        Assert.Equal("20.0 s", TimingFormat.Duration(FrameTiming.Duration(600, rate)));
    }

    [Fact]
    public void LengthModeRoundsAnAwkwardRateToASmallDenominator()
    {
        // 1000 photos in 33.3333 s is 30.00003 fps exactly, which needs a denominator of 333,333.
        Assert.True(Fraction.TryParse("33.3333", out var length));

        var rate = FrameRates.FromTotalLength(1000, length);

        Assert.Equal(new Fraction(30, 1), rate);
        Assert.True(rate.Denominator <= FrameRates.MaxDenominator);
    }

    [Fact]
    public void LengthModeKeepsAnExactRateWhenItIsSmall()
    {
        Assert.True(Fraction.TryParse("24.7", out var length));

        Assert.Equal(new Fraction(7420, 247), FrameRates.FromTotalLength(Photos, length));
    }

    [Theory]
    [InlineData(20_000)]
    [InlineData(100_000)]
    public void LengthModeClampsAnAbsurdlySlowRateInsteadOfThrowing(int seconds)
    {
        var rate = FrameRates.FromTotalLength(1, new Fraction(seconds, 1));

        Assert.Equal(new Fraction(1, FrameRates.MaxDenominator), rate);
        Assert.False(FrameRates.IsInRange(rate));
    }

    [Fact]
    public void LengthModeClampsAnAbsurdlyFastRateInsteadOfThrowing()
    {
        var rate = FrameRates.FromTotalLength(long.MaxValue, new Fraction(1, 1));

        Assert.Equal(new Fraction(int.MaxValue, 1), rate);
        Assert.False(FrameRates.IsInRange(rate));
    }

    [Fact]
    public void ConversionsRejectNonsense()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameRates.FromTotalLength(0, new Fraction(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameRates.FromTotalLength(10, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameRates.TotalLengthSeconds(0, new Fraction(30, 1)));
        Assert.Throws<DivideByZeroException>(() => FrameRates.ToSecondsPerPhoto(default));
    }
}
