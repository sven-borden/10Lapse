using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>Raised when <c>New project</c> has replaced the current project.</summary>
public sealed class ProjectReplacedEventArgs(ProjectViewModel previous, ProjectViewModel current) : EventArgs
{
    /// <summary>The project that was replaced. It is disposed right after the handlers run.</summary>
    public ProjectViewModel Previous { get; } = previous;

    /// <summary>The new, empty project, already <see cref="ShellViewModel.CurrentProject"/>.</summary>
    public ProjectViewModel Current { get; } = current;
}

/// <summary>The window's view model: the current project, the title text, and the project-wide commands.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IErrorPresenter _errors;
    private readonly Func<Project> _createProject;
    private bool _replacing;

    /// <summary>Creates the shell with a fresh project.</summary>
    /// <param name="dialogs">Asks before work is lost.</param>
    /// <param name="errors">
    /// The error path: the commands go through it, and it takes the failures of closing the old project, which never
    /// fail <c>New project</c>.
    /// </param>
    /// <param name="createProject">
    /// Makes each new project; defaults to <c>new Project()</c>. #21 can pass one that applies the user's
    /// default settings with the <see cref="Project"/> settings constructor. It must return a new, empty,
    /// clean project every time, on the UI thread.
    /// </param>
    public ShellViewModel(IDialogService dialogs, IErrorPresenter errors, Func<Project>? createProject = null)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(errors);
        _dialogs = dialogs;
        _errors = errors;
        _createProject = createProject ?? (() => new Project());
        CurrentProject = new ProjectViewModel(CreateEmptyProject(previous: null), errors);
        NewProjectCommand = errors.AsyncCommand(NewProjectAsync, ProjectText.NewProjectFailed);
        OpenSettingsCommand = errors.Command(() => IsSettingsOpen = true, ProjectText.OpenSettingsFailed);
        CloseSettingsCommand = errors.Command(() => IsSettingsOpen = false, ProjectText.CloseSettingsFailed);
    }

    /// <summary>
    /// Raised once per <c>New project</c>, after <see cref="CurrentProject"/> changed and before the previous
    /// project is disposed. Long-lived views (the filmstrip, #20; the player, #19) rebind here to
    /// <see cref="ProjectReplacedEventArgs.Current"/> and let go of the previous project's resources. Every
    /// handler runs even when an earlier one throws; the failures go to the error sink after the dispose.
    /// </summary>
    public event EventHandler<ProjectReplacedEventArgs>? ProjectReplaced;

    /// <summary>
    /// Whether the project may change now: an export (#24) closes it while it runs. <c>New project</c>, the imports and the
    /// filmstrip's edits ask it first.
    /// </summary>
    public EditGate Edits { get; } = new();

    /// <summary>The project being edited. Only <c>New project</c> replaces it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(HasUnsavedChanges), nameof(ShowsProjectName), nameof(ShowsEmpty), nameof(ShowsEditing))]
    public partial ProjectViewModel CurrentProject { get; private set; }

    /// <summary>
    /// True while the Settings page covers the body (a page with a Back button, not a dialog; DESIGN.md). The
    /// project stays as it is underneath.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmpty), nameof(ShowsEditing))]
    public partial bool IsSettingsOpen { get; private set; }

    /// <summary>True when the body shows the Empty state: no photos, and Settings is not open.</summary>
    public bool ShowsEmpty => !CurrentProject.HasFrames && !IsSettingsOpen;

    /// <summary>True when the body shows the Editing state: photos, and Settings is not open.</summary>
    public bool ShowsEditing => CurrentProject.HasFrames && !IsSettingsOpen;

    /// <summary>
    /// <c>New project</c>: asks first when the current project has work to lose, then replaces it with an
    /// empty one. Cancel keeps everything as it was. While the question is open the command cannot run again.
    /// A failure goes to the error path and leaves the current project in place.
    /// </summary>
    public IAsyncRelayCommand NewProjectCommand { get; }

    /// <summary>
    /// The question before the current project is replaced with unsaved work (#52): <c>ProjectFilesViewModel</c> sets it to
    /// its "Save changes to …?" (Save, Don't save, Cancel), the question Open and Close ask too, so <c>New project</c> and
    /// the drop's "Start new project" ask it as well. True to go on (saved, or not to be saved; also when there is nothing
    /// to ask), false to keep the project. Null (tests, before #52) keeps 3.0's confirmation for <c>New project</c>.
    /// </summary>
    public Func<Task<bool>>? UnsavedWorkQuestion { get; set; }

    /// <summary>Opens the Settings page (the command bar's Settings button).</summary>
    public IRelayCommand OpenSettingsCommand { get; }

    /// <summary>Leaves the Settings page (its Back button, Escape).</summary>
    public IRelayCommand CloseSettingsCommand { get; }

    /// <summary>The window title (see <see cref="ProjectText.WindowTitle"/>).</summary>
    public string Title =>
        ProjectText.WindowTitle(CurrentProject.Name, CurrentProject.HasFrames, CurrentProject.IsDirty);

    /// <summary>True while the current project has unsaved work (#28 draws the title bar dot from this).</summary>
    public bool HasUnsavedChanges => CurrentProject.IsDirty;

    /// <summary>
    /// True when the title bar shows the project name next to "10Lapse": once the project has photos or unsaved
    /// changes, the same rule as <see cref="Title"/>, so a fresh window reads only "10Lapse".
    /// </summary>
    public bool ShowsProjectName => CurrentProject.HasFrames || CurrentProject.IsDirty;

    private async Task NewProjectAsync()
    {
        // AsyncRelayCommand blocks re-entry only through CanExecute, which ExecuteAsync does not check, so a
        // second caller while the question is open would ask again and replace twice. Ignore it instead.
        if (_replacing || !Edits.Allow())
        {
            return;
        }

        _replacing = true;
        try
        {
            await ReplaceProjectAsync(askFirst: true);
        }
        finally
        {
            _replacing = false;
        }
    }

    /// <summary>
    /// Replaces the current project with an empty one without asking: the caller already asked (#12's drop question,
    /// "Start new project"). Raises <see cref="ProjectReplaced"/> and disposes the previous project like
    /// <c>New project</c>. On the UI thread.
    /// </summary>
    /// <returns>False when <c>New project</c> is already replacing the project (its question is open), or an export holds the project (<see cref="Edits"/>), so nothing changed.</returns>
    internal bool ReplaceWithoutAsking()
    {
        if (_replacing || !Edits.Allow())
        {
            return false;
        }

        Replace(CurrentProject);
        return true;
    }

    /// <summary>
    /// Replaces the current project, without asking, with the empty one <paramref name="create"/> makes (#52: an opened
    /// or restored project, made with its file's settings; the caller asked first and adds the frames next, so the views
    /// see them arrive as an import's). Raises <see cref="ProjectReplaced"/> and disposes the previous project like
    /// <c>New project</c>. On the UI thread.
    /// </summary>
    /// <param name="create">Makes a new, empty, clean project (the <see cref="Project"/> settings constructor).</param>
    /// <returns>False when nothing changed: <c>New project</c>'s question is open, or an export holds the project.</returns>
    public bool ReplaceWithoutAsking(Func<Project> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (_replacing || !Edits.Allow())
        {
            return false;
        }

        Replace(CurrentProject, create);
        return true;
    }

    private async Task ReplaceProjectAsync(bool askFirst)
    {
        // Only this method and ReplaceWithoutAsking replace CurrentProject, and _replacing keeps them from running
        // while the question is open, so it is the same after the await.
        var previous = CurrentProject;
        if (askFirst && UnsavedWorkQuestion is { } question)
        {
            if (!await question())
            {
                return;
            }
        }
        else if (askFirst
            && previous.Model.HasWorkToLose
            && !await _dialogs.ConfirmAsync(ProjectText.NewProjectConfirmation(previous.FrameCount)))
        {
            return;
        }

        Replace(previous);
    }

    private void Replace(ProjectViewModel previous, Func<Project>? create = null)
    {
        CurrentProject = new ProjectViewModel(CreateEmptyProject(previous.Model, create), _errors);

        // The swap is done, so nothing below may fail the command: each handler runs in its own try, the old
        // project is disposed (which never throws), then every handler failure is reported.
        List<Exception>? failures = null;
        if (ProjectReplaced is { } replaced)
        {
            var args = new ProjectReplacedEventArgs(previous, CurrentProject);
            foreach (var handler in replaced.GetInvocationList().Cast<EventHandler<ProjectReplacedEventArgs>>())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
        }

        previous.Dispose();
        // A view that did not switch may still show the old project: the user should know.
        foreach (var failure in failures ?? [])
        {
            _errors.Show(failure, ProjectText.SwitchingViewFailed);
        }
    }

    // The generator types oldValue as non-null, but it is null on the first assignment in the constructor.
    partial void OnCurrentProjectChanged(ProjectViewModel oldValue, ProjectViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnProjectPropertyChanged;
        }

        newValue.PropertyChanged += OnProjectPropertyChanged;
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ProjectViewModel.Name):
                OnPropertyChanged(nameof(Title));
                break;
            case nameof(ProjectViewModel.HasFrames):
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(ShowsProjectName));
                OnPropertyChanged(nameof(ShowsEmpty));
                OnPropertyChanged(nameof(ShowsEditing));
                break;
            case nameof(ProjectViewModel.IsDirty):
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(ShowsProjectName));
                break;
        }
    }

    private Project CreateEmptyProject(Project? previous, Func<Project>? create = null)
    {
        var project = (create ?? _createProject)();
        if (project is null || ReferenceEquals(project, previous) || project.Frames.Length > 0 || project.SkippedFiles.Length > 0 || project.IsDirty)
        {
            throw new InvalidOperationException(
                "The project factory must return a new, empty, clean project: pass the settings to the Project constructor instead of setting them.");
        }

        return project;
    }
}
