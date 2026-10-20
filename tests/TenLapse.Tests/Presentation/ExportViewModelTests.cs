using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// The export (#24) against a fake exporter: when Export shows, what is exported, the states it goes through (editing,
/// exporting, finished, cancelled, failed), the Save dialog's folder, the edits it holds back, closing the window during it,
/// and how often the panel follows the progress.
/// </summary>
public sealed class ExportViewModelTests
{
    private static readonly FakeOutputCapabilities _everything = new();

    // ---- When Export shows ----

    [Fact]
    public void ExportIsAbsentWithoutFramesAndShowsOnceThereAreSome()
    {
        using var app = new ExportHarness(frames: 0, capabilities: _everything);

        Assert.False(app.On(() => app.Export.ShowsExportButton));
        app.On(() => app.Project.AddFrames(Projects.TestFrames.Folder("a", 3)));
        Assert.True(app.On(() => app.Export.ShowsExportButton));
    }

    [Fact]
    public void ExportIsAbsentUntilTheOutputPanelHasAnOutputThisPcCanExport()
    {
        using var app = new ExportHarness(capabilities: null);

        Assert.False(app.On(() => app.Export.ShowsExportButton));
        app.On(() => app.Output.SetCapabilities(new FakeOutputCapabilities { Max = null }));
        Assert.False(app.On(() => app.Export.ShowsExportButton));
        app.On(() => app.Output.SetCapabilities(_everything));
        Assert.True(app.On(() => app.Export.ShowsExportButton));
    }

    [Fact]
    public void ExportWithoutFramesOrAnOutputDoesNothing()
    {
        using var app = new ExportHarness(frames: 0, capabilities: _everything);

        app.StartExport();

        Assert.Empty(app.Picker.Asked);
        Assert.Empty(app.Exporter.Calls);
        Assert.Equal(ExportPanelState.Editing, app.On(() => app.Export.State));
    }

    // ---- What is exported ----

    [Fact]
    public void TheExportUsesTheEffectiveOutputAndItsSizeNotTheStoredPreference()
    {
        // The user asked for 4K HEVC; this PC exports up to 1440p and no HEVC, so the panel shows 1440p H.264.
        using var app = new ExportHarness(
            capabilities: new FakeOutputCapabilities { Max = OutputResolution.Qhd1440, Hevc = false },
            setup: project => project.Output = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4Hevc, OutputQuality.Maximum));

        app.StartExport();

        var (snapshot, path) = Assert.Single(app.Exporter.Calls);
        Assert.Equal(@"C:\Videos\10Lapse\Harbor sunset.mp4", path);
        Assert.Equal(new OutputSettings(OutputResolution.Qhd1440, VideoFormat.Mp4H264, OutputQuality.Maximum), snapshot.Output);
        Assert.Equal(app.On(() => app.Output.EffectiveOutput), snapshot.Output);
        Assert.Equal(OutputSize.For(OutputResolution.Qhd1440, snapshot.FrameSettings.Aspect, snapshot.Frames), snapshot.VideoSize);
        Assert.Equal(new PixelSize(2560, 1440), snapshot.VideoSize);

        // The preference stays what the user chose, and the snapshot is the project's as it was.
        Assert.Equal(OutputResolution.Uhd2160, app.On(() => app.Project.Output.Resolution));
        Assert.Equal(app.On(() => app.Project.Version), snapshot.Version);
        Assert.Equal("1440p · H.264 · Maximum", app.On(() => app.Export.OutputSummary));
    }

    // ---- The Save dialog and its folder ----

    [Fact]
    public void TheSaveDialogSuggestsTheProjectNameInVideosTheFirstTime()
    {
        using var app = new ExportHarness(capabilities: _everything);

        app.StartExport();

        Assert.Equal(("Harbor sunset.mp4", (string?)null), Assert.Single(app.Picker.Asked));
    }

    [Fact]
    public void TheFolderIsRememberedOnlyAfterASuccessfulExportAndUsedNextTime()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.Folders.ExportFolder = @"D:\Earlier";

        app.Exporter.CleanUp.SetResult(true);
        app.StartExport();
        Assert.Equal(@"D:\Earlier", app.Picker.Asked[0].Folder);
        app.On(() => app.Export.CancelCommand.Execute(null));
        app.Settle();
        Assert.Equal(@"D:\Earlier", app.Folders.ExportFolder);

        app.StartExport();
        app.Exporter.Fail(new InvalidOperationException("Encoder broke."));
        app.Settle();
        Assert.Equal(@"D:\Earlier", app.Folders.ExportFolder);

        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.Equal(@"C:\Videos\10Lapse", app.Folders.ExportFolder);

        app.On(() => app.Export.BackToEditingCommand.Execute(null));
        app.StartExport();
        Assert.Equal(@"C:\Videos\10Lapse", app.Picker.Asked[^1].Folder);
    }

    [Fact]
    public void ACancelledSaveDialogStartsNothing()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.Picker.Answer = null;

        app.StartExport();

        Assert.Single(app.Picker.Asked);
        Assert.Empty(app.Exporter.Calls);
        Assert.Equal(ExportPanelState.Editing, app.On(() => app.Export.State));
        Assert.Empty(app.Lifecycle);
    }

    [Theory]
    [InlineData("Harbor sunset", "Harbor sunset.mp4")]
    [InlineData("Sunset: 5/12 *best*", "Sunset 512 best.mp4")]
    [InlineData("  Trailing dots...  ", "Trailing dots.mp4")]
    [InlineData("???", "Timelapse.mp4")]
    public void TheSuggestedNameLeavesOutWhatWindowsRefuses(string project, string expected) =>
        Assert.Equal(expected, ExportText.SuggestedFileName(project));

    // ---- Exporting, then finished ----

    [Fact]
    public void ARunningExportShowsThePanelHoldsTheProjectAndTellsEveryone()
    {
        using var app = new ExportHarness(capabilities: _everything);

        app.StartExport();

        Assert.Equal(ExportPanelState.Exporting, app.On(() => app.Export.State));
        Assert.True(app.On(() => app.Export.ShowsExportButton));
        Assert.False(app.On(() => app.Export.CanStartExport));
        Assert.Equal("Exporting…", app.On(() => app.Export.ExportButtonText));
        Assert.False(app.On(() => app.Shell.Edits.IsOpen));
        Assert.Equal("Harbor sunset.mp4", app.On(() => app.Export.FileName));
        Assert.Equal("0%", app.On(() => app.Export.PercentText));
        Assert.Equal(ExportText.GettingReady, app.On(() => app.Export.TimeLeftText));
        Assert.Equal("0000 of 0120", app.On(() => app.Export.FramesText));
        Assert.Equal(["progress ?"], app.Window.Calls);
        Assert.Equal(["started"], app.Lifecycle);
        Assert.Equal(["Exporting Harbor sunset.mp4."], app.Announced);
        Assert.All(app.On(() => app.Strip.Items.ToList()), item => Assert.True(item.IsPending));
        Assert.True(app.On(() => app.Strip.IsExporting));

        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.False(app.On(() => app.Strip.IsExporting));
    }

    [Fact]
    public void ProgressMovesThePreviewToTheFrameBeingWrittenAndDimsOnlyWhatIsStillToCome()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        app.Now = TimeSpan.FromSeconds(1);
        app.Report(ExportPhase.Encoding, 46, speed: 4.3, left: TimeSpan.FromSeconds(150));

        Assert.Equal(46, app.On(() => app.Player.CurrentFrame));
        Assert.Equal(46, app.On(() => app.Export.CurrentFrame));
        var pending = app.On(() => app.Strip.Items.Select(item => item.IsPending).ToList());
        Assert.All(pending.Take(46), Assert.False);
        Assert.All(pending.Skip(46), Assert.True);
        Assert.Equal("38%", app.On(() => app.Export.PercentText));
        Assert.Equal(38, app.On(() => app.Export.Percent));
        Assert.Equal("About 2 min 30 s left", app.On(() => app.Export.TimeLeftText));
        Assert.Equal("0046 of 0120", app.On(() => app.Export.FramesText));
        Assert.Equal("4.3 photos per second", app.On(() => app.Export.SpeedText));
        Assert.Equal("Rendering frame 0047", app.On(() => app.Export.RenderingText));
        Assert.Equal("progress 0.38", app.Window.Calls[^1]);
    }

    [Fact]
    public void AfterTheUserMovesThePreviewItStaysThereForAFewSeconds()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        app.Now = TimeSpan.FromSeconds(1);
        app.Report(ExportPhase.Encoding, 10);
        Assert.Equal(10, app.On(() => app.Player.CurrentFrame));

        // The user steps the preview to frame 50; the next reports leave it there.
        app.On(() => app.Player.Seek(50));
        app.Now = TimeSpan.FromSeconds(2);
        app.Report(ExportPhase.Encoding, 20);
        Assert.Equal(50, app.On(() => app.Player.CurrentFrame));
        Assert.Equal(20, app.On(() => app.Export.CurrentFrame));

        // A few seconds later it follows the export again.
        app.Now = TimeSpan.FromSeconds(1) + ExportViewModel.UserSeekPause;
        app.Report(ExportPhase.Encoding, 40);
        Assert.Equal(40, app.On(() => app.Player.CurrentFrame));
    }

    [Fact]
    public void ASuccessfulExportShowsTheFinishedStateFromTheResultAndTheRealFileSize()
    {
        using var app = new ExportHarness(capabilities: _everything);
        Assert.True(app.On(() => app.Project.IsDirty));
        app.StartExport();
        app.Now = TimeSpan.FromSeconds(171);

        app.Exporter.Finish(app.Video(bytes: 173L << 20));
        app.Settle();

        Assert.Equal(ExportPanelState.Finished, app.On(() => app.Export.State));
        Assert.Equal("Harbor sunset.mp4 is ready", app.On(() => app.Export.FinishedTitle));
        Assert.Equal("4.0 s · 1080p · H.264 · 173 MB", app.On(() => app.Export.FinishedSummary));
        Assert.Equal("2 min 51 s", app.On(() => app.Export.ExportedInText));
        Assert.Equal("Videos › 10Lapse", app.On(() => app.Export.SavedToText));
        Assert.False(app.On(() => app.Export.HasSavedBeside));
        Assert.False(app.On(() => app.Project.IsDirty));
        Assert.True(app.On(() => app.Shell.Edits.IsOpen));
        Assert.All(app.On(() => app.Strip.Items.ToList()), item => Assert.False(item.IsPending));
        Assert.Equal(["progress ?", "clear", "notify: Harbor sunset.mp4 is ready"], app.Window.Calls);
        Assert.Equal(["started", "ended Succeeded"], app.Lifecycle);
        Assert.Equal("Harbor sunset.mp4 is ready", app.Announced[^1]);
        Assert.Equal([app.Picker.Answer!], app.Premieres);

        // Play premieres it again; Show in folder opens Explorer on it; Back to editing returns to the inspector.
        app.On(() => app.Export.PlayCommand.Execute(null));
        Assert.Equal(2, app.Premieres.Count);
        app.On(() => app.Export.ShowInFolderCommand.Execute(null));
        Assert.Equal([app.Picker.Answer!], app.Reveal.Shown);
        app.On(() => app.Export.BackToEditingCommand.Execute(null));
        Assert.Equal(ExportPanelState.Editing, app.On(() => app.Export.State));
        Assert.True(app.On(() => app.Export.CanStartExport));
    }

    [Fact]
    public void TheFinishedStateKeepsExportDimmedAndCtrlEDoesNothingThere()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();

        Assert.True(app.On(() => app.Export.ShowsExportButton));
        Assert.False(app.On(() => app.Export.CanStartExport));
        Assert.Equal(ExportText.ExportVideo, app.On(() => app.Export.ExportButtonText));

        // Ctrl+E runs the same command: nothing happens, no dialog, the finished state stays.
        app.StartExport();

        Assert.Single(app.Picker.Asked);
        Assert.Single(app.Exporter.Calls);
        Assert.Equal(ExportPanelState.Finished, app.On(() => app.Export.State));
    }

    [Fact]
    public void AnOutputLostWhileTheSaveDialogIsOpenStartsNothingAndSaysSo()
    {
        using var app = new ExportHarness(capabilities: _everything);

        // The probe's answer arrives while the dialog is open: this PC can export nothing.
        app.Picker.WhileOpen = () => app.Output.SetCapabilities(new FakeOutputCapabilities { Max = null });
        app.StartExport();

        Assert.Empty(app.Exporter.Calls);
        Assert.Equal(ExportPanelState.Editing, app.On(() => app.Export.State));
        var bar = Assert.Single(app.InfoBars.Shown);
        Assert.Equal((InfoBarSeverity.Caution, ExportText.ChangedDuringDialog), (bar.Severity, bar.Message));
    }

    [Fact]
    public void AnEditMadeDuringTheExportKeepsTheProjectUnexported()
    {
        // Edits are held back, but a setting changed under the panel (by code, here) still means the file is not the project.
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        app.On(() => app.Project.Name = "Renamed");

        app.Exporter.Finish(app.Video());
        app.Settle();

        Assert.True(app.On(() => app.Project.IsDirty));
    }

    [Fact]
    public void WhenAnotherAppHeldTheFileTheFinishedStateSaysWhereTheVideoWent()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        app.Exporter.Finish(app.Video(@"C:\Videos\10Lapse\Harbor sunset (2).mp4") with { TargetInUse = @"C:\Videos\10Lapse\Harbor sunset.mp4" });
        app.Settle();

        Assert.True(app.On(() => app.Export.HasSavedBeside));
        Assert.Equal(
            ProjectText.ExportSavedBeside(@"C:\Videos\10Lapse\Harbor sunset.mp4", @"C:\Videos\10Lapse\Harbor sunset (2).mp4"),
            app.On(() => app.Export.SavedBesideText));
        Assert.Equal("Harbor sunset (2).mp4 is ready", app.On(() => app.Export.FinishedTitle));
        Assert.Equal(@"C:\Videos\10Lapse", app.Folders.ExportFolder);
    }

    // ---- Cancelled ----

    [Fact]
    public void CancelReturnsToTheInspectorAndSaysTheFileIsGoneOnlyOnceItIs()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        app.Report(ExportPhase.Encoding, 30);

        app.On(() => app.Export.CancelCommand.Execute(null));
        app.Settle();

        Assert.Equal(ExportPanelState.Editing, app.On(() => app.Export.State));
        Assert.True(app.On(() => app.Shell.Edits.IsOpen));
        Assert.All(app.On(() => app.Strip.Items.ToList()), item => Assert.False(item.IsPending));
        Assert.Equal(["started", "ended Cancelled"], app.Lifecycle);
        Assert.Equal([@"C:\Videos\10Lapse\Harbor sunset.mp4"], app.Exporter.CleanUpAsked);
        Assert.Empty(app.InfoBars.Shown);
        Assert.Empty(app.Errors.Shown);

        app.Exporter.CleanUp.SetResult(true);
        app.Settle();

        var bar = Assert.Single(app.InfoBars.Shown);
        Assert.Equal(InfoBarSeverity.Neutral, bar.Severity);
        Assert.Equal("Export cancelled. The unfinished file was removed.", bar.Message);
        Assert.Equal("clear", app.Window.Calls[^1]);
        Assert.Null(app.Folders.ExportFolder);
        Assert.Equal([@"C:\Videos\10Lapse\Harbor sunset.mp4"], app.Picker.Discarded);
    }

    [Fact]
    public void ACancelWhoseFileCouldNotBeRemovedSaysSo()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        app.On(() => app.Export.CancelCommand.Execute(null));
        app.Exporter.CleanUp.SetResult(false);
        app.Settle();

        Assert.Equal(ExportText.CancelledFileLeft, Assert.Single(app.InfoBars.Shown).Message);
    }

    // ---- Failed ----

    [Fact]
    public void AFailedExportReturnsToTheInspectorAndTheErrorPathSaysWhy()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        var failure = new IOException("The disk is full.");

        app.Exporter.Fail(failure);
        app.Settle();

        Assert.Equal(ExportPanelState.Editing, app.On(() => app.Export.State));
        Assert.True(app.On(() => app.Shell.Edits.IsOpen));
        var (shown, context) = Assert.Single(app.Errors.Shown);
        Assert.Same(failure, shown);
        Assert.Equal(ExportText.ExportFailed, context);
        Assert.Equal(["started", "ended Failed"], app.Lifecycle);
        Assert.Equal(ExportText.FailedAnnouncement, app.Announced[^1]);
        Assert.Equal(["progress ?", "failed", "notify: Harbor sunset.mp4 couldn't be exported. Open 10Lapse to see why."], app.Window.Calls);
        Assert.Empty(app.InfoBars.Shown);
        Assert.Empty(app.Premieres);
        Assert.True(app.On(() => app.Project.IsDirty));
        Assert.Equal([@"C:\Videos\10Lapse\Harbor sunset.mp4"], app.Picker.Discarded);
    }

    [Fact]
    public void ACancellationNobodyAskedForIsAFailure()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        app.Exporter.Fail(new OperationCanceledException("A timeout."));
        app.Settle();

        Assert.IsType<UnexpectedCancellationException>(Assert.Single(app.Errors.Shown).Exception);
        Assert.Equal(["started", "ended Failed"], app.Lifecycle);
        Assert.Empty(app.Exporter.CleanUpAsked);
    }

    // ---- Edits wait ----

    [Fact]
    public void EditsAreHeldBackWhileTheExportRunsWithOneSentence()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        Assert.Equal(0, app.On(() => app.Strip.Remove()));
        Assert.False(app.On(() => app.Strip.MoveBy(1)));
        Assert.False(app.On(() => app.Strip.Undo()));
        app.On(() => app.Shell.NewProjectCommand.Execute(null));
        app.Settle();

        Assert.Equal(120, app.On(() => app.Project.Frames.Length));
        Assert.Empty(app.Dialogs.Asked);
        Assert.All(app.InfoBars.Shown, bar => Assert.Equal(ExportText.EditsWait, bar.Message));
        Assert.Equal(InfoBarSeverity.Caution, app.InfoBars.Shown[0].Severity);

        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.Equal(1, app.On(() => app.Strip.Remove()));
        Assert.Equal(119, app.On(() => app.Project.Frames.Length));
    }

    [Fact]
    public void ResetToDefaultsDuringTheExportChangesNothingAndSaysWhy()
    {
        using var app = new ExportHarness(
            capabilities: _everything,
            setup: project => project.Output = new OutputSettings(OutputResolution.Qhd1440, VideoFormat.Mp4H264, OutputQuality.Maximum));
        var remembered = app.On(() =>
        {
            var settings = new RememberedSettings(new TenLapse.Core.Settings.MemorySettingsStore(), app.Errors, new FakeLog());
            settings.Attach(app.Shell);
            return settings;
        });
        var before = app.On(() => (app.Project.Timing, app.Project.FrameSettings, app.Project.Output));
        app.StartExport();

        Assert.False(app.On(remembered.ResetToDefaults));

        Assert.Equal(before, app.On(() => (app.Project.Timing, app.Project.FrameSettings, app.Project.Output)));
        var bar = Assert.Single(app.InfoBars.Shown);
        Assert.Equal((InfoBarSeverity.Caution, ExportText.EditsWait), (bar.Severity, bar.Message));

        // Once the export ends, Reset works again.
        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.True(app.On(remembered.ResetToDefaults));
        Assert.Equal(OutputSettings.Default, app.On(() => app.Project.Output));
        app.On(remembered.Dispose);
    }

    [Fact]
    public void CtrlZWithNothingToUndoDuringTheExportShowsNoInfoBar()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        Assert.False(app.On(() => app.Strip.Undo()));

        Assert.Empty(app.InfoBars.Shown);
    }

    [Fact]
    public async Task ImportsDropsAndPickersAreRefusedWhileTheExportRuns()
    {
        using var app = new ExportHarness(capabilities: _everything);
        var source = new TenLapse.Tests.Import.ScriptedImportSource(Projects.TestFrames.Folder("more", 4));
        var picker = new TenLapse.Tests.Import.FakePhotoPicker { Folder = @"C:\Photos\More", Photos = [@"C:\Photos\More\a.jpg"] };
        var import = app.On(() => new ImportViewModel(app.Shell, source, picker, new SessionImportFolders(), app.Ui, app.Dialogs, app.InfoBars, new FakeCodecExtensions(), app.Errors));
        app.StartExport();

        await app.Ui.RunAsync(() => import.ImportAsync([@"C:\Photos\More"]));
        await app.Ui.RunAsync(() => import.DropAsync([@"C:\Photos\More"]));
        app.On(() => import.ChooseFolderCommand.Execute(null));
        app.On(() => import.ChoosePhotosCommand.Execute(null));
        app.Settle();
        await import.WhenIdleAsync();

        Assert.Equal(120, app.On(() => app.Project.Frames.Length));
        Assert.Empty(picker.StartFolders);
        Assert.Empty(app.Dialogs.Asked);
        Assert.Equal(4, app.InfoBars.Shown.Count);
        Assert.All(app.InfoBars.Shown, bar => Assert.Equal(ExportText.EditsWait, bar.Message));
    }

    [Fact]
    public void ExportWaitsForAnImportToFinish()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.Importing = true;

        app.StartExport();

        Assert.Empty(app.Picker.Asked);
        Assert.Equal(ExportText.ImportStillRunning, Assert.Single(app.InfoBars.Shown).Message);
    }

    [Fact]
    public void ANewProjectAfterAFinishedExportLeavesTheFinishedState()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();

        app.On(() => app.Shell.NewProjectCommand.Execute(null));
        app.Settle();

        Assert.Equal(ExportPanelState.Editing, app.On(() => app.Export.State));
    }

    // ---- Closing the window ----

    [Fact]
    public void TheWindowClosesAtOnceWhenNothingIsExported()
    {
        using var app = new ExportHarness(capabilities: _everything);

        Assert.True(app.On(() => app.Export.MayCloseNow));
        app.Exporter.AllCleanedUp = new TaskCompletionSource().Task;
        Assert.False(app.On(() => app.Export.MayCloseNow));
    }

    [Fact]
    public async Task ClosingDuringAnExportAsksAndKeepsTheExportOnNo()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        app.Dialogs.Answer = false;

        Assert.False(app.On(() => app.Export.MayCloseNow));
        var close = app.On(() => app.Export.PrepareToCloseAsync());

        Assert.False(await close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(ExportText.CloseTitle, Assert.Single(app.Dialogs.Asked).Title);
        Assert.Equal(ExportPanelState.Exporting, app.On(() => app.Export.State));
    }

    [Fact]
    public async Task ClosingDuringAnExportStopsItWaitsForTheCleanUpAndShowsNoInfoBar()
    {
        // The fake registers the cancelled export's clean-up only as the export ends, as ExportCleanup does: a close that
        // asked for the clean-ups before the export had ended would not wait for it.
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        var close = app.On(() => app.Export.PrepareToCloseAsync());
        app.Settle();

        Assert.False(close.IsCompleted);
        Assert.Equal(ExportPanelState.Editing, app.On(() => app.Export.State));
        Assert.Equal(["started", "ended Cancelled"], app.Lifecycle);
        app.Exporter.CleanUp.SetResult(true);
        Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        app.Settle();
        Assert.Empty(app.InfoBars.Shown);
    }

    [Fact]
    public async Task ACloseGivesUpWaitingForTheCleanUpAfterTheLimit()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        // The cancelled export's clean-up never finishes.
        var started = System.Diagnostics.Stopwatch.StartNew();
        var close = app.On(() => app.Export.PrepareToCloseAsync());

        Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.InRange(started.Elapsed, ExportViewModel.CloseLimit - TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(8));
    }

    // ---- How often the panel follows the progress ----

    [Fact]
    public void ProgressIsShownAtMostEveryHundredMillisecondsExceptPhaseChangesAndTheLastFrame()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        app.Now = TimeSpan.FromMilliseconds(1000);
        app.Report(ExportPhase.Encoding, 10);
        Assert.Equal("0010 of 0120", app.On(() => app.Export.FramesText));

        // 50 ms later: too soon.
        app.Now = TimeSpan.FromMilliseconds(1050);
        app.Report(ExportPhase.Encoding, 12);
        Assert.Equal("0010 of 0120", app.On(() => app.Export.FramesText));

        // 100 ms after the last one shown.
        app.Now = TimeSpan.FromMilliseconds(1100);
        app.Report(ExportPhase.Encoding, 14);
        Assert.Equal("0014 of 0120", app.On(() => app.Export.FramesText));

        // The last frame and a new phase always show, however soon.
        app.Now = TimeSpan.FromMilliseconds(1110);
        app.Report(ExportPhase.Encoding, 120);
        Assert.Equal("0120 of 0120", app.On(() => app.Export.FramesText));
        app.Now = TimeSpan.FromMilliseconds(1115);
        app.Report(ExportPhase.Finishing, 120);
        Assert.Equal(ExportText.Finishing, app.On(() => app.Export.TimeLeftText));

        // A report that goes back is never shown.
        app.Now = TimeSpan.FromMilliseconds(2000);
        app.Report(ExportPhase.Encoding, 50);
        Assert.Equal("0120 of 0120", app.On(() => app.Export.FramesText));
    }

    [Fact]
    public void AReportThatArrivesAfterTheExportEndedIsIgnored()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        var progress = app.Exporter.Progress!;
        app.Exporter.Finish(app.Video());
        app.Settle();

        app.Now = TimeSpan.FromSeconds(10);
        progress.Report(new ExportStatus(ExportPhase.Encoding, 60, 120, null, null));
        app.Settle();

        Assert.Equal(ExportPanelState.Finished, app.On(() => app.Export.State));
        Assert.All(app.On(() => app.Strip.Items.ToList()), item => Assert.False(item.IsPending));
    }

    [Fact]
    public void NarratorHearsFinishingOnceAndNeverEveryFrame()
    {
        using var app = new ExportHarness(frames: 1000, capabilities: _everything);
        app.StartExport();

        // A fast export: a report every millisecond, as many as frames.
        for (var frame = 1; frame <= 1000; frame++)
        {
            app.Now = TimeSpan.FromMilliseconds(frame);
            app.Report(ExportPhase.Encoding, frame, 500, TimeSpan.FromSeconds(1));
        }

        app.Now += TimeSpan.FromMilliseconds(5);
        app.Report(ExportPhase.Finishing, 1000, 500, null);
        app.Now += TimeSpan.FromMilliseconds(500);
        app.Report(ExportPhase.Finishing, 1000, 500, null);
        app.Exporter.Finish(app.Video());
        app.Settle();

        // A one-second export is over before the first progress announcement may come (MinAnnouncementInterval).
        Assert.DoesNotContain(app.Announced, line => line.EndsWith(" percent exported.", StringComparison.Ordinal));
        Assert.Equal("Exporting Harbor sunset.mp4.", app.Announced[0]);
        Assert.Equal(["Finishing the file.", "Harbor sunset.mp4 is ready"], app.Announced[^2..]);
        Assert.Single(app.Announced, line => line == ExportText.FinishingAnnouncement);
    }

    [Fact]
    public void NarratorHearsTheStartEveryTenPercentAtMostAndTheEnd()
    {
        using var app = new ExportHarness(frames: 100, capabilities: _everything);
        app.StartExport();

        // A minute's export: ten percent every 6 s, each one said.
        for (var frame = 1; frame <= 100; frame++)
        {
            app.Now = TimeSpan.FromMilliseconds(600 * frame);
            app.Report(ExportPhase.Encoding, frame, 1.7, TimeSpan.FromSeconds(60));
        }

        app.Exporter.Finish(app.Video());
        app.Settle();

        Assert.Equal(
            [
                "Exporting Harbor sunset.mp4.",
                "10 percent exported.", "20 percent exported.", "30 percent exported.", "40 percent exported.", "50 percent exported.",
                "60 percent exported.", "70 percent exported.", "80 percent exported.", "90 percent exported.",
                "Harbor sunset.mp4 is ready",
            ],
            app.Announced);
    }

    [Fact]
    public void AFastExportIsHeardAtMostEveryFiveSeconds()
    {
        // #38 review: a GPU export passed ten percent every 0.3 s, and Narrator got 12 sentences in 2.9 s.
        using var app = new ExportHarness(frames: 100, capabilities: _everything);
        app.StartExport();

        // 20 s: ten percent every 2 s.
        for (var frame = 1; frame <= 100; frame++)
        {
            app.Now = TimeSpan.FromMilliseconds(200 * frame);
            app.Report(ExportPhase.Encoding, frame, 5, TimeSpan.FromSeconds(10));
        }

        app.Exporter.Finish(app.Video());
        app.Settle();

        // At 5 s (25 %), 10 s (50 %) and 15 s (75 %): the newest whole ten each time.
        Assert.Equal(
            ["Exporting Harbor sunset.mp4.", "20 percent exported.", "50 percent exported.", "70 percent exported.", "Harbor sunset.mp4 is ready"],
            app.Announced);
    }

    // ---- Words ----

    [Theory]
    [InlineData(0, "About 0 s left")]
    [InlineData(42.4, "About 42 s left")]
    [InlineData(59.6, "About 1 min left")]
    [InlineData(150, "About 2 min 30 s left")]
    [InlineData(179, "About 2 min 30 s left")]
    [InlineData(181, "About 3 min left")]
    public void TimeLeftReadsLikeTheCanvas(double seconds, string expected) =>
        Assert.Equal(expected, ExportText.TimeLeft(TimeSpan.FromSeconds(seconds), System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(312, 742, "0312 of 0742")]
    [InlineData(5, 12_000, "00005 of 12000")]
    public void FrameCountsArePaddedToTheTotal(int done, int total, string expected) =>
        Assert.Equal(expected, ExportText.Frames(done, total, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(@"C:\Users\sven\Videos\10Lapse\Harbor.mp4", "Videos › 10Lapse")]
    [InlineData(@"D:\Videos\Harbor.mp4", "D: › Videos")]
    [InlineData(@"D:\Harbor.mp4", @"D:\")]
    public void SavedToNamesTheLastTwoFolders(string path, string expected) =>
        Assert.Equal(expected, ExportText.SavedTo(path));

    [Theory]
    [InlineData(500L << 10, "Less than 1 MB")]
    [InlineData(4L << 20, "4 MB")]
    [InlineData(4_435_476L, "4.23 MB")]
    [InlineData(19_293_798L, "18.4 MB")]
    [InlineData(173L << 20, "173 MB")]
    [InlineData(1_048_051_712L, "1 GB")]
    [InlineData(1_320_702_444L, "1.23 GB")]
    public void TheFinishedSizeHasThreeFigures(long bytes, string expected) =>
        Assert.Equal(expected, ExportText.FileSize(bytes, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void ThePercentReachesAHundredOnlyAtTheEnd()
    {
        Assert.Equal("99%", ExportText.Percent(0.999, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("100%", ExportText.Percent(1, System.Globalization.CultureInfo.InvariantCulture));
    }
}
