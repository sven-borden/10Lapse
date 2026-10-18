using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Projects;

public sealed class ProjectTests
{
    [Fact]
    public void ANewProjectIsEmptyCleanAndHasTheDefaults()
    {
        var project = new Project();

        Assert.Empty(project.Frames);
        Assert.Empty(project.SkippedFiles);
        Assert.False(project.IsDirty);
        Assert.False(project.HasWorkToLose);
        Assert.Equal(Project.DefaultName, project.Name);
        Assert.Equal(new Fraction(30, 1), project.Timing.Rate);
        Assert.Equal(SpeedMode.PerSecond, project.Timing.Mode);
        Assert.Equal(FrameSettings.Default, project.FrameSettings);
        Assert.Equal(OutputSettings.Default, project.Output);
        Assert.Equal(SortMode.CaptureTime, project.SortMode);
    }

    [Fact]
    public void AddFramesAppendsInOrderMarksDirtyAndNotifiesOnce()
    {
        var project = new Project();
        var changes = new List<ProjectChanges>();
        project.Changed += (_, e) => changes.Add(e.Changes);
        var first = TestFrames.Folder("a", 3);
        var second = TestFrames.Folder("b", 2);

        Assert.Equal(3, project.AddFrames(first));
        Assert.Equal(2, project.AddFrames(second));

        Assert.Equal([.. first, .. second], project.Frames);
        Assert.True(project.IsDirty);
        Assert.True(project.HasWorkToLose);
        Assert.Equal([ProjectChanges.Frames | ProjectChanges.Dirty, ProjectChanges.Frames], changes);
    }

    [Fact]
    public void AddFramesLeavesOutPathsAndIdsAlreadyInTheProject()
    {
        var project = new Project();
        var frame = TestFrames.At(@"C:\Photos\a\IMG_0001.jpg");
        project.AddFrames([frame]);
        var changed = 0;
        project.Changed += (_, _) => changed++;

        var added = project.AddFrames([frame, TestFrames.At(@"c:\photos\A\img_0001.JPG"), frame with { CaptureTime = DateTime.Now }]);

        Assert.Equal(0, added);
        Assert.Single(project.Frames);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void RemoveFramesRemovesOnlyThoseAndTheirPathsCanComeBack()
    {
        var project = new Project();
        var frames = TestFrames.Folder("a", 4);
        project.AddFrames(frames);

        var removed = project.RemoveFrames([frames[1].Id, frames[3].Id, Guid.NewGuid()]);

        Assert.Equal(2, removed);
        Assert.Equal([frames[0], frames[2]], project.Frames);
        Assert.Equal(1, project.AddFrames([TestFrames.At(frames[1].SourcePath)]));
        Assert.Equal(0, project.RemoveFrames([Guid.NewGuid()]));
    }

    [Fact]
    public void RemovingAFrameNeverTouchesTheFileOnDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tenlapse-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF]);
        try
        {
            var info = new FileInfo(path);
            var project = new Project();
            var frame = new Frame(path, info.Length, info.LastWriteTimeUtc);
            project.AddFrames([frame]);

            project.RemoveFrames([frame.Id]);

            Assert.Empty(project.Frames);
            Assert.True(File.Exists(path));
            Assert.Equal(3, new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReorderFramesTakesAPermutationOfTheIds()
    {
        var project = new Project();
        var frames = TestFrames.Folder("a", 3);
        project.AddFrames(frames);
        var changed = 0;
        project.Changed += (_, _) => changed++;

        project.ReorderFrames([frames[2].Id, frames[0].Id, frames[1].Id]);
        project.ReorderFrames([frames[2].Id, frames[0].Id, frames[1].Id]);

        Assert.Equal([frames[2], frames[0], frames[1]], project.Frames);
        Assert.Equal(1, changed);
        Assert.Throws<ArgumentException>(() => project.ReorderFrames([frames[0].Id, frames[1].Id]));
        Assert.Throws<ArgumentException>(() => project.ReorderFrames([frames[0].Id, frames[0].Id, frames[1].Id]));
        Assert.Throws<ArgumentException>(() => project.ReorderFrames([frames[0].Id, frames[1].Id, Guid.NewGuid()]));
    }

    [Fact]
    public void SettingsChangesMarkDirtyAndRepeatsAreSilent()
    {
        var project = new Project();
        var changes = new List<ProjectChanges>();
        project.Changed += (_, e) => changes.Add(e.Changes);

        project.Timing = new TimingSettings(FrameRates.Ntsc24, SpeedMode.PerSecond);
        project.Timing = new TimingSettings(FrameRates.Ntsc24, SpeedMode.PerSecond);
        project.FrameSettings = project.FrameSettings with { Fit = FitMode.Fit };
        project.Output = project.Output with { Resolution = OutputResolution.Uhd2160 };
        project.SortMode = SortMode.Name;
        project.SortMode = SortMode.Name;
        project.Name = "Harbor sunset";

        Assert.Equal(
            [
                ProjectChanges.Timing | ProjectChanges.Dirty,
                ProjectChanges.FrameSettings,
                ProjectChanges.Output,
                ProjectChanges.SortMode,
                ProjectChanges.Name,
            ],
            changes);
        Assert.Throws<ArgumentNullException>(() => project.Timing = null!);
        Assert.Throws<ArgumentOutOfRangeException>(() => project.SortMode = (SortMode)42);
    }

    [Fact]
    public void AnExportedProjectHasNoWorkToLoseUntilItChangesAgain()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 2));

        project.MarkExported();

        Assert.False(project.IsDirty);
        Assert.False(project.HasWorkToLose);
        project.Output = project.Output with { Quality = OutputQuality.Maximum };
        Assert.True(project.HasWorkToLose);
    }

    [Fact]
    public void SkippedFilesAreKeptWithTheirReason()
    {
        var project = new Project();
        var broken = new SkippedFile(@"C:\Photos\a\broken.jpg", SkipReason.DecodeFailed, "The file ends early.");

        project.AddSkippedFiles([broken]);
        project.AddSkippedFiles([]);

        Assert.Equal([broken], project.SkippedFiles);
        Assert.True(project.IsDirty);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SkippedFile("x.jpg", (SkipReason)42, ""));
    }

    [Theory]
    [InlineData(@"C:\Photos\Harbor sunset", "Harbor sunset")]
    [InlineData(@"C:\Photos\Harbor sunset\", "Harbor sunset")]
    [InlineData(@"D:\DCIM\100CANON", "100CANON")]
    [InlineData(@"C:\", Project.DefaultName)]
    [InlineData(@"C:", Project.DefaultName)]
    [InlineData("", Project.DefaultName)]
    [InlineData("   ", Project.DefaultName)]
    [InlineData(null, Project.DefaultName)]
    public void TheNameComesFromTheFolder(string? folder, string expected)
    {
        Assert.Equal(expected, Project.NameFromFolder(folder));
    }

    [Fact]
    public void ABlankNameFallsBackToTheDefault()
    {
        var project = new Project { Name = "  Harbor sunset " };
        Assert.Equal("Harbor sunset", project.Name);

        project.Name = " ";

        Assert.Equal(Project.DefaultName, project.Name);
    }

    [Fact]
    public void TwoProjectsNeverShareFrames()
    {
        var first = new Project();
        var second = new Project();

        first.AddFrames(TestFrames.Folder("a", 3));
        second.AddFrames(TestFrames.Folder("b", 2));

        Assert.Equal(3, first.Frames.Length);
        Assert.Equal(2, second.Frames.Length);
        Assert.All(second.Frames, frame => Assert.Contains(@"\b\", frame.SourcePath));
    }
}
