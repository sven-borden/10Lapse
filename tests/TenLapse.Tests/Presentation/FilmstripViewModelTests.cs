using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Filmstrip;
using TenLapse.Presentation.Preview;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

public sealed class FilmstripViewModelTests
{
    private long _now;

    [Fact]
    public void ItemsFollowTheProjectWithNumbersAndNames()
    {
        var (shell, _, strip) = Strip(3);

        Assert.Equal(3, strip.Items.Count);
        Assert.Equal("Frame 2, IMG_0002.jpg", strip.Items[1].Name);
        shell.CurrentProject.Model.InsertFrames(0, [TestFrames.At(@"C:\Photos\b\IMG_0100.jpg")]);

        Assert.Equal(4, strip.Items.Count);
        Assert.Equal([1, 2, 3, 4], strip.Items.Select(item => item.Number));
        Assert.Equal("Frame 3, IMG_0002.jpg", strip.Items[2].Name);
        Assert.Equal(shell.CurrentProject.Model.Frames.Select(frame => frame.Id), strip.Items.Select(item => item.Id));
    }

    [Fact]
    public void AClickSelectsTheFrameAloneAndShowsIt()
    {
        var (_, player, strip) = Strip(20);

        strip.Click(7, shift: false, control: false);

        Assert.Equal(7, player.CurrentFrame);
        Assert.Equal(7, strip.PlayheadIndex);
        Assert.True(strip.Items[7].IsSelected);
        Assert.True(strip.Items[7].IsPlayhead);
        Assert.Equal(1, strip.SelectedCount);
        Assert.Equal("Frame 0008 of 0020", strip.CaptionText);
        Assert.Equal("IMG_0008.jpg", strip.FileText);
    }

    [Fact]
    public void CtrlAddsOrTakesOutAndShiftSelectsARange()
    {
        var (_, player, strip) = Strip(20);
        strip.Click(2, false, false);
        strip.Click(5, false, control: true);
        strip.Click(9, false, control: true);

        Assert.Equal(Selected(strip), [2, 5, 9]);
        Assert.Equal(9, player.CurrentFrame);
        Assert.Equal("Frame 0010 of 0020 · 3 selected", strip.CaptionText);

        // Taking one out does not show it.
        strip.Click(5, false, control: true);
        Assert.Equal(Selected(strip), [2, 9]);
        Assert.Equal(9, player.CurrentFrame);

        // Shift: from the anchor (the last Ctrl+click, 5) to the click, replacing the rest.
        strip.Click(7, shift: true, control: false);
        Assert.Equal(Selected(strip), [5, 6, 7]);
        Assert.Equal(7, player.CurrentFrame);

        // Ctrl+Shift adds the range.
        strip.Click(12, false, false);
        strip.Click(14, shift: true, control: true);
        strip.Click(2, shift: true, control: true);
        Assert.Equal(Selected(strip), [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14]);
    }

    [Fact]
    public void ASingleSelectionFollowsThePlayheadAndSeveralStayPut()
    {
        var (_, player, strip) = Strip(30);
        strip.Click(3, false, false);

        player.Handle(TransportAction.StepForwardTen);
        Assert.Equal(Selected(strip), [13]);

        player.Play();
        _now += 1000;
        player.Tick();
        Assert.True(player.CurrentFrame > 13);
        Assert.Equal(Selected(strip), [player.CurrentFrame]);
        Assert.True(strip.Items[player.CurrentFrame].IsPlayhead);
        player.Pause();

        strip.Click(20, false, control: true);
        var several = Selected(strip);
        player.Seek(2);
        Assert.Equal(several, Selected(strip));
        Assert.True(strip.Items[2].IsPlayhead);
        Assert.False(strip.Items[20].IsPlayhead);
    }

    [Fact]
    public void RemovingKeepsThePlayheadOnItsPhotoAndSaysSo()
    {
        var (shell, player, strip) = Strip(100);
        var announced = Announcements(strip);
        strip.Click(50, false, false);
        var shown = strip.Items[50].Id;
        strip.Click(10, false, control: true);
        strip.Click(12, shift: true, control: true);
        player.Seek(50);

        Assert.Equal(4, strip.Remove());

        Assert.Equal(96, strip.Items.Count);
        Assert.Equal(96, shell.CurrentProject.Model.Frames.Length);
        Assert.Equal(shown, strip.Items[player.CurrentFrame].Id);
        Assert.Equal(46, player.CurrentFrame);
        Assert.Equal(Selected(strip), [46]);
        Assert.Equal(Enumerable.Range(1, 96), strip.Items.Select(item => item.Number));
        Assert.Equal(["Removed 4 frames from the project. The photos stay on your PC. Ctrl+Z puts them back."], announced);
        Assert.True(strip.CanUndo);
        Assert.True(strip.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void RemovingTheFrameOnScreenShowsTheNextOneAndDeleteAgainRemovesThatOne()
    {
        var (_, player, strip) = Strip(10);
        strip.Click(4, false, false);
        var next = strip.Items[5].Id;

        Assert.True(strip.Handle(FilmstripAction.Remove, 4).Handled);

        Assert.Equal(4, player.CurrentFrame);
        Assert.Equal(next, strip.Items[4].Id);
        Assert.Equal(Selected(strip), [4]);
        Assert.Equal(1, strip.Remove());
        Assert.Equal(8, strip.Items.Count);
    }

    [Fact]
    public void UndoPutsTheFramesBackInPlaceSelectsThemAndSaysSo()
    {
        var (shell, player, strip) = Strip(30);
        var original = shell.CurrentProject.Model.Frames.Select(frame => frame.Id).ToList();
        var announced = Announcements(strip);
        strip.Click(3, false, false);
        strip.Click(7, false, control: true);
        strip.Click(20, false, control: true);
        strip.Remove();

        Assert.True(strip.Undo());

        Assert.Equal(original, strip.Items.Select(item => item.Id));
        Assert.Equal(original, shell.CurrentProject.Model.Frames.Select(frame => frame.Id));
        Assert.Equal(Selected(strip), [3, 7, 20]);
        Assert.Equal(3, player.CurrentFrame);
        Assert.Equal("Put 3 frames back.", announced[^1]);
        Assert.False(strip.CanUndo);
        Assert.False(strip.UndoCommand.CanExecute(null));

        Assert.False(strip.Undo());
        Assert.Equal(ProjectText.FilmstripNothingToUndo, announced[^1]);
    }

    [Fact]
    public void AnUndoAfterAReorderUndoesTheReorderFirst()
    {
        var (shell, _, strip) = Strip(10);
        var project = shell.CurrentProject.Model;
        var original = project.Frames.Select(frame => frame.Id).ToList();
        strip.Click(2, false, false);
        strip.Remove();
        var afterRemoval = project.Frames.Select(frame => frame.Id).ToList();
        strip.Click(0, false, false);
        strip.MoveTo(5);

        Assert.Equal(SortMode.Manual, project.SortMode);
        strip.Undo();
        Assert.Equal(afterRemoval, strip.Items.Select(item => item.Id));
        Assert.Equal(SortMode.CaptureTime, project.SortMode);
        strip.Undo();
        Assert.Equal(original, strip.Items.Select(item => item.Id));
    }

    [Fact]
    public void ADropMovesTheSelectionAsABlockAndSwitchesToManual()
    {
        var (shell, _, strip) = Strip(10);
        var project = shell.CurrentProject.Model;
        var ids = project.Frames.Select(frame => frame.Id).ToList();
        var announced = Announcements(strip);
        strip.Click(1, false, false);
        strip.Click(3, false, control: true);

        Assert.True(strip.MoveTo(7));

        Assert.Equal([ids[0], ids[2], ids[4], ids[5], ids[6], ids[1], ids[3], ids[7], ids[8], ids[9]], strip.Items.Select(item => item.Id));
        Assert.Equal(strip.Items.Select(item => item.Id), project.Frames.Select(frame => frame.Id));
        Assert.Equal(SortMode.Manual, project.SortMode);
        Assert.Equal(Selected(strip), [5, 6]);
        Assert.Equal("Moved 2 frames to place 6.", announced[^1]);
    }

    [Fact]
    public void ADropWhereTheFramesAlreadyAreChangesNothing()
    {
        var (shell, _, strip) = Strip(10);
        var project = shell.CurrentProject.Model;
        var version = project.Version;
        strip.Click(4, false, false);

        Assert.False(strip.MoveTo(4));
        Assert.False(strip.MoveTo(5));

        Assert.Equal(version, project.Version);
        Assert.Equal(SortMode.CaptureTime, project.SortMode);
        Assert.False(strip.CanUndo);
    }

    [Fact]
    public void AltArrowsMoveTheSelectionOnePlace()
    {
        var (_, _, strip) = Strip(6);
        var announced = Announcements(strip);
        strip.Click(2, false, false);
        var moved = strip.Items[2].Id;

        var result = strip.Handle(FilmstripAction.MoveEarlier, 2);
        Assert.Equal(new FilmstripKeyResult(true, 1), result);
        Assert.Equal(moved, strip.Items[1].Id);
        Assert.Equal("Moved frame 3 to place 2.", announced[^1]);

        strip.Handle(FilmstripAction.MoveLater, 1);
        strip.Handle(FilmstripAction.MoveLater, 2);
        Assert.Equal(moved, strip.Items[3].Id);

        strip.Click(0, false, false);
        Assert.Equal(new FilmstripKeyResult(true, 0), strip.Handle(FilmstripAction.MoveEarlier, 0));
    }

    [Fact]
    public void TheArrowsSelectShowAndPauseAndShiftExtends()
    {
        var (_, player, strip) = Strip(20);
        strip.Click(5, false, false);
        player.Play();

        Assert.Equal(new FilmstripKeyResult(true, 6), strip.Handle(FilmstripAction.Next, 5));
        Assert.False(player.IsPlaying);
        Assert.Equal(6, player.CurrentFrame);
        Assert.Equal(Selected(strip), [6]);

        strip.Handle(FilmstripAction.ExtendNext, 6);
        strip.Handle(FilmstripAction.ExtendNext, 7);
        Assert.Equal(Selected(strip), [6, 7, 8]);
        Assert.Equal(8, player.CurrentFrame);

        Assert.Equal(new FilmstripKeyResult(true, 9), strip.Handle(FilmstripAction.FocusNext, 8));
        Assert.Equal(Selected(strip), [6, 7, 8]);
        strip.Handle(FilmstripAction.Toggle, 12);
        Assert.Equal(Selected(strip), [6, 7, 8, 12]);

        strip.Handle(FilmstripAction.SelectAll, 12);
        Assert.Equal(20, strip.SelectedCount);
        Assert.True(strip.Handle(FilmstripAction.Collapse, 12).Handled);
        Assert.Equal(Selected(strip), [player.CurrentFrame]);
        Assert.False(strip.Handle(FilmstripAction.Collapse, 12).Handled);

        strip.Handle(FilmstripAction.Last, 0);
        Assert.Equal(19, player.CurrentFrame);
        strip.Handle(FilmstripAction.ExtendFirst, 19);
        Assert.Equal(20, strip.SelectedCount);
        Assert.False(strip.Handle(FilmstripAction.None, 3).Handled);
    }

    [Fact]
    public void GapMarkersSitInFrontOfTheFrameThatJumpedAndNotInManualOrder()
    {
        var shell = new ShellViewModel(new FakeDialogService(), new FakeErrorSink());
        var names = new[] { 1, 2, 3, 4, 8, 9, 10 };
        shell.CurrentProject.Model.AddFrames(names.Select(n => TestFrames.At($@"C:\Photos\a\IMG_{n:0000}.jpg")));
        shell.CurrentProject.Model.ApplySort(SortMode.Name);
        var player = new PlayerViewModel(shell, new FakeErrorSink(), () => _now, 1000);
        using var strip = new FilmstripViewModel(shell, player, new FakeErrorSink());
        var invalidated = 0;
        strip.GapsInvalidated += (_, _) => invalidated++;

        strip.RefreshGaps();

        Assert.Equal(1, strip.GapCount);
        Assert.Equal(3, strip.Items[4].GapBefore!.MissingFrames);
        Assert.Equal("3 photos seem to be missing before this one: the file numbers skip 3.", strip.Items[4].GapText);
        Assert.Null(strip.Items[3].GapBefore);

        strip.Click(0, false, false);
        strip.MoveTo(2);
        Assert.Equal(1, invalidated);
        strip.RefreshGaps();
        Assert.Equal(0, strip.GapCount);
        Assert.All(strip.Items, item => Assert.Null(item.GapBefore));
    }

    [Fact]
    public void ALongPauseIsAMarkerToo()
    {
        var gap = new FrameGap(4, FrameGapKinds.CaptureInterval, 0, TimeSpan.FromSeconds(130), TimeSpan.FromSeconds(5));

        Assert.Equal("Photos may be missing before this one: it was taken 2 min 10 s after the one before, where about 5.0 s was usual.", ProjectText.FilmstripGap(gap));
        Assert.Equal("1 photo seems to be missing before this one: the file numbers skip one.", ProjectText.FilmstripGap(gap with { Kinds = FrameGapKinds.Numbering, MissingFrames = 1 }));
    }

    [Fact]
    public void PendingFramesAreThoseFromTheBoundaryOn()
    {
        var (_, _, strip) = Strip(10);

        strip.SetPendingFrom(6);
        Assert.Equal([false, false, false, false, false, false, true, true, true, true], strip.Items.Select(item => item.IsPending));

        strip.SetPendingFrom(null);
        Assert.All(strip.Items, item => Assert.False(item.IsPending));
    }

    [Fact]
    public void TheThumbnailHeightSnapsToTheGridWithinItsRange()
    {
        var (_, _, strip) = Strip(1);

        Assert.Equal(32, strip.ThumbnailHeight);
        Assert.Equal(48, strip.ThumbnailWidth);
        strip.ThumbnailHeight = 61;
        Assert.Equal(60, strip.ThumbnailHeight);
        Assert.Equal(90, strip.ThumbnailWidth);
        strip.ThumbnailHeight = 500;
        Assert.Equal(96, strip.ThumbnailHeight);
        strip.ThumbnailHeight = double.NaN;
        Assert.Equal(96, strip.ThumbnailHeight);
        strip.ThumbnailHeight = 0;
        Assert.Equal(32, strip.ThumbnailHeight);
    }

    [Fact]
    public void TheFileCaptionHasTheCaptureTime()
    {
        var shell = new ShellViewModel(new FakeDialogService(), new FakeErrorSink());
        shell.CurrentProject.Model.AddFrames([TestFrames.At(@"C:\Photos\a\IMG_0312.JPG") with { CaptureTime = new DateTime(2026, 6, 21, 19, 26, 5) }]);
        var player = new PlayerViewModel(shell, new FakeErrorSink(), () => _now, 1000);
        using var strip = new FilmstripViewModel(shell, player, new FakeErrorSink());

        Assert.Equal("IMG_0312.JPG · 19:26:05", strip.FileText);
        Assert.Equal("Frame 0001 of 0001", strip.CaptionText);
    }

    [Fact]
    public void ALargeRemovalOrSortRebuildsTheListInOnePiece()
    {
        var (shell, player, strip) = Strip(500);
        var resets = 0;
        strip.Items.CollectionChanged += (_, e) => resets += e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset ? 1 : 0;
        strip.Click(100, false, false);
        strip.Click(399, shift: true, control: false);

        Assert.Equal(300, strip.Remove());
        Assert.Equal(1, resets);
        Assert.Equal(200, strip.Items.Count);
        Assert.Equal(Enumerable.Range(1, 200), strip.Items.Select(item => item.Number));
        Assert.Equal(shell.CurrentProject.Model.Frames.Select(frame => frame.Id), strip.Items.Select(item => item.Id));
        Assert.Equal(100, player.CurrentFrame);

        strip.Undo();
        Assert.Equal(2, resets);
        Assert.Equal(500, strip.Items.Count);
        Assert.Equal(shell.CurrentProject.Model.Frames.Select(frame => frame.Id), strip.Items.Select(item => item.Id));
    }

    [Fact]
    public void ANewProjectStartsAFreshStripAndHistory()
    {
        var (shell, _, strip) = Strip(5);
        strip.Click(1, false, false);
        strip.Remove();
        Assert.True(shell.ReplaceWithoutAsking());
        Assert.Empty(strip.Items);
        Assert.Equal(string.Empty, strip.CaptionText);
        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("b", 2));

        Assert.Equal(2, strip.Items.Count);
        Assert.False(strip.CanUndo);
        Assert.Equal(Selected(strip), [0]);
        Assert.Equal("Frame 0001 of 0002", strip.CaptionText);
        Assert.False(strip.Undo());
    }

    [Fact]
    public void FrameMovesPlaceTheBlockAtTheSeam()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();

        Assert.Equal([ids[1], ids[2], ids[0], ids[3], ids[4], ids[5]], FrameMoves.Move(ids, new HashSet<Guid> { ids[0] }, 3));
        Assert.Equal([ids[4], ids[0], ids[1], ids[2], ids[3], ids[5]], FrameMoves.Move(ids, new HashSet<Guid> { ids[4] }, 0));
        Assert.Equal([ids[0], ids[2], ids[3], ids[4], ids[5], ids[1]], FrameMoves.Move(ids, new HashSet<Guid> { ids[1] }, 99));
        Assert.Equal(ids, FrameMoves.Move(ids, new HashSet<Guid> { ids[2], ids[3] }, 3));
    }

    private static List<int> Selected(FilmstripViewModel strip) =>
        [.. strip.Items.Where(item => item.IsSelected).Select(item => item.Number - 1)];

    private static List<string> Announcements(FilmstripViewModel strip)
    {
        var said = new List<string>();
        strip.Announced += (_, text) => said.Add(text);
        return said;
    }

    private (ShellViewModel Shell, PlayerViewModel Player, FilmstripViewModel Strip) Strip(int frames)
    {
        var shell = new ShellViewModel(new FakeDialogService(), new FakeErrorSink());
        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", frames));
        var player = new PlayerViewModel(shell, new FakeErrorSink(), () => _now, 1000);
        return (shell, player, new FilmstripViewModel(shell, player, new FakeErrorSink()));
    }
}
