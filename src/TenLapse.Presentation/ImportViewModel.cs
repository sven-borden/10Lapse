using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Import;
using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// Add photos and drag and drop (#12): picks or takes the files and folders, adds them to the current project (or
/// a new one after a drop), and shows the progress. Built for 5,000+ photos: the work runs off the UI thread in two
/// phases (<see cref="IPhotoImportSource"/>), and every change to the project is one batch on the UI thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threads.</b> Construct it, run its commands and call <see cref="DropAsync"/> on the UI thread. An import
/// captures its target <see cref="Project"/> when it starts and changes it only through <see cref="IUiDispatcher"/>
/// (the project is single-writer); its own steps run on the thread pool. It never reads
/// <see cref="ShellViewModel.CurrentProject"/> again, so a late batch can never land in a project started meanwhile.
/// </para>
/// <para>
/// <b>Lifetime.</b> Each import registers itself with its project (<see cref="ProjectViewModel.AddResource"/>):
/// <c>New project</c> disposes the old project, which cancels the import, and a batch that still arrives meets a
/// closed project (<see cref="ObjectDisposedException"/>), which counts as cancelled. Imports run one at a time, in
/// the order they were started.
/// </para>
/// <para>
/// <b>Phases.</b> Listing adds frames with their file facts only, so the filmstrip fills at once (the first batch
/// within a fraction of a second). Reading fills in pixel size and capture time with one <see cref="Project.UpdateFrames"/>
/// per batch, and in the same batch removes the files that cannot be used and lists them as skipped. Then the
/// frames are sorted: <see cref="ProjectOrdering.ApplyDefaultSort"/> when the project was empty before and the user
/// changed neither its sort mode nor its order meanwhile, otherwise
/// <see cref="ProjectOrdering.Resort"/>. At the end one InfoBar lists the skipped files, if any.
/// </para>
/// </remarks>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly IPhotoImportSource _source;
    private readonly IPhotoPicker _picker;
    private readonly IImportFolderMemory _folders;
    private readonly IUiDispatcher _ui;
    private readonly IDialogService _dialogs;
    private readonly IInfoBarService _infoBars;
    private readonly IErrorPresenter _errors;
    private readonly ICodecExtensions _codecs;
    private readonly CodecExtensionPrompts _extensionPrompts;

    // One import at a time: a second waits for its turn (the progress then shows it).
    private readonly SemaphoreSlim _turn = new(1, 1);

    private readonly Lock _gate = new();
    private readonly List<ImportRun> _runs = [];

    // UI thread only: true while an import sorts its project (its Reset is not the user's reorder).
    private bool _sorting;

    /// <param name="shell">The shell: the current project, and a new one when a drop asks for it.</param>
    /// <param name="source">Lists and reads the photos (the App passes Media's <c>PhotoImporter</c>).</param>
    /// <param name="picker">The folder and file pickers.</param>
    /// <param name="folders">Where the pickers open; #21 passes one that lasts across launches.</param>
    /// <param name="ui">Runs the project changes on the UI thread.</param>
    /// <param name="dialogs">Asks what a drop onto a project with photos should do.</param>
    /// <param name="infoBars">Says what was skipped, found or stopped.</param>
    /// <param name="codecs">Knows which photo extensions this PC has and opens their Store pages (#14).</param>
    /// <param name="errors">The error path for the commands and the imports.</param>
    public ImportViewModel(
        ShellViewModel shell,
        IPhotoImportSource source,
        IPhotoPicker picker,
        IImportFolderMemory folders,
        IUiDispatcher ui,
        IDialogService dialogs,
        IInfoBarService infoBars,
        ICodecExtensions codecs,
        IErrorPresenter errors)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentNullException.ThrowIfNull(codecs);
        ArgumentNullException.ThrowIfNull(errors);
        _shell = shell;
        _source = source;
        _picker = picker;
        _folders = folders;
        _ui = ui;
        _dialogs = dialogs;
        _infoBars = infoBars;
        _errors = errors;
        _codecs = codecs;
        _extensionPrompts = new CodecExtensionPrompts(codecs, infoBars, errors);
        ProgressText = string.Empty;
        ChooseFolderCommand = errors.AsyncCommand(ChooseFolderAsync, ProjectText.ChooseFolderFailed);
        ChoosePhotosCommand = errors.AsyncCommand(ChoosePhotosAsync, ProjectText.ChoosePhotosFailed);
        CancelCommand = errors.Command(CancelAll, ProjectText.CancelImportFailed);
    }

    /// <summary><c>Choose folder…</c>: picks a folder and adds its photos to the current project.</summary>
    public IAsyncRelayCommand ChooseFolderCommand { get; }

    /// <summary><c>Choose photos…</c>: picks photos and adds them to the current project.</summary>
    public IAsyncRelayCommand ChoosePhotosCommand { get; }

    /// <summary>Stops every running import. The photos already read stay; the ones still unread leave the project.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary><c>Include subfolders</c>: folders picked or dropped are searched with their subfolders. Off by default.</summary>
    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; }

    /// <summary>True while an import runs (the progress shows).</summary>
    [ObservableProperty]
    public partial bool IsImporting { get; private set; }

    /// <summary>"Found 1,240 photos…", then "Adding 1,240 of 5,000 photos…".</summary>
    [ObservableProperty]
    public partial string ProgressText { get; private set; }

    /// <summary>True while the photos are being listed and the total is not known yet.</summary>
    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; private set; }

    /// <summary>How far the reading is, 0 to 100.</summary>
    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    /// <summary>True when this file's extension is a photo format this PC reads (the drag caption counts with it).</summary>
    public bool IsPhotoFile(string path) => _source.IsPhotoFile(path);

    /// <summary>
    /// Adds the photos of <paramref name="paths"/> (files and folders) to the current project. On the UI thread. The
    /// task completes when the import has finished; it never throws (failures go to the error path).
    /// </summary>
    public Task ImportAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Start(_shell.CurrentProject, paths);
    }

    /// <summary>
    /// A drop onto the window: on a project with photos it first asks whether to add to it or start a new project
    /// with only the dropped photos (Cancel does neither); on an empty project it just imports. Items that are
    /// neither folders nor photo files are left out; when nothing is left, an InfoBar says so. On the UI thread.
    /// Completes once the import has started; <see cref="WhenIdleAsync"/> waits for it to finish.
    /// </summary>
    public async Task DropAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // Off the UI thread: a folder on a sleeping network drive can take seconds to answer.
        var usable = await Task.Run(() => paths.Where(IsUsable).ToList());
        if (usable.Count == 0)
        {
            _infoBars.Show(InfoBarSeverity.Caution, ProjectText.ImportNothingFound);
            return;
        }

        var current = _shell.CurrentProject;
        if (current.HasFrames)
        {
            switch (await _dialogs.ChooseAsync(ProjectText.DropChoice(current.FrameCount)))
            {
                case ChoiceResult.Primary:
                    break;
                case ChoiceResult.Secondary:
                    if (!_shell.ReplaceWithoutAsking())
                    {
                        return;
                    }

                    break;
                default:
                    return;
            }
        }

        _ = Start(_shell.CurrentProject, usable);
    }

    /// <summary>Completes when every import started so far has finished (tests, and a clean shutdown).</summary>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return Task.WhenAll(_runs.Select(run => run.Completion));
        }
    }

    private bool IsUsable(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && (Directory.Exists(path) || (_source.IsPhotoFile(path) && File.Exists(path)));

    private async Task ChooseFolderAsync()
    {
        var folder = await _picker.PickFolderAsync(_folders.LastFolder);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        // Next time the picker opens beside this folder, where the next shoot usually is.
        _folders.LastFolder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder)) ?? folder;
        _ = Start(_shell.CurrentProject, [folder]);
    }

    private async Task ChoosePhotosAsync()
    {
        var files = await _picker.PickPhotosAsync(_folders.LastFolder);
        if (files.Count == 0)
        {
            return;
        }

        _folders.LastFolder = Path.GetDirectoryName(files[0]) ?? _folders.LastFolder;
        _ = Start(_shell.CurrentProject, files);
    }

    private void CancelAll()
    {
        ImportRun[] runs;
        lock (_gate)
        {
            runs = [.. _runs];
        }

        foreach (var run in runs)
        {
            run.Cancel();
        }
    }

    // Registers the run with its project, then runs it through the error path: a cancellation by its own token (Cancel,
    // or the project closing) shows nothing, anything else shows an InfoBar.
    private Task Start(ProjectViewModel target, IReadOnlyList<string> paths)
    {
        var run = new ImportRun(target, new ImportRequest([.. paths], IncludeSubfolders), OnRunCancelled);
        target.AddResource(run);
        lock (_gate)
        {
            _runs.Add(run);
        }

        run.Completion = FinishAsync(run, _errors.GuardCancellableAsync(() => RunAsync(run), ProjectText.ImportFailed, run.Token));
        return run.Completion;
    }

    private async Task FinishAsync(ImportRun run, Task guarded)
    {
        await guarded.ConfigureAwait(false);
        lock (_gate)
        {
            _runs.Remove(run);
        }

        // The project lets go of the finished run (and the paths it holds) instead of keeping it until New project.
        run.Owner.RemoveResource(run);
    }

    private async Task RunAsync(ImportRun run)
    {
        // Everything below runs on the thread pool; the project and the progress change only inside _ui calls.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        var token = run.Token;
        await _turn.WaitAsync(token).ConfigureAwait(false);

        // A cancelled run gives up its turn at once: the next import starts while this one only tidies up (one UI step).
        var released = 0;
        void Release()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                _turn.Release();
            }
        }

        var registration = token.Register(Release);
        try
        {
            // The user may have installed an extension since the last import: ask Windows again, never reuse an answer.
            _codecs.Refresh();
            await ImportAsync(run).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (run.Target.IsClosed)
        {
            // The user started a new project: the import is over, nothing to say.
            run.Cancel();
            throw new OperationCanceledException(ProjectText.ImportFailed, token);
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            Release();
            await EndProgressAsync(run).ConfigureAwait(false);
        }
    }

    // Cancel (or New project) clears the progress at once, unless another import is still going; the run itself ends
    // a moment later. Any thread.
    private void OnRunCancelled(ImportRun run) => _ = EndProgressAsync(run);

    private async Task ImportAsync(ImportRun run)
    {
        var target = run.Target;
        var name = NameFor(run.Request);

        // What the end sort needs from the start: an empty project takes the default sort only if the user has neither
        // picked another order nor dragged frames while the import ran (#15, #20); otherwise its own order is kept.
        var reordered = false;
        void Watch(object? sender, ProjectChangedEventArgs e)
        {
            if (!_sorting && e.Frames?.Kind == FrameChangeKind.Reset)
            {
                reordered = true;
            }
        }

        var (wasEmpty, modeAtStart) = await _ui.InvokeAsync(() =>
        {
            ShowProgress(run, ProjectText.ImportListingProgress(0), indeterminate: true, percent: 0);
            target.Changed += Watch;
            return (target.Frames.Length == 0, target.SortMode);
        }).ConfigureAwait(false);
        try
        {
            await ImportAsync(run, name, wasEmpty, () => wasEmpty && !reordered && target.SortMode == modeAtStart).ConfigureAwait(false);
        }
        finally
        {
            // Removing a handler is safe on any thread.
            target.Changed -= Watch;
        }
    }

    private async Task ImportAsync(ImportRun run, string? name, bool wasEmpty, Func<bool> takesDefaultSort)
    {
        var target = run.Target;
        var request = run.Request;
        var token = run.Token;

        // Phase 1: list. Each batch is added at once; `listed` keeps the frames this import really added (a photo
        // already in the project is left out by AddFrames), in order, for phase 2.
        // `unread` holds the frames added but not read yet: Cancel takes them out again, in either phase.
        var listed = new List<Frame>();
        var unread = new HashSet<Guid>();
        var skipped = new List<SkippedFile>();
        var unopened = new List<UnopenedPath>();
        var cloudOnly = new List<Frame>();
        var declined = 0;
        var found = 0;
        var kept = 0;
        try
        {
            await foreach (var batch in _source.ListAsync(request, token).ConfigureAwait(false))
            {
                found += batch.Frames.Count + batch.CloudOnly.Count;
                var added = await _ui.InvokeAsync(() => AddListed(run, batch, wasEmpty ? name : null, listed.Count)).ConfigureAwait(false);
                listed.AddRange(added);
                unread.UnionWith(added.Select(frame => frame.Id));
                skipped.AddRange(batch.Skipped);
                unopened.AddRange(batch.Unopened);
                cloudOnly.AddRange(batch.CloudOnly);
            }

            // Online-only OneDrive photos: reading one downloads it, so ask once, then add them or list them as skipped.
            if (cloudOnly.Count > 0)
            {
                var pending = await _ui.InvokeAsync(() => NotInProject(target, cloudOnly)).ConfigureAwait(false);
                if (pending.Count > 0 && await AskToDownloadAsync(pending, token).ConfigureAwait(false))
                {
                    var added = await _ui.InvokeAsync(() => AddListed(run, new ImportListBatch(pending, []), wasEmpty ? name : null, listed.Count)).ConfigureAwait(false);
                    listed.AddRange(added);
                    unread.UnionWith(added.Select(frame => frame.Id));
                }
                else if (pending.Count > 0)
                {
                    // Listed in the project, not in the InfoBar: the user has just chosen this.
                    SkippedFile[] left = [.. pending.Select(frame => new SkippedFile(frame.SourcePath, SkipReason.CloudOnly, "Only in OneDrive; not downloaded."))];
                    await _ui.RunAsync(() => target.AddSkippedFiles(left)).ConfigureAwait(false);
                    declined = left.Length;
                }
            }

            // Phase 2: read, then write back once per batch.
            var read = 0;
            await foreach (var readings in _source.ReadAsync(listed, token).ConfigureAwait(false))
            {
                read += readings.Count;
                var progress = read;
                var applied = await _ui.InvokeAsync(() => ApplyReadings(run, readings, progress, listed.Count)).ConfigureAwait(false);
                kept += applied.Kept;
                skipped.AddRange(applied.Skipped);
                foreach (var reading in readings)
                {
                    unread.Remove(reading.Frame.Id);
                }
            }

            await _ui.RunAsync(() => Sort(target, takesDefaultSort())).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !target.IsClosed)
        {
            // Cancel: the photos read so far stay, the unread ones (never checked) leave; then the order is made right.
            await _ui.RunAsync(() =>
            {
                target.RemoveFrames(unread);
                Sort(target, takesDefaultSort());
            }).ConfigureAwait(false);
            ReportSkipped(skipped);
            ReportUnopened(unopened);
            _infoBars.Show(InfoBarSeverity.Neutral, ProjectText.ImportStopped(kept));
            throw;
        }

        ReportSkipped(skipped);
        ReportUnopened(unopened);
        if (found == 0 && skipped.Count == 0 && unopened.Count == 0)
        {
            _infoBars.Show(
                InfoBarSeverity.Caution,
                !request.IncludeSubfolders && request.Paths.Any(Directory.Exists) ? ProjectText.ImportNothingFoundSubfolders : ProjectText.ImportNothingFound);
        }
        else if (found > 0 && listed.Count == 0 && declined == 0)
        {
            _infoBars.Show(InfoBarSeverity.Neutral, ProjectText.ImportAlreadyAdded);
        }
    }

    // UI thread. The online-only photos whose path is not in the project yet (a second import of the same folder asks
    // nothing and skips nothing for them).
    private static List<Frame> NotInProject(Project target, List<Frame> frames)
    {
        var present = new HashSet<string>(target.Frames.Select(frame => frame.SourcePath), StringComparer.OrdinalIgnoreCase);
        return [.. frames.Where(frame => present.Add(frame.SourcePath))];
    }

    // Asks once whether to download the online-only photos (their reported sizes give the total). The default button
    // skips them, so Esc or closing the dialog downloads nothing. A Cancel of the import stops the wait.
    private async Task<bool> AskToDownloadAsync(List<Frame> pending, CancellationToken token)
    {
        var question = ProjectText.CloudDownload(pending.Count, pending.Sum(frame => frame.FileSize));
        var answer = await _ui.InvokeAsync(() => _dialogs.ConfirmAsync(question)).ConfigureAwait(false);
        return await answer.WaitAsync(token).ConfigureAwait(false);
    }

    // UI thread. Adds one listing batch and returns the frames really added (AddFrames appends them, so they are the
    // last ones), naming an empty project after the folder with the first photo.
    private Frame[] AddListed(ImportRun run, ImportListBatch batch, string? name, int listedSoFar)
    {
        var target = run.Target;
        if (batch.Skipped.Count > 0)
        {
            target.AddSkippedFiles(batch.Skipped);
        }

        var wasEmpty = target.Frames.Length == 0;
        var count = target.AddFrames(batch.Frames);
        var frames = target.Frames;
        var added = count == 0 ? [] : frames.AsSpan(frames.Length - count, count).ToArray();
        if (count > 0 && wasEmpty && name is not null && target.Name == Project.DefaultName)
        {
            target.Name = name;
        }

        ShowProgress(run, ProjectText.ImportListingProgress(listedSoFar + count), indeterminate: true, percent: 0);
        return added;
    }

    // UI thread. One reading batch: the frames that cannot be used leave and join the skipped list, the others get
    // their facts, each in one change. A frame the user removed meanwhile (#20) is left alone.
    private Applied ApplyReadings(ImportRun run, IReadOnlyList<FrameReading> readings, int read, int total)
    {
        var target = run.Target;
        var present = new Dictionary<Guid, string>(target.Frames.Length);
        foreach (var frame in target.Frames)
        {
            present[frame.Id] = frame.SourcePath;
        }

        var failed = new List<Guid>();
        var skipped = new List<SkippedFile>();
        var updates = new List<Frame>();
        foreach (var reading in readings)
        {
            if (!present.TryGetValue(reading.Frame.Id, out var path) || !string.Equals(path, reading.Frame.SourcePath, StringComparison.Ordinal))
            {
                continue;
            }

            if (reading.Skipped is { } file)
            {
                failed.Add(reading.Frame.Id);
                skipped.Add(file);
            }
            else
            {
                updates.Add(reading.Frame);
            }
        }

        if (failed.Count > 0)
        {
            // One commit, so no reader sees a frame gone that is not listed as skipped yet.
            target.RemoveFramesAndSkip(failed, skipped);
        }

        if (updates.Count > 0)
        {
            target.UpdateFrames(updates);
        }

        ShowProgress(run, ProjectText.ImportReadingProgress(read, total), indeterminate: false, percent: total == 0 ? 100 : 100.0 * read / total);
        return new Applied(updates.Count, skipped);
    }

    // UI thread. The import's own sorts are marked, so another import's watcher does not take them for the user's drag.
    private void Sort(Project target, bool takesDefaultSort)
    {
        if (target.Frames.Length == 0)
        {
            return;
        }

        _sorting = true;
        try
        {
            if (takesDefaultSort)
            {
                target.ApplyDefaultSort();
            }
            else
            {
                target.Resort();
            }
        }
        finally
        {
            _sorting = false;
        }
    }

    // Photos that need a Store extension get one InfoBar per missing extension (#14); the rest get the one list. The
    // prompts decide once (and ask the PC) which files they speak for, so the list gets exactly the others.
    private void ReportSkipped(List<SkippedFile> skipped)
    {
        var plan = _extensionPrompts.Plan(skipped);
        if (SkippedFilesInfoBar.Create(plan.NotCovered) is { } request)
        {
            _infoBars.Show(request);
        }

        _extensionPrompts.Show(plan);
    }

    // Folders that would not open are not photos: one InfoBar of their own, never the skipped list.
    private void ReportUnopened(List<UnopenedPath> unopened)
    {
        if (unopened.Count == 1)
        {
            _infoBars.Show(InfoBarSeverity.Caution, ProjectText.ImportFolderFailed(unopened[0].Path));
        }
        else if (unopened.Count > 1)
        {
            _infoBars.ShowWithDetails(
                InfoBarSeverity.Caution,
                ProjectText.ImportFoldersFailed(unopened.Count),
                unopened.Select(path => new InfoBarDetail(path.Path)));
        }
    }

    // The name a new project takes from what was imported: the one folder, or the folder all the files are in.
    private static string? NameFor(ImportRequest request)
    {
        if (request.Paths.Count == 1 && Directory.Exists(request.Paths[0]))
        {
            return Project.NameFromFolder(request.Paths[0]);
        }

        var parents = request.Paths.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return parents.Count == 1 && parents[0] is { } parent && !request.Paths.Any(Directory.Exists)
            ? Project.NameFromFolder(parent)
            : null;
    }

    // UI thread. A cancelled run shows nothing more: a batch still in flight must not bring back a stale "Adding…".
    private void ShowProgress(ImportRun run, string text, bool indeterminate, double percent)
    {
        if (run.IsCancelled)
        {
            return;
        }

        ProgressText = text;
        IsProgressIndeterminate = indeterminate;
        ProgressPercent = percent;
        IsImporting = true;
    }

    // Hides the progress unless another import is still going or waiting for its turn (one not cancelled). Never
    // throws: it runs in a finally and from Cancel.
    private async Task EndProgressAsync(ImportRun ending)
    {
        try
        {
            await _ui.RunAsync(() =>
            {
                lock (_gate)
                {
                    // The ending run is still in the list (FinishAsync removes it after).
                    if (_runs.Any(run => run != ending && !run.Finished && !run.IsCancelled))
                    {
                        return;
                    }
                }

                IsImporting = false;
                ProgressText = string.Empty;
                ProgressPercent = 0;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            // The window is closing and its dispatcher takes no more work: there is no progress left to hide. Not an
            // error: the error path logs a cancellation as such (the log has no level below Info) and shows nothing.
            _errors.Show(exception, ProjectText.ImportUiStepFailed);
        }
        catch (Exception exception)
        {
            _errors.Report(exception, ProjectText.ImportUiStepFailed);
        }
    }

    private sealed record Applied(int Kept, List<SkippedFile> Skipped);

    /// <summary>
    /// One import: its target project, its request and its cancellation. Registered with the project, which disposes
    /// it when <c>New project</c> replaces it: that cancels the import.
    /// </summary>
    private sealed class ImportRun(ProjectViewModel owner, ImportRequest request, Action<ImportRun> cancelled) : IDisposable
    {
        // Never disposed: a source without timers or linked tokens holds nothing that needs it, and leaving it alive
        // means Cancel can never race a dispose.
        private readonly CancellationTokenSource _cancellation = new();
        private int _cancelled;

        public ProjectViewModel Owner { get; } = owner;

        public Project Target { get; } = owner.Model;

        public bool IsCancelled => _cancellation.IsCancellationRequested;

        public ImportRequest Request { get; } = request;

        public CancellationToken Token => _cancellation.Token;

        public Task Completion { get; set; } = Task.CompletedTask;

        public bool Finished => Completion.IsCompleted;

        // Any thread. The first Cancel also tells the view model, which clears the progress at once.
        public void Cancel()
        {
            _cancellation.Cancel();
            if (Interlocked.Exchange(ref _cancelled, 1) == 0)
            {
                cancelled(this);
            }
        }

        public void Dispose() => Cancel();
    }
}
