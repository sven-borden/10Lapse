using TenLapse.Core.Diagnostics;
using TenLapse.Core.Projects;
using TenLapse.Core.Settings;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// The settings 10Lapse remembers between launches (#21) and the one place that keeps them. It loads them once when
/// it is made, gives each new project its starting values (<see cref="CreateProject"/>), follows the current project's
/// timing, frame and output changes (<see cref="Attach"/>), and writes the file a moment after the last change,
/// off the UI thread (<see cref="Flush"/> writes at once, for closing). It is also where the import pickers
/// (<see cref="IImportFolderMemory"/>) and #24's Save dialog (<see cref="ExportFolder"/>) keep their last folder.
/// </summary>
/// <remarks>
/// Everything but the debounced write runs on the UI thread, like <see cref="Project"/>. The output stored is
/// <c>Project.Output</c>, the user's preference; the effective output is never stored. The portrait hint's
/// dismissal is not remembered: projects are not saved, so it lives and dies with the open project.
/// </remarks>
public sealed class RememberedSettings : IImportFolderMemory, Export.IExportFolderMemory, IDisposable
{
    /// <summary>How long after the last change the file is written.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(600);

    /// <summary>The longest closing the window or Reset waits for the file to be written.</summary>
    public static readonly TimeSpan FinalWriteLimit = TimeSpan.FromSeconds(2);

    private readonly ISettingsStore _store;
    private readonly IErrorPresenter _errors;
    private readonly ILog _log;
    private readonly Lock _gate = new();
    private readonly Lock _saveGate = new();
    private readonly ITimer _timer;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _finalWriteLimit;
    private UserSettings _current;
    private bool _pending;
    private bool _disposed;
    private bool _failureShown;
    private Project? _attached;
    private ShellViewModel? _shell;

    /// <summary>Loads the settings (a small read, on the calling thread) and logs once what could not be read.</summary>
    /// <param name="store">Where the settings are kept.</param>
    /// <param name="errors">Takes a failed write: the first goes to the user as an InfoBar, repeats only to the log.</param>
    /// <param name="log">Takes what could not be read at start-up, and how long loading took.</param>
    /// <param name="time">The clock the debounce runs on; tests pass a fake.</param>
    /// <param name="debounce">The wait after the last change; <see cref="DefaultDebounce"/> when null.</param>
    /// <param name="finalWriteLimit">How long closing and Reset wait for the file; <see cref="FinalWriteLimit"/> when null.</param>
    public RememberedSettings(ISettingsStore store, IErrorPresenter errors, ILog log, TimeProvider? time = null, TimeSpan? debounce = null, TimeSpan? finalWriteLimit = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(log);
        _store = store;
        _errors = errors;
        _log = log;
        _debounce = debounce ?? DefaultDebounce;
        _finalWriteLimit = finalWriteLimit ?? FinalWriteLimit;

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        SettingsLoadResult loaded;
        try
        {
            loaded = store.Load();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Load promises not to throw; if a store breaks that, the app still starts, with the defaults.
            _log.Error(ProjectText.SettingsLoadFailed, exception);
            loaded = SettingsLoadResult.Fresh;
        }

        _current = loaded.Settings;
        if (loaded.Problems.Count > 0)
        {
            // One line for the whole load: the fields that read keep their values, the others use the defaults.
            _log.Warning(ProjectText.SettingsLoadProblems(loaded.Problems));
        }

        _log.Info(ProjectText.SettingsLoaded(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        _timer = (time ?? TimeProvider.System).CreateTimer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The settings as they are now, including changes not yet written.</summary>
    public UserSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>The folder the import pickers open in, or null for Pictures (<see cref="IImportFolderMemory"/>).</summary>
    public string? LastFolder
    {
        get => Current.ImportFolder;
        set => Update(Current with { ImportFolder = value });
    }

    /// <summary>The folder #24's Save dialog opens in, or null for Videos. Setting it saves it.</summary>
    public string? ExportFolder
    {
        get => Current.ExportFolder;
        set => Update(Current with { ExportFolder = value });
    }

    /// <summary>
    /// A new, empty, clean project that starts with the remembered timing, frame shape and output preference: the
    /// factory for <see cref="ShellViewModel"/>. On the UI thread.
    /// </summary>
    public Project CreateProject()
    {
        var settings = Current;
        return new Project(settings.Timing, settings.Frame, settings.Output, SortMode.CaptureTime);
    }

    /// <summary>
    /// Starts following the shell's current project, and each project that replaces it. Call it once the window
    /// is built and the start-up switches have been applied, so only the user's changes are remembered.
    /// </summary>
    public void Attach(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        shell.ProjectReplaced += OnProjectReplaced;
        Follow(shell.CurrentProject.Model);
    }

    /// <summary>
    /// <i>Reset to defaults</i>: forgets everything remembered (the folders too), writes that at once, and sets the
    /// current project's timing, frame shape and output to the defaults through the project, so the panels follow.
    /// The frames and the project's order are not touched. On the UI thread. Returns whether the file was written;
    /// a failure has been reported already. While an export runs (<see cref="ShellViewModel.Edits"/> closed) nothing
    /// changes and it returns false.
    /// </summary>
    public bool ResetToDefaults()
    {
        // While an export holds the project (#24) nothing is reset: the export says why (its EditsWait InfoBar), and the
        // remembered file and the open project stay as they are together.
        if (_shell is { } shell && !shell.Edits.Allow())
        {
            return false;
        }

        var project = _shell?.CurrentProject.Model;
        lock (_gate)
        {
            _current = UserSettings.Default;
            _pending = true;
        }

        if (project is { IsClosed: false })
        {
            project.Timing = TimingSettings.Default;
            project.FrameSettings = FrameSettings.Default;
            project.Output = OutputSettings.Default;
        }

        // The project's own change events saw the defaults arrive; write once now, since the user asked for it.
        return WriteBounded(final: false);
    }

    /// <summary>Writes any change not yet written, now, and waits for it (at most <see cref="FinalWriteLimit"/>). For closing the window. Never throws.</summary>
    public void Flush() => WriteBounded(final: true);

    /// <summary>Writes what is pending, stops following the project, and stops the timer.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (_shell is { } shell)
        {
            shell.ProjectReplaced -= OnProjectReplaced;
        }

        Follow(null);
        _timer.Dispose();
        WriteBounded(final: true);
    }

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e) => Follow(e.Current.Model);

    private void Follow(Project? project)
    {
        if (_attached is { } old)
        {
            old.Changed -= OnProjectChanged;
        }

        _attached = project;
        if (project is not null)
        {
            project.Changed += OnProjectChanged;
        }
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        const ProjectChanges Remembered = ProjectChanges.Timing | ProjectChanges.FrameSettings | ProjectChanges.Output;
        if ((e.Changes & Remembered) == 0)
        {
            return;
        }

        var next = Current;
        if (e.Changes.HasFlag(ProjectChanges.Timing))
        {
            next = next with { Timing = e.Snapshot.Timing };
        }

        if (e.Changes.HasFlag(ProjectChanges.FrameSettings))
        {
            next = next with { Frame = e.Snapshot.FrameSettings };
        }

        if (e.Changes.HasFlag(ProjectChanges.Output))
        {
            // The preference as the user picked it. The effective output (what this PC can encode) is never stored.
            next = next with { Output = e.Snapshot.Output };
        }

        Update(next);
    }

    private void Update(UserSettings next)
    {
        lock (_gate)
        {
            if (_disposed || next == _current)
            {
                return;
            }

            _current = next;
            _pending = true;
        }

        // Restarts the wait: a burst of changes (a stepper held down) is one write after the last of them.
        _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
    }

    // Runs on a thread-pool thread. A throwing timer callback would end the process, so nothing escapes.
    private void OnTimer(object? state)
    {
        try
        {
            Write(final: false);
        }
        catch (Exception exception)
        {
            _log.Error(ProjectText.SettingsSaveFailed, exception);
        }
    }

    // The write the user is waiting for (closing, Reset): a stalled disk must not hang the window, so it waits for
    // the write a couple of seconds at most. On a timeout the write carries on in the background; what it could not
    // finish stays pending. Returns whether the settings are on disk.
    private bool WriteBounded(bool final)
    {
        try
        {
            var write = Task.Run(() => Write(final));
            if (write.Wait(_finalWriteLimit))
            {
                return write.Result;
            }

            _log.Warning(ProjectText.SettingsSaveTimedOut);
            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log.Error(ProjectText.SettingsSaveFailed, exception);
            return false;
        }
    }

    // Writes the pending settings. A failure leaves them pending, so the next change tries again. The first failure
    // is shown to the user; while it keeps failing, the repeats go to the log only. Closing cannot show an InfoBar.
    // Returns false only when the write failed (nothing pending counts as written).
    private bool Write(bool final)
    {
        // One write at a time, in the order of the snapshots: the timer's thread and the UI thread (reset, closing).
        lock (_saveGate)
        {
            UserSettings snapshot;
            lock (_gate)
            {
                if (!_pending)
                {
                    return true;
                }

                snapshot = _current;
                _pending = false;
            }

            try
            {
                _store.Save(snapshot);
                _failureShown = false;
                return true;
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _pending = true;
                }

                if (_failureShown || final)
                {
                    _errors.Report(exception, ProjectText.SettingsSaveFailed);
                }
                else
                {
                    _failureShown = true;
                    _errors.Show(exception, ProjectText.SettingsSaveFailed);
                }

                return false;
            }
        }
    }
}
