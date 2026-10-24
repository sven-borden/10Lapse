using System.Collections.Concurrent;
using System.Diagnostics;
using TenLapse.Core.Look;
using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Look;
using TenLapse.Presentation.Services;
using TenLapse.Tests.Import;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// Deflicker's exposure pass and the Look panel (#45): the photos are measured in the background while deflicker is on,
/// the results land on their frames, a clean project stays clean, a failure is told once, Cancel stops at once and turns
/// deflicker off, and an export waits for the finished curve.
/// </summary>
public sealed class LookPanelTests
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TurningDeflickerOnMeasuresEveryPhotoAndWritesItToItsFrame()
    {
        using var app = new LookHarness(frames: 30);

        app.On(() => app.Panel.IsDeflickerOn = true);
        await app.UntilAsync(() => app.Project.Frames.All(frame => frame.Exposure is not null) && !app.Analysis.IsMeasuring);

        var frames = app.On(() => app.Project.Frames);
        Assert.All(frames, frame => Assert.Equal(LookHarness.LuminanceOf(frame), frame.Exposure!.Luminance));
        Assert.Equal(30, app.Meter.Calls.Count);
        Assert.Equal(string.Empty, app.On(() => app.Panel.MeasuringText));
        Assert.Equal(30, ExposureCurve.For(app.On(() => app.Project.Snapshot)).Count);
        Assert.Empty(app.Ui.Failures);
    }

    [Fact]
    public async Task AProjectOpenedWithDeflickerOnIsMeasuredWithoutBecomingDirty()
    {
        // The measurements are the photos', not the user's work: a clean project stays clean.
        using var app = new LookHarness(frames: 12, setup: project =>
        {
            project.Look = new LookSettings(true, 80);
            project.MarkSaved(project.Version);
        });

        await app.UntilAsync(() => app.Project.Frames.All(frame => frame.Exposure is not null) && !app.Analysis.IsMeasuring);

        Assert.False(app.On(() => app.Project.IsDirty));
    }

    [Fact]
    public async Task APhotoIsMeasuredOnlyOnceItsSizeIsKnown()
    {
        // An import reads the size first; measuring after it means the import's own update never overwrites a measurement.
        using var app = new LookHarness(frames: 4, sized: false);
        app.On(() => app.Panel.IsDeflickerOn = true);
        app.Settle();
        Assert.Empty(app.Meter.Calls);

        app.On(() => app.Project.UpdateFrames(app.Project.Frames.Select(frame => frame with { PixelSize = new PixelSize(30, 20) })));

        await app.UntilAsync(() => app.Project.Frames.All(frame => frame.Exposure is not null));
        Assert.Equal(4, app.Meter.Calls.Count);
    }

    [Fact]
    public async Task PhotosAddedWhileMeasuringJoinThePass()
    {
        using var app = new LookHarness(frames: 10);
        app.Meter.Hold();
        app.On(() => app.Panel.IsDeflickerOn = true);
        await app.UntilAsync(() => app.Meter.Calls.Count >= 1);

        app.On(() => app.Project.AddFrames(TestFrames.Folder("more", 5).Select(frame => frame with { PixelSize = new PixelSize(30, 20) })));
        Assert.EndsWith("of 15 photos…", app.On(() => app.Panel.MeasuringText), StringComparison.Ordinal);
        app.Meter.Release();

        await app.UntilAsync(() => app.Project.Frames.All(frame => frame.Exposure is not null) && !app.Analysis.IsMeasuring);
        Assert.Equal(15, app.On(() => app.Project.Frames.Length));
    }

    [Fact]
    public async Task APhotoThatCannotBeMeasuredIsTriedTwiceToldOnceAndMeasuredAgainOnRequest()
    {
        using var app = new LookHarness(frames: 6);
        var broken = app.On(() => app.Project.Frames[2]);
        app.Meter.Failing.Add(broken.SourcePath);

        app.On(() => app.Panel.IsDeflickerOn = true);
        await app.UntilAsync(() => !app.Analysis.IsMeasuring && app.InfoBars.Shown.Count > 0);

        var bar = Assert.Single(app.InfoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, bar.Severity);
        Assert.Equal("1 photo couldn't be measured, so deflicker takes its correction from the photos beside it. Check that the file opens, then choose Measure in the Look section.", bar.Message);
        Assert.Equal(broken.FileName, Assert.Single(bar.Details).Text);
        Assert.Null(app.On(() => app.Project.Frames[2].Exposure));
        Assert.Equal(2, app.Meter.Calls.Count(path => path == broken.SourcePath));

        // It does not flash: its correction is the one between its neighbours.
        var curve = ExposureCurve.For(app.On(() => app.Project.Snapshot));
        var frames = app.On(() => app.Project.Frames);
        Assert.Equal((curve.Correction(frames[1].Id) + curve.Correction(frames[3].Id)) / 2, curve.Correction(frames[2].Id), 1e-12);

        // The Look section says so, with Measure.
        Assert.True(app.On(() => app.Panel.HasUnmeasured));
        Assert.Equal("1 photo not measured", app.On(() => app.Panel.UnmeasuredText));

        // Another edit does not try it again; Measure does.
        app.On(() => app.Panel.Strength = 50);
        app.Settle();
        Assert.Equal(7, app.Meter.Calls.Count);
        app.Meter.Failing.Clear();
        app.On(() => app.Panel.MeasureCommand.Execute(null));
        await app.UntilAsync(() => app.Project.Frames[2].Exposure is not null && !app.Analysis.IsMeasuring);
        Assert.False(app.On(() => app.Panel.HasUnmeasured));
    }

    [Fact]
    public async Task APhotoThatFailsOnceIsMeasuredOnTheSecondTryWithoutAnInfoBar()
    {
        using var app = new LookHarness(frames: 6);
        var flaky = app.On(() => app.Project.Frames[4]);
        app.Meter.FailingOnce[flaky.SourcePath] = 0;

        app.On(() => app.Panel.IsDeflickerOn = true);
        await app.UntilAsync(() => app.Project.Frames.All(frame => frame.Exposure is not null) && !app.Analysis.IsMeasuring);

        Assert.Empty(app.InfoBars.Shown);
        Assert.False(app.On(() => app.Panel.HasUnmeasured));
    }

    [Fact]
    public async Task PhotosRemovedWhileMeasuringLeaveThePass()
    {
        using var app = new LookHarness(frames: 20, parallelism: 1);
        app.Meter.Hold();
        app.On(() => app.Panel.IsDeflickerOn = true);
        await app.UntilAsync(() => app.Meter.Calls.Count >= 1);

        app.On(() => app.Project.RemoveFrames(app.Project.Frames.Skip(10).Select(frame => frame.Id).ToList()));
        Assert.Equal("Measuring 0 of 10 photos…", app.On(() => app.Panel.MeasuringText));
        app.Meter.Release();

        await app.UntilAsync(() => app.Project.Frames.All(frame => frame.Exposure is not null) && !app.Analysis.IsMeasuring);
        Assert.Equal(10, app.Meter.Calls.Count);
    }

    [Fact]
    public async Task CancelOnAPassThatStartedByItselfKeepsDeflickerOnAndTheProjectClean()
    {
        // Opened with deflicker on: the pass is not the user's doing, so Cancel means "not now", not "drop deflicker".
        using var app = new LookHarness(frames: 40, parallelism: 1, holdAfter: 5, setup: project =>
        {
            project.Look = new LookSettings(true, 80);
            project.MarkSaved(project.Version);
        });
        await app.UntilAsync(() => app.Project.Frames.Count(frame => frame.Exposure is not null) == 5);

        app.On(() => app.Panel.CancelMeasuringCommand.Execute(null));

        Assert.False(app.On(() => app.Panel.IsMeasuring));
        Assert.True(app.On(() => app.Project.Look.Deflicker));
        Assert.False(app.On(() => app.Project.IsDirty));
        Assert.True(app.On(() => app.Panel.HasUnmeasured));
        Assert.Equal("35 photos not measured", app.On(() => app.Panel.UnmeasuredText));
        Assert.Equal(40, ExposureCurve.For(app.On(() => app.Project.Snapshot)).Count);
        app.Meter.Release();
        app.Settle();
        Assert.False(app.On(() => app.Analysis.IsMeasuring));

        app.On(() => app.Panel.MeasureCommand.Execute(null));
        await app.UntilAsync(() => app.Project.Frames.All(frame => frame.Exposure is not null) && !app.Analysis.IsMeasuring);
        Assert.False(app.On(() => app.Project.IsDirty));
        Assert.False(app.On(() => app.Panel.HasUnmeasured));
    }

    [Fact]
    public async Task CancelStopsAtOnceKeepsWhatWasMeasuredAndTurnsDeflickerOff()
    {
        // Right after the user turned deflicker on, Cancel takes the switch back.
        using var app = new LookHarness(frames: 40, parallelism: 1);
        app.Meter.HoldAfter(5);
        app.On(() => app.Panel.IsDeflickerOn = true);
        await app.UntilAsync(() => app.Project.Frames.Count(frame => frame.Exposure is not null) == 5);

        var clock = Stopwatch.StartNew();
        app.On(() => app.Panel.CancelMeasuringCommand.Execute(null));
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"Cancel took {clock.Elapsed.TotalMilliseconds:0} ms.");
        Assert.False(app.On(() => app.Panel.IsMeasuring));
        Assert.False(app.On(() => app.Project.Look.Deflicker));
        Assert.Equal(5, app.On(() => app.Project.Frames.Count(frame => frame.Exposure is not null)));
        app.Meter.Release();
        app.Settle();

        // On again: only the photos left are measured.
        app.On(() => app.Panel.IsDeflickerOn = true);
        await app.UntilAsync(() => app.Project.Frames.All(frame => frame.Exposure is not null) && !app.Analysis.IsMeasuring);
        Assert.Equal(40, app.Meter.Calls.Distinct().Count());
    }

    [Fact]
    public void TheStrengthIsAWholePercentAndTheSwitchesFollowTheProject()
    {
        using var app = new LookHarness(frames: 2);

        app.On(() => app.Panel.Strength = 63.4);
        Assert.Equal(63, app.On(() => app.Project.Look.Strength));
        Assert.Equal("63%", app.On(() => app.Panel.StrengthText));

        app.On(() => app.Project.Look = new LookSettings(true, 20));
        Assert.True(app.On(() => app.Panel.IsDeflickerOn));
        Assert.Equal(20, app.On(() => app.Panel.Strength));
    }

    [Fact]
    public void BeforeShowsTheOriginalsOnlyWhileDeflickerIsOnAndResetsWhenItGoesOff()
    {
        using var app = new LookHarness(frames: 2);
        var raised = 0;
        app.On(() => app.Panel.ShowsOriginalChanged += (_, _) => raised++);

        app.On(() => app.Panel.CompareIndex = 0);
        Assert.False(app.On(() => app.Panel.ShowsOriginal));

        app.On(() => app.Panel.IsDeflickerOn = true);
        app.On(() => app.Panel.CompareIndex = 0);
        Assert.True(app.On(() => app.Panel.ShowsOriginal));

        app.On(() => app.Panel.IsDeflickerOn = false);
        Assert.False(app.On(() => app.Panel.ShowsOriginal));
        Assert.Equal(1, app.On(() => app.Panel.CompareIndex));
        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task AnExportWaitsForTheMeasuringAndExportsTheFinishedCurve()
    {
        var meter = new FakeExposureMeter();
        meter.Hold();
        using var app = new ExportHarness(
            frames: 8,
            capabilities: new FakeOutputCapabilities(),
            setup: project =>
            {
                project.UpdateFrames(project.Frames.Select(frame => frame with { PixelSize = new PixelSize(30, 20) }));
                project.Look = new LookSettings(true, 100);
            },
            measuring: shell => new ExposureAnalysis(shell, meter, new UiThreadDispatcher(), new FakeErrorSink(), new FakeInfoBarService()));

        var heard = new List<string>();
        app.On(() => app.Export.Announced += (_, text) => heard.Add(text));
        app.StartExport();
        Assert.Empty(app.Exporter.Calls);

        // The panel says why it waits, with the count and its own bar (#45 review).
        Assert.True(app.On(() => app.Export.IsWaitingForMeasuring));
        Assert.Equal("Deflicker is measuring the photos first: 0 of 8", app.On(() => app.Export.MeasuringText));
        Assert.Equal("Waiting for deflicker…", app.On(() => app.Export.TimeLeftText));
        Assert.Contains("Deflicker is measuring the photos first. The export starts when it is done.", heard);

        meter.Release();
        var deadline = Stopwatch.StartNew();
        while (app.Exporter.Calls.Count == 0 && deadline.Elapsed < _patience)
        {
            app.Settle();
        }

        var (snapshot, _) = Assert.Single(app.Exporter.Calls);
        Assert.All(snapshot.Frames, frame => Assert.NotNull(frame.Exposure));
        Assert.False(app.On(() => app.Export.IsWaitingForMeasuring));
    }

    [Fact]
    public void CancelWhileTheExportWaitsForTheMeasuringStopsItBeforeAnythingIsExported()
    {
        // The wait sits inside the export's try: Cancel there ends the export as any Cancel does, and no exporter (with its
        // photo source, #49's disposable) is ever started. The measuring itself goes on: it belongs to the Look section.
        var meter = new FakeExposureMeter();
        meter.Hold();
        using var app = new ExportHarness(
            frames: 8,
            capabilities: new FakeOutputCapabilities(),
            setup: project =>
            {
                project.UpdateFrames(project.Frames.Select(frame => frame with { PixelSize = new PixelSize(30, 20) }));
                project.Look = new LookSettings(true, 100);
            },
            measuring: shell => new ExposureAnalysis(shell, meter, new UiThreadDispatcher(), new FakeErrorSink(), new FakeInfoBarService()));

        app.StartExport();
        Assert.True(app.On(() => app.Export.IsWaitingForMeasuring));

        app.On(() => app.Export.CancelCommand.Execute(null));
        app.Settle();

        Assert.Equal(ExportPanelState.Editing, app.On(() => app.Export.State));
        Assert.False(app.On(() => app.Export.IsWaitingForMeasuring));
        Assert.Equal(string.Empty, app.On(() => app.Export.MeasuringText));
        Assert.Equal(["started", "ended Cancelled"], app.Lifecycle);
        Assert.True(app.On(() => app.Shell.Edits.IsOpen));

        meter.Release();
        app.Settle();
        Assert.Empty(app.Exporter.Calls);
    }

    [Fact]
    public void AnOpenedProjectsDetailsReadKeepsAMeasurementThatLandedFirst()
    {
        // A photo changed on disk since the save: it is read again for its details and measured at once (its saved size
        // is known). When the measurement lands first, the details, read from the frame as it was, must not wipe it.
        var project = new Project();
        var frame = TestFrames.At(@"C:\Photos\Changed\IMG_0001.jpg") with { PixelSize = new PixelSize(30, 20) };
        project.AddFrames([frame]);
        var reading = TenLapse.Core.Import.FrameReading.Read(frame with { PixelSize = new PixelSize(6000, 4000) });
        var measured = new PhotoExposure(-2.5);
        project.UpdateFrames([project.Frames[0] with { Exposure = measured }]);
        project.MarkSaved(project.Version);

        ImportViewModel.ApplyDetails(project, [reading]);

        Assert.Equal(new PixelSize(6000, 4000), project.Frames[0].PixelSize);
        Assert.Same(measured, project.Frames[0].Exposure);
        Assert.False(project.IsDirty);
    }

    /// <summary>A meter that answers from the photo's name, can fail some photos, and can hold its answers.</summary>
    internal sealed class FakeExposureMeter : IExposureMeter
    {
        private readonly ManualResetEventSlim _open = new(true);
        private int _holdAfter = int.MaxValue;

        public ConcurrentQueue<string> Calls { get; } = new();

        public ConcurrentBag<string> Failing { get; } = [];

        // Photos whose first measuring fails and the next one works (a file share waking up).
        public ConcurrentDictionary<string, byte> FailingOnce { get; } = new();

        public void Hold() => _open.Reset();

        public void HoldAfter(int answers) => _holdAfter = answers;

        public void Release()
        {
            _holdAfter = int.MaxValue;
            _open.Set();
        }

        public async Task<ExposureMeasurement> MeasureAsync(Frame frame, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Calls.Enqueue(frame.SourcePath);
            if (Calls.Count > _holdAfter)
            {
                _open.Reset();
            }

            // Waits as a slow decode would, and stops at a cancel as one does.
            while (!_open.Wait(10, CancellationToken.None))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Failing.Contains(frame.SourcePath) || FailingOnce.TryRemove(frame.SourcePath, out _)
                ? ExposureMeasurement.Failed("unreadable")
                : ExposureMeasurement.Measured(new PhotoExposure(LookHarness.LuminanceOf(frame)));
        }
    }

    /// <summary>The export harness's UI thread is its synchronization context: this posts there.</summary>
    private sealed class UiThreadDispatcher : IUiDispatcher
    {
        private readonly SynchronizationContext _context = SynchronizationContext.Current ?? throw new InvalidOperationException("Make it on the UI thread.");

        public Task<T> InvokeAsync<T>(Func<T> work)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _context.Post(
                _ =>
                {
                    try
                    {
                        result.TrySetResult(work());
                    }
                    catch (Exception exception)
                    {
                        result.TrySetException(exception);
                    }
                },
                null);
            return result.Task;
        }
    }

    /// <summary>A shell, its project of photos with known sizes, the exposure pass and the Look panel, on a stand-in UI thread.</summary>
    private sealed class LookHarness : IDisposable
    {
        public LookHarness(int frames, bool sized = true, Action<Project>? setup = null, int parallelism = 2, int holdAfter = int.MaxValue)
        {
            Meter.HoldAfter(holdAfter);
            Ui.Run(() =>
            {
                Shell = new ShellViewModel(new FakeDialogService(), Errors);
                Shell.CurrentProject.Model.AddFrames(TestFrames.Folder("harbor", frames).Select(frame => sized ? frame with { PixelSize = new PixelSize(30, 20) } : frame));
                setup?.Invoke(Shell.CurrentProject.Model);
                Analysis = new ExposureAnalysis(Shell, Meter, Ui, Errors, InfoBars, parallelism);
                Panel = new LookPanelViewModel(Shell, Analysis, Errors);
                return true;
            });
        }

        public UiThread Ui { get; } = new();

        public FakeErrorSink Errors { get; } = new();

        public FakeInfoBarService InfoBars { get; } = new();

        public FakeExposureMeter Meter { get; } = new();

        public ShellViewModel Shell { get; private set; } = null!;

        public ExposureAnalysis Analysis { get; private set; } = null!;

        public LookPanelViewModel Panel { get; private set; } = null!;

        public Project Project => Shell.CurrentProject.Model;

        // A brightness from the photo's number, so each frame's measurement can be checked.
        public static double LuminanceOf(Frame frame) => -2 - (int.Parse(frame.FileName[4..8], System.Globalization.CultureInfo.InvariantCulture) % 7 * 0.1);

        public T On<T>(Func<T> work) => Ui.Run(work);

        public void On(Action work) => Ui.Run(() =>
        {
            work();
            return true;
        });

        public void Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                On(() => { });
                Thread.Sleep(20);
            }
        }

        // Polls the condition on the UI thread until it holds, failing after the patience.
        public async Task UntilAsync(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (!On(condition))
            {
                Assert.True(clock.Elapsed < _patience, "The condition did not come true in time.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        public void Dispose()
        {
            Meter.Release();
            On(() =>
            {
                Panel.Dispose();
                Analysis.Dispose();
            });
            Ui.Dispose();
        }
    }
}
