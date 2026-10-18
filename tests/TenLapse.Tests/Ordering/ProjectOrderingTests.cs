using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;

namespace TenLapse.Tests.Ordering;

public static class ProjectOrderingTests
{
    private static readonly DateTime _base = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Unspecified);

    private static Frame Photo(string name, double? seconds = null) =>
        new($@"C:\Photos\{name}", 1_000, new DateTime(2026, 5, 2, 8, 0, 0, DateTimeKind.Utc))
        {
            CaptureTime = seconds is { } s ? _base.AddSeconds(s) : null,
        };

    private static Project Filled(params Frame[] frames)
    {
        var project = new Project();
        project.AddFrames(frames);
        return project;
    }

    private static string[] Names(Project project) => [.. project.Frames.Select(frame => frame.FileName)];

    [Fact]
    public static void ApplySortReordersInOneCommitAndRecordsTheMode()
    {
        var project = Filled(Photo("IMG_10.jpg", 1), Photo("IMG_9.jpg", 2), Photo("IMG_2.jpg", 3));
        var changes = new List<ProjectChanges>();
        project.Changed += (_, e) => changes.Add(e.Changes);

        var reordered = project.ApplySort(SortMode.Name);

        Assert.True(reordered);
        Assert.Equal(["IMG_2.jpg", "IMG_9.jpg", "IMG_10.jpg"], Names(project));
        Assert.Equal(SortMode.Name, project.SortMode);
        Assert.Equal([ProjectChanges.Frames, ProjectChanges.SortMode], changes);
    }

    [Fact]
    public static void ApplyingTheSameSortAgainChangesNothing()
    {
        var project = Filled(Photo("IMG_10.jpg"), Photo("IMG_9.jpg"));
        project.ApplySort(SortMode.Name);
        var version = project.Version;
        var raised = 0;
        project.Changed += (_, _) => raised++;

        Assert.False(project.ApplySort(SortMode.Name));
        Assert.False(project.Resort());

        Assert.Equal(version, project.Version);
        Assert.Equal(0, raised);
    }

    [Fact]
    public static void SwitchingModeWithTheOrderAlreadyRightOnlyRecordsTheMode()
    {
        var project = Filled(Photo("IMG_1.jpg", 1), Photo("IMG_2.jpg", 2));
        var changes = new List<ProjectChanges>();
        project.Changed += (_, e) => changes.Add(e.Changes);

        Assert.False(project.ApplySort(SortMode.Name));

        Assert.Equal(SortMode.Name, project.SortMode);
        Assert.Equal([ProjectChanges.SortMode], changes);
    }

    [Fact]
    public static void ManualRecordsTheModeAndKeepsTheFramesWhereTheyAre()
    {
        var project = Filled(Photo("IMG_10.jpg"), Photo("IMG_9.jpg"));

        Assert.False(project.ApplySort(SortMode.Manual));

        Assert.Equal(["IMG_10.jpg", "IMG_9.jpg"], Names(project));
        Assert.Equal(SortMode.Manual, project.SortMode);
    }

    [Fact]
    public static void ADragSwitchesTheModeToManualEvenWhenTheOrderStays()
    {
        var project = Filled(Photo("IMG_1.jpg", 1), Photo("IMG_2.jpg", 2), Photo("IMG_3.jpg", 3));
        project.ApplySort(SortMode.CaptureTime);

        project.ReorderManually([project.Frames[2].Id, project.Frames[0].Id, project.Frames[1].Id]);

        Assert.Equal(["IMG_3.jpg", "IMG_1.jpg", "IMG_2.jpg"], Names(project));
        Assert.Equal(SortMode.Manual, project.SortMode);

        // The same order again: still manual, nothing else changes.
        var version = project.Version;
        project.ReorderManually([.. project.Frames.Select(frame => frame.Id)]);
        Assert.Equal(version, project.Version);
        project.ApplySort(SortMode.Name);
        project.ReorderManually([.. project.Frames.Select(frame => frame.Id)]);
        Assert.Equal(SortMode.Manual, project.SortMode);
    }

    [Fact]
    public static void ABadDragLeavesTheOrderAndTheModeAlone()
    {
        var project = Filled(Photo("IMG_1.jpg", 1), Photo("IMG_2.jpg", 2));
        project.ApplySort(SortMode.CaptureTime);

        Assert.Throws<ArgumentException>(() => project.ReorderManually([project.Frames[0].Id]));
        Assert.Throws<ArgumentException>(() => project.ReorderManually([project.Frames[0].Id, Guid.NewGuid()]));

        Assert.Equal(SortMode.CaptureTime, project.SortMode);
        Assert.Equal(["IMG_1.jpg", "IMG_2.jpg"], Names(project));
    }

    [Fact]
    public static void ResortAfterNewFramesKeepsTheModeAndLeavesManualAlone()
    {
        var project = Filled(Photo("IMG_3.jpg"), Photo("IMG_1.jpg"));
        project.ApplySort(SortMode.Name);
        project.AddFrames([Photo("IMG_2.jpg")]);
        Assert.Equal(["IMG_1.jpg", "IMG_3.jpg", "IMG_2.jpg"], Names(project));

        Assert.True(project.Resort());
        Assert.Equal(["IMG_1.jpg", "IMG_2.jpg", "IMG_3.jpg"], Names(project));
        Assert.Equal(SortMode.Name, project.SortMode);

        project.ReorderManually([project.Frames[2].Id, project.Frames[1].Id, project.Frames[0].Id]);
        project.AddFrames([Photo("IMG_0.jpg")]);
        Assert.False(project.Resort());
        Assert.Equal(["IMG_3.jpg", "IMG_2.jpg", "IMG_1.jpg", "IMG_0.jpg"], Names(project));
    }

    [Fact]
    public static void ImportThatFillsCaptureTimesLaterCanResortByThem()
    {
        // Import adds the frames first (names only), then reads the EXIF times in the background and writes them back.
        var project = Filled(Photo("DSC09990.jpg"), Photo("IMG_0001.jpg"), Photo("DSC09991.jpg"), Photo("IMG_0002.jpg"));
        Assert.Equal(SortMode.Name, project.ApplyDefaultSort());
        Assert.Equal(["DSC09990.jpg", "DSC09991.jpg", "IMG_0001.jpg", "IMG_0002.jpg"], Names(project));

        var times = new Dictionary<string, double> { ["IMG_0001.jpg"] = 0, ["DSC09990.jpg"] = 5, ["IMG_0002.jpg"] = 10, ["DSC09991.jpg"] = 15 };
        project.UpdateFrames(project.Frames.Select(frame => frame with { CaptureTime = _base.AddSeconds(times[frame.FileName]) }));

        Assert.Equal(SortMode.CaptureTime, project.ApplyDefaultSort());
        Assert.Equal(["IMG_0001.jpg", "DSC09990.jpg", "IMG_0002.jpg", "DSC09991.jpg"], Names(project));
    }

    [Fact]
    public static void AClosedProjectRefusesASort()
    {
        var project = Filled(Photo("IMG_2.jpg"), Photo("IMG_1.jpg"));
        project.Close();

        Assert.Throws<ObjectDisposedException>(() => project.ApplySort(SortMode.Name));
        Assert.Throws<ObjectDisposedException>(() => project.ReorderManually([.. project.Frames.Select(frame => frame.Id)]));
        Assert.Equal(["IMG_2.jpg", "IMG_1.jpg"], Names(project));
    }

    [Fact]
    public static void DeletingAFrameRemovesItFromTheProjectNeverFromDisk()
    {
        var folder = Directory.CreateTempSubdirectory("tenlapse-delete-");
        try
        {
            var written = new DateTime(2026, 5, 2, 8, 0, 0, DateTimeKind.Utc);
            var frames = new List<Frame>();
            foreach (var name in new[] { "IMG_0001.jpg", "IMG_0002.jpg", "IMG_0003.jpg" })
            {
                var path = Path.Combine(folder.FullName, name);
                File.WriteAllBytes(path, ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 10:00:00")));
                frames.Add(new Frame(path, new FileInfo(path).Length, written));
            }

            var project = Filled([.. frames]);
            project.ApplySort(SortMode.Name);
            var second = project.Frames[1];

            Assert.Equal(1, project.RemoveFrames([second.Id]));

            Assert.Equal(["IMG_0001.jpg", "IMG_0003.jpg"], Names(project));
            Assert.True(File.Exists(second.SourcePath));
            Assert.Equal(3, Directory.GetFiles(folder.FullName).Length);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
