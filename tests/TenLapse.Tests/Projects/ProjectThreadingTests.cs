using TenLapse.Core.Projects;

namespace TenLapse.Tests.Projects;

public sealed class ProjectThreadingTests
{
    [Fact]
    public void AReaderOnAnotherThreadNeverSeesATornFrameList()
    {
        var project = new Project();
        var frames = TestFrames.Folder("a", 500);
        project.AddFrames(frames);
        var order = frames.Select(frame => frame.Id).ToArray();
        using var reading = new ManualResetEventSlim();
        var stop = false;
        var torn = 0;
        var reads = 0;
        Exception? failure = null;
        var reader = new Thread(() =>
        {
            try
            {
                while (!Volatile.Read(ref stop))
                {
                    var snapshot = project.Frames;
                    if (snapshot.Count() != 500 || snapshot.Distinct().Count() != 500)
                    {
                        torn++;
                    }

                    reads++;
                    reading.Set();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                reading.Set();
            }
        });
        reader.Start();
        Assert.True(reading.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        for (var i = 0; i < 300 && failure is null; i++)
        {
            Array.Reverse(order);
            project.ReorderFrames(order);
        }

        Volatile.Write(ref stop, true);
        Assert.True(reader.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
        Assert.Equal(0, torn);
        Assert.True(reads > 0);
    }

    [Fact]
    public void ASnapshotTakenBeforeAChangeKeepsItsFramesAndVersion()
    {
        var project = new Project();
        var frames = TestFrames.Folder("a", 3);
        project.AddFrames(frames);
        var before = project.Snapshot;

        project.RemoveFrames([frames[0].Id]);

        Assert.Equal(frames, before.Frames);
        Assert.Equal(2, project.Frames.Length);
        Assert.True(project.Version > before.Version);
        Assert.Equal(project.Version, project.Snapshot.Version);
    }

    [Fact]
    public void TheVersionGrowsWithEveryRealChangeOnly()
    {
        var project = new Project();
        var versions = new List<long> { project.Version };

        project.AddFrames(TestFrames.Folder("a", 2));
        versions.Add(project.Version);
        project.Name = "Harbor sunset";
        versions.Add(project.Version);
        project.Name = "Harbor sunset";
        versions.Add(project.Version);
        project.SortMode = SortMode.Name;
        versions.Add(project.Version);

        Assert.Equal([0, 1, 2, 2, 3], versions);
    }

    [Fact]
    public async Task OnlyTheThreadThatCreatedTheProjectMayChangeIt()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 2));

        var count = await Task.Run(() => project.Frames.Length, TestContext.Current.CancellationToken);
        var mutations = new Action[]
        {
            () => project.AddFrames(TestFrames.Folder("b", 1)),
            () => project.RemoveFrames([project.Frames[0].Id]),
            () => project.ReorderFrames([project.Frames[1].Id, project.Frames[0].Id]),
            () => project.AddSkippedFiles([new SkippedFile(@"C:\Photos\a\x.jpg", SkipReason.Other, "")]),
            () => project.Name = "Other",
            () => project.SortMode = SortMode.Manual,
            () => project.FrameSettings = project.FrameSettings with { Fit = FitMode.Fit },
            () => project.Output = project.Output with { Quality = OutputQuality.Maximum },
        };

        foreach (var mutation in mutations)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => RunOnNewThread(mutation));
        }

        Assert.Equal(2, count);
        Assert.Equal(2, project.Frames.Length);
        Assert.Equal(Project.DefaultName, project.Name);
    }

    private static Task RunOnNewThread(Action action)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        });
        thread.Start();
        return done.Task;
    }
}
