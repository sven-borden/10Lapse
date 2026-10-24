using TenLapse.Core.Projects;

namespace TenLapse.Tests.Projects;

/// <summary>
/// Opening a project's photos (#52): found by the path beside the project first, then by the saved full path; missing ones
/// are listed, never fatal; and the file and dirty bookkeeping of <see cref="Project"/>.
/// </summary>
public sealed class ProjectFilesTests
{
    private static readonly DateTime _written = new(2026, 5, 1, 18, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void PhotosThatAreThereOpenInOrderAndMissingOnesAreListed()
    {
        var disk = new FakeDisk { [@"C:\Shoot\3.jpg"] = new(30, _written), [@"C:\Shoot\1.jpg"] = new(10, _written) };
        var content = new ProjectFileContent
        {
            Frames = [new(@"C:\Shoot\3.jpg") { FileSize = 30, LastWriteTimeUtc = _written, CaptureTime = new DateTime(2026, 1, 1) }, new(@"C:\Shoot\2.jpg"), new(@"C:\Shoot\1.jpg")],
        };

        var resolved = SavedFrames.Resolve(content, @"C:\Projects\Shoot.10lapse", disk);

        Assert.Equal([@"C:\Shoot\3.jpg", @"C:\Shoot\1.jpg"], resolved.Frames.Select(frame => frame.SourcePath));
        Assert.Equal([@"C:\Shoot\2.jpg"], resolved.Missing);
        Assert.Equal(new DateTime(2026, 1, 1), resolved.Frames[0].CaptureTime);
        // 1.jpg had no saved facts: it counts as changed, 3.jpg matches.
        Assert.Equal(1, resolved.Changed);
    }

    [Fact]
    public void AFolderMovedWithItsProjectOpensThePhotosBesideIt()
    {
        // Saved as D:\Trip\Trip.10lapse with D:\Trip\Photos\1.jpg; the whole folder now lives in E:\Archive\Trip.
        var disk = new FakeDisk { [@"E:\Archive\Trip\Photos\1.jpg"] = new(10, _written) };
        var content = new ProjectFileContent { Frames = [new(@"D:\Trip\Photos\1.jpg") { RelativePath = @"Photos\1.jpg" }] };

        var resolved = SavedFrames.Resolve(content, @"E:\Archive\Trip\Trip.10lapse", disk);

        Assert.Equal(@"E:\Archive\Trip\Photos\1.jpg", Assert.Single(resolved.Frames).SourcePath);
        Assert.Empty(resolved.Missing);
        Assert.Equal(1, resolved.FoundBesideProject);
    }

    [Fact]
    public void AProjectFileMovedAwayFromItsPhotosStillFindsThem()
    {
        var disk = new FakeDisk { [@"D:\Trip\Photos\1.jpg"] = new(10, _written) };
        var content = new ProjectFileContent { Frames = [new(@"D:\Trip\Photos\1.jpg") { RelativePath = @"Photos\1.jpg" }] };

        var resolved = SavedFrames.Resolve(content, @"C:\Users\Jane\Desktop\Trip.10lapse", disk);

        Assert.Equal(@"D:\Trip\Photos\1.jpg", Assert.Single(resolved.Frames).SourcePath);
        Assert.Equal(0, resolved.FoundBesideProject);
    }

    [Fact]
    public void TheSavedFullPathWinsOverAPhotoOfTheSameNameBesideTheProject()
    {
        // D:\Shoots\2024-05\Harbor.10lapse copied alone into 2024-06, where the camera's numbering started again.
        var disk = new FakeDisk
        {
            [@"D:\Shoots\2024-05\IMG_0001.jpg"] = new(10, _written),
            [@"D:\Shoots\2024-06\IMG_0001.jpg"] = new(99, _written.AddDays(30)),
        };
        var content = new ProjectFileContent
        {
            Frames = [new(@"D:\Shoots\2024-05\IMG_0001.jpg") { RelativePath = "IMG_0001.jpg", FileSize = 10, LastWriteTimeUtc = _written }],
        };

        var resolved = SavedFrames.Resolve(content, @"D:\Shoots\2024-06\Harbor.10lapse", disk);

        Assert.Equal(@"D:\Shoots\2024-05\IMG_0001.jpg", Assert.Single(resolved.Frames).SourcePath);
        Assert.Equal(0, resolved.FoundBesideProject);
        Assert.Equal(0, resolved.Changed);
    }

    [Fact]
    public void OnlineOnlyPhotosAreCountedAndNotReadAgainAndUnknownOrChangedOnesAre()
    {
        var disk = new FakeDisk
        {
            [@"C:\Shoot\known.jpg"] = new(10, _written),
            [@"C:\Shoot\unknown.jpg"] = new(10, _written),
            [@"C:\Shoot\changed.jpg"] = new(11, _written),
            [@"C:\Shoot\cloud.jpg"] = new(3_000_000, _written, IsCloudOnly: true),
        };
        var pixels = new PixelSize(6000, 4000);
        var content = new ProjectFileContent
        {
            Frames =
            [
                new(@"C:\Shoot\known.jpg") { FileSize = 10, LastWriteTimeUtc = _written, PixelSize = pixels },
                new(@"C:\Shoot\unknown.jpg") { FileSize = 10, LastWriteTimeUtc = _written },
                new(@"C:\Shoot\changed.jpg") { FileSize = 10, LastWriteTimeUtc = _written, PixelSize = pixels },
                new(@"C:\Shoot\cloud.jpg"),
            ],
        };

        var resolved = SavedFrames.Resolve(content, null, disk);

        Assert.Equal([@"C:\Shoot\unknown.jpg", @"C:\Shoot\changed.jpg"], resolved.ToRead.Select(frame => frame.SourcePath));
        Assert.Equal(1, resolved.CloudOnly);
        Assert.Equal(3_000_000, resolved.CloudOnlyBytes);
    }

    [Fact]
    public void AnUnchangedPhotoKeepsItsExposureAndAChangedOneIsMeasuredAgain()
    {
        // #45: the measurement is the photo's pixels'; a photo edited since the save has new ones.
        var disk = new FakeDisk
        {
            [@"C:\Shoot\same.jpg"] = new(10, _written),
            [@"C:\Shoot\edited.jpg"] = new(12, _written),
        };
        var exposure = new PhotoExposure(-2.5) { ExposureTime = 0.5, FNumber = 11, Iso = 100 };
        var content = new ProjectFileContent
        {
            Frames =
            [
                new(@"C:\Shoot\same.jpg") { FileSize = 10, LastWriteTimeUtc = _written, Exposure = exposure },
                new(@"C:\Shoot\edited.jpg") { FileSize = 10, LastWriteTimeUtc = _written, Exposure = exposure },
            ],
        };

        var frames = SavedFrames.Resolve(content, null, disk).Frames;

        Assert.Equal(exposure, frames[0].Exposure);
        Assert.Null(frames[1].Exposure);
    }

    [Fact]
    public void MissingPhotosAreKeptWithTheirPlaceAndWrittenBackThere()
    {
        var disk = new FakeDisk { [@"C:\Shoot\1.jpg"] = new(10, _written), [@"C:\Shoot\3.jpg"] = new(10, _written) };
        var content = new ProjectFileContent
        {
            Frames = [new(@"E:\Card\0.jpg"), new(@"C:\Shoot\1.jpg"), new(@"E:\Card\2.jpg"), new(@"C:\Shoot\3.jpg")],
        };
        var resolved = SavedFrames.Resolve(content, @"C:\Projects\Shoot.10lapse", disk);
        Assert.Equal([@"E:\Card\0.jpg", @"E:\Card\2.jpg"], resolved.Unresolved.Select(item => item.Entry.Path));

        // As opened: the missing ones go back where they were.
        var project = new Project();
        project.AddFrames(resolved.Frames);
        Assert.Equal(
            [@"E:\Card\0.jpg", @"C:\Shoot\1.jpg", @"E:\Card\2.jpg", @"C:\Shoot\3.jpg"],
            Write(project).Frames.Select(frame => frame.Path));

        // Reordered: each follows the frame it followed. 1.jpg removed: 2.jpg goes back near its old place.
        project.ReorderFrames([project.Frames[1].Id, project.Frames[0].Id]);
        Assert.Equal(
            [@"E:\Card\0.jpg", @"C:\Shoot\3.jpg", @"C:\Shoot\1.jpg", @"E:\Card\2.jpg"],
            Write(project).Frames.Select(frame => frame.Path));
        project.RemoveFrames([project.Frames.Single(frame => frame.SourcePath.EndsWith("1.jpg", StringComparison.Ordinal)).Id]);
        Assert.Equal(
            [@"E:\Card\0.jpg", @"C:\Shoot\3.jpg", @"E:\Card\2.jpg"],
            Write(project).Frames.Select(frame => frame.Path));

        // One imported again since: written once, as the project's own frame.
        project.AddFrames([new Frame(@"E:\Card\2.jpg", 10, _written)]);
        Assert.Equal(
            [@"E:\Card\0.jpg", @"C:\Shoot\3.jpg", @"E:\Card\2.jpg"],
            Write(project).Frames.Select(frame => frame.Path));

        ProjectFileContent Write(Project source) =>
            ProjectFileContent.From(source.Snapshot, @"C:\Projects\Shoot.10lapse").Carrying(resolved.Unresolved, source.Snapshot.Frames, @"C:\Projects\Shoot.10lapse");
    }

    [Fact]
    public void AFrameWithOnlyARelativePathAndNoProjectFileIsMissing()
    {
        var content = new ProjectFileContent { Frames = [new SavedFrame(null) { RelativePath = @"Photos\1.jpg" }] };

        var resolved = SavedFrames.Resolve(content, null, new FakeDisk());

        Assert.Empty(resolved.Frames);
        Assert.Equal([@"Photos\1.jpg"], resolved.Missing);
    }

    [Fact]
    public void AProjectFileIsNeverWrittenIntoAFolderThatIsGone()
    {
        var folder = Path.Combine(Path.GetTempPath(), "claude", "feat-52-save-projects", "folder-gone", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "Shoots", "Harbor.10lapse");

        Assert.Throws<DirectoryNotFoundException>(() => ProjectFileSystem.Instance.WriteText(path, "{}"));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void SetFileNamesTheProjectAfterItsFileWithoutMakingItDirty()
    {
        var project = new Project();
        var changes = new List<ProjectChanges>();
        project.Changed += (_, e) => changes.Add(e.Changes);

        project.SetFile(@"C:\Projects\Harbor sunset.10lapse");

        Assert.Equal(@"C:\Projects\Harbor sunset.10lapse", project.FilePath);
        Assert.Equal("Harbor sunset", project.Name);
        Assert.False(project.IsDirty);
        Assert.Equal(0, project.Version);
        Assert.Equal([ProjectChanges.File | ProjectChanges.Name], changes);

        project.SetFile(@"C:\Projects\Harbor sunset.10lapse");
        Assert.Single(changes);
    }

    [Fact]
    public void MarkSavedCleansOnlyWhenNothingChangedSinceTheWrite()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("Harbor", 3));
        var written = project.Version;
        project.RemoveFrames([project.Frames[0].Id]);

        Assert.False(project.MarkSaved(written));
        Assert.True(project.IsDirty);
        Assert.True(project.MarkSaved(project.Version));
        Assert.False(project.IsDirty);
        Assert.False(project.HasWorkToLose);
    }

    [Fact]
    public void AnExportCleansAProjectOnlyWhileItHasNoFile()
    {
        var unsaved = new Project();
        unsaved.AddFrames(TestFrames.Folder("A", 2));
        Assert.True(unsaved.MarkExported(unsaved.Version));
        Assert.False(unsaved.IsDirty);

        var saved = new Project();
        saved.AddFrames(TestFrames.Folder("B", 2));
        saved.SetFile(@"C:\Projects\B.10lapse");
        Assert.False(saved.MarkExported(saved.Version));
        Assert.True(saved.IsDirty);
    }

    [Fact]
    public void TheSongIsFoundAtItsPathElseBesideTheProjectElseKeptAsItWas()
    {
        // #50, as the photos: saved as D:\Trip\Trip.10lapse with D:\Trip\Music\Sunrise.mp3.
        var music = MusicSettings.Default with { Path = @"D:\Trip\Music\Sunrise.mp3" };
        var content = new ProjectFileContent { Music = music, MusicRelativePath = @"Music\Sunrise.mp3" };

        var there = SavedFrames.Resolve(content, @"D:\Trip\Trip.10lapse", new FakeDisk { [@"D:\Trip\Music\Sunrise.mp3"] = new(10, _written) });
        Assert.Equal(music, there.Music);

        // The folder moved to E:\Archive\Trip with its project: the song is found beside it.
        var moved = SavedFrames.Resolve(content, @"E:\Archive\Trip\Trip.10lapse", new FakeDisk { [@"E:\Archive\Trip\Music\Sunrise.mp3"] = new(10, _written) });
        Assert.Equal(@"E:\Archive\Trip\Music\Sunrise.mp3", moved.Music!.Path);

        // Nowhere: the project keeps the saved path, and the Music section says the file is missing.
        var gone = SavedFrames.Resolve(content, @"E:\Archive\Trip\Trip.10lapse", new FakeDisk());
        Assert.Equal(music, gone.Music);
        Assert.Equal(MusicSettings.Default, SavedFrames.Resolve(new ProjectFileContent(), null, new FakeDisk()).Music);
    }

    private sealed class FakeDisk : Dictionary<string, FileFacts>, IProjectFileSystem
    {
        public FakeDisk()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public string ReadText(string path) => throw new FileNotFoundException(path);

        public void WriteText(string path, string text) => throw new NotSupportedException();

        public FileFacts? Probe(string path) => TryGetValue(path, out var facts) ? facts : null;
    }
}
