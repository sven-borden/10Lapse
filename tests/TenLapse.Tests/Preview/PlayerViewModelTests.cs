using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Presentation.Preview;
using TenLapse.Tests.Presentation;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Preview;

public sealed class PlayerViewModelTests
{
    // The test's clock counts milliseconds.
    private long _now;

    [Fact]
    public void PlaysAtTheProjectRateFromTheFrameOnScreenAndLoops()
    {
        var (shell, player) = Player(90);

        player.Seek(30);
        player.Play();
        _now += 1000;
        player.Tick();

        Assert.True(player.IsPlaying);
        Assert.Equal(60, player.CurrentFrame);
        Assert.Equal("0061 / 0090", player.CounterText);
        Assert.Equal("00:02.00 / 00:03.00", player.TimecodeText);

        // Narrator's names stay where playback started: they change only while paused.
        Assert.Equal("Frame 31 of 90", player.CounterName);
        Assert.Equal("Time 00:01.00 of 00:03.00", player.TimecodeName);

        _now += 1000;
        player.Tick();
        Assert.Equal(0, player.CurrentFrame);

        _now += 500;
        player.Pause();
        Assert.False(player.IsPlaying);
        Assert.Equal(15, player.CurrentFrame);
        Assert.Equal("Frame 16 of 90", player.CounterName);
        Assert.Equal(ProjectText.PreviewPlay, player.PlayPauseName);
        Assert.NotNull(shell);
    }

    [Fact]
    public void StepsPauseAndStopAtTheEnds()
    {
        var (_, player) = Player(90);
        player.Play();
        _now += 100;

        Assert.True(player.Handle(TransportAction.StepForwardTen));
        Assert.False(player.IsPlaying);
        Assert.Equal(13, player.CurrentFrame);
        Assert.True(player.Handle(TransportAction.StepBack));
        Assert.Equal(12, player.CurrentFrame);

        player.Step(-1000);
        Assert.Equal(0, player.CurrentFrame);
        player.Handle(TransportAction.Last);
        Assert.Equal(89, player.CurrentFrame);
        player.Handle(TransportAction.StepForward);
        Assert.Equal(89, player.CurrentFrame);
        player.Handle(TransportAction.First);
        Assert.Equal(0, player.CurrentFrame);
        Assert.False(player.Handle(TransportAction.None));

        player.Handle(TransportAction.TogglePlay);
        Assert.True(player.IsPlaying);
        player.PlayPauseCommand.Execute(null);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void ASeekWhilePlayingPlaysOnFromThere()
    {
        var (_, player) = Player(300);
        player.Play();
        _now += 2000;

        player.Seek(200);
        _now += 1000;
        player.Tick();

        Assert.True(player.IsPlaying);
        Assert.Equal(230, player.CurrentFrame);
    }

    [Fact]
    public void PhotosArrivingWhilePlayingKeepThePace()
    {
        var (shell, player) = Player(90);
        var project = shell.CurrentProject.Model;
        player.Play();

        // Half a second in (frame 15), an import adds photos: the loop gets longer, the frame on screen goes on.
        _now = 510;
        project.AddFrames(TestFrames.Folder("more", 60));
        _now = 1000;
        player.Tick();
        Assert.Equal(30, player.CurrentFrame);
        Assert.Equal(150, player.FrameCount);

        // A new rate: from the moment the frame on screen began, at the new pace.
        project.Timing = TimingSettings.PerSecond(new Fraction(60, 1));
        _now = 1500;
        player.Tick();
        Assert.Equal(60, player.CurrentFrame);
        Assert.Equal(new Fraction(60, 1), player.Clock!.Rate);
    }

    [Fact]
    public void RemovingFramesPastThePlayheadMovesItToTheLastFrame()
    {
        var (shell, player) = Player(90);
        var project = shell.CurrentProject.Model;
        player.Seek(80);

        project.RemoveFrames(project.Frames.Skip(70).Select(frame => frame.Id));

        Assert.Equal(70, player.FrameCount);
        Assert.Equal(69, player.CurrentFrame);
        Assert.Equal(69, player.State.Frame);
        Assert.Equal("0070 / 0070", player.CounterText);
    }

    [Fact]
    public void ANewProjectStopsPlaybackAndTheOldProjectNoLongerReachesThePlayer()
    {
        var (shell, player) = Player(90);
        var old = shell.CurrentProject.Model;
        player.Seek(40);
        player.Play();

        Assert.True(shell.ReplaceWithoutAsking());

        Assert.False(player.IsPlaying);
        Assert.Equal(0, player.FrameCount);
        Assert.Equal(0, player.CurrentFrame);
        Assert.Equal(string.Empty, player.CounterText);
        Assert.NotSame(old.Snapshot, player.State.Snapshot);

        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("new", 12));
        Assert.Equal(12, player.FrameCount);
        Assert.Equal(shell.CurrentProject.Model.Snapshot, player.State.Snapshot);
    }

    [Fact]
    public void OpeningSettingsPausesAndPlayWaitsUntilItCloses()
    {
        var (shell, player) = Player(90);
        player.Play();

        shell.OpenSettingsCommand.Execute(null);
        Assert.False(player.IsPlaying);
        player.Play();
        Assert.False(player.IsPlaying);

        shell.CloseSettingsCommand.Execute(null);
        player.Play();
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public void EveryChangeIsPublishedForTheRenderThread()
    {
        var (shell, player) = Player(90);
        var changes = 0;
        player.StateChanged += (_, _) => changes++;
        var before = player.State.Version;

        player.Seek(10);
        player.Play();
        shell.CurrentProject.Model.FrameSettings = new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit);
        _now += 1000;

        var state = player.State;
        Assert.Equal(3, changes);
        Assert.Equal(before + 3, state.Version);
        Assert.True(state.IsPlaying);
        Assert.Equal(FitMode.Fit, state.Snapshot.FrameSettings.Fit);
        Assert.Equal(40, state.FrameAt(_now));

        // The counter's own tick publishes nothing: the render thread reads the same clock.
        player.Tick();
        Assert.Equal(3, changes);
    }

    [Fact]
    public void WithoutFramesThereIsNothingToPlay()
    {
        var (_, player) = Player(0);

        player.Play();
        player.Seek(5);

        Assert.False(player.IsPlaying);
        Assert.False(player.HasFrames);
        Assert.Equal(0, player.LastFrame);
        Assert.Equal(string.Empty, player.TimecodeText);
    }

    [Theory]
    [InlineData(0.0, 742, 0)]
    [InlineData(1.0, 742, 741)]
    [InlineData(0.5, 742, 371)]
    [InlineData(-3.0, 742, 0)]
    [InlineData(7.0, 742, 741)]
    [InlineData(0.5, 1, 0)]
    [InlineData(double.NaN, 10, 0)]
    public void TheScrubberMapsItsWidthOntoTheFrames(double fraction, int count, int frame)
    {
        Assert.Equal(frame, PlayerViewModel.FrameAtFraction(fraction, count));
        if (count > 1 && fraction is >= 0 and <= 1)
        {
            Assert.Equal(frame, PlayerViewModel.FrameAtFraction(PlayerViewModel.FractionOfFrame(frame, count), count));
        }
    }

    private (ShellViewModel Shell, PlayerViewModel Player) Player(int frames)
    {
        var shell = new ShellViewModel(new FakeDialogService(), new FakeErrorSink());
        if (frames > 0)
        {
            shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", frames));
        }

        return (shell, new PlayerViewModel(shell, new FakeErrorSink(), () => _now, 1000));
    }
}
