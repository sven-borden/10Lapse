using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Diagnostics;
using TenLapse.Core.Projects;
using TenLapse.Core.Settings;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.ProjectFiles;

/// <summary>The recent project files (#52), kept with the app's settings (<see cref="RememberedSettings"/>).</summary>
public interface IRecentProjects
{
    /// <summary>The files, the most recent first.</summary>
    PathList RecentProjects { get; }

    /// <summary>Raised on the UI thread when <see cref="RecentProjects"/> changed.</summary>
    event EventHandler? RecentProjectsChanged;

    /// <summary>Puts a file first (it was opened or saved).</summary>
    void AddRecentProject(string path);

    /// <summary>Takes a file off the list (it is gone).</summary>
    void RemoveRecentProject(string path);

    /// <summary>Empties the list.</summary>
    void ClearRecentProjects();
}

/// <summary>
/// Saving and opening projects (#52): the File menu's Open, Save and Save as (Ctrl+O, Ctrl+S, Ctrl+Shift+S), opening a
/// file passed at launch or picked from the recent list, the save-first question before unsaved changes would be lost
/// (opening another project, closing the window), and the offer to restore a project 10Lapse could not save before it
/// ended. Everything runs on the UI thread; the file work runs off it.
/// </summary>
/// <remarks>
/// <para>
/// An opened project is made empty with the file's settings and replaces the current one (<see cref="ShellViewModel.ReplaceWithoutAsking(Func{Project})"/>),
/// then gets its frames in one change, as an import's first batch, so every view follows as it does for an import; then it
/// is tied to its file and marked saved. Removing a frame never touches the disk, so a saved project simply has no entry for it.
/// </para>
/// <para>
/// Photos that are not there are listed in one InfoBar and the project opens with what exists, but they stay in its file:
/// the open project carries them (<see cref="OpenedFile"/>) and every save of it, and its recovery copy, writes them back
/// in their places (<see cref="ProjectFileContent.Carrying"/>). A file none of whose photos are there does not open at all
/// (it would open empty and tied to its file, so the next Save would overwrite it with whatever the user did next). A file
/// from a newer 10Lapse opens, but Save never overwrites it: it asks where, as Save as does.
/// </para>
/// </remarks>
public sealed partial class ProjectFilesViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly IProjectFilePicker _picker;
    private readonly IProjectFileSystem _disk;
    private readonly IRecentProjects _recent;
    private readonly IRecoveryStore _recovery;
    private readonly IDialogService _dialogs;
    private readonly IInfoBarService _infoBars;
    private readonly IErrorPresenter _errors;
    private readonly ILog _log;
    private readonly IFrameDetailsReader? _details;
    private readonly Func<Task<bool>> _question;
    private ProjectViewModel? _watched;
    private bool _busy;

    // What the open project brought from its file: written on the UI thread, read by the autosave's thread (immutable).
    private volatile OpenedFile? _opened;

    /// <param name="shell">The window's project.</param>
    /// <param name="picker">The Open and Save dialogs.</param>
    /// <param name="disk">Reads and writes the project files, and looks for their photos.</param>
    /// <param name="recent">The recent list, which each open and save updates.</param>
    /// <param name="recovery">Where a recovery copy left behind is found (<see cref="OfferRecoveryAsync"/>).</param>
    /// <param name="dialogs">Asks before unsaved changes would be lost.</param>
    /// <param name="infoBars">Says what went wrong, and what was left out.</param>
    /// <param name="errors">The commands go through it.</param>
    /// <param name="log">Takes what each open and save did.</param>
    /// <param name="details">Reads an opened project's photos' size and capture time again (the import); none when null.</param>
    public ProjectFilesViewModel(
        ShellViewModel shell,
        IProjectFilePicker picker,
        IProjectFileSystem disk,
        IRecentProjects recent,
        IRecoveryStore recovery,
        IDialogService dialogs,
        IInfoBarService infoBars,
        IErrorPresenter errors,
        ILog log,
        IFrameDetailsReader? details = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(log);
        _shell = shell;
        _picker = picker;
        _disk = disk;
        _recent = recent;
        _recovery = recovery;
        _dialogs = dialogs;
        _infoBars = infoBars;
        _errors = errors;
        _log = log;
        _details = details;
        OpenCommand = errors.AsyncCommand(OpenAsync, ProjectFileText.OpenFailedContext);
        SaveCommand = errors.AsyncCommand(() => SaveAsync(saveAs: false), ProjectFileText.SaveFailedContext);
        SaveAsCommand = errors.AsyncCommand(() => SaveAsync(saveAs: true), ProjectFileText.SaveFailedContext);
        _shell.PropertyChanged += OnShellPropertyChanged;
        Watch(_shell.CurrentProject);

        // New project and the drop's "Start new project" ask the same question as Open and Close.
        _question = AskToSaveFirstAsync;
        _shell.UnsavedWorkQuestion = _question;
    }

    /// <summary>File, Open… (Ctrl+O): asks for a project file and opens it.</summary>
    public IAsyncRelayCommand OpenCommand { get; }

    /// <summary>File, Save (Ctrl+S): writes the project to its file, or asks where the first time.</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>File, Save as… (Ctrl+Shift+S): asks where, and writes the project there.</summary>
    public IAsyncRelayCommand SaveAsCommand { get; }

    /// <summary>True when the project has photos, so Save and Save as are in the File menu (absent otherwise).</summary>
    public bool CanSave => _shell.CurrentProject.HasFrames;

    /// <summary>True when closing or opening another project would lose changes: the project has photos and changed since it was saved.</summary>
    public bool HasUnsavedWork => _shell.CurrentProject.HasFrames && _shell.CurrentProject.Model.HasWorkToLose;

    /// <summary>
    /// Opens <paramref name="path"/> (the recent list, a file 10Lapse was started with): reads it, asks first when the
    /// current project has unsaved changes, and replaces the current project. A file that cannot be opened becomes a
    /// Critical InfoBar and changes nothing. On the UI thread.
    /// </summary>
    /// <returns>True when the project opened.</returns>
    public async Task<bool> OpenPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // During an export nothing is read or asked: the export's own InfoBar says why.
        if (_busy || !_shell.Edits.Allow())
        {
            return false;
        }

        _busy = true;
        try
        {
            return await OpenCoreAsync(Path.GetFullPath(path));
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// The close check (#52, with <see cref="Export.WindowCloseGate"/>): when there are unsaved changes, asks "Save changes
    /// to …?". True when the window may close: saved, or the user chose not to save; false on Cancel or a failed save.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!HasUnsavedWork)
        {
            return true;
        }

        return await AskToSaveAsync();
    }

    /// <summary>
    /// At launch: when a 10Lapse that ended without closing left a recovery copy, offers it in an InfoBar with one action,
    /// Restore. A copy that cannot be read is dropped, with a line in the log. On the UI thread.
    /// </summary>
    public async Task OfferRecoveryAsync()
    {
        RecoveryCopy? copy;
        try
        {
            copy = await Task.Run(_recovery.TakeOrphan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Nobody asked for it: a copy that cannot be read is a line in the log, not an InfoBar.
            _errors.Report(exception, ProjectFileText.RecoveryLookFailedContext);
            return;
        }

        if (copy is null)
        {
            return;
        }

        var read = ProjectFileJson.Read(copy.Text);
        if (read.Content is not { Frames.Length: > 0 } content)
        {
            _log.Warning(ProjectFileText.RecoveryUnreadable(read.Problems));
            return;
        }

        _log.Info(ProjectFileText.RecoveryOffered(content.Frames.Length, copy.Saved));
        var name = content.RecoveredName
            ?? (content.RecoveredFilePath is { } file ? Path.GetFileNameWithoutExtension(file) : Project.DefaultName);
        _infoBars.Show(new InfoBarRequest(
            InfoBarSeverity.Caution,
            ProjectFileText.RecoveryOffer(name),
            ProjectFileText.RecoveryRestore,
            () => _errors.GuardAsync(() => RestoreAsync(content), ProjectFileText.RecoveryRestoreFailedContext)));
    }

    /// <summary>
    /// What a recovery copy of <paramref name="project"/> holds (#52): its <paramref name="snapshot"/>, its name and file,
    /// and the frames its file could not find when it was opened. Any thread (the autosave's).
    /// </summary>
    public ProjectFileContent RecoveryContent(Project project, ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(snapshot);
        return ContentFor(project, snapshot, snapshot.FilePath) with { RecoveredName = snapshot.Name, RecoveredFilePath = snapshot.FilePath };
    }

    /// <summary>Stops following the shell.</summary>
    public void Dispose()
    {
        _shell.PropertyChanged -= OnShellPropertyChanged;
        if (ReferenceEquals(_shell.UnsavedWorkQuestion, _question))
        {
            _shell.UnsavedWorkQuestion = null;
        }

        Watch(null);
    }

    // The file's content for the project's snapshot, with the frames its file could not find written back in their places.
    private ProjectFileContent ContentFor(Project project, ProjectSnapshot snapshot, string? path)
    {
        var content = ProjectFileContent.From(snapshot, path);
        return _opened is { } opened && ReferenceEquals(opened.Project, project) && opened.Unresolved.Length > 0
            ? content.Carrying(opened.Unresolved, snapshot.Frames, path)
            : content;
    }

    private async Task OpenAsync()
    {
        // During an export the picker does not even open: the export's own InfoBar says why.
        if (_busy || !_shell.Edits.Allow())
        {
            return;
        }

        _busy = true;
        try
        {
            if (await _picker.PickOpenAsync(StartFolder()) is { Length: > 0 } path)
            {
                await OpenCoreAsync(path);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<bool> OpenCoreAsync(string path)
    {
        // Read and look for the photos off the UI thread: 5,000 files are 5,000 small disk questions.
        Opening opening;
        try
        {
            opening = await Task.Run(() => Read(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            _errors.Report(exception, ProjectFileText.OpenReadFailedContext);
            _infoBars.Show(InfoBarSeverity.Critical, ProjectFileText.OpenUnreadable(path));
            if (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                _recent.RemoveRecentProject(path);
            }

            return false;
        }

        if (opening.Read.Content is not { } content)
        {
            _log.Warning(ProjectFileText.NotAProjectLog(opening.Read.Problems));
            _infoBars.Show(InfoBarSeverity.Critical, ProjectFileText.OpenNotAProject(path));
            return false;
        }

        var frames = opening.Frames!;
        if (content.Frames.Length > 0 && frames.Frames.Length == 0)
        {
            // Not one photo is there (a drive not connected): opening it empty and tied to its file would let the next
            // Save overwrite it. The current project stays; the file stays as it is, and off the recent list.
            _log.Warning(ProjectFileText.Opened(0, frames.Missing.Length, 0, 0, opening.Read.Version, opening.Read.Problems) + " Not opened: none of its photos were found.");
            ShowNoneFound(Path.GetFileNameWithoutExtension(path), frames.Missing);
            return false;
        }

        if (!await AskToSaveFirstAsync())
        {
            return false;
        }

        if (!Replace(content, frames))
        {
            return false;
        }

        var current = _shell.CurrentProject;
        var project = current.Model;
        project.SetFile(path);
        project.MarkSaved(project.Version);
        _opened = new OpenedFile(project, path, frames.Unresolved, opening.Read.IsNewerVersion);
        _recent.AddRecentProject(path);
        Report(path, opening.Read, frames);
        ReadDetails(current, frames);
        return true;
    }

    // The restore of a recovery copy: the project as it was, with its file (if it had one) but unsaved.
    private async Task RestoreAsync(ProjectFileContent content)
    {
        // During an export nothing is asked: the export's own InfoBar says why.
        if (_busy || !_shell.Edits.Allow())
        {
            return;
        }

        _busy = true;
        try
        {
            var frames = await Task.Run(() => SavedFrames.Resolve(content, content.RecoveredFilePath, _disk));
            var name = content.RecoveredName
                ?? (content.RecoveredFilePath is { } named ? Path.GetFileNameWithoutExtension(named) : Project.DefaultName);
            if (frames.Frames.Length == 0)
            {
                // As for Open: nothing to restore until the photos are back; the copy stays offered until this run closes.
                ShowNoneFound(name, frames.Missing);
                return;
            }

            if (!await AskToSaveFirstAsync() || !Replace(content, frames))
            {
                return;
            }

            var current = _shell.CurrentProject;
            var project = current.Model;
            if (content.RecoveredFilePath is { } file)
            {
                project.SetFile(file);
            }
            else if (content.RecoveredName is not null)
            {
                project.Name = name;
            }

            _opened = new OpenedFile(project, content.RecoveredFilePath, frames.Unresolved, IsNewer: false);
            _recovery.MarkOfferRestored();
            ShowMissing(frames.Missing);
            ShowCloudOnly(frames);
            ReadDetails(current, frames);
        }
        finally
        {
            _busy = false;
        }
    }

    // Replaces the current project with the content's settings and frames; false when the shell refused (an export holds it).
    private bool Replace(ProjectFileContent content, ResolvedFrames frames)
    {
        if (!_shell.ReplaceWithoutAsking(content.CreateEmptyProject))
        {
            return false;
        }

        _shell.CurrentProject.Model.AddFrames(frames.Frames);
        return true;
    }

    // The photos the file knew nothing about, or that changed since the save, are read again in the background.
    private void ReadDetails(ProjectViewModel project, ResolvedFrames frames)
    {
        if (_details is { } details && frames.ToRead.Length > 0)
        {
            _ = _errors.GuardAsync(() => details.ReadDetailsAsync(project, frames.ToRead), ProjectFileText.DetailsReadFailedContext);
        }
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        try
        {
            return await SaveCoreAsync(saveAs);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<bool> SaveCoreAsync(bool saveAs)
    {
        var current = _shell.CurrentProject;
        if (!current.HasFrames)
        {
            return false;
        }

        var model = current.Model;
        var path = model.FilePath;
        if (!saveAs && path is not null && _opened is { IsNewer: true } opened && ReferenceEquals(opened.Project, model)
            && string.Equals(opened.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            // A newer 10Lapse wrote it: saving over it would drop what this version does not know. Ask where instead.
            _infoBars.Show(InfoBarSeverity.Caution, ProjectFileText.NewerSaveAs(path));
            saveAs = true;
        }

        if (saveAs || path is null)
        {
            path = await _picker.PickSaveAsync(model.Name, StartFolder());
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            path = Path.GetFullPath(path);
        }

        // The snapshot is immutable: written off the UI thread while the user carries on. The photos the file could not
        // find when it was opened go back in with it.
        var snapshot = model.Snapshot;
        var content = ContentFor(model, snapshot, path);
        var started = Stopwatch.GetTimestamp();
        try
        {
            await Task.Run(() => _disk.WriteText(path, ProjectFileJson.Write(content)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A folder that is gone is never made again (another drive may have its letter now): say so, and offer Save as.
            _errors.Report(exception, ProjectFileText.SaveWriteFailedContext);
            _infoBars.Show(new InfoBarRequest(
                InfoBarSeverity.Critical,
                exception is DirectoryNotFoundException ? ProjectFileText.SaveFolderGone(path) : ProjectFileText.SaveFailed(path),
                ProjectFileText.SaveAs,
                () => SaveAsCommand.ExecuteAsync(null)));
            return false;
        }

        _log.Info(ProjectFileText.Saved(content.Frames.Length, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        _recent.AddRecentProject(path);
        if (_opened is { IsNewer: true } written && ReferenceEquals(written.Project, model))
        {
            // The file it is tied to from now on is this version's own.
            _opened = written with { IsNewer = false };
        }

        if (!model.IsClosed)
        {
            model.SetFile(path);
            model.MarkSaved(snapshot.Version);
        }

        return true;
    }

    // Before the current project is replaced: nothing to ask, or the user's answer. False to stop.
    private async Task<bool> AskToSaveFirstAsync() => !HasUnsavedWork || await AskToSaveAsync();

    private async Task<bool> AskToSaveAsync() =>
        await _dialogs.ChooseAsync(ProjectFileText.UnsavedQuestion(_shell.CurrentProject.Name)) switch
        {
            ChoiceResult.Primary => await SaveCoreAsync(saveAs: false),
            ChoiceResult.Secondary => true,
            _ => false,
        };

    private Opening Read(string path)
    {
        var read = ProjectFileJson.Read(_disk.ReadText(path));
        return new Opening(read, read.Content is { } content ? SavedFrames.Resolve(content, path, _disk) : null);
    }

    private void Report(string path, ProjectFileReadResult read, ResolvedFrames frames)
    {
        _log.Info(ProjectFileText.Opened(frames.Frames.Length, frames.Missing.Length, frames.Changed, frames.FoundBesideProject, read.Version, read.Problems));
        if (read.IsNewerVersion)
        {
            _infoBars.Show(InfoBarSeverity.Caution, ProjectFileText.OpenNewerVersion(path));
        }
        else if (read.Problems.Count > 0)
        {
            _infoBars.Show(InfoBarSeverity.Caution, ProjectFileText.OpenSomeSettings(path));
        }

        ShowMissing(frames.Missing);
        ShowCloudOnly(frames);
    }

    // One InfoBar for every photo not found, with a line each: the file name, and the folder it was looked for in.
    private void ShowMissing(IReadOnlyList<string> missing)
    {
        if (missing.Count > 0)
        {
            _infoBars.ShowWithDetails(InfoBarSeverity.Caution, ProjectFileText.Missing(missing.Count), Where(missing));
        }
    }

    // The project did not open: none of its photos were found. The same list, so the user sees which drive to connect.
    private void ShowNoneFound(string name, IReadOnlyList<string> missing) =>
        _infoBars.ShowWithDetails(InfoBarSeverity.Caution, ProjectFileText.NoneFound(missing.Count, name), Where(missing));

    // Online-only OneDrive photos are not skipped (they are the project's), but the user hears they will download.
    private void ShowCloudOnly(ResolvedFrames frames)
    {
        if (frames.CloudOnly > 0)
        {
            _infoBars.Show(InfoBarSeverity.Caution, ProjectFileText.CloudOnly(frames.CloudOnly, frames.CloudOnlyBytes));
        }
    }

    private static IEnumerable<InfoBarDetail> Where(IReadOnlyList<string> missing) =>
        missing.Select(path => new InfoBarDetail(
            Path.GetFileName(path) is { Length: > 0 } name ? name : path,
            Path.GetDirectoryName(path)));

    // The dialogs open where the project is, else where the last project was, else in Documents.
    private string? StartFolder() =>
        Path.GetDirectoryName(_shell.CurrentProject.Model.FilePath ?? _recent.RecentProjects.FirstOrDefault() ?? string.Empty) is { Length: > 0 } folder
            ? folder
            : null;

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentProject))
        {
            if (_opened is { } opened && !ReferenceEquals(opened.Project, _shell.CurrentProject.Model))
            {
                _opened = null;
            }

            Watch(_shell.CurrentProject);
            OnPropertyChanged(nameof(CanSave));
        }
    }

    private void Watch(ProjectViewModel? project)
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnProjectPropertyChanged;
        }

        _watched = project;
        if (project is not null)
        {
            project.PropertyChanged += OnProjectPropertyChanged;
        }
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectViewModel.HasFrames))
        {
            OnPropertyChanged(nameof(CanSave));
        }
    }

    private sealed record Opening(ProjectFileReadResult Read, ResolvedFrames? Frames);

    /// <summary>What the open project brought from its file (#52).</summary>
    /// <param name="Project">The project it belongs to; it lapses when another replaces it.</param>
    /// <param name="Path">The file it was opened from (or a restored copy belonged to); null for none.</param>
    /// <param name="Unresolved">The file's frames whose photo was not found: every save writes them back.</param>
    /// <param name="IsNewer">A newer 10Lapse wrote <paramref name="Path"/>: Save asks where instead of overwriting it.</param>
    private sealed record OpenedFile(Project Project, string? Path, ImmutableArray<UnresolvedFrame> Unresolved, bool IsNewer);
}
