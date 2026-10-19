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
    };

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
}
