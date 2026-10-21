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
        _now += 500;
        player.Tick();

        // Narrator's names follow at most once a second while playing: half a second in, they are where playback started.
        Assert.Equal(45, player.CurrentFrame);
        Assert.Equal("0046 / 0090", player.CounterText);
        Assert.Equal("Frame 31 of 90", player.CounterName);
        Assert.Equal("Time 00:01.00 of 00:03.00", player.TimecodeName);

        _now += 500;
        player.Tick();

        Assert.True(player.IsPlaying);
        Assert.Equal(60, player.CurrentFrame);
        Assert.Equal("0061 / 0090", player.CounterText);
        Assert.Equal("00:02.00 / 00:03.00", player.TimecodeText);
        Assert.Equal("Frame 61 of 90", player.CounterName);
        Assert.Equal("Time 00:02.00 of 00:03.00", player.TimecodeName);

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
    public void RemovingFramesBeforeThePlayheadKeepsItOnTheSamePhotoWhilePaused()
    {
        var (shell, player) = Player(400);
        var project = shell.CurrentProject.Model;
        player.Seek(300);
        var photo = project.Frames[300].Id;

        project.RemoveFrames(project.Frames.Take(10).Select(frame => frame.Id));

        Assert.Equal(290, player.CurrentFrame);
        Assert.Equal(photo, player.State.Snapshot.Frames[player.State.Frame].Id);
        Assert.Equal("Frame 291 of 390", player.CounterName);

        // A reorder too: the photo moves, the playhead with it.
        project.ReorderFrames([.. project.Frames.Reverse().Select(frame => frame.Id)]);
        Assert.Equal(99, player.CurrentFrame);
        Assert.Equal(photo, player.State.Snapshot.Frames[player.State.Frame].Id);
    }

    [Fact]
    public void RemovingFramesBeforeThePlayheadKeepsItOnTheSamePhotoWhilePlayingAtThePace()
    {
        var (shell, player) = Player(400);
        var project = shell.CurrentProject.Model;
        player.Seek(300);
        player.Play();

        // 510 ms in: frame 315, which began at 500 ms.
        _now = 510;
        var photo = project.Frames[315].Id;
        project.RemoveFrames(project.Frames.Take(10).Select(frame => frame.Id));
        player.Tick();

        Assert.True(player.IsPlaying);
        Assert.Equal(305, player.CurrentFrame);
        Assert.Equal(photo, player.State.Snapshot.Frames[305].Id);

        // A second after that frame began, thirty frames on.
        _now = 1500;
        player.Tick();
        Assert.Equal(335, player.CurrentFrame);
    }

    [Fact]
    public void WhenThePhotoOnScreenIsRemovedThePlayheadKeepsItsPlace()
    {
        var (shell, player) = Player(90);
        var project = shell.CurrentProject.Model;
        player.Seek(50);
        var next = project.Frames[51].Id;

        project.RemoveFrames([project.Frames[50].Id]);

        Assert.Equal(50, player.CurrentFrame);
        Assert.Equal(next, player.State.Snapshot.Frames[50].Id);
    }

    [Fact]
    public void AHiddenWindowPausesAndPlaysOnWhenItShowsAgain()
    {
        var (_, player) = Player(90);
        player.Play();
        _now = 500;

        player.SetWindowShown(false);
        Assert.False(player.IsPlaying);
        Assert.Equal(15, player.CurrentFrame);
        player.Play();
        Assert.False(player.IsPlaying);

        _now = 5000;
        player.SetWindowShown(true);
        Assert.True(player.IsPlaying);
        _now = 5500;
        player.Tick();
        Assert.Equal(30, player.CurrentFrame);

        // Paused before it was hidden: still paused when it shows.
        player.Pause();
        player.SetWindowShown(false);
        player.SetWindowShown(true);
        Assert.False(player.IsPlaying);
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

    [Fact]
    public void PlaysTheVideosFramesWithTheirHoldsAndTheWayBack()
    {
        // #49: 10 photos at 10 fps as a boomerang with 0.5 s held first: 5 frames of photo 0, then 0 to 9 and back to 0.
        var (shell, player) = Player(10);
        var project = shell.CurrentProject.Model;
        project.Timing = TimingSettings.PerSecond(new Fraction(10, 1));
        project.Sequence = new SequenceSettings(TimeSpan.FromSeconds(0.5), TimeSpan.Zero, 1, boomerang: true);

        Assert.Equal((10, 24, 23), (player.FrameCount, player.PositionCount, player.LastPosition));
        player.Play();
        _now += 300;
        player.Tick();
        Assert.Equal((3, 0), (player.Position, player.CurrentFrame));
        Assert.Equal("0001 / 0010", player.CounterText);

        _now += 700;
        player.Tick();
        Assert.Equal((10, 5), (player.Position, player.CurrentFrame));

        // On the way back: frame 15 shows photo 8; the time code runs on the video's timeline.
        _now += 500;
        player.Pause();
        Assert.Equal((15, 8), (player.Position, player.CurrentFrame));
        Assert.Equal("00:01.50 / 00:02.40", player.TimecodeText);
        Assert.Equal(8, player.State.FrameAt(_now));
        Assert.Equal(8, player.State.PhotoAt(player.State.Position));

        // A photo picked in the strip goes to its showing nearest the playhead: photo 3 on the way back, not the way out.
        player.Seek(3);
        Assert.Equal((20, 3), (player.Position, player.CurrentFrame));

        // The scrubber and the ends are on the video's timeline: the boomerang ends on the first photo.
        player.SeekPosition(7);
        Assert.Equal((7, 2), (player.Position, player.CurrentFrame));
        Assert.True(player.Handle(TransportAction.Last));
        Assert.Equal((23, 0), (player.Position, player.CurrentFrame));
        Assert.True(player.Handle(TransportAction.First));
        Assert.Equal((0, 0), (player.Position, player.CurrentFrame));

        // The arrows walk the photos: one step on from the hold is photo 1.
        player.Step(1);
        Assert.Equal((6, 1), (player.Position, player.CurrentFrame));

        // A new hold keeps the playhead on its photo, in the same pass.
        player.Seek(3);
        project.Sequence = project.Sequence with { HoldStart = TimeSpan.Zero };
        Assert.Equal((3, 3), (player.Position, player.CurrentFrame));
        Assert.Equal(19, player.PositionCount);
    }

    [Fact]
    public void PlaybackLoopsOverTheWholeVideo()
    {
        // 4 photos played 3 times at 10 fps with 0.3 s held at the end: 12 + 3 frames, 1.5 s, then it starts over.
        var (shell, player) = Player(4);
        var project = shell.CurrentProject.Model;
        project.Timing = TimingSettings.PerSecond(new Fraction(10, 1));
        project.Sequence = new SequenceSettings(TimeSpan.Zero, TimeSpan.FromSeconds(0.3), 3, boomerang: false);

        player.Play();
        var shown = new List<int>();
        for (var step = 0; step < 30; step++)
        {
            shown.Add(player.State.FrameAt(_now));
            _now += 100;
        }

        Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 3, 3, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 3, 3, 3], shown);
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
