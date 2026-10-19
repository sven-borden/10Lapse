using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;

namespace TenLapse.Tests.Projects;

public sealed class FrameHistoryTests
{
    [Fact]
    public void UndoPutsScatteredRemovedFramesBackInTheirPlacesInOneChange()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 10));
        var original = Ids(project);
        var history = new FrameHistory(project);

        Assert.Equal(4, history.Remove([original[1], original[3], original[4], original[8]]));
        Assert.Equal(6, project.Frames.Length);
        Assert.Equal(new FrameEdit(FrameEditKind.Removal, 4), history.NextUndo);

        var events = Record(project);
        var undo = history.Undo();

        Assert.NotNull(undo);
        Assert.Equal(FrameEditKind.Removal, undo.Kind);
        Assert.Equal([original[1], original[3], original[4], original[8]], undo.Frames);
        Assert.Equal(original, Ids(project));
        var change = Assert.Single(events);
        Assert.Equal(FrameChangeKind.Added, change.Frames!.Kind);
        Assert.Equal([1, 3, 4, 8], change.Frames.Indices);
        Assert.Equal(1, change.Frames.Index);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void AnUndoAfterAnImportPutsTheFramesBesideTheirOldNeighbours()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 6));
        var original = Ids(project);
        var history = new FrameHistory(project);
        history.Remove([original[0], original[3]]);
        var added = TestFrames.Folder("b", 2);
        project.AddFrames(added);

        history.Undo();

        Assert.Equal([original[0], original[1], original[2], original[3], original[4], original[5], added[0].Id, added[1].Id], Ids(project));
    }

    [Fact]
    public void AFrameWhoseNeighbourLeftTooGoesAfterTheNearestEarlierOneStillThere()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 5));
        var o = Ids(project);
        var history = new FrameHistory(project);
        history.Remove([o[3]]);

        // Removed outside the history (an import that found the photo unreadable): stays out.
        project.RemoveFrames([o[2]]);
        history.Undo();

        Assert.Equal([o[0], o[1], o[3], o[4]], Ids(project));
    }

    [Fact]
    public void UndoingAReorderRestoresTheOrderAndTheSortMode()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 5));
        project.ApplySort(SortMode.Name);
        var o = Ids(project);
        var history = new FrameHistory(project);

        Assert.True(history.Reorder([o[1], o[0], o[2], o[3], o[4]], [o[0]]));
        Assert.Equal(SortMode.Manual, project.SortMode);

        var undo = history.Undo();

        Assert.Equal(new FrameUndo(FrameEditKind.Reorder, [o[0]]).Kind, undo!.Kind);
        Assert.Equal([o[0]], undo.Frames);
        Assert.Equal(o, Ids(project));
        Assert.Equal(SortMode.Name, project.SortMode);
    }

    [Fact]
    public void ARemovalThenAReorderUndoInTurn()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 6));
        var o = Ids(project);
        var history = new FrameHistory(project);
        history.Remove([o[1], o[2]]);
        history.Reorder([o[5], o[0], o[3], o[4]], [o[5]]);

        Assert.Equal(FrameEditKind.Reorder, history.Undo()!.Kind);
        Assert.Equal([o[0], o[3], o[4], o[5]], Ids(project));
        Assert.Equal(FrameEditKind.Removal, history.Undo()!.Kind);
        Assert.Equal(o, Ids(project));
        Assert.Null(history.Undo());
    }

    [Fact]
    public void AnUndoOfARemovalAfterAReorderFollowsTheNewOrder()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 5));
        var o = Ids(project);
        var history = new FrameHistory(project);
        history.Reorder([o[4], o[0], o[1], o[2], o[3]]);
        history.Remove([o[1]]);

        history.Undo();

        Assert.Equal([o[4], o[0], o[1], o[2], o[3]], Ids(project));
        Assert.Equal(SortMode.Manual, project.SortMode);
    }

    [Fact]
    public void AFrameImportedAgainMeanwhileIsNotPutBackTwice()
    {
        var project = new Project();
        var frames = TestFrames.Folder("a", 3);
        project.AddFrames(frames);
        var history = new FrameHistory(project);
        history.Remove([frames[1].Id]);
        project.AddFrames([TestFrames.At(frames[1].SourcePath)]);

        var undo = history.Undo();

        Assert.Empty(undo!.Frames);
        Assert.Equal(3, project.Frames.Length);
    }

    [Fact]
    public void NothingRemovedOrNoChangeRecordsNothing()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 3));
        var history = new FrameHistory(project);

        Assert.Equal(0, history.Remove([Guid.NewGuid()]));
        project.SortMode = SortMode.Manual;
        Assert.False(history.Reorder(Ids(project)));
        Assert.False(history.CanUndo);
        Assert.Null(history.Undo());
    }

    [Fact]
    public void ItKeepsTheLatestHundredEdits()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", FrameHistory.Capacity + 5));
        var history = new FrameHistory(project);
        foreach (var id in Ids(project).Take(FrameHistory.Capacity + 5))
        {
            history.Remove([id]);
        }

        var undone = 0;
        while (history.Undo() is not null)
        {
            undone++;
        }

        Assert.Equal(FrameHistory.Capacity, undone);
        Assert.Equal(FrameHistory.Capacity, project.Frames.Length);
    }

    [Fact]
    public void RemovingNeverTouchesTheFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", "history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "IMG_0001.jpg");
            File.WriteAllBytes(path, [1, 2, 3]);
            var project = new Project();
            project.AddFrames([new Frame(path, 3, File.GetLastWriteTimeUtc(path))]);
            var history = new FrameHistory(project);

            history.Remove([project.Frames[0].Id]);

            Assert.True(File.Exists(path));
            Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ARestoreWithABadOrderChangesNothing()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 3));
        var o = Ids(project);
        var back = TestFrames.At(@"C:\Photos\b\IMG_0001.jpg");
        var version = project.Version;

        // The frames already there out of their order, one missing, an unknown id, a repeat.
        Assert.Throws<ArgumentException>(() => project.RestoreFrames([back], [o[1], o[0], back.Id, o[2]]));
        Assert.Throws<ArgumentException>(() => project.RestoreFrames([back], [o[0], back.Id, o[2]]));
        Assert.Throws<ArgumentException>(() => project.RestoreFrames([back], [o[0], o[1], back.Id, o[2], Guid.NewGuid()]));
        Assert.Throws<ArgumentException>(() => project.RestoreFrames([back], [o[0], o[1], back.Id, back.Id, o[2]]));
        Assert.Equal(version, project.Version);

        Assert.Equal(1, project.RestoreFrames([back], [o[0], back.Id, o[1], o[2]]));
        Assert.Equal([o[0], back.Id, o[1], o[2]], Ids(project));
    }

    private static List<Guid> Ids(Project project) => [.. project.Frames.Select(frame => frame.Id)];

    private static List<ProjectChangedEventArgs> Record(Project project)
    {
        var events = new List<ProjectChangedEventArgs>();
        project.Changed += (_, e) => events.Add(e);
        return events;
    }
}
