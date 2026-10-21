using System.Collections.Concurrent;
using System.Threading.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using TenLapse.Core.Look;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Look;

/// <summary>
/// Deflicker's exposure pass (#45): while the project's deflicker is on, every photo without a measurement is measured
/// in the background (<see cref="IExposureMeter"/>) and the result written to its frame (<see cref="Frame.Exposure"/>),
/// in batches, with progress. One per window; it follows the shell's current project.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the results live.</b> In the project, on each frame: they move with the photo through reorders, removals and
/// undo, the curve is computed from the snapshot alone (so preview and export agree), and the <c>.10lapse</c> file keeps
/// them, so reopening a project or turning deflicker off and on measures nothing again. A measurement is the photo's,
/// not the user's work: a clean project stays clean, as with the pixel sizes an opened project reads again.
/// </para>
/// <para>
/// <b>Which photos.</b> Those without a measurement whose pixel size is known: a photo an import is still reading gets
/// its size first and its measurement after, so the import's own update never overwrites a measurement. Photos added
/// while a pass runs join it; photos removed leave it (they are not measured, counted or waited for). A photo that cannot
/// be measured is tried once more; if it fails again it is listed once in an InfoBar and set aside, and the curve gives it
/// the correction between its measured neighbours (<see cref="ExposureCurve"/>), so it never flashes.
/// </para>
/// <para>
/// <b>Cancel.</b> A pass the user started by turning deflicker on stops, and the Look panel turns deflicker off again: the
/// user is taking back the switch they just turned. A pass that started by itself (a project opened or photos imported
/// with deflicker on) stops without touching the project: deflicker stays on, the photos left are set aside like failed
/// ones, and <see cref="Unmeasured"/> counts them until <see cref="MeasureRemaining"/> (the Look panel's Measure) or
/// turning deflicker on again tries them.
/// </para>
/// <para>
/// <b>Cost.</b> Two or three photos at once (a quarter of the cores, at most three), each a decode of at most 256 px, so
/// the preview's own decodes keep their share of the CPU. <see cref="Cancel"/> returns at once: the measurements in
/// flight stop at their next check, and their results are dropped.
/// </para>
/// </remarks>
public sealed class ExposureAnalysis : ObservableObject, IDisposable
{
    private static readonly TimeSpan _applyEvery = TimeSpan.FromMilliseconds(250);

    private readonly ShellViewModel _shell;
    private readonly IExposureMeter _meter;
    private readonly IUiDispatcher _ui;
    private readonly IErrorPresenter _errors;
    private readonly IInfoBarService _infoBars;
    private readonly int _parallelism;

    // UI thread only. Photos failed twice, and photos a Cancel set aside: neither is measured until asked again.
    private readonly HashSet<Guid> _failed = [];
    private readonly HashSet<Guid> _setAside = [];
    private Project _project;
    private Run? _run;
    private bool _wasOn;
    private bool _disposed;
    private string _progressText = string.Empty;
    private double _progressPercent;
    private int _measured;
    private int _toMeasure;
    private int _unmeasured;

    /// <param name="shell">The shell whose current project is measured.</param>
    /// <param name="meter">Measures one photo (Media's <c>ExposureMeter</c>).</param>
    /// <param name="ui">The UI thread, where the project is changed.</param>
    /// <param name="errors">The error path: a pass that fails as a whole is shown and logged.</param>
    /// <param name="infoBars">Where the photos that could not be measured are listed.</param>
    /// <param name="parallelism">Photos measured at once; null for a quarter of the cores, one to three.</param>
    public ExposureAnalysis(ShellViewModel shell, IExposureMeter meter, IUiDispatcher ui, IErrorPresenter errors, IInfoBarService infoBars, int? parallelism = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(infoBars);
        _shell = shell;
        _meter = meter;
        _ui = ui;
        _errors = errors;
        _infoBars = infoBars;
        _parallelism = parallelism ?? Math.Clamp(Environment.ProcessorCount / 4, 1, 3);
        _project = shell.CurrentProject.Model;
        _project.Changed += OnProjectChanged;
        _shell.ProjectReplaced += OnProjectReplaced;
        _wasOn = _project.Look.Deflicker;
        _errors.Guard(Refresh, LookText.MeasureFailedContext);
    }

    /// <summary>True while photos are being measured.</summary>
    public bool IsMeasuring => _run is not null;

    /// <summary>"Measuring 1,240 of 5,000 photos…"; empty while nothing is measured.</summary>
    public string ProgressText => _progressText;

    /// <summary>The share measured, 0 to 100.</summary>
    public double ProgressPercent => _progressPercent;

    /// <summary>The photos of the running pass measured so far (or failed); 0 while nothing is measured.</summary>
    public int Measured => _measured;

    /// <summary>All the photos of the running pass, those still in the project; 0 while nothing is measured.</summary>
    public int ToMeasure => _toMeasure;

    /// <summary>
    /// While deflicker is on: the photos without a measurement that no pass is measuring (they failed twice, or a Cancel
    /// set them aside). Their correction comes from their neighbours; <see cref="MeasureRemaining"/> tries them again.
    /// </summary>
    public int Unmeasured => _unmeasured;

    /// <summary>
    /// Completes when no photo of the project is waiting to be measured (at once when deflicker is off), so an export
    /// starts from the finished curve. UI thread. A pass stopped by <see cref="Cancel"/> or a new project also ends it.
    /// </summary>
    public async Task WhenMeasuredAsync(CancellationToken cancellationToken)
    {
        while (_run is { } run)
        {
            await run.Finished.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Stops the pass now; what was measured stays. UI thread. True when the pass was the one turning deflicker on started
    /// (the Look panel then turns it off again); false otherwise, and the photos left are set aside (<see cref="Unmeasured"/>).
    /// </summary>
    public bool Cancel()
    {
        if (_run is not { } run)
        {
            return false;
        }

        if (!run.StartedByTurningOn)
        {
            _setAside.UnionWith(run.Queued.Where(id => !run.Completed.Contains(id)));
        }

        // Counted before the pass ends, so the panel has the count as the progress goes and Measure takes its place.
        CountUnmeasured();
        EndRun(run);
        return run.StartedByTurningOn;
    }

    /// <summary>Measures the photos that failed or were set aside, now. UI thread.</summary>
    public void MeasureRemaining()
    {
        _failed.Clear();
        _setAside.Clear();
        Refresh();
    }

    /// <summary>Stops following the project and stops the pass.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRun();
        _project.Changed -= OnProjectChanged;
        _shell.ProjectReplaced -= OnProjectReplaced;
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        if ((e.Changes & (ProjectChanges.Frames | ProjectChanges.Look)) != 0)
        {
            _errors.Guard(Refresh, LookText.MeasureFailedContext);
        }
    }

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e)
    {
        StopRun();
        _failed.Clear();
        _setAside.Clear();
        _project.Changed -= OnProjectChanged;
        _project = e.Current.Model;
        _project.Changed += OnProjectChanged;
        _wasOn = _project.Look.Deflicker;
        _errors.Guard(Refresh, LookText.MeasureFailedContext);
    }

    // UI thread: starts, feeds, trims or stops the pass to match the project.
    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var snapshot = _project.Snapshot;
        var on = snapshot.Look.Deflicker;
        var turnedOn = on && !_wasOn;
        if (turnedOn)
        {
            // Turned on again: the user asks anew, so photos that failed or were set aside are tried again.
            _failed.Clear();
            _setAside.Clear();
        }

        _wasOn = on;
        if (!on || _project.IsClosed)
        {
            StopRun();
            CountUnmeasured();
            return;
        }

        var run = _run;
        if (run is not null)
        {
            Forget(run, snapshot);
        }

        var pending = snapshot.Frames.Where(frame => frame.Exposure is null && frame.PixelSize is not null && !_failed.Contains(frame.Id) && !_setAside.Contains(frame.Id)).ToList();
        if (run is null)
        {
            if (pending.Count == 0)
            {
                CountUnmeasured();
                return;
            }

            run = _run = new Run(_project, turnedOn);
            for (var i = 0; i < _parallelism; i++)
            {
                _ = WorkAsync(run);
            }

            OnPropertyChanged(nameof(IsMeasuring));
        }

        foreach (var frame in pending.Where(frame => run.Queued.Add(frame.Id)))
        {
            run.Removed.TryRemove(frame.Id, out _);
            run.Queue.Writer.TryWrite(frame);
        }

        if (run.Done >= run.Total)
        {
            // The photos left were all removed.
            Finish(run);
            return;
        }

        ShowProgress(run);
        CountUnmeasured();
    }

    // UI thread: photos that left the project leave the pass, its count and the export's wait; the workers skip them.
    private static void Forget(Run run, ProjectSnapshot snapshot)
    {
        var present = new HashSet<Guid>(snapshot.Frames.Length);
        foreach (var frame in snapshot.Frames)
        {
            present.Add(frame.Id);
        }

        foreach (var id in run.Queued.Where(id => !present.Contains(id)).ToList())
        {
            run.Queued.Remove(id);
            run.Completed.Remove(id);
            run.Removed.TryAdd(id, 0);
        }
    }

    // A worker: measures queued photos until the pass completes or stops. Off the UI thread.
    private async Task WorkAsync(Run run)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            await foreach (var frame in run.Queue.Reader.ReadAllAsync(run.Stop.Token).ConfigureAwait(false))
            {
                if (run.Removed.ContainsKey(frame.Id))
                {
                    continue;
                }

                var result = await _meter.MeasureAsync(frame, run.Stop.Token).ConfigureAwait(false);
                run.Results.Enqueue((frame, result));
                if (Interlocked.Exchange(ref run.FlushPending, 1) == 0)
                {
                    _ = FlushLaterAsync(run);
                }
            }
        }
        catch (OperationCanceledException) when (run.Stop.IsCancellationRequested)
        {
            // Stopped: Cancel, deflicker off, a new project or the window closing.
        }
        catch (Exception exception)
        {
            // Not a photo's failure (those come back as results): a bug or the system. Shown once, and the pass ends.
            await RunOnUiAsync(() => Fail(run, exception)).ConfigureAwait(false);
        }
    }

    // Results are written to the project in batches, at most every quarter second, so 5,000 photos are a few dozen edits.
    private async Task FlushLaterAsync(Run run)
    {
        try
        {
            await Task.Delay(_applyEvery, run.Stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RunOnUiAsync(() => Apply(run)).ConfigureAwait(false);
    }

    private async Task RunOnUiAsync(Action work)
    {
        try
        {
            await _ui.RunAsync(() => _errors.Guard(work, LookText.MeasureFailedContext)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The window is closing; nothing is left to tell.
        }
    }

    // UI thread: writes the measurements that came in to their frames, tries a failed photo once more, and ends the pass
    // once every photo still in the project is done.
    private void Apply(Run run)
    {
        Interlocked.Exchange(ref run.FlushPending, 0);
        if (!ReferenceEquals(run, _run) || run.Target.IsClosed)
        {
            return;
        }

        var measured = new Dictionary<Guid, PhotoExposure>();
        while (run.Results.TryDequeue(out var item))
        {
            var id = item.Frame.Id;
            if (!run.Queued.Contains(id))
            {
                // Removed while it was measured.
                continue;
            }

            if (item.Result.Exposure is { } exposure)
            {
                run.Completed.Add(id);
                measured[id] = exposure;
            }
            else if (run.Retried.Add(id))
            {
                // A read can fail once (a file share waking up, a file still being copied): one more try.
                run.Queue.Writer.TryWrite(item.Frame);
            }
            else
            {
                run.Completed.Add(id);
                _failed.Add(id);
                run.Failures.Add((item.Frame.FileName, item.Result.Failure ?? string.Empty));
            }
        }

        // Only frames still in the project with the same photo, and still without a measurement.
        var updates = run.Target.Frames
            .Where(frame => frame.Exposure is null && measured.ContainsKey(frame.Id))
            .Select(frame => frame with { Exposure = measured[frame.Id] })
            .ToList();
        if (updates.Count > 0)
        {
            var wasClean = !run.Target.IsDirty;
            run.Target.UpdateFrames(updates);
            if (wasClean)
            {
                run.Target.MarkSaved(run.Target.Version);
            }
        }

        if (!ReferenceEquals(run, _run))
        {
            // The write's own refresh ended the pass (deflicker turned off meanwhile, or every photo is done).
            return;
        }

        if (run.Done >= run.Total)
        {
            Finish(run);
            return;
        }

        ShowProgress(run);
    }

    // UI thread: the pass is done; the photos that failed are listed once, and the next pass may start.
    private void Finish(Run run)
    {
        EndRun(run);
        if (run.Failures.Count > 0)
        {
            _infoBars.ShowWithDetails(
                InfoBarSeverity.Caution,
                LookText.MeasureFailedMessage(run.Failures.Count),
                run.Failures.Select(failure => new InfoBarDetail(failure.Name)));
            // The log gets the reasons, the first twenty in full: a folder of 5,000 unreadable files is one story.
            var reasons = string.Join(Environment.NewLine, run.Failures.Take(20).Select(failure => $"{failure.Name}: {failure.Why}"));
            _errors.Report(new InvalidOperationException($"{run.Failures.Count} photos could not be measured.{Environment.NewLine}{reasons}"), LookText.MeasureFailedContext);
        }

        Refresh();
    }

    // UI thread: a worker failed as a whole. The photos still queued are set aside like failed ones.
    private void Fail(Run run, Exception exception)
    {
        if (!ReferenceEquals(run, _run))
        {
            return;
        }

        _failed.UnionWith(run.Queued.Where(id => !run.Completed.Contains(id)));
        EndRun(run);
        CountUnmeasured();
        _errors.Show(exception, LookText.MeasureFailedContext);
    }

    private void StopRun()
    {
        if (_run is { } run)
        {
            EndRun(run);
        }
    }

    private void EndRun(Run run)
    {
        run.Stop.Cancel();
        run.Queue.Writer.TryComplete();
        if (ReferenceEquals(run, _run))
        {
            _run = null;
            ShowProgress(null);
            OnPropertyChanged(nameof(IsMeasuring));
        }

        run.Finished.TrySetResult();
    }

    // UI thread: the photos deflicker has no measurement for and no pass is measuring.
    private void CountUnmeasured()
    {
        var count = 0;
        if (!_disposed && !_project.IsClosed && _project.Look.Deflicker && (_failed.Count > 0 || _setAside.Count > 0))
        {
            count = _project.Frames.Count(frame => frame.Exposure is null && (_failed.Contains(frame.Id) || _setAside.Contains(frame.Id)));
        }

        SetProperty(ref _unmeasured, count, nameof(Unmeasured));
    }

    private void ShowProgress(Run? run)
    {
        var done = run is null ? 0 : Math.Min(run.Done, run.Total);
        var total = run?.Total ?? 0;
        var text = run is null ? string.Empty : LookText.MeasuringProgress(done, total);
        var percent = run is null || total == 0 ? 0 : 100.0 * done / total;
        SetProperty(ref _progressText, text, nameof(ProgressText));
        SetProperty(ref _progressPercent, percent, nameof(ProgressPercent));
        SetProperty(ref _measured, done, nameof(Measured));
        SetProperty(ref _toMeasure, total, nameof(ToMeasure));
    }

    /// <summary>One pass: its queue, its workers' stop, and what came back.</summary>
    private sealed class Run(Project target, bool startedByTurningOn)
    {
        public Project Target { get; } = target;

        /// <summary>True when the user's turning deflicker on started it (Cancel then turns it off again).</summary>
        public bool StartedByTurningOn { get; } = startedByTurningOn;

        public CancellationTokenSource Stop { get; } = new();

        public Channel<Frame> Queue { get; } = Channel.CreateUnbounded<Frame>(new UnboundedChannelOptions { SingleWriter = true });

        public ConcurrentQueue<(Frame Frame, ExposureMeasurement Result)> Results { get; } = new();

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Photos that left the project: the workers skip them. Written on the UI thread, read by the workers.
        public ConcurrentDictionary<Guid, byte> Removed { get; } = new();

        // UI thread only: the photos in the pass that are still in the project, those done (measured or failed twice),
        // and those already tried a second time.
        public HashSet<Guid> Queued { get; } = [];

        public HashSet<Guid> Completed { get; } = [];

        public HashSet<Guid> Retried { get; } = [];

        public List<(string Name, string Why)> Failures { get; } = [];

        public int Total => Queued.Count;

        public int Done => Completed.Count;

        // 1 while a flush is on its way (workers set it, the UI thread clears it).
        public int FlushPending;
    }
}
