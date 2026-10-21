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
/// dismissal is not remembered: it is not a project setting either (#52), so it lives and dies with the open project.
/// The recent project files (#52) are kept here too, with the app's settings.
/// <para>
/// Two 10Lapse windows (#52: a project opened while 10Lapse runs starts a second one) share the file, so a write never
/// puts back what this process loaded: it rereads the file under a lock every process takes
/// (<see cref="ISettingsStore.Update"/>) and writes only the fields this process changed since its last read or write
/// (<see cref="Merge"/>); the export count adds this process's exports to the stored count, and the recent list replays
/// this process's own steps (open, remove, clear) onto the stored list, so the most recent use in either window comes first.
/// </para>
/// </remarks>
public sealed class RememberedSettings : IImportFolderMemory, Export.IExportFolderMemory, Rating.IRatingMemory, ProjectFiles.IRecentProjects, IDisposable
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
    private readonly List<Func<PathList, PathList>> _recentSteps = [];
    private UserSettings _current;

    // What the file held at this process's last read or write: a field that still equals it was not changed here.
    private UserSettings _baseline;

    // _recentSteps: this process's steps on the recent list since its last write, replayed onto the stored list.
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
        _baseline = loaded.Settings;
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

    /// <summary>The app version the rating prompt was last shown in (#31), or null. Setting it saves it.</summary>
    public string? RatingAskedVersion
    {
        get => Current.RatingAskedVersion;
        set => Update(Current with { RatingAskedVersion = value });
    }

    /// <summary>How many exports have succeeded on this PC (#31).</summary>
    public int SuccessfulExports => Current.SuccessfulExports;

    /// <summary>The project files opened or saved last (#52), the most recent first.</summary>
    public PathList RecentProjects => Current.RecentProjects;

    /// <summary>Raised on the calling thread (the UI thread) when <see cref="RecentProjects"/> changed.</summary>
    public event EventHandler? RecentProjectsChanged;

    /// <summary>Puts <paramref name="path"/> first in <see cref="RecentProjects"/> (#52: it was opened or saved) and saves the list after the pause.</summary>
    public void AddRecentProject(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        UpdateRecent(list => list.WithFirst(path, UserSettings.MaxRecentProjects));
    }

    /// <summary>Takes <paramref name="path"/> off <see cref="RecentProjects"/> (it is gone) and saves the list after the pause.</summary>
    public void RemoveRecentProject(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        UpdateRecent(list => list.Without(path));
    }

    /// <summary>Empties <see cref="RecentProjects"/> (the first-launch screen's <i>Clear list</i>) and saves that after the pause.</summary>
    public void ClearRecentProjects() => UpdateRecent(_ => PathList.Empty);

    /// <summary>
    /// Reads the stored list again (another 10Lapse window may have opened or saved a project since) and, with this
    /// process's unwritten steps on top, raises <see cref="RecentProjectsChanged"/> when it differs. The read runs off
    /// the UI thread; call it on the UI thread. A file that cannot be read changes nothing.
    /// </summary>
    public async Task RefreshRecentProjectsAsync()
    {
        var loaded = await Task.Run(_store.Load);
        if (loaded.Problems.Count > 0)
        {
            return;
        }

        var stored = loaded.Settings.RecentProjects;
        lock (_gate)
        {
            var next = _recentSteps.Aggregate(stored, (list, step) => step(list));
            if (_disposed || next.Equals(_current.RecentProjects))
            {
                return;
            }

            _current = _current with { RecentProjects = next };
        }

        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    // A step is written even when it changes nothing here: P already first in this window can be behind Q in the file,
    // which another window opened since, and opening P again here makes it the most recent there too.
    private void UpdateRecent(Func<PathList, PathList> step)
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var next = step(_current.RecentProjects);
            changed = !next.Equals(_current.RecentProjects);
            _recentSteps.Add(step);
            _current = _current with { RecentProjects = next };
            _pending = true;
        }

        _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        if (changed)
        {
            RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Counts one successful export (#31), saves the count after the pause, and returns it. Stops counting at <see cref="int.MaxValue"/>.</summary>
    public int CountSuccessfulExport()
    {
        var next = Current;
        next = next with { SuccessfulExports = next.SuccessfulExports == int.MaxValue ? int.MaxValue : next.SuccessfulExports + 1 };
        Update(next);
        return next.SuccessfulExports;
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
    /// The frames and the project's order are not touched. The rating prompt's count and last version are not preferences
    /// and stay (#31), so a reset cannot bring the prompt back. On the UI thread. Returns whether the file was written;
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
            _current = UserSettings.Default with
            {
                SuccessfulExports = _current.SuccessfulExports,
                RatingAskedVersion = _current.RatingAskedVersion,

                // History, not a preference (#52): the first-launch screen clears it on its own.
                RecentProjects = _current.RecentProjects,
            };
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

    /// <summary>
    /// <paramref name="stored"/> with this process's changes on top: each field <paramref name="mine"/> changed since
    /// <paramref name="baseline"/> (the last read or write) is <paramref name="mine"/>'s, every other the stored one; the
    /// export count adds this process's exports; the recent list is the stored one with <paramref name="steps"/> replayed.
    /// A new field of <see cref="UserSettings"/> needs a line here (<c>RememberedSettingsTests.EveryFieldIsMerged</c> fails until it has one).
    /// </summary>
    internal static UserSettings Merge(UserSettings stored, UserSettings baseline, UserSettings mine, IEnumerable<Func<PathList, PathList>> steps)
    {
        var exports = (long)stored.SuccessfulExports + mine.SuccessfulExports - baseline.SuccessfulExports;
        return new UserSettings
        {
            Timing = Pick(stored.Timing, baseline.Timing, mine.Timing),
            Frame = Pick(stored.Frame, baseline.Frame, mine.Frame),
            Output = Pick(stored.Output, baseline.Output, mine.Output),
            ImportFolder = Pick(stored.ImportFolder, baseline.ImportFolder, mine.ImportFolder),
            ExportFolder = Pick(stored.ExportFolder, baseline.ExportFolder, mine.ExportFolder),
            RatingAskedVersion = Pick(stored.RatingAskedVersion, baseline.RatingAskedVersion, mine.RatingAskedVersion),
            SuccessfulExports = (int)Math.Clamp(exports, 0, int.MaxValue),
            RecentProjects = steps.Aggregate(stored.RecentProjects, (list, step) => step(list)),
        };

        static T Pick<T>(T stored, T baseline, T mine) => EqualityComparer<T>.Default.Equals(mine, baseline) ? stored : mine;
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
            UserSettings baseline;
            Func<PathList, PathList>[] steps;
            lock (_gate)
            {
                if (!_pending)
                {
                    return true;
                }

                snapshot = _current;
                baseline = _baseline;
                steps = [.. _recentSteps];
                _recentSteps.Clear();
                _pending = false;
            }

            try
            {
                // This process's changes onto what is stored now, under the lock the other windows take too.
                var written = _store.Update(stored => Merge(stored, baseline, snapshot, steps));
                lock (_gate)
                {
                    // The other windows' changes come in too, except where this process changed a field since the snapshot.
                    _current = Merge(written, snapshot, _current, _recentSteps);
                    _baseline = written;
                }

                _failureShown = false;
                return true;
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _recentSteps.InsertRange(0, steps);
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
