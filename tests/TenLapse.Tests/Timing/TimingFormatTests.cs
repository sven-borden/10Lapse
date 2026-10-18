using TenLapse.Core.Timing;

namespace TenLapse.Tests.Timing;

public sealed class TimingFormatTests
{
    private const long Second = FrameTiming.TicksPerSecond;

    [Theory]
    [InlineData(0L, "0.0 s")]
    [InlineData(247_000_000L, "24.7 s")]
    [InlineData(450_000_000L, "45.0 s")]
    [InlineData(499_999L, "0.0 s")]     // below 0.05 s
    [InlineData(500_000L, "0.1 s")]     // 0.05 s rounds half up
    [InlineData(599_499_999L, "59.9 s")]
    [InlineData(599_500_000L, "1 min 0 s")] // 59.95 s must not read "60.0 s"
    [InlineData(600_000_000L, "1 min 0 s")]
    [InlineData(720_000_000L, "1 min 12 s")]
    [InlineData(725_000_000L, "1 min 13 s")]
    [InlineData(35_990_000_000L, "59 min 59 s")] // 3599 s
    [InlineData(35_994_999_999L, "59 min 59 s")]
    [InlineData(35_995_000_000L, "1 h 00 min")]  // 3599.5 s rounds up into the hour
    [InlineData(36_000_000_000L, "1 h 00 min")]  // 3600 s
    [InlineData(37_200_000_000L, "1 h 02 min")]
    [InlineData(363_000_000_000L, "10 h 05 min")]
    public void DurationReadsAsTheReadoutShowsIt(long ticks, string expected)
    {
        Assert.Equal(expected, TimingFormat.Duration(ticks));
    }

    [Fact]
    public void Legacy_AFortyFiveSecondClipNeverReadsZeroSeconds()
    {
        // The legacy label divided seconds by 60 twice and showed "0 s".
        Assert.Equal("45.0 s", TimingFormat.Duration(45 * Second));
    }

    [Fact]
    public void Legacy_ClipsUnderAMinuteKeepTheirSeconds()
    {
        for (var seconds = 1; seconds < 60; seconds++)
        {
            Assert.Equal($"{seconds}.0 s", TimingFormat.Duration(seconds * Second));
        }
    }

    [Fact]
    public void DurationOfAClipFromItsFrameCount()
    {
        var ticks = FrameTiming.Duration(742, new Fraction(30, 1));

        Assert.Equal("24.7 s", TimingFormat.Duration(ticks));
    }

    [Fact]
    public void NegativeDurationThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TimingFormat.Duration(-1));
    }

    [Theory]
    [InlineData(24, 1, "24 fps")]
    [InlineData(25, 1, "25 fps")]
    [InlineData(30, 1, "30 fps")]
    [InlineData(30000, 1001, "29.97 fps")]
    [InlineData(60000, 1001, "59.94 fps")]
    [InlineData(24000, 1001, "23.98 fps")]
    [InlineData(25, 2, "12.5 fps")]
    [InlineData(1, 1, "1 fps")]
    [InlineData(120, 1, "120 fps")]
    [InlineData(1, 2, "2 s per photo")]
    [InlineData(1, 5, "5 s per photo")]
    [InlineData(1, 10, "10 s per photo")]
    [InlineData(2, 5, "2.5 s per photo")]
    [InlineData(3, 10, "3.33 s per photo")]
    public void FrameRateLabel(int num, int den, string expected)
    {
        Assert.Equal(expected, TimingFormat.FrameRate(new Fraction(num, den)));
    }

    [Theory]
    [InlineData(10, 1, "0.1 s per photo")]
    [InlineData(2, 1, "0.5 s per photo")]
    [InlineData(1, 1, "1 s per photo")]
    [InlineData(1, 2, "2 s per photo")]
    [InlineData(30, 1, "0.03 s per photo")]
    public void SecondsPerPhotoLabelWhateverTheRate(int num, int den, string expected)
    {
        Assert.Equal(expected, TimingFormat.SecondsPerPhoto(new Fraction(num, den)));
    }

    [Fact]
    public void LabelsUseOnlyTheWordsOfTimingWords()
    {
        Assert.Equal($"30 {TimingWords.FramesPerSecond}", TimingFormat.FrameRate(new Fraction(30, 1)));
        Assert.Equal($"2 {TimingWords.Second} {TimingWords.PerPhoto}", TimingFormat.FrameRate(new Fraction(1, 2)));
        Assert.Equal($"1 {TimingWords.Minute} 12 {TimingWords.Second}", TimingFormat.Duration(72 * Second));
        Assert.Equal($"1 {TimingWords.Hour} 02 {TimingWords.Minute}", TimingFormat.Duration(3720 * Second));
    }

    [Fact]
    public void FrameRateLabelRejectsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TimingFormat.FrameRate(default));
    }
}
