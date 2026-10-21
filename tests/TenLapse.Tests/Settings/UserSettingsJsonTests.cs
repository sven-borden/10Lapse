using TenLapse.Core.Projects;
using TenLapse.Core.Settings;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Settings;

/// <summary>The settings file's format (#21): round trip, defaults, a bad field costing only itself, versions.</summary>
public sealed class UserSettingsJsonTests
{
    private static readonly UserSettings _custom = new()
    {
        Timing = new TimingSettings(SpeedMode.PerPhoto, new Fraction(30000, 1001), new Fraction(95, 2), new Fraction(1, 4)),
        Frame = new FrameSettings(FrameAspect.Ratio4x5, FitMode.Fit),
        Output = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4Hevc, OutputQuality.Maximum),
        ImportFolder = @"C:\Photos\Trip",
        ExportFolder = @"D:\Videos\10Lapse",
        SuccessfulExports = 7,
        RatingAskedVersion = "3.0.0.0",
        RecentProjects = PathList.Of([@"C:\Projects\Harbor.10lapse", @"D:\Trips\Alps.10lapse"]),
    };

    [Fact]
    public void TheRecentListKeepsFullPathsOnlyInOrderAndAtMostFive()
    {
        var result = UserSettingsJson.Read("""
            { "version": 1, "recentProjects": [ "C:\\A.10lapse", "relative.10lapse", 3, "C:\\B.10lapse", "c:\\a.10lapse",
              "C:\\C.10lapse", "C:\\D.10lapse", "C:\\E.10lapse", "C:\\F.10lapse" ] }
            """);

        Assert.Equal([@"C:\A.10lapse", @"C:\B.10lapse", @"C:\C.10lapse", @"C:\D.10lapse", @"C:\E.10lapse"], result.Settings.RecentProjects);
        Assert.Equal(2, result.Problems.Count);
    }

    [Fact]
    public void AnOlderFileHasNoRecentProjects() =>
        Assert.Empty(UserSettingsJson.Read("""{ "version": 1 }""").Settings.RecentProjects);

    [Fact]
    public void ARecentPathMovesToTheFrontOnceWhateverItsCase() =>
        Assert.Equal(
            PathList.Of([@"C:\B.10lapse", @"C:\A.10lapse"]),
            PathList.Of([@"C:\A.10lapse", @"C:\B.10lapse"]).WithFirst(@"c:\b.10lapse", UserSettings.MaxRecentProjects));

    private const string V1 = """
        {
          "version": 1,
          "timing": { "mode": "Length", "framesPerSecond": [24, 1], "lengthSeconds": [45, 1], "secondsPerPhoto": [1, 2] },
          "frame": { "aspect": "Ratio9x16", "fit": "Fit" },
          "output": { "resolution": "Qhd1440", "format": "Mp4H264", "quality": "Standard" },
          "importFolder": "C:\\Photos",
          "exportFolder": null
        }
        """;

    [Fact]
    public void EveryFieldSurvivesAWriteAndARead()
    {
        var result = UserSettingsJson.Read(UserSettingsJson.Write(_custom));

        Assert.Empty(result.Problems);
        Assert.Equal(_custom, result.Settings);
        // 29.97 is kept exact, not rounded to a decimal.
        Assert.Equal(new Fraction(30000, 1001), result.Settings.Timing.FramesPerSecond);
    }

    [Fact]
    public void EveryValueOfEveryChoiceSurvivesAWriteAndARead()
    {
        foreach (var mode in Enum.GetValues<SpeedMode>())
        foreach (var aspect in Enum.GetValues<FrameAspect>())
        foreach (var fit in Enum.GetValues<FitMode>())
        {
            var settings = new UserSettings { Timing = TimingSettings.Default with { Mode = mode }, Frame = new FrameSettings(aspect, fit) };
            Assert.Equal(settings, UserSettingsJson.Read(UserSettingsJson.Write(settings)).Settings);
        }

        foreach (var resolution in Enum.GetValues<OutputResolution>())
        foreach (var format in Enum.GetValues<VideoFormat>())
        foreach (var quality in Enum.GetValues<OutputQuality>())
        {
            var settings = new UserSettings { Output = new OutputSettings(resolution, format, quality) };
            Assert.Equal(settings, UserSettingsJson.Read(UserSettingsJson.Write(settings)).Settings);
        }

        foreach (var size in Enum.GetValues<GifSize>())
        foreach (var rate in Enum.GetValues<GifFrameRate>())
        {
            var settings = new UserSettings { Output = OutputSettings.Default with { Format = VideoFormat.Gif, Gif = new GifSettings(size, rate) } };
            Assert.Equal(settings, UserSettingsJson.Read(UserSettingsJson.Write(settings)).Settings);
        }
    }

    [Fact]
    public void TheDefaultsRoundTripWithNoFolders()
    {
        var result = UserSettingsJson.Read(UserSettingsJson.Write(UserSettings.Default));

        Assert.Empty(result.Problems);
        Assert.Equal(UserSettings.Default, result.Settings);
        Assert.Null(result.Settings.ImportFolder);
        Assert.Null(result.Settings.ExportFolder);
    }

    [Fact]
    public void AFileWrittenByThisBuildIsVersionOne()
    {
        Assert.Equal(1, UserSettingsJson.CurrentVersion);
        Assert.Contains("\"version\": 1", UserSettingsJson.Write(_custom));
    }

    [Fact]
    public void AVersionOneFileReadsFieldForField()
    {
        var result = UserSettingsJson.Read(V1);

        Assert.Empty(result.Problems);
        Assert.Equal(SpeedMode.Length, result.Settings.Timing.Mode);
        Assert.Equal(new Fraction(24, 1), result.Settings.Timing.FramesPerSecond);
        Assert.Equal(new Fraction(45, 1), result.Settings.Timing.LengthSeconds);
        Assert.Equal(new Fraction(1, 2), result.Settings.Timing.SecondsPerPhoto);
        Assert.Equal(new FrameSettings(FrameAspect.Ratio9x16, FitMode.Fit), result.Settings.Frame);
        Assert.Equal(new OutputSettings(OutputResolution.Qhd1440, VideoFormat.Mp4H264, OutputQuality.Standard), result.Settings.Output);
        Assert.Equal(@"C:\Photos", result.Settings.ImportFolder);
        Assert.Null(result.Settings.ExportFolder);
    }

    [Fact]
    public void AFileFromBeforeTheRatingPromptNeedsNoMigrationAndStartsTheCountAtZero()
    {
        // #31 added the count and the version asked in as new fields. A new field needs no schema version (an older build
        // ignores it), so the version stays 1 and a #21 file reads quietly with the prompt not yet due.
        var result = UserSettingsJson.Read(V1);

        Assert.Empty(result.Problems);
        Assert.Equal(0, result.Settings.SuccessfulExports);
        Assert.Null(result.Settings.RatingAskedVersion);
    }

    [Fact]
    public void TheRatingFieldsAreWrittenAndRead()
    {
        var json = UserSettingsJson.Write(UserSettings.Default with { SuccessfulExports = 2, RatingAskedVersion = "3.0.1.0" });

        Assert.Contains("\"successfulExports\": 2", json);
        Assert.Contains("\"ratingAskedVersion\": \"3.0.1.0\"", json);
        var result = UserSettingsJson.Read(json);
        Assert.Equal(2, result.Settings.SuccessfulExports);
        Assert.Equal("3.0.1.0", result.Settings.RatingAskedVersion);
    }

    [Theory]
    [InlineData("""{ "version": 1, "importFolder": "C:\\Photos", "successfulExports": -1 }""")]
    [InlineData("""{ "version": 1, "importFolder": "C:\\Photos", "successfulExports": "two" }""")]
    [InlineData("""{ "version": 1, "importFolder": "C:\\Photos", "successfulExports": 1.5 }""")]
    [InlineData("""{ "version": 1, "importFolder": "C:\\Photos", "ratingAskedVersion": 3 }""")]
    [InlineData("""{ "version": 1, "importFolder": "C:\\Photos", "ratingAskedVersion": "3.0\u0000" }""")]
    public void ABadRatingFieldCostsOnlyItselfAndKeepsTheOtherFields(string text)
    {
        var result = UserSettingsJson.Read(text);

        Assert.Single(result.Problems);
        Assert.Equal(@"C:\Photos", result.Settings.ImportFolder);
        Assert.Equal(0, result.Settings.SuccessfulExports);
        Assert.Null(result.Settings.RatingAskedVersion);
    }

    [Fact]
    public void AVersionOneFileWithoutTheFieldsAddedLaterStillReads()
    {
        // The export folder arrived with #24's hook; a file from before it has no such key and says nothing about it.
        var result = UserSettingsJson.Read("""{ "version": 1, "frame": { "aspect": "Ratio1x1", "fit": "Fill" } }""");

        Assert.Empty(result.Problems);
        Assert.Equal(FrameAspect.Ratio1x1, result.Settings.Frame.Aspect);
        Assert.Equal(OutputSettings.Default, result.Settings.Output);
        Assert.Equal(TimingSettings.Default, result.Settings.Timing);
    }

    [Fact]
    public void ACommentAndATrailingCommaDoNotBreakTheRead()
    {
        var result = UserSettingsJson.Read("""{ "version": 1, /* hand edited */ "frame": { "aspect": "Ratio4x3", "fit": "Fit", }, }""");

        Assert.Empty(result.Problems);
        Assert.Equal(new FrameSettings(FrameAspect.Ratio4x3, FitMode.Fit), result.Settings.Frame);
    }

    [Fact]
    public void AnEnumNameIsReadWithoutRegardToCase()
    {
        var result = UserSettingsJson.Read("""{ "version": 1, "output": { "quality": "maximum" } }""");

        Assert.Empty(result.Problems);
        Assert.Equal(OutputQuality.Maximum, result.Settings.Output.Quality);
    }

    [Fact]
    public void ABadFieldFallsBackAloneAndTheRestIsKept()
    {
        var result = UserSettingsJson.Read("""
            {
              "version": 1,
              "timing": { "mode": "Sideways", "framesPerSecond": [500, 1], "lengthSeconds": [60, 1], "secondsPerPhoto": [3, 0] },
              "frame": { "aspect": "Ratio4x5", "fit": 3 },
              "output": { "resolution": "Uhd2160", "format": "Mp4Hevc", "quality": "Ultra" },
              "importFolder": "photos\\trip",
              "exportFolder": "D:\\Videos"
            }
            """);

        // The good fields keep their values.
        Assert.Equal(new Fraction(60, 1), result.Settings.Timing.LengthSeconds);
        Assert.Equal(FrameAspect.Ratio4x5, result.Settings.Frame.Aspect);
        Assert.Equal(OutputResolution.Uhd2160, result.Settings.Output.Resolution);
        Assert.Equal(VideoFormat.Mp4Hevc, result.Settings.Output.Format);
        Assert.Equal(@"D:\Videos", result.Settings.ExportFolder);

        // Each bad one is the default for that field and is listed.
        Assert.Equal(SpeedMode.PerSecond, result.Settings.Timing.Mode);
        Assert.Equal(new Fraction(30, 1), result.Settings.Timing.FramesPerSecond);
        Assert.Equal(new Fraction(1, 2), result.Settings.Timing.SecondsPerPhoto);
        Assert.Equal(FitMode.Fill, result.Settings.Frame.Fit);
        Assert.Equal(OutputQuality.High, result.Settings.Output.Quality);
        Assert.Null(result.Settings.ImportFolder);
        Assert.Equal(6, result.Problems.Count);
        Assert.Contains(result.Problems, problem => problem.Contains("timing.mode", StringComparison.Ordinal));
        Assert.Contains(result.Problems, problem => problem.Contains("output.quality", StringComparison.Ordinal));
        Assert.Contains(result.Problems, problem => problem.Contains("importFolder", StringComparison.Ordinal));
    }

    [Fact]
    public void ASectionOfTheWrongKindIsTheDefaultsForThatSectionOnly()
    {
        var result = UserSettingsJson.Read("""{ "version": 1, "timing": "fast", "frame": { "aspect": "Ratio1x1", "fit": "Fit" } }""");

        Assert.Equal(TimingSettings.Default, result.Settings.Timing);
        Assert.Equal(new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit), result.Settings.Frame);
        Assert.Single(result.Problems);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("null")]
    [InlineData("""{ "frame": { "aspect": "Ratio1x1" } }""")]
    [InlineData("""{ "version": 0 }""")]
    [InlineData("""{ "version": "one" }""")]
    public void AFileThatIsNotUsableIsTheDefaultsWithOneProblem(string text)
    {
        var result = UserSettingsJson.Read(text);

        Assert.Equal(UserSettings.Default, result.Settings);
        Assert.Single(result.Problems);
    }

    [Fact]
    public void ANewerVersionKeepsTheFieldsThisBuildKnows()
    {
        var result = UserSettingsJson.Read("""{ "version": 99, "frame": { "aspect": "Ratio1x1", "fit": "Fit" }, "somethingNew": { } }""");

        Assert.Equal(new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit), result.Settings.Frame);
        var problem = Assert.Single(result.Problems);
        Assert.Contains("99", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsNullFromARead()
    {
        Assert.NotNull(UserSettingsJson.Read(null).Settings);
    }

    [Theory]
    [InlineData("""{ "version": 1, "timing": { "framesPerSecond": [null, 1] } }""")]
    [InlineData("""{ "version": 1, "timing": { "framesPerSecond": [1, null] } }""")]
    [InlineData("""{ "version": 1, "timing": { "framesPerSecond": [null, null] } }""")]
    [InlineData("""{ "version": 1, "timing": { "framesPerSecond": [[1], 2] } }""")]
    [InlineData("""{ "version": 1, "timing": { "framesPerSecond": [{ "a": 1 }, 2] } }""")]
    [InlineData("""{ "version": 1, "timing": { "lengthSeconds": ["a", 1] } }""")]
    [InlineData("""{ "version": 1, "timing": { "secondsPerPhoto": [1.5, 1] } }""")]
    [InlineData("""{ "version": 1, "timing": { "framesPerSecond": [1e400, 1] } }""")]
    [InlineData("""{ "version": 1, "timing": { "framesPerSecond": {} } }""")]
    [InlineData("""{ "version": 1, "timing": [] }""")]
    [InlineData("""{ "version": 1, "frame": { "aspect": [1], "fit": 5 } }""")]
    [InlineData("""{ "version": 1, "output": { "resolution": null, "format": {}, "quality": true } }""")]
    [InlineData("""{ "version": 1, "importFolder": [1] }""")]
    [InlineData("""{ "version": 1, "exportFolder": 5 }""")]
    [InlineData("""{ "version": 1, "importFolder": "C:\\a\u0000b" }""")]
    [InlineData("""[null, 1]""")]
    [InlineData("""{ "version": [1], "timing": null }""")]
    public void HostileShapesNeverThrowAndGiveUsableSettings(string text)
    {
        var result = UserSettingsJson.Read(text);

        Assert.NotNull(result.Settings);
        Assert.NotNull(result.Problems);
    }

    [Fact]
    public void ANullFractionElementIsOneProblemAndTheOtherFieldsKeepTheirValues()
    {
        var result = UserSettingsJson.Read("""
            { "version": 1,
              "timing": { "framesPerSecond": [null, 1], "lengthSeconds": [45, 1] },
              "frame": { "aspect": "Ratio9x16", "fit": "Fit" } }
            """);

        Assert.Single(result.Problems);
        Assert.Equal(UserSettings.Default.Timing.FramesPerSecond, result.Settings.Timing.FramesPerSecond);
        Assert.Equal(new Fraction(45, 1), result.Settings.Timing.LengthSeconds);
        Assert.Equal(FrameAspect.Ratio9x16, result.Settings.Frame.Aspect);
    }
}
