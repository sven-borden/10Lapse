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
    private readonly IErrorSink _errors;
    private readonly Func<Project> _createProject;

    /// <summary>Creates the shell with a fresh project.</summary>
    /// <param name="dialogs">Asks before work is lost.</param>
    /// <param name="errors">Takes the failures of closing the old project, which never fail <c>New project</c>.</param>
    /// <param name="createProject">
    /// Makes each new project; defaults to <c>new Project()</c>. #21 can pass one that applies the user's
    /// default settings with the <see cref="Project"/> settings constructor. It must return a new, empty,
    /// clean project every time, on the UI thread.
    /// </param>
    public ShellViewModel(IDialogService dialogs, IErrorSink errors, Func<Project>? createProject = null)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(errors);
        _dialogs = dialogs;
        _errors = errors;
        _createProject = createProject ?? (() => new Project());
        CurrentProject = new ProjectViewModel(CreateEmptyProject(previous: null), errors);
    }

    /// <summary>
    /// Raised once per <c>New project</c>, after <see cref="CurrentProject"/> changed and before the previous
    /// project is disposed. Long-lived views (the filmstrip, #20; the player, #19) rebind here to
    /// <see cref="ProjectReplacedEventArgs.Current"/> and let go of the previous project's resources. Every
    /// handler runs even when an earlier one throws; the failures go to the error sink after the dispose.
    /// </summary>
    public event EventHandler<ProjectReplacedEventArgs>? ProjectReplaced;

    /// <summary>The project being edited. Only <c>New project</c> replaces it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(HasUnsavedChanges))]
    public partial ProjectViewModel CurrentProject { get; private set; }

    /// <summary>The window title (see <see cref="ProjectText.WindowTitle"/>).</summary>
    public string Title =>
        ProjectText.WindowTitle(CurrentProject.Name, CurrentProject.HasFrames, CurrentProject.IsDirty);

    /// <summary>True while the current project has unsaved work (#28 draws the title bar dot from this).</summary>
    public bool HasUnsavedChanges => CurrentProject.IsDirty;

    /// <summary>
    /// <c>New project</c>: asks first when the current project has work to lose, then replaces it with an
    /// empty one. Cancel keeps everything as it was. While the question is open the command cannot run again.
    /// </summary>
    [RelayCommand]
    private async Task NewProjectAsync()
    {
        // Only this command replaces CurrentProject, and it never runs twice at once, so it is the same after the await.
        var previous = CurrentProject;
        if (previous.Model.HasWorkToLose
            && !await _dialogs.ConfirmAsync(ProjectText.NewProjectConfirmation(previous.FrameCount)))
        {
            return;
        }

        CurrentProject = new ProjectViewModel(CreateEmptyProject(previous.Model), _errors);

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
        foreach (var failure in failures ?? [])
        {
            _errors.Report(failure, ProjectText.SwitchingViewFailed);
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
            case nameof(ProjectViewModel.Name) or nameof(ProjectViewModel.HasFrames):
                OnPropertyChanged(nameof(Title));
                break;
            case nameof(ProjectViewModel.IsDirty):
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                break;
        }
    }

    private Project CreateEmptyProject(Project? previous)
    {
        var project = _createProject();
        if (project is null || ReferenceEquals(project, previous) || project.Frames.Length > 0 || project.SkippedFiles.Length > 0 || project.IsDirty)
        {
            throw new InvalidOperationException(
                "The project factory must return a new, empty, clean project: pass the settings to the Project constructor instead of setting them.");
        }

        return project;
    }
}
