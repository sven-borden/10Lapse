using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Filmstrip;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;
using TenLapse.Tests.Import;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// An exporter that writes nothing: each export waits until the test finishes, fails or (through its token) cancels it,
/// and the test reports progress through what it was given. Clean-up waits for the test too.
/// </summary>
internal sealed class FakeVideoExporter : IVideoExporter
{
    private readonly Lock _gate = new();
    private readonly List<Task> _cleanUps = [];
    private TaskCompletionSource<ExportedVideo>? _pending;

    public List<(ProjectSnapshot Snapshot, string Path)> Calls { get; } = [];

    public IProgress<ExportStatus>? Progress { get; private set; }

    /// <summary>
    /// The clean-up of a cancelled or failed export: <see cref="CleanUpAsync"/> returns it, and its result says whether the
    /// unfinished file is gone. Like Media's <c>ExportCleanup</c>, it is registered only as the export ends, so
    /// <see cref="WhenAllCleanedUp"/> asked before that does not wait for it.
    /// </summary>
    public TaskCompletionSource<bool> CleanUp { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Another clean-up <see cref="WhenAllCleanedUp"/> waits for (an earlier export's, say).</summary>
    public Task AllCleanedUp { get; set; } = Task.CompletedTask;

    public List<string> CleanUpAsked { get; } = [];

    public Task<ExportedVideo> ExportAsync(ProjectSnapshot snapshot, string path, IProgress<ExportStatus> progress, CancellationToken cancellationToken)
    {
        Calls.Add((snapshot, path));
        Progress = progress;
        _pending = new TaskCompletionSource<ExportedVideo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = _pending;
        cancellationToken.Register(() =>
        {
            // Media's exporter ends a cancelled export later, on another thread: after what the canceller does in the
            // same step.
            if (EndOn is { } ui)
            {
                _ = ui.InvokeAsync(() => EndCancelled(pending, cancellationToken));
            }
            else
            {
                EndCancelled(pending, cancellationToken);
            }
        });
        return pending.Task;
    }

    /// <summary>Where a cancelled export ends, queued behind the cancelling step; at once when null.</summary>
    public IUiDispatcher? EndOn { get; set; }

    public void Report(ExportStatus status) => Progress!.Report(status);

    public void Finish(ExportedVideo video) => _pending!.SetResult(video);

    public void Fail(Exception exception)
    {
        TrackCleanUp();
        _pending!.SetException(exception);
    }

    public Task<bool> CleanUpAsync(string path)
    {
        CleanUpAsked.Add(path);
        return CleanUp.Task;
    }

    // A snapshot, as ExportCleanup.WhenAllDone takes one: clean-ups registered later are not in it.
    public Task WhenAllCleanedUp()
    {
        lock (_gate)
        {
            return Task.WhenAll([AllCleanedUp, .. _cleanUps]);
        }
    }

    private bool EndCancelled(TaskCompletionSource<ExportedVideo> pending, CancellationToken cancellationToken)
    {
        TrackCleanUp();
        return pending.TrySetCanceled(cancellationToken);
    }

    private void TrackCleanUp()
    {
        lock (_gate)
        {
            _cleanUps.Add(CleanUp.Task);
        }
    }
}

/// <summary>The Save dialog's answer, and what it was asked.</summary>
internal sealed class FakeSavePicker : ISaveVideoPicker
{
    public string? Answer { get; set; } = @"C:\Videos\10Lapse\Harbor sunset.mp4";

    public List<(string Name, string? Folder)> Asked { get; } = [];

    /// <summary>Runs while the dialog is open, before it answers (the user doing something else meanwhile).</summary>
    public Action? WhileOpen { get; set; }

    public List<string> Discarded { get; } = [];

    public Task<string?> PickVideoAsync(string suggestedName, string? startFolder)
    {
        Asked.Add((suggestedName, startFolder));
        WhileOpen?.Invoke();
        return Task.FromResult(Answer);
    }

    public void Discard(string path) => Discarded.Add(path);
}

internal sealed class FakeExportFolders : IExportFolderMemory
{
    public string? ExportFolder { get; set; }
}

internal sealed class FakeReveal : IFileReveal
{
    public List<string> Shown { get; } = [];

    public void ShowInFolder(string path) => Shown.Add(path);
}

/// <summary>Records the taskbar and notification calls, as "progress 0.50", "clear", "failed", "notify: ...".</summary>
internal sealed class FakeExportWindow : IExportWindow
{
    public List<string> Calls { get; } = [];

    public void ShowProgress(double? fraction) => Calls.Add(fraction is { } value ? FormattableString.Invariant($"progress {value:0.00}") : "progress ?");

    public void ClearProgress() => Calls.Add("clear");

    public void ShowFailed() => Calls.Add("failed");

    public void NotifyIfInBackground(string message) => Calls.Add("notify: " + message);
}

/// <summary>Counts the power requests an export takes and lets go of; it can be told to throw.</summary>
internal sealed class FakeKeepAwake : IKeepAwake
{
    private int _held;
    private int _disposeCalls;

    /// <summary>The reasons asked for, one per export.</summary>
    public List<string> Reasons { get; } = [];

    /// <summary>Leases held now.</summary>
    public int Held => Volatile.Read(ref _held);

    /// <summary>Calls to dispose a lease, repeats included: an export disposes its lease once.</summary>
    public int DisposeCalls => Volatile.Read(ref _disposeCalls);

    /// <summary>Makes <see cref="Hold"/> throw, as a stand-in that breaks its promise would.</summary>
    public bool Throws { get; set; }

    public IDisposable Hold(string reason)
    {
        if (Throws)
        {
            throw new InvalidOperationException("No power request today.");
        }

        Reasons.Add(reason);
        Interlocked.Increment(ref _held);
        return new Lease(this);
    }

    private sealed class Lease(FakeKeepAwake owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            Interlocked.Increment(ref owner._disposeCalls);
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner._held);
            }
        }
    }
}

/// <summary>
/// An export view model on a stand-in UI thread (as in the app, the project and every view model live on one thread and
/// progress posts back to it), over a project of photos, a PC that exports everything, and the fakes above.
/// </summary>
internal sealed class ExportHarness : IDisposable
{
    private long _nowTicks;

    public ExportHarness(int frames = 120, IOutputCapabilities? capabilities = null, Action<Project>? setup = null, Func<ShellViewModel, TenLapse.Presentation.Look.ExposureAnalysis>? measuring = null)
    {
        Exporter.EndOn = Ui;
        Ui.Run(() =>
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Shell = new ShellViewModel(Dialogs, Errors);
            Shell.CurrentProject.Model.Name = "Harbor sunset";
            if (frames > 0)
            {
                Shell.CurrentProject.Model.AddFrames(TestFrames.Folder("harbor", frames));
            }

            setup?.Invoke(Shell.CurrentProject.Model);
            Player = new PlayerViewModel(Shell, Errors, () => 0, 1000);
            Strip = new FilmstripViewModel(Shell, Player, Errors);
            Output = new OutputPanelViewModel(Shell, Errors);
            if (capabilities is not null)
            {
                Output.SetCapabilities(capabilities);
            }

            Export = new ExportViewModel(
                Shell,
                Output,
                Player,
                Strip,
                () => Importing,
                new ExportServices(Exporter, Picker, Folders, Dialogs, InfoBars, Reveal, Window, KeepAwake),
                Errors,
                () => TimeSpan.FromTicks(Interlocked.Read(ref _nowTicks)))
            {
                Measuring = measuring?.Invoke(Shell),
            };
            Export.Announced += (_, message) => Announced.Add(message);
            Export.Started += (_, _) => Lifecycle.Add("started");
            Export.Ended += (_, e) => Lifecycle.Add("ended " + e.Outcome);
            Export.PremiereRequested += (_, video) =>
            {
                Premieres.Add(video.Path);
                PremieredVideos.Add(video);
            };
            return true;
        });
    }

    public UiThread Ui { get; } = new();

    public FakeErrorSink Errors { get; } = new();

    public FakeDialogService Dialogs { get; } = new();

    public FakeInfoBarService InfoBars { get; } = new();

    public FakeVideoExporter Exporter { get; } = new();

    public FakeSavePicker Picker { get; } = new();

    public FakeExportFolders Folders { get; } = new();

    public FakeReveal Reveal { get; } = new();

    public FakeExportWindow Window { get; } = new();

    public FakeKeepAwake KeepAwake { get; } = new();

    public bool Importing { get; set; }

    public List<string> Announced { get; } = [];

    public List<string> Lifecycle { get; } = [];

    public List<string> Premieres { get; } = [];

    /// <summary>What each premiere was asked to show, with its format.</summary>
    public List<ExportedVideo> PremieredVideos { get; } = [];

    public ShellViewModel Shell { get; private set; } = null!;

    public PlayerViewModel Player { get; private set; } = null!;

    public FilmstripViewModel Strip { get; private set; } = null!;

    public OutputPanelViewModel Output { get; private set; } = null!;

    public ExportViewModel Export { get; private set; } = null!;

    public Project Project => Shell.CurrentProject.Model;

    /// <summary>The view model's clock.</summary>
    public TimeSpan Now
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _nowTicks));
        set => Interlocked.Exchange(ref _nowTicks, value.Ticks);
    }

    /// <summary>Runs <paramref name="work"/> on the UI thread and waits.</summary>
    public T On<T>(Func<T> work) => Ui.Run(work);

    /// <summary>Runs <paramref name="work"/> on the UI thread and waits.</summary>
    public void On(Action work) => Ui.Run(() =>
    {
        work();
        return true;
    });

    /// <summary>Starts Export video (the Save dialog answers at once) and lets the UI thread settle.</summary>
    public void StartExport()
    {
        On(() => Export.ExportCommand.Execute(null));
        Settle();
    }

    /// <summary>Lets the UI thread run what was posted to it, and what that posted in turn.</summary>
    public void Settle()
    {
        for (var i = 0; i < 4; i++)
        {
            On(() => { });
            Thread.Sleep(5);
        }
    }

    /// <summary>A report from the exporter's worker thread, shown once the UI thread has run it.</summary>
    public void Report(ExportPhase phase, int frame, double? speed = null, TimeSpan? left = null)
    {
        Exporter.Report(new ExportStatus(phase, frame, On(() => Project.Frames.Length), speed, left));

        // Progress<T> posted it straight to the UI thread: one round trip later it has run.
        On(() => { });
    }

    public ExportedVideo Video(string? path = null, long bytes = 26L << 20) => new(
        path ?? Picker.Answer!,
        Project.Frames.Length,
        FrameTiming.Duration(Project.Frames.Length, new Fraction(30, 1)),
        new PixelSize(1920, 1080),
        new Fraction(30, 1),
        VideoFormat.Mp4H264,
        bytes);

    public void Dispose()
    {
        Assert.Empty(Ui.Failures);
        Ui.Dispose();
    }
}
