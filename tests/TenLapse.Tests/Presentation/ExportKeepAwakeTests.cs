using TenLapse.Presentation.Export;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// The PC stays awake for the length of an export (#25): the power request is taken as the export starts and let go
/// once, however it ends, and a minimized or hidden window neither slows nor stops the export.
/// </summary>
public sealed class ExportKeepAwakeTests
{
    private static readonly FakeOutputCapabilities _everything = new();

    [Fact]
    public void AnExportHoldsTheRequestFromItsStartAndNamesTheFile()
    {
        using var app = new ExportHarness(capabilities: _everything);
        Assert.Equal(0, app.KeepAwake.Held);

        app.StartExport();

        Assert.Equal(1, app.KeepAwake.Held);
        Assert.Equal("Exporting Harbor sunset.mp4", Assert.Single(app.KeepAwake.Reasons));
    }

    [Fact]
    public void NothingIsHeldWhenTheSaveDialogIsCancelled()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.Picker.Answer = null;

        app.StartExport();

        Assert.Empty(app.KeepAwake.Reasons);
        Assert.Equal(0, app.KeepAwake.Held);
    }

    [Fact]
    public void ASuccessLetsGoOnce()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        app.Report(ExportPhase.Encoding, 60);
        Assert.Equal(1, app.KeepAwake.Held);

        app.Exporter.Finish(app.Video());
        app.Settle();

        Assert.Equal(0, app.KeepAwake.Held);
        Assert.Equal(1, app.KeepAwake.DisposeCalls);
        Assert.Equal(["started", "ended Succeeded"], app.Lifecycle);
    }

    [Fact]
    public void ACancelLetsGoOnce()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        app.On(() => app.Export.CancelCommand.Execute(null));
        app.Settle();

        Assert.Equal(0, app.KeepAwake.Held);
        Assert.Equal(1, app.KeepAwake.DisposeCalls);
        Assert.Equal(["started", "ended Cancelled"], app.Lifecycle);
    }

    [Fact]
    public void AFailureLetsGoOnce()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        app.Exporter.Fail(new InvalidOperationException("The disk is full."));
        app.Settle();

        Assert.Equal(0, app.KeepAwake.Held);
        Assert.Equal(1, app.KeepAwake.DisposeCalls);
        Assert.Equal(["started", "ended Failed"], app.Lifecycle);
    }

    [Fact]
    public async Task ClosingTheWindowDuringAnExportKeepsTheRequestOnNoAndLetsGoOnYes()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        // "No": the export and the request go on.
        app.Dialogs.Answer = false;
        Assert.False(await app.On(() => app.Export.PrepareToCloseAsync()).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(1, app.KeepAwake.Held);
        Assert.Equal(0, app.KeepAwake.DisposeCalls);

        // "Yes": the export stops and the request goes with it, before the clean-up the close waits for is done.
        app.Dialogs.Answer = true;
        var close = app.On(() => app.Export.PrepareToCloseAsync());
        app.Settle();
        Assert.False(close.IsCompleted);
        Assert.Equal(0, app.KeepAwake.Held);
        app.Exporter.CleanUp.SetResult(true);
        Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Equal(1, app.KeepAwake.DisposeCalls);
    }

    [Fact]
    public void DisposingTheViewModelDuringAnExportLetsGoAtOnce()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();

        app.On(() => app.Export.Dispose());
        Assert.Equal(0, app.KeepAwake.Held);
        app.Settle();

        Assert.Equal(0, app.KeepAwake.Held);
        Assert.Equal(1, app.KeepAwake.DisposeCalls);
    }

    [Fact]
    public void ASecondExportTakesAndLetsGoOfItsOwnRequest()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        app.Exporter.Fail(new InvalidOperationException("The disk is full."));
        app.Settle();
        Assert.Equal(0, app.KeepAwake.Held);

        app.Picker.Answer = @"C:\Videos\10Lapse\Harbor again.mp4";
        app.StartExport();
        Assert.Equal(1, app.KeepAwake.Held);
        app.Exporter.Finish(app.Video());
        app.Settle();

        Assert.Equal(0, app.KeepAwake.Held);
        Assert.Equal(["Exporting Harbor sunset.mp4", "Exporting Harbor again.mp4"], app.KeepAwake.Reasons);
        Assert.Equal(2, app.KeepAwake.DisposeCalls);
    }

    [Fact]
    public void AMissingPowerRequestDoesNotStopTheExport()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.KeepAwake.Throws = true;

        app.StartExport();

        // The export runs; the stand-in's failure is logged, not shown.
        Assert.Single(app.Exporter.Calls);
        Assert.Equal(ExportPanelState.Exporting, app.On(() => app.Export.State));
        Assert.Equal(ExportText.KeepAwakeFailed, Assert.Single(app.Errors.Reported).Context);
        Assert.Empty(app.Errors.Shown);
        app.Exporter.Finish(app.Video());
        app.Settle();

        Assert.Equal(ExportPanelState.Finished, app.On(() => app.Export.State));
    }

    [Fact]
    public void ASubscriberThatThrowsOnStartedLeavesNoRequestAndNoExportBehind()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.On(() => app.Export.Started += (_, _) => throw new InvalidOperationException("A subscriber failed."));

        app.StartExport();

        // The request was taken before Started, and let go when Started threw; the exporter never began.
        Assert.Equal(["Exporting Harbor sunset.mp4"], app.KeepAwake.Reasons);
        Assert.Equal(0, app.KeepAwake.Held);
        Assert.Equal(1, app.KeepAwake.DisposeCalls);
        Assert.False(app.On(() => app.Export.IsExporting));
        Assert.Empty(app.Exporter.Calls);
        Assert.Single(app.Errors.Shown);
    }

    [Fact]
    public void AMinimizedWindowNeitherSlowsNorStopsTheExport()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        app.Now = TimeSpan.FromSeconds(1);
        app.Report(ExportPhase.Encoding, 10);
        Assert.Equal(10, app.On(() => app.Player.CurrentFrame));

        app.On(() => app.Player.SetWindowShown(false));
        app.Window.Calls.Clear();

        // The panel and the taskbar follow every report as before (at most every 100 ms), and the export gets all its frames.
        for (var frame = 20; frame <= 100; frame += 20)
        {
            app.Now = TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(frame * 10);
            app.Report(ExportPhase.Encoding, frame);
            Assert.Equal(frame, app.On(() => app.Export.CurrentFrame));
        }

        Assert.Equal(5, app.Window.Calls.Count(call => call.StartsWith("progress ", StringComparison.Ordinal)));

        // The hidden preview is not moved to the frame being written: nothing decodes and draws unseen.
        Assert.Equal(10, app.On(() => app.Player.CurrentFrame));
        Assert.Equal(1, app.KeepAwake.Held);

        app.Report(ExportPhase.Encoding, 120);
        app.Report(ExportPhase.Finishing, 120);
        app.Exporter.Finish(app.Video());
        app.Settle();

        Assert.Equal(ExportPanelState.Finished, app.On(() => app.Export.State));
        Assert.Equal(0, app.KeepAwake.Held);
    }

    [Fact]
    public void ThePreviewFollowsTheExportAgainOnceTheWindowShows()
    {
        using var app = new ExportHarness(capabilities: _everything);
        app.StartExport();
        app.On(() => app.Player.SetWindowShown(false));
        app.Now = TimeSpan.FromSeconds(1);
        app.Report(ExportPhase.Encoding, 30);
        Assert.Equal(0, app.On(() => app.Player.CurrentFrame));

        app.On(() => app.Player.SetWindowShown(true));
        app.Now = TimeSpan.FromSeconds(2);
        app.Report(ExportPhase.Encoding, 40);

        Assert.Equal(40, app.On(() => app.Player.CurrentFrame));
    }
}
