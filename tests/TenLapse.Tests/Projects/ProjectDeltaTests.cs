using TenLapse.Core.Projects;

namespace TenLapse.Tests.Projects;

public sealed class ProjectDeltaTests
{
    [Fact]
    public void EachFrameChangeSaysWhatHappened()
    {
        var project = new Project();
        var events = new List<ProjectChangedEventArgs>();
        project.Changed += (_, e) => events.Add(e);
        var first = TestFrames.Folder("a", 3);
        var second = TestFrames.Folder("b", 2);

        project.AddFrames(first);
        project.AddFrames(second);
        project.RemoveFrames([second[0].Id, first[1].Id]);
        project.ReorderFrames([.. project.Frames.Reverse().Select(frame => frame.Id)]);
        project.Name = "Harbor sunset";

        Assert.Equal(5, events.Count);
        AssertAdded(events[0].Frames, 0, first);
        AssertAdded(events[1].Frames, 3, second);
        var removed = events[2].Frames!;
        Assert.Equal(FrameChangeKind.Removed, removed.Kind);
        Assert.Equal([1, 3], removed.Indices);
        Assert.Equal([first[1].Id, second[0].Id], removed.Ids);
        Assert.Equal(2, removed.Count);
        Assert.Equal(FrameChangeKind.Reset, events[3].Frames!.Kind);
        Assert.Null(events[4].Frames);
        Assert.Equal(project.Snapshot, events[4].Snapshot);
    }

    [Fact]
    public void UndoPutsDeletedFramesBackInPlaceInOneChange()
    {
        var project = new Project();
        var frames = TestFrames.Folder("a", 5);
        project.AddFrames(frames);
        project.RemoveFrames([frames[1].Id, frames[2].Id]);
        var events = new List<ProjectChangedEventArgs>();
        project.Changed += (_, e) => events.Add(e);

        var restored = project.InsertFrames(1, [frames[1], frames[2]]);

        Assert.Equal(2, restored);
        Assert.Equal(frames, project.Frames);
        AssertAdded(Assert.Single(events).Frames, 1, [frames[1], frames[2]]);
    }

    [Fact]
    public void InsertFramesChecksTheIndexAndLeavesOutDuplicates()
    {
        var project = new Project();
        var frames = TestFrames.Folder("a", 2);
        project.AddFrames(frames);
        var changed = 0;
        project.Changed += (_, _) => changed++;

        Assert.Throws<ArgumentOutOfRangeException>(() => project.InsertFrames(-1, TestFrames.Folder("b", 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => project.InsertFrames(3, TestFrames.Folder("b", 1)));
        Assert.Equal(0, project.InsertFrames(0, [frames[1], TestFrames.At(frames[0].SourcePath)]));
        Assert.Equal(0, changed);

        var front = TestFrames.At(@"C:\Photos\a\IMG_0000.jpg");
        Assert.Equal(1, project.InsertFrames(0, [front]));
        Assert.Equal([front, .. frames], project.Frames);
    }

    [Fact]
    public void ReplayingTheDeltasRebuildsTheList()
    {
        var project = new Project();
        var mirror = new List<Frame>();
        project.Changed += (_, e) =>
        {
            switch (e.Frames?.Kind)
            {
                case FrameChangeKind.Added:
                    mirror.InsertRange(e.Frames.Index, e.Snapshot.Frames.Skip(e.Frames.Index).Take(e.Frames.Count));
                    break;
                case FrameChangeKind.Removed:
                    foreach (var index in e.Frames.Indices.Reverse())
                    {
                        mirror.RemoveAt(index);
                    }

                    break;
                case FrameChangeKind.Reset:
                    mirror = [.. e.Snapshot.Frames];
                    break;
            }
        };
        var frames = TestFrames.Folder("a", 8);

        project.AddFrames(frames[..4]);
        project.InsertFrames(2, frames[4..]);
        project.RemoveFrames([frames[0].Id, frames[5].Id, frames[3].Id]);
        project.InsertFrames(0, [frames[0]]);

        Assert.Equal(project.Frames, mirror);
    }

    private static void AssertAdded(FrameChange? change, int index, Frame[] frames)
    {
        Assert.NotNull(change);
        Assert.Equal(FrameChangeKind.Added, change.Kind);
        Assert.Equal(index, change.Index);
        Assert.Equal(frames.Length, change.Count);
        Assert.Equal(frames.Select(frame => frame.Id), change.Ids);
        Assert.Equal(Enumerable.Range(index, frames.Length), change.Indices);
    }
}
