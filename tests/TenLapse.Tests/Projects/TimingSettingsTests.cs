using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Projects;

public sealed class TimingSettingsTests
{
    public static TheoryData<int, long, long, RateClamp> PerSecondRates => new()
    {
        { 0, 30, 1, RateClamp.None },
        { 1, 30, 1, RateClamp.None },
        { 3, 30, 1, RateClamp.None },
        { 5000, 30, 1, RateClamp.None },
    };

    // 30 s target: 0 photos and 1 photo ask for less than 0.1 fps, 3 photos for exactly 0.1, 5,000 for 166.7.
    public static TheoryData<int, long, long, RateClamp> LengthRates => new()
    {
        { 0, 1, 10, RateClamp.Min },
        { 1, 1, 10, RateClamp.Min },
        { 3, 1, 10, RateClamp.None },
        { 5000, 120, 1, RateClamp.Max },
    };

    public static TheoryData<int, long, long, RateClamp> PerPhotoRates => new()
    {
        { 0, 2, 1, RateClamp.None },
        { 1, 2, 1, RateClamp.None },
        { 3, 2, 1, RateClamp.None },
        { 5000, 2, 1, RateClamp.None },
    };

    [Theory]
    [MemberData(nameof(PerSecondRates))]
    public void PerSecondPlaysAtItsInput(int count, long numerator, long denominator, RateClamp clamp)
    {
        var rate = TimingSettings.PerSecond(new Fraction(30, 1)).EffectiveRate(count);

        Assert.Equal(new PlaybackRate(new Fraction(numerator, denominator), clamp), rate);
        Assert.False(rate.IsClamped);
    }

    [Theory]
    [MemberData(nameof(LengthRates))]
    public void LengthComputesTheRateAndSaysWhenItIsClamped(int count, long numerator, long denominator, RateClamp clamp)
    {
        var rate = TimingSettings.Length(new Fraction(30, 1)).EffectiveRate(count);

        Assert.Equal(new PlaybackRate(new Fraction(numerator, denominator), clamp), rate);
        Assert.Equal(clamp != RateClamp.None, rate.IsClamped);
    }

    [Theory]
    [MemberData(nameof(PerPhotoRates))]
    public void PerPhotoPlaysAtOneOverItsInput(int count, long numerator, long denominator, RateClamp clamp)
    {
        var rate = TimingSettings.PerPhoto(new Fraction(1, 2)).EffectiveRate(count);

        Assert.Equal(new PlaybackRate(new Fraction(numerator, denominator), clamp), rate);
    }

    [Fact]
    public void LengthKeepsItsTargetWhateverTheFrameCount()
    {
        var timing = TimingSettings.Length(new Fraction(30, 1));

        Assert.Equal(new Fraction(742, 30), timing.EffectiveRate(742).Rate);
        Assert.Equal(new Fraction(720, 30), timing.EffectiveRate(720).Rate);
        Assert.Equal(new Fraction(30, 1), timing.LengthSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => timing.EffectiveRate(-1));
    }

    [Fact]
    public void EachModeKeepsItsOwnInputAcrossModeSwitches()
    {
        var timing = new TimingSettings(SpeedMode.PerSecond, FrameRates.Ntsc24, new Fraction(45, 1), new Fraction(2, 1));

        var length = timing with { Mode = SpeedMode.Length };
        var perPhoto = length with { Mode = SpeedMode.PerPhoto };

        Assert.Equal(FrameRates.Ntsc24, perPhoto.FramesPerSecond);
        Assert.Equal(new Fraction(45, 1), perPhoto.LengthSeconds);
        Assert.Equal(new Fraction(2, 1), perPhoto.SecondsPerPhoto);
        Assert.Equal(new Fraction(1, 2), perPhoto.EffectiveRate(10).Rate);
        Assert.Equal(new Fraction(10, 45), length.EffectiveRate(10).Rate);
        Assert.Equal(FrameRates.Ntsc24, (perPhoto with { Mode = SpeedMode.PerSecond }).EffectiveRate(10).Rate);
    }

    [Fact]
    public void EachInputStaysInItsRangeEvenThroughWith()
    {
        var timing = TimingSettings.Default;

        Assert.Equal(new Fraction(1, 1), (timing with { FramesPerSecond = new Fraction(1, 1) }).FramesPerSecond);
        Assert.Equal(new Fraction(120, 1), (timing with { FramesPerSecond = new Fraction(120, 1) }).FramesPerSecond);
        Assert.Throws<ArgumentOutOfRangeException>(() => timing with { FramesPerSecond = new Fraction(1, 2) });
        Assert.Throws<ArgumentOutOfRangeException>(() => timing with { FramesPerSecond = new Fraction(121, 1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => timing with { FramesPerSecond = default });
        Assert.Throws<ArgumentOutOfRangeException>(() => timing with { LengthSeconds = new Fraction(1, 11) });
        Assert.Throws<ArgumentOutOfRangeException>(() => timing with { LengthSeconds = new Fraction(36_001, 1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => timing with { SecondsPerPhoto = new Fraction(1, 11) });
        Assert.Throws<ArgumentOutOfRangeException>(() => timing with { SecondsPerPhoto = new Fraction(11, 1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => timing with { Mode = (SpeedMode)9 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TimingSettings.PerPhoto(default));
    }

    [Fact]
    public void TheProjectGivesTheRateForItsOwnFrames()
    {
        var project = new Project { Timing = TimingSettings.Length(new Fraction(30, 1)) };

        Assert.Equal(new PlaybackRate(FrameRates.Min, RateClamp.Min), project.EffectiveRate);
        project.AddFrames(TestFrames.Folder("a", 742));

        Assert.Equal(new PlaybackRate(new Fraction(742, 30), RateClamp.None), project.EffectiveRate);
        Assert.Equal(project.EffectiveRate, project.Snapshot.EffectiveRate);
    }
}
