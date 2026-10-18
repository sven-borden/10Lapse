using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Projects;

public sealed class ProjectValueTests
{
    [Fact]
    public void WithKeepsTheFrameIdentity()
    {
        var frame = TestFrames.At(@"C:\Photos\a\IMG_0001.jpg");

        var known = frame with { PixelSize = new PixelSize(6000, 4000), CaptureTime = new DateTime(2026, 5, 1, 19, 2, 3) };

        Assert.Equal(frame.Id, known.Id);
        Assert.NotEqual(frame.Id, TestFrames.At(frame.SourcePath).Id);
        Assert.Equal("IMG_0001.jpg", known.FileName);
        Assert.False(known.PixelSize!.Value.IsPortrait);
    }

    [Fact]
    public void FrameFileFactsAreStoredInUtcAndMatchTheSameFile()
    {
        var utc = new DateTime(2026, 5, 1, 18, 30, 0, DateTimeKind.Utc);

        var fromLocal = new Frame(@"C:\a.jpg", 10, utc.ToLocalTime());
        var fromUnspecified = new Frame(@"C:\a.jpg", 10, DateTime.SpecifyKind(utc, DateTimeKind.Unspecified));

        Assert.Equal(DateTimeKind.Utc, fromLocal.LastWriteTimeUtc.Kind);
        Assert.Equal(utc, fromLocal.LastWriteTimeUtc);
        Assert.Equal(utc, fromUnspecified.LastWriteTimeUtc);
        Assert.True(fromLocal.Matches(10, utc));
        Assert.False(fromLocal.Matches(11, utc));
        Assert.False(fromLocal.Matches(10, utc.AddSeconds(1)));
    }

    [Fact]
    public void FrameRejectsNonsense()
    {
        Assert.Throws<ArgumentException>(() => new Frame(" ", 1, DateTime.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Frame(@"C:\a.jpg", -1, DateTime.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelSize(0, 10));
    }

    [Fact]
    public void TimingSettingsHoldOnlyARateInRangeEvenThroughWith()
    {
        var timing = new TimingSettings(FrameRates.Ntsc30, SpeedMode.Length);

        Assert.Equal(FrameRates.Ntsc30, timing.Rate);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimingSettings(default, SpeedMode.PerSecond));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimingSettings(new Fraction(121, 1), SpeedMode.PerSecond));
        Assert.Throws<ArgumentOutOfRangeException>(() => timing with { Rate = new Fraction(1, 11) });
        Assert.Throws<ArgumentOutOfRangeException>(() => timing with { Mode = (SpeedMode)9 });
        Assert.Equal(new TimingSettings(FrameRates.Min, SpeedMode.PerPhoto), timing with { Rate = FrameRates.Min, Mode = SpeedMode.PerPhoto });
    }

    [Fact]
    public void FrameAndOutputSettingsRejectUnknownValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameSettings.Default with { Aspect = (FrameAspect)99 });
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameSettings.Default with { Fit = (FitMode)99 });
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputSettings.Default with { Resolution = (OutputResolution)99 });
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputSettings.Default with { Format = (VideoFormat)99 });
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputSettings.Default with { Quality = (OutputQuality)99 });
    }
}
