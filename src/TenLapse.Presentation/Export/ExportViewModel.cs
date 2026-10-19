using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Filmstrip;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Export;

/// <summary>What the inspector column shows (#24).</summary>
public enum ExportPanelState
{
    /// <summary>The inspector's sections: no export, or the last one was cancelled or failed.</summary>
    Editing,

    /// <summary>The export panel: progress, the film gate, Cancel export.</summary>
    Exporting,

    /// <summary>The finished state: the check, "Harbor sunset.mp4 is ready", Play, Show in folder, Back to editing.</summary>
    Finished,
}

/// <summary>
/// The export (#24), inline in the inspector: the Export button (and Ctrl+E), the Save dialog, the running export with its
/// progress, the finished state, cancel and failure. All of it runs on the UI thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is exported.</b> The project's snapshot when the export starts, with the Output panel's
/// <see cref="OutputPanelViewModel.EffectiveOutput"/> in place of the stored preference
/// (<see cref="ProjectSnapshot.WithOutput"/>), so the video has the resolution, codec, quality and size the panel shows.
/// The Export button shows only with frames and <see cref="OutputPanelViewModel.CanExport"/> (absent otherwise); while the
/// export panel or the finished state shows it stays, dimmed and disabled, as the canvas has it.
/// </para>
/// <para>
/// <b>While it runs</b> the project holds still (<see cref="ShellViewModel.Edits"/> is closed): a removal, a move, an
/// import or a new project is refused with one InfoBar sentence. The preview shows the frame being written, the
/// filmstrip dims the frames not written yet, and the taskbar button shows the progress. Progress reaches the panel
/// through a <see cref="Progress{T}"/> made on the UI thread and is shown at most every <see cref="MinProgressInterval"/>
/// (each phase change and the last frame always). Narrator hears the start, every ten percent at most, and the end.
/// </para>
/// <para>
/// <b>Hooks.</b> <see cref="Started"/> and <see cref="Ended"/> bracket every export: #25 takes its power request there,
/// #31 counts <see cref="ExportOutcome.Succeeded"/> for the rating prompt.
/// </para>
/// </remarks>
public sealed partial class ExportViewModel : ObservableObject, IDisposable
{
    /// <summary>The shortest gap between two progress updates of the panel (except phase changes and the last frame).</summary>
    public static readonly TimeSpan MinProgressInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How long a close waits for a cancelled export to remove its unfinished file before the window goes anyway.</summary>
    public static readonly TimeSpan CloseLimit = TimeSpan.FromSeconds(2);

    private readonly ShellViewModel _shell;
    private readonly OutputPanelViewModel _output;
    private readonly PlayerViewModel _player;
    private readonly FilmstripViewModel _filmstrip;
    private readonly Func<bool> _importing;
    private readonly ExportServices _services;
    private readonly IErrorPresenter _errors;
    private readonly Func<TimeSpan> _clock;

    // The running export, counted so a progress report posted before it ended is ignored after.
    private int _run;
    private ExportStatus? _applied;
    private TimeSpan _appliedAt;
    private int _announcedTenth;
    private bool _closing;
    private bool _disposed;

    /// <param name="shell">The current project, its edit gate and whether the Editing state shows.</param>
    /// <param name="output">The effective output and whether it can be exported (#23).</param>
    /// <param name="player">The preview: it pauses, then shows the frame being written.</param>
    /// <param name="filmstrip">Dims the frames not written yet.</param>
    /// <param name="importing">True while photos are still being added; an export waits for them.</param>
    /// <param name="services">The exporter, the Save dialog and the window's taskbar and notification.</param>
    /// <param name="errors">The error path: a failed export becomes an InfoBar that says what happened and what to do.</param>
    /// <param name="clock">The time since some start, for the progress throttle and the export's duration; a stopwatch when null.</param>
    public ExportViewModel(
        ShellViewModel shell,
        OutputPanelViewModel output,
        PlayerViewModel player,
        FilmstripViewModel filmstrip,
        Func<bool> importing,
        ExportServices services,
        IErrorPresenter errors,
        Func<TimeSpan>? clock = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _filmstrip = filmstrip ?? throw new ArgumentNullException(nameof(filmstrip));
        _importing = importing ?? throw new ArgumentNullException(nameof(importing));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        if (clock is null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed;
        }

        _clock = clock;
        FileName = string.Empty;
        PercentText = ExportText.Percent(0);
        TimeLeftText = string.Empty;
        FramesText = string.Empty;
        SpeedText = string.Empty;
        OutputSummary = string.Empty;
        RenderingText = string.Empty;
        FinishedTitle = string.Empty;
        FinishedSummary = string.Empty;
        ExportedInText = string.Empty;
        SavedToText = string.Empty;
        SavedBesideText = string.Empty;
        VideoPath = string.Empty;
        ExportFrames = [];

        ExportCommand = errors.AsyncCommand(ExportAsync, ExportText.ExportFailed);
        CancelCommand = errors.Command(Cancel, ExportText.CancelFailed);
        PlayCommand = errors.Command(Premiere, ExportText.PlayFailed);
        ShowInFolderCommand = errors.Command(ShowInFolder, ExportText.ShowInFolderFailed);
        BackToEditingCommand = errors.Command(BackToEditing, ExportText.BackFailed);

        _shell.PropertyChanged += OnShellPropertyChanged;
        _output.PropertyChanged += OnOutputPropertyChanged;
        _shell.Edits.Refused += OnEditRefused;
        _shell.ProjectReplaced += OnProjectReplaced;
    }

    /// <summary>Raised as an export starts, after the Save dialog (#25 takes its power request here).</summary>
    public event EventHandler? Started;

    /// <summary>Raised once per export as it ends, however it ends (#25 lets go of its power request; #31 counts successes).</summary>
    public event EventHandler<ExportEndedEventArgs>? Ended;

    /// <summary>Something for Narrator to hear (the view raises it as a UIA notification).</summary>
    public event EventHandler<string>? Announced;

    /// <summary>
    /// The preview should play the finished video from its first frame: once the export ends (the view waits the motion
    /// spec's 1.1 s) and on Play. The argument is the video's path.
    /// </summary>
    public event EventHandler<string>? PremiereRequested;

    /// <summary>Export video (the amber button, Ctrl+E): the Save dialog, then the export. Its <c>Cancel()</c> stops a running export.</summary>
    public IAsyncRelayCommand ExportCommand { get; }

    /// <summary>Cancel export.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Play: the preview plays the finished video from frame one.</summary>
    public IRelayCommand PlayCommand { get; }

    /// <summary>Show in folder: File Explorer with the video selected.</summary>
    public IRelayCommand ShowInFolderCommand { get; }

    /// <summary>Back to editing: the inspector again.</summary>
    public IRelayCommand BackToEditingCommand { get; }

    /// <summary>What the inspector column shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsExporting), nameof(IsFinished), nameof(CanStartExport), nameof(ExportButtonText))]
    public partial ExportPanelState State { get; private set; }

    /// <summary>True while the inspector's sections show.</summary>
    public bool IsEditing => State == ExportPanelState.Editing;

    /// <summary>True while an export runs (the export panel shows).</summary>
    public bool IsExporting => State == ExportPanelState.Exporting;

    /// <summary>True while the finished state shows.</summary>
    public bool IsFinished => State == ExportPanelState.Finished;

    /// <summary>
    /// True when the Export button shows: the project has frames and the Output panel has an output this PC can export.
    /// Without frames (the first-launch screen, #29) it is absent. While the export panel or the finished state shows it
    /// stays, dimmed and disabled (<see cref="CanStartExport"/>), as the canvas has it.
    /// </summary>
    public bool ShowsExportButton => _shell.CurrentProject.HasFrames && _output.CanExport;

    /// <summary>True when Export video (and Ctrl+E) can start an export: the button shows and the inspector is up.</summary>
    public bool CanStartExport => IsEditing && ShowsExportButton;

    /// <summary>The Export button's label: "Exporting…" while an export runs, "Export video" otherwise (the canvas's <c>exportLabel</c>).</summary>
    public string ExportButtonText => IsExporting ? ExportText.ExportingButton : ExportText.ExportVideo;

    /// <summary>The video's file name ("Harbor sunset.mp4") while it is written.</summary>
    [ObservableProperty]
    public partial string FileName { get; private set; }

    /// <summary>"46%".</summary>
    [ObservableProperty]
    public partial string PercentText { get; private set; }

    /// <summary>The progress, 0 to 100 (the bar's value, a RangeValue for UI Automation).</summary>
    [ObservableProperty]
    public partial double Percent { get; private set; }

    /// <summary>"About 2 min 30 s left", "Getting ready…", "Finishing the file…".</summary>
    [ObservableProperty]
    public partial string TimeLeftText { get; private set; }

    /// <summary>"0312 of 0742".</summary>
    [ObservableProperty]
    public partial string FramesText { get; private set; }

    /// <summary>"4.3 photos per second".</summary>
    [ObservableProperty]
    public partial string SpeedText { get; private set; }

    /// <summary>"4K · H.264 · High": the effective output being written.</summary>
    [ObservableProperty]
    public partial string OutputSummary { get; private set; }

    /// <summary>"Rendering frame 0313", over the preview.</summary>
    [ObservableProperty]
    public partial string RenderingText { get; private set; }

    /// <summary>The 0-based frame being written: the film gate's position.</summary>
    [ObservableProperty]
    public partial int CurrentFrame { get; private set; }

    /// <summary>The frames the running export writes (the film gate's thumbnails); empty when none runs.</summary>
    [ObservableProperty]
    public partial ImmutableArray<Frame> ExportFrames { get; private set; }

    /// <summary>"Harbor sunset.mp4 is ready".</summary>
    [ObservableProperty]
    public partial string FinishedTitle { get; private set; }

    /// <summary>"24.7 s · 4K · H.264 · 173 MB", from the result and the file's real size.</summary>
    [ObservableProperty]
    public partial string FinishedSummary { get; private set; }

    /// <summary>"2 min 51 s".</summary>
    [ObservableProperty]
    public partial string ExportedInText { get; private set; }

    /// <summary>"Videos › 10Lapse".</summary>
    [ObservableProperty]
    public partial string SavedToText { get; private set; }

    /// <summary>
    /// When another app held the chosen file, where the video went instead (<see cref="ProjectText.ExportSavedBeside"/>);
    /// empty otherwise.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedBeside))]
    public partial string SavedBesideText { get; private set; }

    /// <summary>True when <see cref="SavedBesideText"/> has something to say.</summary>
    public bool HasSavedBeside => SavedBesideText.Length > 0;

    /// <summary>The finished video's full path.</summary>
    [ObservableProperty]
    public partial string VideoPath { get; private set; }

    /// <summary>
    /// True when the window may close at once: no export runs and no cancelled export is still removing its unfinished
    /// file. Otherwise the window calls <see cref="PrepareToCloseAsync"/> first.
    /// </summary>
    public bool MayCloseNow => !IsExporting && _services.Exporter.WhenAllCleanedUp().IsCompleted;

    /// <summary>
    /// Before the window closes: while an export runs, asks "Stop the export and close?" (false keeps the window and the
    /// export); then cancels it and waits, at most <see cref="CloseLimit"/>, for its unfinished file to go. Whatever is
    /// left, the next launch deletes.
    /// </summary>
    /// <returns>True when the window may close.</returns>
    public async Task<bool> PrepareToCloseAsync()
    {
        if (IsExporting)
        {
            if (!await _services.Dialogs.ConfirmAsync(new Confirmation(ExportText.CloseTitle, ExportText.CloseMessage, ExportText.CloseConfirm, ExportText.CloseCancel)))
            {
                return false;
            }

            if (IsExporting)
            {
                _closing = true;
                ExportCommand.Cancel();
            }
        }

        // One budget for both waits. First the export itself: a cancelled export registers its clean-up only as it ends
        // (on a worker thread, after the token fires), so the clean-ups are asked for afterwards, not before. Whatever is
        // left after the budget, the journal deletes at the next launch.
        var budget = Stopwatch.StartNew();
        var running = ExportCommand.ExecutionTask ?? Task.CompletedTask;
        if (!await WithinAsync(running, CloseLimit))
        {
            return true;
        }

        var left = CloseLimit - budget.Elapsed;
        if (left > TimeSpan.Zero)
        {
            await WithinAsync(_services.Exporter.WhenAllCleanedUp(), left);
        }

        return true;
    }

    // Whether the task ended (in any way) within the limit. Never throws.
    private static async Task<bool> WithinAsync(Task task, TimeSpan limit)
    {
        if (task.IsCompleted)
        {
            return true;
        }

        using var timeout = new CancellationTokenSource(limit);
        var delay = Task.Delay(Timeout.Infinite, timeout.Token);
        return await Task.WhenAny(task, delay) == task;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shell.PropertyChanged -= OnShellPropertyChanged;
        _output.PropertyChanged -= OnOutputPropertyChanged;
        _shell.Edits.Refused -= OnEditRefused;
        _shell.ProjectReplaced -= OnProjectReplaced;
        if (IsExporting)
        {
            _closing = true;
            ExportCommand.Cancel();
        }
    }

    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        // Only from the inspector: Ctrl+E on the export panel or the finished state does nothing, as the dimmed button.
        if (_disposed || !CanStartExport || _shell.IsSettingsOpen)
        {
            return;
        }

        if (_importing())
        {
            _services.InfoBars.Show(InfoBarSeverity.Caution, ExportText.ImportStillRunning);
            return;
        }

        var project = _shell.CurrentProject;
        var path = await _services.Picker.PickVideoAsync(ExportText.SuggestedFileName(project.Name), _services.Folders.ExportFolder);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // The dialog took a while: the project, the output or an import may have changed meanwhile. The user picked a file,
        // so say why nothing happens.
        if (_disposed)
        {
            return;
        }

        if (!CanStartExport || !ReferenceEquals(project, _shell.CurrentProject) || _output.EffectiveOutput is not { } output)
        {
            _services.InfoBars.Show(InfoBarSeverity.Caution, ExportText.ChangedDuringDialog);
            return;
        }

        if (_importing())
        {
            _services.InfoBars.Show(InfoBarSeverity.Caution, ExportText.ImportStillRunning);
            return;
        }

        await RunAsync(project, project.Model.Snapshot.WithOutput(output), output, path, cancellationToken);
    }

    private async Task RunAsync(ProjectViewModel project, ProjectSnapshot snapshot, OutputSettings output, string path, CancellationToken cancellationToken)
    {
        var run = ++_run;
        var fileName = Path.GetFileName(path);
        Begin(snapshot, output, fileName);
        var progress = new Progress<ExportStatus>(status => _errors.Guard(() => OnProgress(run, status), ExportText.ProgressFailed));
        var started = _clock();

        ExportedVideo video;
        try
        {
            video = await _services.Exporter.ExportAsync(snapshot, path, progress, cancellationToken);
        }
        catch (Exception exception) when (ErrorMessages.IsCancellation(exception) && cancellationToken.IsCancellationRequested)
        {
            End(failed: false);
            State = ExportPanelState.Editing;
            RaiseEnded(ExportOutcome.Cancelled, null);

            // Not awaited here: the encoder can take seconds to let go of the file, and Export must be ready again at once.
            _ = _errors.GuardAsync(() => SayCancelledAsync(path), ExportText.CancelFailed);
            return;
        }
        catch
        {
            End(failed: true);
            State = ExportPanelState.Editing;
            Announced?.Invoke(this, ExportText.FailedAnnouncement);
            _services.Window.NotifyIfInBackground(ExportText.FailedNotification(fileName));
            RaiseEnded(ExportOutcome.Failed, null);

            // The error path shows what happened and what to do (VideoExportErrorMapper's sentences).
            throw;
        }

        Finish(project, snapshot, output, video, _clock() - started);
    }

    // The InfoBar says the unfinished file is gone only once it is (or that the next launch removes it). Not while the
    // window closes: nobody would read it.
    private async Task SayCancelledAsync(string path)
    {
        var removed = await _services.Exporter.CleanUpAsync(path);
        if (!_closing && !_disposed)
        {
            _services.InfoBars.Show(InfoBarSeverity.Neutral, removed ? ExportText.Cancelled : ExportText.CancelledFileLeft);
        }
    }

    // The export panel comes up: the numbers start from zero, the project holds still, the preview pauses on the first frame.
    private void Begin(ProjectSnapshot snapshot, OutputSettings output, string fileName)
    {
        _applied = null;
        _announcedTenth = 0;
        FileName = fileName;
        OutputSummary = ExportText.OutputSummary(output);
        ExportFrames = snapshot.Frames;
        Show(new ExportStatus(ExportPhase.Preparing, 0, snapshot.Frames.Length, null, null));
        SavedBesideText = string.Empty;
        _shell.Edits.Close();
        State = ExportPanelState.Exporting;

        _player.Pause();
        _player.Seek(0);
        _filmstrip.SetPending(snapshot.Frames, 0);
        _services.Window.ShowProgress(null);
        Announced?.Invoke(this, ExportText.Started(fileName));
        Started?.Invoke(this, EventArgs.Empty);
    }

    // Shown at most every MinProgressInterval; a new phase and the last frame always. Reports of an export that has ended
    // (posted before it ended, run after) are dropped.
    private void OnProgress(int run, ExportStatus status)
    {
        if (run != _run || !IsExporting || _disposed)
        {
            return;
        }

        var now = _clock();
        if (_applied is { } prior)
        {
            if (status.Frame < prior.Frame || status.Phase < prior.Phase)
            {
                return;
            }

            var always = status.Phase != prior.Phase || status.Frame >= status.FrameCount;
            if (!always && now - _appliedAt < MinProgressInterval)
            {
                return;
            }
        }

        _applied = status;
        _appliedAt = now;
        Show(status);

        // The preview shows the frame being written, unless the user is playing it; the strip dims what is still to come.
        var writing = Math.Clamp(status.Frame, 0, Math.Max(0, status.FrameCount - 1));
        if (!_player.IsPlaying)
        {
            _player.Seek(writing);
        }

        _filmstrip.SetPending(ExportFrames, status.Frame);
        _services.Window.ShowProgress(status.Phase == ExportPhase.Preparing ? null : status.Fraction);

        var tenth = ExportText.WholePercent(status.Fraction) / 10;
        if (tenth > _announcedTenth && tenth < 10)
        {
            _announcedTenth = tenth;
            Announced?.Invoke(this, ExportText.ProgressAnnouncement(tenth * 10));
        }
    }

    private void Show(ExportStatus status)
    {
        Percent = ExportText.WholePercent(status.Fraction);
        PercentText = ExportText.Percent(status.Fraction);
        TimeLeftText = ExportText.TimeLeft(status);
        FramesText = ExportText.Frames(status.Frame, status.FrameCount);
        SpeedText = ExportText.Speed(status.FramesPerSecond);
        CurrentFrame = Math.Clamp(status.Frame, 0, Math.Max(0, status.FrameCount - 1));
        RenderingText = ExportText.RenderingFrame(CurrentFrame, status.FrameCount);
    }

    private void Finish(ProjectViewModel project, ProjectSnapshot snapshot, OutputSettings output, ExportedVideo video, TimeSpan took)
    {
        End(failed: false);

        // The Save dialog opens here next time, also after a restart (#21).
        if (Path.GetDirectoryName(video.Path) is { Length: > 0 } folder)
        {
            _services.Folders.ExportFolder = folder;
        }

        // Clean again, unless the project changed while the video was written.
        if (ReferenceEquals(project, _shell.CurrentProject))
        {
            project.Model.MarkExported(snapshot.Version);
        }

        var name = Path.GetFileName(video.Path);
        VideoPath = video.Path;
        FinishedTitle = ExportText.Ready(name);
        FinishedSummary = ExportText.ReadySummary(video, output.Resolution);
        ExportedInText = Core.Timing.TimingFormat.Duration(Math.Max(0, took.Ticks), System.Globalization.CultureInfo.CurrentCulture);
        SavedToText = ExportText.SavedTo(video.Path);
        SavedBesideText = video.TargetInUse is { } held ? ProjectText.ExportSavedBeside(held, video.Path) : string.Empty;
        State = ExportPanelState.Finished;

        Announced?.Invoke(this, FinishedTitle);
        _services.Window.NotifyIfInBackground(FinishedTitle);
        RaiseEnded(ExportOutcome.Succeeded, video);
        PremiereRequested?.Invoke(this, video.Path);
    }

    // However the export ended: the project may change again, nothing is dimmed, the taskbar shows no progress (or the failure).
    private void End(bool failed)
    {
        _run++;
        _applied = null;
        _shell.Edits.Open();
        _filmstrip.ClearPending();
        ExportFrames = [];
        if (failed)
        {
            _services.Window.ShowFailed();
        }
        else
        {
            _services.Window.ClearProgress();
        }
    }

    private void RaiseEnded(ExportOutcome outcome, ExportedVideo? video) => Ended?.Invoke(this, new ExportEndedEventArgs(outcome, video));

    private void Cancel()
    {
        if (IsExporting)
        {
            ExportCommand.Cancel();
        }
    }

    private void Premiere()
    {
        if (IsFinished && VideoPath.Length > 0)
        {
            PremiereRequested?.Invoke(this, VideoPath);
        }
    }

    private void ShowInFolder()
    {
        if (VideoPath.Length > 0)
        {
            _services.Reveal.ShowInFolder(VideoPath);
        }
    }

    private void BackToEditing()
    {
        if (IsFinished)
        {
            State = ExportPanelState.Editing;
        }
    }

    // A new project after a finished export: the finished state belonged to the old one.
    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e) => BackToEditing();

    // One sentence per refused edit (the queue folds repeats while one shows).
    private void OnEditRefused(object? sender, EventArgs e)
    {
        if (IsExporting)
        {
            _services.InfoBars.Show(InfoBarSeverity.Caution, ExportText.EditsWait);
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.ShowsEditing) or nameof(ShellViewModel.CurrentProject))
        {
            OnPropertyChanged(nameof(ShowsExportButton));
            OnPropertyChanged(nameof(CanStartExport));
        }
    }

    private void OnOutputPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OutputPanelViewModel.CanExport) or null or "")
        {
            OnPropertyChanged(nameof(ShowsExportButton));
            OnPropertyChanged(nameof(CanStartExport));
        }
    }
}
