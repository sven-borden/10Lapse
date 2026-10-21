using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Presentation.Export;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>Animated GIF in the Output panel and the export (#51): what shows, what is stored, what the words say.</summary>
public sealed class GifOutputPanelTests
{
    private static ShellViewModel Shell() => new(new FakeDialogService(), new FakeErrorSink());

    private static OutputPanelViewModel Panel(ShellViewModel shell, IOutputCapabilities capabilities)
    {
        var panel = new OutputPanelViewModel(shell, new FakeErrorSink());
        panel.SetCapabilities(capabilities);
        return panel;
    }

    private static OutputOption<VideoFormat> Gif(OutputPanelViewModel panel) => panel.Formats.Single(option => option.Value == VideoFormat.Gif);

    [Fact]
    public void ChoosingGifShowsItsSizeAndRateInPlaceOfResolutionAndQuality()
    {
        var shell = Shell();
        var panel = Panel(shell, new FakeOutputCapabilities());
        Assert.True(panel.ShowsVideoOptions);
        Assert.False(panel.ShowsGifOptions);
        Assert.Equal("Animated GIF", Gif(panel).Label);

        panel.SelectedFormat = Gif(panel);

        Assert.Equal(VideoFormat.Gif, shell.CurrentProject.Model.Output.Format);
        Assert.True(panel.IsGif);
        Assert.False(panel.ShowsVideoOptions);
        Assert.True(panel.ShowsGifOptions);
        Assert.Equal(["Small · 320 × 180", "Medium · 480 × 270", "Large · 640 × 360"], panel.GifSizes.Select(option => option.Label));
        Assert.Equal(["Up to 10 fps", "Up to 20 fps", "Up to 25 fps"], panel.GifRates.Select(option => option.Label));
        Assert.Equal(GifSize.Medium, panel.SelectedGifSize?.Value);
        Assert.Equal(GifFrameRate.Fps10, panel.SelectedGifRate?.Value);
        Assert.True(panel.CanExport);
        Assert.Equal(VideoFormat.Gif, panel.EffectiveOutput?.Format);
        Assert.Empty(panel.Notice);
    }

    [Fact]
    public void PickingASizeAndARateStoresThemAndTheVideoSizeFollows()
    {
        var shell = Shell();
        var panel = Panel(shell, new FakeOutputCapabilities());
        panel.SelectedFormat = Gif(panel);

        panel.SelectedGifSize = panel.GifSizes.Single(option => option.Value == GifSize.Large);
        panel.SelectedGifRate = panel.GifRates.Single(option => option.Value == GifFrameRate.Fps25);

        var project = shell.CurrentProject.Model;
        Assert.Equal(new GifSettings(GifSize.Large, GifFrameRate.Fps25), project.Output.Gif);
        Assert.Equal(new PixelSize(640, 360), project.Snapshot.VideoSize);

        // The sizes follow the shape.
        project.FrameSettings = project.FrameSettings with { Aspect = FrameAspect.Ratio9x16 };
        Assert.Equal("Large · 360 × 640", panel.SelectedGifSize?.Label);

        // Back to MP4: resolution and quality are as they were, the GIF settings wait.
        panel.SelectedFormat = panel.Formats.First();
        Assert.Equal(OutputSettings.Default.Resolution, project.Output.Resolution);
        Assert.Equal(OutputSettings.Default.Quality, project.Output.Quality);
        Assert.True(panel.ShowsVideoOptions);
        Assert.Equal(new GifSettings(GifSize.Large, GifFrameRate.Fps25), project.Output.Gif);
    }

    [Fact]
    public void GifIsOfferedWhereNoVideoCanBeExportedAndIsNeverReplaced()
    {
        // A PC whose encoders take no size at all (or the conservative answer): GIF is still there and exports.
        var shell = Shell();
        var panel = Panel(shell, new FakeOutputCapabilities { Max = null });
        Assert.False(panel.CanExport);

        panel.SelectedFormat = Gif(panel);

        Assert.True(panel.CanExport);
        Assert.Equal(shell.CurrentProject.Model.Output, panel.EffectiveOutput);
        panel.SetCapabilities(ConservativeOutputCapabilities.Instance);
        Assert.Equal(VideoFormat.Gif, panel.EffectiveOutput?.Format);
        Assert.False(panel.HasReplacement);
    }

    [Fact]
    public void AVideoFormatThisPcLacksBecomesH264NotGif()
    {
        var shell = Shell();
        shell.CurrentProject.Model.Output = OutputSettings.Default with { Format = VideoFormat.Mp4Hevc };
        var panel = Panel(shell, new FakeOutputCapabilities { Hevc = false });
        Assert.Equal(VideoFormat.Mp4H264, panel.EffectiveOutput?.Format);
    }

    [Fact]
    public void TheEstimateIsARangeForTheFramesTheGifShows()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.AddFrames(TestFrames.Folder("harbor", 300));
        var panel = Panel(shell, new FakeOutputCapabilities());
        panel.SelectedFormat = Gif(panel);

        // 300 photos at 30 fps are 10 s; at up to 10 fps the GIF shows 100 of them.
        var bytes = GifOutput.EstimateBytes(100, new PixelSize(480, 270));
        Assert.Equal(OutputText.GifEstimate(bytes), panel.EstimatedSize);
        Assert.Equal("4.4 MB to 9.4 MB", OutputText.GifEstimate(bytes, CultureInfo.GetCultureInfo("en-US")));
        Assert.Empty(panel.Note);
    }

    [Fact]
    public void AVideoSlowerThanThePresetKeepsItsSpeedAndTheNoteSaysSo()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.AddFrames(TestFrames.Folder("harbor", 20));
        project.Timing = TimingSettings.PerPhoto(new Fraction(1, 2));
        var panel = Panel(shell, new FakeOutputCapabilities());
        panel.SelectedFormat = Gif(panel);

        Assert.Equal("The GIF keeps your video's speed: 2 fps.", panel.Note);
        Assert.Equal(OutputText.GifEstimate(GifOutput.EstimateBytes(20, new PixelSize(480, 270))), panel.EstimatedSize);
    }

    [Theory]
    [InlineData(0, "Less than 1 MB")]
    [InlineData(600_000, "Less than 1 MB")]
    [InlineData(1_000_000, "Up to 1.4 MB")]
    [InlineData(10L << 20, "7 MB to 15 MB")]
    public void TheGifEstimateSaysWhatItCan(long bytes, string expected) =>
        Assert.Equal(expected, OutputText.GifEstimate(bytes, CultureInfo.GetCultureInfo("en-US")));

    [Fact]
    public void TheGifEstimatesNumbersAreWrittenInTheUsersCulture() =>
        // The words follow the app's language (English in the tests), the numbers the regional format.
        Assert.Equal("4,4 MB to 9,4 MB", OutputText.GifEstimate(6_560_064, CultureInfo.GetCultureInfo("de-DE")));

    [Fact]
    public void TheExportWordsNameTheGifsSizeAndRate()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("480 × 270 · GIF · 10 fps", ExportText.GifOutputSummary(new PixelSize(480, 270), new Fraction(10, 1), culture));
        Assert.Equal("480 × 270 · GIF · 2 s per photo", ExportText.GifOutputSummary(new PixelSize(480, 270), new Fraction(1, 2), culture));

        var gif = new ExportedVideo(@"C:\Videos\Harbor sunset.gif", 100, FrameTiming.Duration(100, new Fraction(10, 1)), new PixelSize(480, 270), new Fraction(10, 1), VideoFormat.Gif, 4_410_000);
        Assert.Equal("10.0 s · 480 × 270 · GIF · 4.21 MB", ExportText.ReadySummary(gif, OutputSettings.Default with { Format = VideoFormat.Gif }, culture));

        Assert.Equal("Harbor sunset.gif", ExportText.SuggestedFileName("Harbor sunset", VideoFormat.Gif));
        Assert.Equal("Harbor sunset.mp4", ExportText.SuggestedFileName("Harbor sunset", VideoFormat.Mp4Hevc));
        Assert.Equal(OutputText.GifFormatOption, ExportText.FileTypeNameFor(".GIF"));
        Assert.Equal(ExportText.FileTypeName, ExportText.FileTypeNameFor(".mp4"));
    }

    [Fact]
    public void AGifExportSuggestsAGifNameAndSaysWhatItWrites()
    {
        using var app = new ExportHarness(
            capabilities: new FakeOutputCapabilities(),
            setup: project => project.Output = OutputSettings.Default with { Format = VideoFormat.Gif });
        app.Picker.Answer = @"C:\Videos\10Lapse\Harbor sunset.gif";

        app.StartExport();

        Assert.Equal(("Harbor sunset.gif", (string?)null), Assert.Single(app.Picker.Asked));
        var (snapshot, _) = Assert.Single(app.Exporter.Calls);
        Assert.Equal(VideoFormat.Gif, snapshot.Output.Format);
        Assert.Equal(new PixelSize(480, 270), snapshot.VideoSize);

        // 120 photos at 30 fps as a GIF of up to 10 fps.
        Assert.Equal("480 × 270 · GIF · 10 fps", app.On(() => app.Export.OutputSummary));
    }

    [Fact]
    public void AFormatChangedWhileTheSaveDialogIsOpenStartsNothing()
    {
        using var app = new ExportHarness(capabilities: new FakeOutputCapabilities());
        app.Picker.WhileOpen = () => app.Project.Output = app.Project.Output with { Format = VideoFormat.Gif };

        app.StartExport();

        Assert.Empty(app.Exporter.Calls);
        Assert.Equal(ExportText.ChangedDuringDialog, Assert.Single(app.InfoBars.Shown).Message);
    }
}
