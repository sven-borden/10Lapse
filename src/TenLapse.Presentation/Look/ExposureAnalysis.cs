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
/// while a pass runs join it; removed ones are measured for nothing and dropped. A photo that cannot be measured is
/// listed once in an InfoBar and not tried again until the project is replaced or deflicker is turned on again.
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

    // UI thread only.
    private readonly HashSet<Guid> _failed = [];
    private Project _project;
    private Run? _run;
    private bool _wasOn;
    private bool _disposed;
    private string _progressText = string.Empty;
    private double _progressPercent;

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

    /// <summary>Stops the pass now; what was measured stays. UI thread. The Look panel turns deflicker off with it.</summary>
    public void Cancel() => StopRun();

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
        _project.Changed -= OnProjectChanged;
        _project = e.Current.Model;
        _project.Changed += OnProjectChanged;
        _wasOn = _project.Look.Deflicker;
        _errors.Guard(Refresh, LookText.MeasureFailedContext);
    }

    // UI thread: starts, feeds or stops the pass to match the project.
    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var snapshot = _project.Snapshot;
        var on = snapshot.Look.Deflicker;
        if (on && !_wasOn)
        {
            // Turned on again: the user asks anew, so photos that failed before are tried again.
            _failed.Clear();
        }

        _wasOn = on;
        if (!on || _project.IsClosed)
        {
            StopRun();
            return;
        }

        var pending = snapshot.Frames.Where(frame => frame.Exposure is null && frame.PixelSize is not null && !_failed.Contains(frame.Id)).ToList();
        var run = _run;
        if (run is null)
        {
            if (pending.Count == 0)
            {
                return;
            }

            run = _run = new Run(_project);
            for (var i = 0; i < _parallelism; i++)
            {
                _ = WorkAsync(run);
            }

            OnPropertyChanged(nameof(IsMeasuring));
        }

        foreach (var frame in pending.Where(frame => run.Queued.Add(frame.Id)))
        {
            run.Total++;
            run.Queue.Writer.TryWrite(frame);
        }

        ShowProgress(run);
    }

    // A worker: measures queued photos until the pass completes or stops. Off the UI thread.
    private async Task WorkAsync(Run run)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            await foreach (var frame in run.Queue.Reader.ReadAllAsync(run.Stop.Token).ConfigureAwait(false))
            {
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

    // UI thread: writes the measurements that came in to their frames, and ends the pass once every photo is done.
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
            run.Done++;
            if (item.Result.Exposure is { } exposure)
            {
                measured[item.Frame.Id] = exposure;
            }
            else
            {
                _failed.Add(item.Frame.Id);
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
            // The write's own refresh stopped the pass (deflicker turned off meanwhile).
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

    // UI thread: a worker failed as a whole. The photos still queued are not tried again in this project.
    private void Fail(Run run, Exception exception)
    {
        if (!ReferenceEquals(run, _run))
        {
            return;
        }

        _failed.UnionWith(run.Queued);
        EndRun(run);
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

    private void ShowProgress(Run? run)
    {
        var text = run is null ? string.Empty : LookText.MeasuringProgress(Math.Min(run.Done, run.Total), run.Total);
        var percent = run is null || run.Total == 0 ? 0 : 100.0 * run.Done / run.Total;
        SetProperty(ref _progressText, text, nameof(ProgressText));
        SetProperty(ref _progressPercent, percent, nameof(ProgressPercent));
    }

    /// <summary>One pass: its queue, its workers' stop, and what came back.</summary>
    private sealed class Run(Project target)
    {
        public Project Target { get; } = target;

        public CancellationTokenSource Stop { get; } = new();

        public Channel<Frame> Queue { get; } = Channel.CreateUnbounded<Frame>(new UnboundedChannelOptions { SingleWriter = true });

        public ConcurrentQueue<(Frame Frame, ExposureMeasurement Result)> Results { get; } = new();

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // UI thread only.
        public HashSet<Guid> Queued { get; } = [];

        public List<(string Name, string Why)> Failures { get; } = [];

        public int Total { get; set; }

        public int Done { get; set; }

        // 1 while a flush is on its way (workers set it, the UI thread clears it).
        public int FlushPending;
    }
}
