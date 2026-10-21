using System.Reflection;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Projects;

/// <summary>
/// The <c>.10lapse</c> format (#52): every group and every frame detail round-trips, a missing group is its default, a bad
/// field costs only itself, and a file that is not a project, or a newer one, never throws.
/// </summary>
/// <remarks>
/// <b>A new settings group</b> copies <see cref="TheTimingGroupSurvivesAWriteAndARead"/> (a non-default value written and
/// read back) and adds its snapshot property to <c>ProjectFileJson.StoredSnapshotProperties</c>, which
/// <see cref="EverySnapshotSettingIsStored"/> checks.
/// </remarks>
public sealed class ProjectFileJsonTests
{
    private static readonly DateTime _written = new(2026, 5, 1, 18, 30, 0, DateTimeKind.Utc);

    private static ProjectFileContent RoundTrip(ProjectFileContent content)
    {
        var result = ProjectFileJson.Read(ProjectFileJson.Write(content));
        Assert.Equal(ProjectFileFailure.None, result.Failure);
        Assert.Empty(result.Problems);
        return result.Content!;
    }

    [Fact]
    public void TheTimingGroupSurvivesAWriteAndARead()
    {
        var timing = new TimingSettings(SpeedMode.PerPhoto, new Fraction(30000, 1001), new Fraction(95, 2), new Fraction(1, 4));

        Assert.Equal(timing, RoundTrip(new ProjectFileContent { Timing = timing }).Timing);
    }

    [Fact]
    public void TheFrameOutputAndOrderGroupsSurviveAWriteAndARead()
    {
        foreach (var aspect in Enum.GetValues<FrameAspect>())
        foreach (var fit in Enum.GetValues<FitMode>())
        {
            Assert.Equal(new FrameSettings(aspect, fit), RoundTrip(new ProjectFileContent { Frame = new FrameSettings(aspect, fit) }).Frame);
        }

        foreach (var resolution in Enum.GetValues<OutputResolution>())
        foreach (var format in Enum.GetValues<VideoFormat>())
        foreach (var quality in Enum.GetValues<OutputQuality>())
        {
            var output = new OutputSettings(resolution, format, quality);
            Assert.Equal(output, RoundTrip(new ProjectFileContent { Output = output }).Output);
        }

        foreach (var sort in Enum.GetValues<SortMode>())
        {
            Assert.Equal(sort, RoundTrip(new ProjectFileContent { SortMode = sort }).SortMode);
        }
    }

    [Fact]
    public void EveryFrameDetailSurvivesInOrder()
    {
        var first = new SavedFrame(@"C:\Shoot\IMG_0002.jpg")
        {
            RelativePath = @"Shoot\IMG_0002.jpg",
            FileSize = 4_200_000,
            LastWriteTimeUtc = _written,
            PixelSize = new PixelSize(6000, 4000),
            CaptureTime = new DateTime(2026, 4, 30, 6, 12, 3, 250, DateTimeKind.Unspecified),
            CaptureUtcOffset = TimeSpan.FromMinutes(-150),
        };
        var second = new SavedFrame(@"C:\Shoot\IMG_0001.jpg");

        var frames = RoundTrip(new ProjectFileContent { Frames = [first, second] }).Frames;

        Assert.Equal([first, second], frames);
        Assert.Equal(DateTimeKind.Unspecified, frames[0].CaptureTime!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, frames[0].LastWriteTimeUtc!.Value.Kind);
    }

    [Fact]
    public void AProjectFromASnapshotKeepsItsSettingsAndPathsRelativeToTheFile()
    {
        var project = new Project(TimingSettings.PerSecond(new Fraction(24, 1)), new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit), OutputSettings.Default, SortMode.Name);
        project.AddFrames([new Frame(@"D:\Trips\Harbor\IMG_0001.jpg", 10, _written) { PixelSize = new PixelSize(30, 20) }, new Frame(@"E:\Other\IMG_0002.jpg", 20, _written)]);

        var content = ProjectFileContent.From(project.Snapshot, @"D:\Trips\Harbor.10lapse");

        Assert.Equal(SortMode.Name, content.SortMode);
        Assert.Equal(new Fraction(24, 1), content.Timing.FramesPerSecond);
        Assert.Equal(new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit), content.Frame);
        Assert.Equal(@"Harbor\IMG_0001.jpg", content.Frames[0].RelativePath);
        // Another drive has no relative path.
        Assert.Null(content.Frames[1].RelativePath);
        Assert.Equal(new PixelSize(30, 20), content.Frames[0].PixelSize);
        Assert.Null(content.RecoveredName);
    }

    [Fact]
    public void ARecoveryCopyKeepsTheNameAndTheFile()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("Harbor", 2));
        project.Name = "Harbor sunset";

        var content = RoundTrip(ProjectFileContent.Recovery(project.Snapshot));

        Assert.Equal("Harbor sunset", content.RecoveredName);
        Assert.Null(content.RecoveredFilePath);
        Assert.Equal(2, content.Frames.Length);
    }

    [Fact]
    public void AFileWithOnlyTheMarkerAndVersionIsAnEmptyProjectWithDefaults()
    {
        // A file from before a group existed: the group is its default (for a 3.1 feature: off), quietly.
        var result = ProjectFileJson.Read("""{ "format": "10Lapse project", "version": 1 }""");

        Assert.Equal(ProjectFileFailure.None, result.Failure);
        Assert.Empty(result.Problems);
        Assert.Equal(TimingSettings.Default, result.Content!.Timing);
        Assert.Equal(OutputSettings.Default, result.Content.Output);
        Assert.Empty(result.Content.Frames);
    }

    [Fact]
    public void AnUnknownEnumValueTakesTheDefaultAndSaysSo()
    {
        // A value a newer build added (#51's GIF, say) reads as the default here.
        var result = ProjectFileJson.Read("""
            { "format": "10Lapse project", "version": 1,
              "output": { "resolution": "Uhd2160", "format": "Gif", "quality": "Maximum" } }
            """);

        Assert.Equal(new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4H264, OutputQuality.Maximum), result.Content!.Output);
        Assert.Contains(result.Problems, problem => problem.Contains("output.format", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOutOfRangeTimingInputCostsOnlyItself()
    {
        var result = ProjectFileJson.Read("""
            { "format": "10Lapse project", "version": 1,
              "timing": { "mode": "Length", "framesPerSecond": [500, 1], "lengthSeconds": [12, 1] } }
            """);

        Assert.Equal(SpeedMode.Length, result.Content!.Timing.Mode);
        Assert.Equal(TimingSettings.Default.FramesPerSecond, result.Content.Timing.FramesPerSecond);
        Assert.Equal(new Fraction(12, 1), result.Content.Timing.LengthSeconds);
        Assert.Single(result.Problems);
    }

    [Fact]
    public void AFrameWithoutAUsablePathIsLeftOutAndABadDetailIsUnknown()
    {
        var result = ProjectFileJson.Read("""
            { "format": "10Lapse project", "version": 1, "frames": [
              { "path": "C:\\A\\1.jpg", "width": -3, "height": 20, "captured": "soon", "size": -1 },
              { "path": "relative\\2.jpg" },
              { "relativePath": "B\\3.jpg" },
              42
            ] }
            """);

        var frames = result.Content!.Frames;
        Assert.Equal(2, frames.Length);
        Assert.Equal(@"C:\A\1.jpg", frames[0].Path);
        Assert.Null(frames[0].PixelSize);
        Assert.Null(frames[0].CaptureTime);
        Assert.Null(frames[0].FileSize);
        Assert.Null(frames[1].Path);
        Assert.Equal(@"B\3.jpg", frames[1].RelativePath);
        Assert.Contains(result.Problems, problem => problem.StartsWith("2 frame entries", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", ProjectFileFailure.NotJson)]
    [InlineData("{ not json", ProjectFileFailure.NotJson)]
    [InlineData("[1, 2]", ProjectFileFailure.NotAProject)]
    [InlineData("""{ "version": 1, "frames": [] }""", ProjectFileFailure.NotAProject)]
    [InlineData("""{ "format": "Something else", "version": 1 }""", ProjectFileFailure.NotAProject)]
    [InlineData("""{ "format": "10Lapse project" }""", ProjectFileFailure.NoUsableVersion)]
    [InlineData("""{ "format": "10Lapse project", "version": "one" }""", ProjectFileFailure.NoUsableVersion)]
    [InlineData("""{ "format": "10Lapse project", "version": 0 }""", ProjectFileFailure.NoUsableVersion)]
    [InlineData("""{ "format": "10Lapse project", "format": "x", "version": 1 }""", ProjectFileFailure.NotJson)]
    [InlineData("""{ "format": "10Lapse project", "version": 1, "frames": [ { "path": "C:\\a.jpg", "path": "C:\\b.jpg" } ] }""", ProjectFileFailure.NotJson)]
    [InlineData("""{ "format": "10Lapse project", "version": 1, "timing": { "mode": "PerSecond", "mode": "PerPhoto" } }""", ProjectFileFailure.NotJson)]
    public void AFileThatIsNotAProjectIsAFailureNotAThrow(string text, ProjectFileFailure failure)
    {
        var result = ProjectFileJson.Read(text);

        Assert.Equal(failure, result.Failure);
        Assert.Null(result.Content);
        Assert.NotEmpty(result.Problems);
    }

    [Fact]
    public void ANewerVersionIsReadForWhatThisBuildKnows()
    {
        var result = ProjectFileJson.Read("""
            { "format": "10Lapse project", "version": 7, "deflicker": { "strength": 3 },
              "frame": { "aspect": "Ratio9x16", "fit": "Fit" }, "frames": [ { "path": "C:\\A\\1.jpg" } ] }
            """);

        Assert.True(result.IsNewerVersion);
        Assert.Equal(7, result.Version);
        Assert.Equal(new FrameSettings(FrameAspect.Ratio9x16, FitMode.Fit), result.Content!.Frame);
        Assert.Single(result.Content.Frames);
        Assert.Single(result.Problems);
    }

    [Fact]
    public void AWrittenFileIsVersionOneWithTheMarker()
    {
        var text = ProjectFileJson.Write(new ProjectFileContent());

        Assert.Contains("\"format\": \"10Lapse project\"", text, StringComparison.Ordinal);
        Assert.Contains("\"version\": 1", text, StringComparison.Ordinal);
        Assert.False(ProjectFileJson.Read(text).IsNewerVersion);
    }

    [Fact]
    public void EverySnapshotSettingIsStored()
    {
        // A new ProjectSnapshot property is a setting the project file must keep (or say why not): add a group (see
        // ProjectFileContent) and list it in ProjectFileJson.StoredSnapshotProperties.
        var unknown = typeof(ProjectSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => !ProjectFileJson.StoredSnapshotProperties.Contains(name) && !ProjectFileJson.NotStoredSnapshotProperties.Contains(name))
            .ToList();

        Assert.True(unknown.Count == 0, "Not in the project file: " + string.Join(", ", unknown));
    }
}
