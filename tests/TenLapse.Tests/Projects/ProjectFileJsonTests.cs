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
    public void TheSequenceGroupSurvivesAWriteAndARead()
    {
        // #49: holds in tenths of a second, the plays and the boomerang.
        var sequence = new SequenceSettings(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(10), 5, boomerang: true);

        Assert.Equal(sequence, RoundTrip(new ProjectFileContent { Sequence = sequence }).Sequence);
        Assert.Equal(SequenceSettings.Default, RoundTrip(new ProjectFileContent()).Sequence);
        Assert.Contains("\"holdStartSeconds\": 1.5", ProjectFileJson.Write(new ProjectFileContent { Sequence = sequence }), StringComparison.Ordinal);

        var project = new ProjectFileContent { Sequence = sequence }.CreateEmptyProject();
        Assert.Equal(sequence, project.Sequence);
        Assert.False(project.Snapshot.IsDirty);
    }

    [Fact]
    public void AFileWithoutTheSequenceOrWithABadOnePlaysThePhotosOnce()
    {
        var written = ProjectFileJson.Write(new ProjectFileContent { Sequence = new SequenceSettings(TimeSpan.FromSeconds(2), TimeSpan.Zero, 3, true) });
        var root = System.Text.Json.Nodes.JsonNode.Parse(written)!.AsObject();

        // A file from before #49 has no sequence group: plain, without a word.
        root.Remove("sequence");
        var before = ProjectFileJson.Read(root.ToJsonString());
        Assert.Equal(SequenceSettings.Default, before.Content!.Sequence);
        Assert.Empty(before.Problems);

        // A hand-edited one keeps the default for each field it spoils, and says so.
        root["sequence"] = new System.Text.Json.Nodes.JsonObject { ["holdStartSeconds"] = 0.15m, ["holdEndSeconds"] = 11, ["plays"] = 9, ["boomerang"] = "yes" };
        var spoiled = ProjectFileJson.Read(root.ToJsonString());
        Assert.Equal(SequenceSettings.Default, spoiled.Content!.Sequence);
        Assert.Equal(4, spoiled.Problems.Count);
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

        // #51's GIF size and frame rate are part of the output group.
        foreach (var size in Enum.GetValues<GifSize>())
        foreach (var rate in Enum.GetValues<GifFrameRate>())
        {
            var gif = OutputSettings.Default with { Format = VideoFormat.Gif, Gif = new GifSettings(size, rate) };
            Assert.Equal(gif, RoundTrip(new ProjectFileContent { Output = gif }).Output);
        }

        foreach (var sort in Enum.GetValues<SortMode>())
        {
            Assert.Equal(sort, RoundTrip(new ProjectFileContent { SortMode = sort }).SortMode);
        }
    }

    [Fact]
    public void TheLookGroupSurvivesAWriteAndARead()
    {
        // #45: deflicker on at 60 %.
        var look = new LookSettings(true, 60);

        Assert.Equal(look, RoundTrip(new ProjectFileContent { Look = look }).Look);
        Assert.Equal(LookSettings.Default, RoundTrip(new ProjectFileContent()).Look);
    }

    [Fact]
    public void AnOpenedFileKeepsBothTheSequenceAndTheLook()
    {
        // #49 and #45 each add a project-only group; one file carries both and the opened project starts with both, clean.
        var sequence = new SequenceSettings(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5), 3, boomerang: true);
        var look = new LookSettings(true, 40);

        var read = RoundTrip(new ProjectFileContent { Sequence = sequence, Look = look });
        Assert.Equal(sequence, read.Sequence);
        Assert.Equal(look, read.Look);

        var project = read.CreateEmptyProject();
        Assert.Equal(sequence, project.Sequence);
        Assert.Equal(look, project.Look);
        Assert.False(project.Snapshot.IsDirty);
    }

    [Fact]
    public void AStrengthOutsideItsRangeTakesTheDefaultAndSaysSo()
    {
        var result = ProjectFileJson.Read("""
            { "format": "10Lapse project", "version": 1, "look": { "deflicker": true, "strength": 250 } }
            """);

        Assert.True(result.Content!.Look.Deflicker);
        Assert.Equal(LookSettings.MaxStrength, result.Content.Look.Strength);
        Assert.Contains(result.Problems, problem => problem.Contains("look.strength", StringComparison.Ordinal));
    }

    [Fact]
    public void AFrameKeepsItsExposureMeasurement()
    {
        // #45: the measurement travels with the photo, so a reopened project is not measured again.
        var measured = new SavedFrame(@"C:\Shoot\IMG_0003.jpg")
        {
            Exposure = new PhotoExposure(-3.2512) { ExposureTime = 1.0 / 250, FNumber = 8, Iso = 400 },
        };
        var withoutExif = new SavedFrame(@"C:\Shoot\IMG_0004.jpg") { Exposure = new PhotoExposure(-1.5) };

        Assert.Equal([measured, withoutExif], RoundTrip(new ProjectFileContent { Frames = [measured, withoutExif] }).Frames);
    }

    [Fact]
    public void AnUnusableExposureIsNoMeasurement()
    {
        // A damaged luminance means the photo is measured again; a damaged Exif value is unknown.
        var result = ProjectFileJson.Read("""
            { "format": "10Lapse project", "version": 1, "frames": [
              { "path": "C:\\Shoot\\A.jpg", "exposure": { "luminance": 5 } },
              { "path": "C:\\Shoot\\B.jpg", "exposure": { "luminance": -2, "fNumber": -4, "iso": "x" } },
              { "path": "C:\\Shoot\\C.jpg", "exposure": [1, 2] } ] }
            """);

        var frames = result.Content!.Frames;
        Assert.Null(frames[0].Exposure);
        Assert.Equal(new PhotoExposure(-2), frames[1].Exposure);
        Assert.Null(frames[2].Exposure);
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
        // A value a newer build added reads as the default here (a name no build will ever use).
        var result = ProjectFileJson.Read("""
            { "format": "10Lapse project", "version": 1,
              "output": { "resolution": "Uhd2160", "format": "Format9000", "quality": "Maximum" } }
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

        // And inside each stored group, every setting, however deep (Output.Gif.Size): each is set to another value on
        // its own, written and read back. A field added to a group (or to a record in it) without a writer and a reader in
        // JsonFields comes back as its default and fails here.
        (object Default, Func<object, ProjectFileContent> Make, Func<ProjectFileContent, object> Take)[] groups =
        [
            (TimingSettings.Default, value => new ProjectFileContent { Timing = (TimingSettings)value }, content => content.Timing),
            (FrameSettings.Default, value => new ProjectFileContent { Frame = (FrameSettings)value }, content => content.Frame),
            (OutputSettings.Default, value => new ProjectFileContent { Output = (OutputSettings)value }, content => content.Output),
            (SequenceSettings.Default, value => new ProjectFileContent { Sequence = (SequenceSettings)value }, content => content.Sequence),
            (LookSettings.Default, value => new ProjectFileContent { Look = (LookSettings)value }, content => content.Look),
        ];
        var walked = new List<string>();
        foreach (var (defaults, make, take) in groups)
        {
            foreach (var leaf in Leaves(defaults.GetType(), defaults.GetType().Name + "."))
            {
                walked.Add(leaf.Path);
                foreach (var value in OtherValues(leaf.Path, leaf.Type, leaf.Get(defaults)))
                {
                    var changed = leaf.With(defaults, value);
                    Assert.True(Equals(value, leaf.Get(take(RoundTrip(make(changed))))), $"{leaf.Path} = {value} did not survive a write and a read.");
                }
            }
        }

        Assert.Contains("OutputSettings.Gif.Size", walked);
        Assert.Contains("OutputSettings.Gif.Rate", walked);
        Assert.Contains("SequenceSettings.HoldStart", walked);
        Assert.Contains("SequenceSettings.Boomerang", walked);
        Assert.Contains("LookSettings.Deflicker", walked);
        Assert.Contains("LookSettings.Strength", walked);
    }

    // Every settable property of a settings record, recursing into the records it holds.
    private static IEnumerable<(string Path, Type Type, Func<object, object?> Get, Func<object, object?, object> With)> Leaves(Type type, string prefix)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.SetMethod is not null))
        {
            if (IsRecord(property.PropertyType))
            {
                foreach (var inner in Leaves(property.PropertyType, prefix + property.Name + "."))
                {
                    yield return (
                        inner.Path,
                        inner.Type,
                        root => inner.Get(property.GetValue(root)!),
                        (root, value) => WithProperty(root, property, inner.With(property.GetValue(root)!, value)));
                }
            }
            else
            {
                yield return (prefix + property.Name, property.PropertyType, property.GetValue, (root, value) => WithProperty(root, property, value));
            }
        }
    }

    private static bool IsRecord(Type type) => type.IsClass && type.GetMethod("<Clone>$") is not null;

    private static object WithProperty(object record, PropertyInfo property, object? value)
    {
        var clone = record.GetType().GetMethod("<Clone>$")!.Invoke(record, null)!;
        property.SetValue(clone, value);
        return clone;
    }

    // Values other than the default for a setting: every other enum value, or another fraction.
    private static IEnumerable<object> OtherValues(string path, Type type, object? current)
    {
        if (type.IsEnum)
        {
            return Enum.GetValues(type).Cast<object>().Where(value => !value.Equals(current));
        }

        if (type == typeof(Fraction))
        {
            return [new Fraction(7, 2)];
        }

        // #49's holds, plays and boomerang.
        if (type == typeof(TimeSpan))
        {
            return [TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(10)];
        }

        if (type == typeof(int) && path.StartsWith("LookSettings.", StringComparison.Ordinal))
        {
            // #45's strength, 0 to 100: its ends and a value between, other than the current one.
            return new object[] { 0, 40, 100 }.Where(value => !value.Equals(current));
        }

        if (type == typeof(int))
        {
            return [3, 5];
        }

        if (type == typeof(bool))
        {
            return [!(bool)current!];
        }

        throw new InvalidOperationException($"No test values for a setting of type {type.Name}: add some here.");
    }
}
