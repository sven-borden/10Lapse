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
        _now += 500;
        player.Tick();
        Assert.Equal(28, player.CurrentFrame);
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
        strip.Click(10, false, false);
        strip.Click(12, shift: true, control: false);
        strip.Click(30, false, control: true);
        var shown = strip.Items[50].Id;
        player.Seek(50);
        Assert.Equal(Selected(strip), [10, 11, 12, 30]);

        Assert.Equal(4, strip.Remove());

        Assert.Equal(96, strip.Items.Count);
        Assert.Equal(96, shell.CurrentProject.Model.Frames.Length);
        Assert.Equal(shown, strip.Items[player.CurrentFrame].Id);
        Assert.Equal(46, player.CurrentFrame);
        Assert.Equal(Selected(strip), [46]);
        Assert.True(strip.Items[46].IsPlayhead);
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

    // #82 review, M1: a drag started while the video plays moves the frame that was grabbed, not the one on screen at the drop.
    [Fact]
    public void ADragWhilePlayingMovesTheGrabbedFrame()
    {
        var (shell, player, strip) = Strip(30);
        var pointer = new FilmstripPointer(strip);
        var grabbed = strip.Items[3].Id;
        player.Play();
        _now += 100;
        player.Tick();

        pointer.Press(3, 100, 10, 1, shift: false, control: false, touch: false);
        Assert.Equal(FilmstripPointerResult.DragStarted, pointer.Move(1, 120, 10));
        Assert.False(player.IsPlaying);
        Assert.Equal([3], Selected(strip));

        // Even when the video plays again during the drag (Space), the selection stays and the drop moves frame 3.
        player.Play();
        _now += 200;
        player.Tick();
        Assert.NotEqual(3, player.CurrentFrame);
        Assert.Equal([3], Selected(strip));
        pointer.Over(0);
        Assert.Equal(FilmstripPointerResult.Dropped, pointer.Release(1));

        Assert.Equal(grabbed, shell.CurrentProject.Model.Frames[0].Id);
        Assert.False(strip.IsDragging);
    }

    [Fact]
    public void AltArrowWhilePlayingMovesTheChosenFrame()
    {
        var (shell, player, strip) = Strip(30);
        strip.Click(10, false, false);
        var chosen = strip.Items[10].Id;
        player.Play();
        _now += 300;
        player.Tick();
        Assert.NotEqual([10], Selected(strip));

        strip.Handle(FilmstripAction.MoveEarlier, 10);

        Assert.False(player.IsPlaying);
        Assert.Equal(chosen, shell.CurrentProject.Model.Frames[9].Id);
    }

    [Fact]
    public void DeleteOnAFocusedThumbnailWhilePlayingRemovesThatFrame()
    {
        var (shell, player, strip) = Strip(30);
        strip.Click(10, false, false);
        var chosen = strip.Items[10].Id;
        player.Play();
        _now += 300;
        player.Tick();

        Assert.True(strip.Handle(FilmstripAction.Remove, 10).Handled);

        Assert.False(player.IsPlaying);
        Assert.Equal(29, strip.Items.Count);
        Assert.DoesNotContain(shell.CurrentProject.Model.Frames, frame => frame.Id == chosen);
    }

    [Fact]
    public void AStripKeyActsOnASelectionTheUserMadeNotOnTheFocus()
    {
        var (shell, _, strip) = Strip(30);
        strip.Click(4, false, false);
        strip.Click(6, false, control: true);
        var picked = new[] { strip.Items[4].Id, strip.Items[6].Id };

        strip.Handle(FilmstripAction.Remove, 12);

        Assert.Equal(28, strip.Items.Count);
        Assert.All(picked, id => Assert.DoesNotContain(shell.CurrentProject.Model.Frames, frame => frame.Id == id));
    }

    [Fact]
    public void DeleteWhilePlayingRemovesTheFrameOnScreenAndPauses()
    {
        var (shell, player, strip) = Strip(30);
        strip.Click(5, false, false);
        player.Play();
        _now += 100;
        player.Tick();
        var shown = strip.Items[player.CurrentFrame].Id;

        Assert.True(strip.Handle(FilmstripAction.Remove, -1).Handled);

        Assert.False(player.IsPlaying);
        Assert.DoesNotContain(shell.CurrentProject.Model.Frames, frame => frame.Id == shown);
        Assert.Equal(29, strip.Items.Count);
    }

    [Fact]
    public void TheContextMenuPausesAndSelectsTheFrameUnderTheRightClick()
    {
        var (_, player, strip) = Strip(30);
        strip.Click(2, false, false);
        strip.Click(4, false, control: true);
        player.Play();

        strip.PrepareContextMenu(4);
        Assert.False(player.IsPlaying);
        Assert.Equal([2, 4], Selected(strip));

        strip.PrepareContextMenu(9);
        Assert.Equal([9], Selected(strip));
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

    // #82 review, M4: a frame the user removed leaves no "photos seem to be missing" marker; photos that were never
    // there still do, and a gap in front of a removed frame moves to the next frame still there.
    [Fact]
    public void AHoleTheUserMadeIsNoGap()
    {
        var shell = new ShellViewModel(new FakeDialogService(), new FakeErrorSink());
        var names = new[] { 1, 2, 3, 4, 8, 9, 10, 11, 12 };
        shell.CurrentProject.Model.AddFrames(names.Select(n => TestFrames.At($@"C:\Photos\a\IMG_{n:0000}.jpg")));
        shell.CurrentProject.Model.ApplySort(SortMode.Name);
        var player = new PlayerViewModel(shell, new FakeErrorSink(), () => _now, 1000);
        using var strip = new FilmstripViewModel(shell, player, new FakeErrorSink());

        // IMG_0010 and IMG_0011 out: no marker in front of IMG_0012.
        strip.Click(6, false, false);
        strip.Click(7, false, control: true);
        strip.Remove();
        strip.RefreshGaps();
        Assert.Equal(1, strip.GapCount);
        Assert.Equal("IMG_0008.jpg", strip.Items.Single(item => item.GapBefore is not null).Frame.FileName);

        // IMG_0008 out too: the 3 photos really missing (5 to 7) are now in front of IMG_0009.
        strip.Click(4, false, false);
        strip.Remove();
        strip.RefreshGaps();
        var marked = Assert.Single(strip.Items, item => item.GapBefore is not null);
        Assert.Equal("IMG_0009.jpg", marked.Frame.FileName);
        Assert.Equal(3, marked.GapBefore!.MissingFrames);

        // Put back with Ctrl+Z, nothing changes for the markers.
        strip.Undo();
        strip.Undo();
        strip.RefreshGaps();
        Assert.Equal(1, strip.GapCount);
        Assert.Equal(3, strip.Items[4].GapBefore!.MissingFrames);
    }

    [Fact]
    public void ALongPauseTheUserMadeIsNoGapEither()
    {
        var shell = new ShellViewModel(new FakeDialogService(), new FakeErrorSink());
        var start = new DateTime(2026, 10, 1, 18, 0, 0);
        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", 20).Select((frame, i) => frame with { CaptureTime = start.AddSeconds(5 * i) }));
        shell.CurrentProject.Model.ApplySort(SortMode.CaptureTime);
        var player = new PlayerViewModel(shell, new FakeErrorSink(), () => _now, 1000);
        using var strip = new FilmstripViewModel(shell, player, new FakeErrorSink());

        strip.Click(8, false, false);
        strip.Click(11, shift: true, control: false);
        strip.Remove();
        strip.RefreshGaps();

        Assert.Equal(0, strip.GapCount);
    }

    // #82 review, minor 1: an undo leaves the frames in the order of the sort mode the project claims.
    [Fact]
    public void UndoingARemovalAfterASortKeepsTheSortTrue()
    {
        var shell = new ShellViewModel(new FakeDialogService(), new FakeErrorSink());
        var project = shell.CurrentProject.Model;
        var start = new DateTime(2026, 10, 1, 10, 0, 0);
        project.AddFrames(TestFrames.Folder("a", 5).Select((frame, i) => frame with { CaptureTime = start.AddMinutes(5 - i) }));
        project.ApplySort(SortMode.Name);
        var player = new PlayerViewModel(shell, new FakeErrorSink(), () => _now, 1000);
        using var strip = new FilmstripViewModel(shell, player, new FakeErrorSink());

        strip.Click(2, false, false);
        strip.Remove();
        project.ApplySort(SortMode.CaptureTime);
        strip.Undo();

        Assert.Equal(SortMode.CaptureTime, project.SortMode);
        Assert.Equal(FrameSorter.Sort(project.Frames, SortMode.CaptureTime), project.Frames);
        Assert.Equal(strip.Items.Select(item => item.Id), project.Frames.Select(frame => frame.Id));
    }

    [Fact]
    public void UndoingAMoveAfterAnImportKeepsTheSortTrue()
    {
        var (shell, _, strip) = Strip(5);
        var project = shell.CurrentProject.Model;
        project.ApplySort(SortMode.Name);
        strip.Click(4, false, false);
        strip.MoveTo(0);
        project.AddFrames([TestFrames.At(@"C:\Photos\a\IMG_0000.jpg")]);
        project.Resort();

        strip.Undo();

        Assert.Equal(SortMode.Name, project.SortMode);
        Assert.Equal(FrameSorter.Sort(project.Frames, SortMode.Name), project.Frames);
        Assert.Equal("IMG_0000.jpg", project.Frames[0].FileName);
    }

    // #82 review, minor 5: the menu names the edit Ctrl+Z undoes, and an undo with nothing to put back says so.
    [Fact]
    public void TheUndoIsNamedAfterTheEditAndSaysWhenNothingCameBack()
    {
        var (shell, _, strip) = Strip(10);
        var announced = Announcements(strip);
        Assert.Equal("Undo", strip.UndoText);

        strip.Click(2, false, false);
        strip.Click(4, shift: true, control: false);
        var path = strip.Items[3].Frame.SourcePath;
        strip.Remove();
        Assert.Equal("Undo removing 3 frames", strip.UndoText);

        strip.Click(0, false, false);
        strip.MoveTo(3);
        Assert.Equal("Undo move", strip.UndoText);
        strip.Undo();
        Assert.Equal("Undo removing 3 frames", strip.UndoText);

        // All three photos imported again before the undo: nothing comes back, and Narrator hears why.
        var removed = shell.CurrentProject.Model.Frames.Length;
        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", 10).Where(frame => !shell.CurrentProject.Model.Frames.Any(f => f.SourcePath == frame.SourcePath)));
        Assert.Equal(removed + 3, shell.CurrentProject.Model.Frames.Length);
        Assert.Contains(shell.CurrentProject.Model.Frames, frame => frame.SourcePath == path);
        strip.Undo();

        Assert.Equal(ProjectText.FilmstripRestoredNone, announced[^1]);
        Assert.Equal("Undo", strip.UndoText);
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
        var (shell, _, strip) = Strip(10);
        var exporting = shell.CurrentProject.Model.Frames;

        strip.SetPending(exporting, 6);
        Assert.Equal([false, false, false, false, false, false, true, true, true, true], strip.Items.Select(item => item.IsPending));

        strip.ClearPending();
        Assert.All(strip.Items, item => Assert.False(item.IsPending));
    }

    // #82 review, minor 6: an edit during the export keeps the dim on the photos the export has not written yet.
    [Fact]
    public void PendingFollowsThePhotosThroughEditsDuringTheExport()
    {
        var (shell, _, strip) = Strip(10);
        var project = shell.CurrentProject.Model;
        var exporting = project.Frames;
        strip.SetPending(exporting, 4);

        strip.Click(1, false, false);
        strip.Remove();
        project.AddFrames([TestFrames.At(@"C:\Photos\b\IMG_0500.jpg")]);
        strip.SetPending(exporting, 5);

        Assert.Equal(exporting.Skip(5).Select(frame => frame.Id), strip.Items.Where(item => item.IsPending).Select(item => item.Id));
        Assert.False(strip.Items[^1].IsPending);
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

        // The capture time in the regional long time format (#114): 24-hour in most of Europe, "7:26:05 PM" in en-US.
        Assert.Equal($"IMG_0312.JPG · {new DateTime(2026, 6, 21, 19, 26, 5).ToString("T", System.Globalization.CultureInfo.CurrentCulture)}", strip.FileText);
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
        Assert.Equal(Selected(strip), [100]);

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
