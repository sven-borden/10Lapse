using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Tests.Import;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>The Output panel (#23) against fake capabilities: what it offers, what it replaces, what it estimates.</summary>
public sealed class OutputPanelViewModelTests
{
    private static readonly Fraction _fps30 = new(30, 1);

    private static ShellViewModel Shell(FakeErrorSink? errors = null) =>
        new(new FakeDialogService(), errors ?? new FakeErrorSink());

    private static OutputPanelViewModel Panel(ShellViewModel shell, IOutputCapabilities? capabilities, FakeErrorSink? errors = null)
    {
        var panel = new OutputPanelViewModel(shell, errors ?? new FakeErrorSink());
        if (capabilities is not null)
        {
            panel.SetCapabilities(capabilities);
        }

        return panel;
    }

    private static OutputResolution[] Offered(OutputPanelViewModel panel) => [.. panel.Resolutions.Select(option => option.Value)];

    [Fact]
    public void ThisPcThatDoesEverythingOffersEverySizeAndBothFormatsWithNoNote()
    {
        var shell = Shell();
        var panel = Panel(shell, new FakeOutputCapabilities());

        Assert.Equal([OutputResolution.Hd720, OutputResolution.Hd1080, OutputResolution.Qhd1440, OutputResolution.Uhd2160], Offered(panel));
        Assert.Equal([VideoFormat.Mp4H264, VideoFormat.Mp4Hevc], panel.Formats.Select(option => option.Value));
        Assert.Equal(["Standard", "High", "Maximum"], panel.Qualities.Select(option => option.Label));
        Assert.Equal(["720p · 1280 × 720", "1080p · 1920 × 1080", "1440p · 2560 × 1440", "4K · 3840 × 2160"], panel.Resolutions.Select(option => option.Label));
        Assert.Equal(["MP4 · H.264", "MP4 · HEVC"], panel.Formats.Select(option => option.Label));
        Assert.Equal(string.Empty, panel.Note);
        Assert.Equal(string.Empty, panel.Notice);
        Assert.True(panel.CanExport);

        // The project's defaults are what is selected.
        Assert.Equal(OutputResolution.Hd1080, panel.SelectedResolution?.Value);
        Assert.Equal(VideoFormat.Mp4H264, panel.SelectedFormat?.Value);
        Assert.Equal(OutputQuality.High, panel.SelectedQuality?.Value);
    }

    [Fact]
    public void WithoutAnHevcDecoderHevcIsNotOffered()
    {
        var shell = Shell();
        var panel = Panel(shell, new FakeOutputCapabilities { Hevc = false });

        Assert.Equal([VideoFormat.Mp4H264], panel.Formats.Select(option => option.Value));
        Assert.Equal(VideoFormat.Mp4H264, panel.SelectedFormat?.Value);
        Assert.Equal(string.Empty, panel.Notice);
    }

    [Fact]
    public void AProjectThatAsksForHevcOnAPcWithoutItGetsH264AndIsTold()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.Output = project.Output with { Format = VideoFormat.Mp4Hevc };
        var panel = Panel(shell, new FakeOutputCapabilities { Hevc = false });

        Assert.Equal(VideoFormat.Mp4H264, project.Output.Format);
        Assert.Equal(VideoFormat.Mp4H264, panel.SelectedFormat?.Value);
        Assert.Equal("HEVC can't be exported on this PC, so H.264 is selected.", panel.Notice);
        Assert.True(panel.HasNotice);
        Assert.True(panel.CanExport);
    }

    [Fact]
    public void ALimitOf1440pHidesTheLargerSizesAndSaysWhy()
    {
        var shell = Shell();
        var panel = Panel(shell, new FakeOutputCapabilities { Max = OutputResolution.Qhd1440 });

        Assert.Equal([OutputResolution.Hd720, OutputResolution.Hd1080, OutputResolution.Qhd1440], Offered(panel));
        Assert.DoesNotContain(panel.Resolutions, option => option.Label.StartsWith("4K", StringComparison.Ordinal));
        Assert.Equal("This PC's graphics hardware exports up to 1440p.", panel.Note);
        Assert.True(panel.HasNote);
        Assert.Equal(OutputResolution.Hd1080, panel.SelectedResolution?.Value);
    }

    [Fact]
    public void SizesOnlyTheSoftwareEncoderTakesAreOfferedWithTheSlowerNote()
    {
        var shell = Shell();
        var panel = Panel(shell, new FakeOutputCapabilities { HardwareMax = OutputResolution.Hd1080 });

        Assert.Equal(4, panel.Resolutions.Count);
        Assert.Equal("Software for larger sizes.", panel.Note);
    }

    [Fact]
    public void AStoredResolutionThatIsNoLongerOfferedBecomesTheLargestOfferedOneAndIsSaid()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.Output = project.Output with { Resolution = OutputResolution.Uhd2160 };
        var panel = Panel(shell, new FakeOutputCapabilities { Max = OutputResolution.Qhd1440 });

        Assert.Equal(OutputResolution.Qhd1440, project.Output.Resolution);
        Assert.Equal(OutputResolution.Qhd1440, panel.SelectedResolution?.Value);
        Assert.Equal("4K can't be exported on this PC with these settings, so 1440p is selected.", panel.Notice);
        Assert.True(panel.CanExport);
        Assert.True(project.IsDirty);
    }

    [Fact]
    public void RaisingTheFrameRateReplacesASizeThePcCannotDoAtThatRate()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.Output = project.Output with { Resolution = OutputResolution.Uhd2160 };

        // 4K up to 60 fps, 1440p above (as the probe found for H.264 on this PC).
        var panel = Panel(shell, new FakeOutputCapabilities { MaxAtRate = rate => rate > new Fraction(60, 1) ? OutputResolution.Qhd1440 : OutputResolution.Uhd2160 });
        Assert.Equal(OutputResolution.Uhd2160, project.Output.Resolution);
        Assert.Equal(string.Empty, panel.Notice);

        project.Timing = TimingSettings.PerSecond(new Fraction(120, 1));

        Assert.Equal(OutputResolution.Qhd1440, project.Output.Resolution);
        Assert.Equal([OutputResolution.Hd720, OutputResolution.Hd1080, OutputResolution.Qhd1440], Offered(panel));
        Assert.StartsWith("4K can't be exported", panel.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void WhileTheProbeIsStillRunningNothingIsOfferedAndTheProjectIsLeftAlone()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.Output = project.Output with { Resolution = OutputResolution.Uhd2160, Format = VideoFormat.Mp4Hevc };
        var panel = Panel(shell, capabilities: null);

        Assert.True(panel.IsPending);
        Assert.False(panel.IsSettled);
        Assert.Empty(panel.Resolutions);
        Assert.Empty(panel.Formats);
        Assert.Null(panel.SelectedResolution);
        Assert.Null(panel.SelectedFormat);
        Assert.False(panel.CanExport);
        Assert.Equal("Checking what this PC can encode…", panel.Status);
        Assert.Equal(string.Empty, panel.EstimatedSize);
        Assert.Equal(OutputResolution.Uhd2160, project.Output.Resolution);
        Assert.Equal(VideoFormat.Mp4Hevc, project.Output.Format);
        Assert.Equal(string.Empty, panel.Notice);
    }

    [Fact]
    public void WhenTheProbeAnswersTheListsAppearInOneStepWithoutFlicker()
    {
        var shell = Shell();
        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", 100));
        var panel = Panel(shell, capabilities: null);
        var raised = new List<string?>();
        panel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        panel.SetCapabilities(new FakeOutputCapabilities());

        Assert.False(panel.IsPending);
        Assert.Equal(string.Empty, panel.Status);
        Assert.Equal(1, raised.Count(name => name == nameof(OutputPanelViewModel.Resolutions)));
        Assert.Equal(1, raised.Count(name => name == nameof(OutputPanelViewModel.Formats)));
        Assert.Equal(1, raised.Count(name => name == nameof(OutputPanelViewModel.SelectedResolution)));
        Assert.Equal(1, raised.Count(name => name == nameof(OutputPanelViewModel.EstimatedSize)));

        // The same answer again changes nothing the lists would redraw.
        raised.Clear();
        panel.SetCapabilities(new FakeOutputCapabilities());
        Assert.DoesNotContain(nameof(OutputPanelViewModel.Resolutions), raised);
        Assert.DoesNotContain(nameof(OutputPanelViewModel.Formats), raised);
        Assert.DoesNotContain(nameof(OutputPanelViewModel.SelectedResolution), raised);
    }

    [Fact]
    public void ChoosingAValueWritesTheProjectAndClearsTheNotice()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.Output = project.Output with { Resolution = OutputResolution.Uhd2160 };
        var panel = Panel(shell, new FakeOutputCapabilities { Max = OutputResolution.Qhd1440 });
        Assert.NotEqual(string.Empty, panel.Notice);

        panel.SelectedResolution = panel.Resolutions.First(option => option.Value == OutputResolution.Hd720);
        panel.SelectedQuality = panel.Qualities.First(option => option.Value == OutputQuality.Maximum);

        Assert.Equal(new OutputSettings(OutputResolution.Hd720, VideoFormat.Mp4H264, OutputQuality.Maximum), project.Output);
        Assert.Equal(OutputResolution.Hd720, panel.SelectedResolution?.Value);
        Assert.Equal(OutputQuality.Maximum, panel.SelectedQuality?.Value);
        Assert.Equal(string.Empty, panel.Notice);
        Assert.True(project.IsDirty);
    }

    [Fact]
    public async Task AnEditThatCannotBeWrittenGoesToTheErrorPathAndTheListGoesBack()
    {
        var errors = new FakeErrorSink();
        var shell = Shell(errors);
        var project = shell.CurrentProject.Model;
        var panel = Panel(shell, new FakeOutputCapabilities(), errors);
        var raised = new List<string?>();
        panel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // A write from another thread throws (the project belongs to the thread that made it); the panel must not.
        using var other = new UiThread();
        var pick = panel.Resolutions.First(option => option.Value == OutputResolution.Qhd1440);
        await other.RunAsync(() => panel.SelectedResolution = pick);

        Assert.Equal(OutputResolution.Hd1080, project.Output.Resolution);
        var shown = Assert.Single(errors.Shown);
        Assert.Equal(OutputText.ChangeFailed, shown.Context);
        Assert.Contains(nameof(OutputPanelViewModel.SelectedResolution), raised);
        Assert.Equal(OutputResolution.Hd1080, panel.SelectedResolution?.Value);
    }

    [Fact]
    public void TheEstimateIsExportBitratesAndFollowsTheFramesTheRateAndTheQuality()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var panel = Panel(shell, new FakeOutputCapabilities());

        Assert.Equal(string.Empty, panel.EstimatedSize);
        Assert.False(panel.HasEstimate);

        project.AddFrames(TestFrames.Folder("a", 300));
        var expected = ExportBitrate.EstimateBytes(300, _fps30, new PixelSize(1920, 1080), VideoFormat.Mp4H264, OutputQuality.High);
        Assert.Equal(OutputText.Estimate(expected), panel.EstimatedSize);
        Assert.True(panel.HasEstimate);

        project.AddFrames(TestFrames.Folder("b", 300));
        var more = ExportBitrate.EstimateBytes(600, _fps30, new PixelSize(1920, 1080), VideoFormat.Mp4H264, OutputQuality.High);
        Assert.Equal(OutputText.Estimate(more), panel.EstimatedSize);
        Assert.NotEqual(OutputText.Estimate(expected), panel.EstimatedSize);

        project.Timing = TimingSettings.PerSecond(new Fraction(60, 1));
        var faster = ExportBitrate.EstimateBytes(600, new Fraction(60, 1), new PixelSize(1920, 1080), VideoFormat.Mp4H264, OutputQuality.High);
        Assert.Equal(OutputText.Estimate(faster), panel.EstimatedSize);

        panel.SelectedQuality = panel.Qualities.First(option => option.Value == OutputQuality.Standard);
        panel.SelectedFormat = panel.Formats.First(option => option.Value == VideoFormat.Mp4Hevc);
        panel.SelectedResolution = panel.Resolutions.First(option => option.Value == OutputResolution.Uhd2160);
        var small = ExportBitrate.EstimateBytes(600, new Fraction(60, 1), new PixelSize(3840, 2160), VideoFormat.Mp4Hevc, OutputQuality.Standard);
        Assert.Equal(OutputText.Estimate(small), panel.EstimatedSize);
    }

    [Fact]
    public void TheEstimateIsWrittenInTheCurrentCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            var shell = Shell();
            shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", 9000));
            shell.CurrentProject.Model.Output = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4H264, OutputQuality.Maximum);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var german = Panel(shell, new FakeOutputCapabilities()).EstimatedSize;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var english = Panel(shell, new FakeOutputCapabilities()).EstimatedSize;

            Assert.Matches(@"^About \d(,\d)? GB$", german);
            Assert.Equal(german.Replace(',', '.'), english);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void MatchPhotosTakesTheShapeOfThePhotosForTheSizesAndTheEstimate()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.FrameSettings = new FrameSettings(FrameAspect.MatchPhotos, FitMode.Fill);
        project.AddFrames([.. TestFrames.Folder("a", 10).Select(frame => frame with { PixelSize = new PixelSize(3024, 4032) })]);
        var panel = Panel(shell, new FakeOutputCapabilities());

        Assert.Equal("1080p · 1080 × 1440", panel.Resolutions.First(option => option.Value == OutputResolution.Hd1080).Label);
        var expected = ExportBitrate.EstimateBytes(10, _fps30, new PixelSize(1080, 1440), VideoFormat.Mp4H264, OutputQuality.High);
        Assert.Equal(OutputText.Estimate(expected), panel.EstimatedSize);
    }

    [Fact]
    public async Task NewProjectMakesThePanelFollowTheNewProject()
    {
        var shell = Shell();
        var old = shell.CurrentProject.Model;
        old.Output = new OutputSettings(OutputResolution.Qhd1440, VideoFormat.Mp4Hevc, OutputQuality.Maximum);
        var panel = Panel(shell, new FakeOutputCapabilities());
        Assert.Equal(OutputResolution.Qhd1440, panel.SelectedResolution?.Value);

        await shell.NewProjectCommand.ExecuteAsync(null);

        var fresh = shell.CurrentProject.Model;
        Assert.NotSame(old, fresh);
        Assert.Equal(OutputResolution.Hd1080, panel.SelectedResolution?.Value);
        Assert.Equal(OutputQuality.High, panel.SelectedQuality?.Value);
        panel.SelectedResolution = panel.Resolutions.First(option => option.Value == OutputResolution.Hd720);
        Assert.Equal(OutputResolution.Hd720, fresh.Output.Resolution);
        Assert.Equal(OutputResolution.Qhd1440, old.Output.Resolution);
    }

    [Fact]
    public void NothingOfferedMeansNothingSelectedAndNoExport()
    {
        var shell = Shell();
        var panel = Panel(shell, new FakeOutputCapabilities { Max = null });

        Assert.Empty(panel.Resolutions);
        Assert.Null(panel.SelectedResolution);
        Assert.False(panel.CanExport);
        Assert.True(panel.HasNote);
        Assert.Equal(OutputResolution.Hd1080, shell.CurrentProject.Model.Output.Resolution);
    }

    [Fact]
    public void TheConservativeAnswerOffersH264UpTo1080pAndSaysSo()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.Output = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4Hevc, OutputQuality.High);
        var panel = Panel(shell, ConservativeOutputCapabilities.Instance);

        Assert.Equal([OutputResolution.Hd720, OutputResolution.Hd1080], Offered(panel));
        Assert.Equal([VideoFormat.Mp4H264], panel.Formats.Select(option => option.Value));
        Assert.Equal(OutputText.ConservativeNote, panel.Note);
        Assert.Equal(new OutputSettings(OutputResolution.Hd1080, VideoFormat.Mp4H264, OutputQuality.High), project.Output);
    }

    [Fact]
    public void AProvisionalAnswerShowsTheReplacementButNeverWritesTheProject()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var stored = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4Hevc, OutputQuality.Maximum);
        project.Output = stored;
        project.MarkExported(project.Snapshot.Version);
        var panel = Panel(shell, capabilities: null);

        panel.SetCapabilities(ConservativeOutputCapabilities.Instance, provisional: true);

        Assert.Equal(OutputResolution.Hd1080, panel.SelectedResolution?.Value);
        Assert.Equal(VideoFormat.Mp4H264, panel.SelectedFormat?.Value);
        Assert.NotEmpty(panel.Notice);
        Assert.Equal(stored, project.Output);
        Assert.False(project.IsDirty);
        Assert.False(panel.CanExport);
    }

    [Fact]
    public void ALateRealAnswerRestoresTheStoredSettingsAndClearsTheNotice()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var stored = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4Hevc, OutputQuality.Maximum);
        project.Output = stored;
        project.MarkExported(project.Snapshot.Version);
        var panel = Panel(shell, capabilities: null);
        panel.SetCapabilities(ConservativeOutputCapabilities.Instance, provisional: true);

        panel.SetCapabilities(new FakeOutputCapabilities());

        Assert.Equal(OutputResolution.Uhd2160, panel.SelectedResolution?.Value);
        Assert.Equal(VideoFormat.Mp4Hevc, panel.SelectedFormat?.Value);
        Assert.Equal(string.Empty, panel.Notice);
        Assert.False(panel.HasNotice);
        Assert.Equal(stored, project.Output);
        Assert.False(project.IsDirty);
        Assert.True(panel.CanExport);
    }

    [Fact]
    public void ALateRealAnswerThatCannotDoTheStoredSizeWritesTheCorrectionOnceAndSaysSo()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.Output = project.Output with { Resolution = OutputResolution.Uhd2160 };
        project.MarkExported(project.Snapshot.Version);
        var panel = Panel(shell, capabilities: null);
        panel.SetCapabilities(ConservativeOutputCapabilities.Instance, provisional: true);
        Assert.False(project.IsDirty);
        var writes = 0;
        project.Changed += (_, e) => writes += e.Changes.HasFlag(ProjectChanges.Output) ? 1 : 0;

        panel.SetCapabilities(new FakeOutputCapabilities { Max = OutputResolution.Qhd1440 });

        Assert.Equal(1, writes);
        Assert.Equal(OutputResolution.Qhd1440, project.Output.Resolution);
        Assert.Equal(OutputResolution.Qhd1440, panel.SelectedResolution?.Value);
        Assert.Equal("4K can't be exported on this PC with these settings, so 1440p is selected.", panel.Notice);
        Assert.True(project.IsDirty);
        Assert.True(panel.CanExport);
    }

    [Fact]
    public async Task ADeliveredAnswerReachesThePanelOnTheUiThread()
    {
        using var ui = new UiThread();
        var shell = Shell();
        var panel = Panel(shell, capabilities: null);
        var probe = Task.FromResult<IOutputCapabilities>(new FakeOutputCapabilities { Max = OutputResolution.Hd1080 });

        await panel.DeliverAsync(probe, ui, new FakeErrorSink(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(panel.IsSettled);
        Assert.Equal([OutputResolution.Hd720, OutputResolution.Hd1080], Offered(panel));
    }

    [Fact]
    public async Task AFailedProbeIsShownAndLeavesTheConservativeAnswer()
    {
        using var ui = new UiThread();
        var errors = new FakeErrorSink();
        var shell = Shell(errors);
        var panel = Panel(shell, capabilities: null, errors);
        var probe = Task.FromException<IOutputCapabilities>(new InvalidOperationException("probe bug"));

        await panel.DeliverAsync(probe, ui, errors, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var shown = Assert.Single(errors.Shown);
        Assert.Equal(OutputText.ProbeFailed, shown.Context);
        Assert.True(panel.IsSettled);
        Assert.Equal([OutputResolution.Hd720, OutputResolution.Hd1080], Offered(panel));
    }

    [Fact]
    public async Task ASlowProbeSettlesOnTheConservativeAnswerAndTheRealOneReplacesIt()
    {
        using var ui = new UiThread();
        var shell = Shell();
        var panel = Panel(shell, capabilities: null);
        var probe = new TaskCompletionSource<IOutputCapabilities>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource();
        panel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OutputPanelViewModel.Resolutions))
            {
                settled.TrySetResult();
            }
        };

        var delivery = panel.DeliverAsync(probe.Task, ui, new FakeErrorSink(), TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal([OutputResolution.Hd720, OutputResolution.Hd1080], Offered(panel));
        Assert.False(delivery.IsCompleted);

        probe.SetResult(new FakeOutputCapabilities());
        await delivery.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(4, panel.Resolutions.Count);
        Assert.Equal(2, panel.Formats.Count);
    }
}

/// <summary>
/// A PC for the tests: the largest size its encoders take (per rate if need be), whether HEVC is offered, and how far
/// the graphics hardware goes before the software encoder takes over.
/// </summary>
internal sealed class FakeOutputCapabilities : IOutputCapabilities
{
    /// <summary>The largest size at any rate; null means not even 720p.</summary>
    public OutputResolution? Max { get; init; } = OutputResolution.Uhd2160;

    /// <summary>The largest size at a given rate, when it depends on it. Overrides <see cref="Max"/>.</summary>
    public Func<Fraction, OutputResolution>? MaxAtRate { get; init; }

    /// <summary>The largest size the hardware takes; null means the same as <see cref="Max"/>.</summary>
    public OutputResolution? HardwareMax { get; init; }

    /// <summary>Whether HEVC is encodable and playable.</summary>
    public bool Hevc { get; init; } = true;

    public bool IsAvailable(VideoFormat format) => format == VideoFormat.Mp4H264 || Hevc;

    public bool Supports(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) =>
        IsAvailable(format) && (MaxAtRate is { } byRate ? resolution <= byRate(frameRate) : Max is { } max && resolution <= max);

    public bool UsesHardware(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) =>
        Supports(format, resolution, aspect, frameRate, photoSize) && resolution <= (HardwareMax ?? Max ?? OutputResolution.Hd720);

    public string Summary(VideoFormat format, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize)
    {
        if (HardwareMax is not null)
        {
            return "Software for larger sizes.";
        }

        return Max is { } max
            ? $"This PC's graphics hardware exports up to {OutputText.ResolutionName(MaxAtRate?.Invoke(frameRate) ?? max)}."
            : "This PC cannot export this video.";
    }
}
