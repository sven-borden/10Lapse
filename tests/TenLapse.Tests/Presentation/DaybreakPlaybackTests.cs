using TenLapse.Presentation.FirstLaunch;

namespace TenLapse.Tests.Presentation;

public sealed class DaybreakPlaybackTests
{
    // On screen, in the active window, animations on, no high contrast.
    private static readonly DaybreakConditions _empty = new(
        Loaded: true,
        RegionVisible: true,
        FitsLayout: true,
        AnimationsEnabled: true,
        WindowActive: true,
        HighContrast: false);

    private TimeSpan _now = TimeSpan.FromSeconds(100);

    [Fact]
    public void ANewTimelapseIsHiddenAtTheStillFrame()
    {
        var playback = Playback();

        Assert.Equal(DaybreakMode.Hidden, playback.Mode);
        Assert.False(playback.TakesPlace);
        Assert.Equal(Daybreak.StillFrame, playback.Frame);
    }

    [Fact]
    public void OnTheEmptyScreenItRunsAndTheFrameFollowsTheTime()
    {
        var playback = Playback();

        Assert.Equal(DaybreakMode.Run, playback.Update(_empty));
        Assert.True(playback.TakesPlace);
        _now += TimeSpan.FromSeconds(2);

        Assert.Equal(Daybreak.StillFrame + 60, playback.Frame);
    }

    [Theory]
    [InlineData(false, true, true)]   // not loaded yet, or unloaded
    [InlineData(true, false, true)]   // an import arrived, or Settings opened: the Empty region is hidden
    [InlineData(true, true, false)]   // a narrow or short window
    public void HiddenStopsTheLoopAndPausesTheClock(bool loaded, bool regionVisible, bool fits)
    {
        var playback = Playback();
        playback.Update(_empty);
        _now += TimeSpan.FromSeconds(1);

        var mode = playback.Update(_empty with { Loaded = loaded, RegionVisible = regionVisible, FitsLayout = fits });
        _now += TimeSpan.FromMinutes(5);

        Assert.Equal(DaybreakMode.Hidden, mode);
        Assert.Equal(fits, playback.TakesPlace);
        Assert.Equal(Daybreak.StillFrame + 30, playback.Frame);
    }

    [Fact]
    public void BackToTheEmptyScreenItResumesWhereItWas()
    {
        var playback = Playback();
        playback.Update(_empty);
        _now += TimeSpan.FromSeconds(1);
        playback.Update(_empty with { RegionVisible = false }); // Settings
        _now += TimeSpan.FromMinutes(5);

        Assert.Equal(DaybreakMode.Run, playback.Update(_empty));
        _now += TimeSpan.FromSeconds(1);

        Assert.Equal(Daybreak.StillFrame + 60, playback.Frame);
    }

    [Fact]
    public void InTheBackgroundItStandsStillWhereItWas()
    {
        var playback = Playback();
        playback.Update(_empty);
        _now += TimeSpan.FromSeconds(1);

        Assert.Equal(DaybreakMode.Still, playback.Update(_empty with { WindowActive = false }));
        _now += TimeSpan.FromMinutes(1);

        Assert.True(playback.TakesPlace);
        Assert.Equal(Daybreak.StillFrame + 30, playback.Frame);
    }

    [Fact]
    public void AnimationsOffResetsToTheStillFrameAndPlaysOnFromThere()
    {
        var playback = Playback();
        playback.Update(_empty);
        _now += TimeSpan.FromSeconds(3);

        Assert.Equal(DaybreakMode.Still, playback.Update(_empty with { AnimationsEnabled = false }));
        _now += TimeSpan.FromSeconds(3);
        Assert.Equal(Daybreak.StillFrame, playback.Frame);

        playback.Update(_empty);
        _now += TimeSpan.FromSeconds(1);
        Assert.Equal(Daybreak.StillFrame + 30, playback.Frame);
    }

    [Fact]
    public void AnimationsOffWhileHiddenStillComesBackAtTheStillFrame()
    {
        var playback = Playback();
        playback.Update(_empty);
        _now += TimeSpan.FromSeconds(3);

        playback.Update(_empty with { RegionVisible = false, AnimationsEnabled = false });

        Assert.Equal(DaybreakMode.Still, playback.Update(_empty with { AnimationsEnabled = false }));
        Assert.Equal(Daybreak.StillFrame, playback.Frame);
    }

    [Fact]
    public void HighContrastHidesItAndGivesUpItsColumn()
    {
        var playback = Playback();
        playback.Update(_empty);

        Assert.Equal(DaybreakMode.Hidden, playback.Update(_empty with { HighContrast = true }));
        Assert.False(playback.TakesPlace);

        Assert.Equal(DaybreakMode.Run, playback.Update(_empty)); // high contrast off again
        Assert.True(playback.TakesPlace);
    }

    [Fact]
    public void ABrokenTimelapseStaysOff()
    {
        var playback = Playback();
        playback.Update(_empty);
        _now += TimeSpan.FromSeconds(1);

        playback.Break();
        var frame = playback.Frame;

        Assert.True(playback.IsBroken);
        Assert.Equal(DaybreakMode.Hidden, playback.Mode);
        Assert.Equal(DaybreakMode.Hidden, playback.Update(_empty));
        Assert.Equal(DaybreakMode.Hidden, playback.Update(_empty with { AnimationsEnabled = false }));
        Assert.False(playback.TakesPlace);
        _now += TimeSpan.FromSeconds(5);
        Assert.Equal(frame, playback.Frame);
    }

    private DaybreakPlayback Playback() => new(() => _now);
}
