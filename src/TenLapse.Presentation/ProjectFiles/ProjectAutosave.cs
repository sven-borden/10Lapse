using TenLapse.Core.Diagnostics;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.ProjectFiles;

/// <summary>
/// The crash safety net (#52): a moment after the last change to a project with photos and unsaved changes, a recovery copy
/// of it goes to the app's own folder (<see cref="IRecoveryStore"/>), never to the user's file, which only Save writes.
/// When the project is saved, exported without a file, emptied or replaced, the copy goes. Closing the window clears it
/// (<see cref="Dispose"/>), so only a 10Lapse that ended without closing leaves one, for the next launch to offer
/// (<see cref="ProjectFilesViewModel.OfferRecoveryAsync"/>).
/// </summary>
/// <remarks>
/// It follows the shell's project on the UI thread and writes from a timer on a pool thread, reading the project's
/// immutable snapshot, so a large project never holds the UI. The first failed write is shown (crash recovery is not
/// working); repeats go to the log only.
/// <para>
/// The pause restarts with every change, but never past <see cref="DefaultMaxWait"/> from the first change not yet
/// copied: a stream of changes (deflicker's measurements land every 250 ms for minutes, #45) still gets a copy at least
/// that often.
/// </para>
/// </remarks>
public sealed class ProjectAutosave : IDisposable
{
    /// <summary>How long after the last change the copy is written.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(2);

    /// <summary>The longest a change waits for its copy while more changes keep coming: 10 s from the first one.</summary>
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(10);

    private readonly ShellViewModel _shell;
    private readonly IRecoveryStore _store;
    private readonly IErrorPresenter _errors;
    private readonly ITimer _timer;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _maxWait;
    private readonly TimeProvider _time;
    private readonly Func<Project, ProjectSnapshot, ProjectFileContent> _content;
    private readonly Lock _gate = new();
    private volatile Project? _project;
    private bool _failureShown;
    private bool _disposed;
    private bool _keep;

    // UTC ticks of the first change not yet copied; 0 when none waits. Set on the UI thread, cleared by the write.
    private long _pendingSince;

    /// <param name="shell">The window's project, followed from <see cref="Attach"/>.</param>
    /// <param name="store">Where the copy goes.</param>
    /// <param name="errors">Takes a failed write: the first is shown, repeats are logged.</param>
    /// <param name="time">The clock the pause runs on; tests pass a fake.</param>
    /// <param name="debounce">The pause after the last change; <see cref="DefaultDebounce"/> when null.</param>
    /// <param name="maxWait">The longest a change waits while more keep coming; <see cref="DefaultMaxWait"/> when null.</param>
    /// <param name="content">
    /// What the copy of a project's snapshot holds (<see cref="ProjectFilesViewModel.RecoveryContent"/>: with the photos its
    /// file could not find, so a restored project still writes them back); <see cref="ProjectFileContent.Recovery"/> when
    /// null. Called on the timer's thread.
    /// </param>
    public ProjectAutosave(ShellViewModel shell, IRecoveryStore store, IErrorPresenter errors, TimeProvider? time = null, TimeSpan? debounce = null, Func<Project, ProjectSnapshot, ProjectFileContent>? content = null, TimeSpan? maxWait = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(errors);
        _shell = shell;
        _store = store;
        _errors = errors;
        _debounce = debounce ?? DefaultDebounce;
        _maxWait = maxWait ?? DefaultMaxWait;
        _content = content ?? ((_, snapshot) => ProjectFileContent.Recovery(snapshot));
        _time = time ?? TimeProvider.System;
        _timer = _time.CreateTimer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Starts following the current project and each one that replaces it. On the UI thread.</summary>
    public void Attach()
    {
        _shell.ProjectReplaced += OnProjectReplaced;
        Follow(_shell.CurrentProject.Model);
    }

    /// <summary>Writes (or clears) the copy now, on the calling thread: what the timer does. For tests.</summary>
    internal void WriteNow() => Write();

    /// <summary>
    /// The window closes without the question about unsaved changes (it failed): the copy is written now, and kept when
    /// the window closes, so the next launch offers it. On the UI thread.
    /// </summary>
    public void KeepForNextLaunch()
    {
        Write();
        Volatile.Write(ref _keep, true);
    }

    /// <summary>A clean close: stops following, and the copy goes (and with it the slot's lock); kept after <see cref="KeepForNextLaunch"/>.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Volatile.Write(ref _disposed, true);
        }

        _shell.ProjectReplaced -= OnProjectReplaced;
        Follow(null);
        _timer.Dispose();
        _errors.RunLogged(Volatile.Read(ref _keep) ? _store.Leave : _store.Dispose, ProjectFileText.AutosaveFailedContext);
    }

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e)
    {
        Follow(e.Current.Model);
        Schedule();
    }

    private void Follow(Project? project)
    {
        if (_project is { } old)
        {
            old.Changed -= OnProjectChanged;
        }

        _project = project;
        if (project is not null)
        {
            project.Changed += OnProjectChanged;
        }
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e) => Schedule();

    // Restarts the pause: a burst of changes (an import's batches, a stepper held down) is one write after the last, but
    // no later than the maximum wait after the first change not yet copied. Never waits for a write in progress (that
    // holds _gate on the timer's thread).
    private void Schedule()
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        var now = _time.GetUtcNow().UtcTicks;
        var since = Interlocked.CompareExchange(ref _pendingSince, now, 0);
        var left = since == 0 ? _maxWait : _maxWait - TimeSpan.FromTicks(now - since);
        var due = left < _debounce ? (left > TimeSpan.Zero ? left : TimeSpan.Zero) : _debounce;
        _timer.Change(due, Timeout.InfiniteTimeSpan);
    }

    // A pool thread. A throwing timer callback would end the process, so nothing escapes.
    private void OnTimer(object? state)
    {
        try
        {
            Write();
        }
        catch (Exception exception)
        {
            _errors.Report(exception, ProjectFileText.AutosaveFailedContext);
        }
    }

    private void Write()
    {
        lock (_gate)
        {
            // What this write copies; a change from here on waits for the next one.
            Interlocked.Exchange(ref _pendingSince, 0);
            if (_disposed || _project is not { } project)
            {
                return;
            }

            var snapshot = project.Snapshot;
            if (!snapshot.IsDirty || snapshot.Frames.Length == 0)
            {
                // Nothing to lose: saved, exported without a file, or empty.
                _store.Clear();
                return;
            }

            try
            {
                _store.Save(ProjectFileJson.Write(_content(project, snapshot)));
                _failureShown = false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (_failureShown)
                {
                    _errors.Report(exception, ProjectFileText.AutosaveFailedContext);
                }
                else
                {
                    _failureShown = true;
                    _errors.Show(exception, ProjectFileText.AutosaveFailedContext);
                }
            }
        }
    }
}
