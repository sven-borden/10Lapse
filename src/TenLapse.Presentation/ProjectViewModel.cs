using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using TenLapse.Core.Projects;

namespace TenLapse.Presentation;

/// <summary>
/// The bindable face of one <see cref="Project"/>, and the owner of everything built for it. The preview
/// player and the proxy cache (#19) register themselves with <see cref="AddResource"/>; <c>New project</c>
/// disposes this view model, which disposes them, so nothing of the old project outlives it.
/// </summary>
public sealed partial class ProjectViewModel : ObservableObject, IDisposable
{
    private readonly List<IDisposable> _resources = [];

    /// <summary>Wraps <paramref name="model"/> and follows its changes until disposed.</summary>
    public ProjectViewModel(Project model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        Refresh();
        Model.Changed += OnModelChanged;
    }

    /// <summary>The project. #12 adds frames with <c>Model.AddFrames</c>; the panels set its settings.</summary>
    public Project Model { get; }

    /// <summary>The number of frames.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFrames))]
    public partial int FrameCount { get; private set; }

    /// <summary>The number of files import skipped (#13 shows "3 photos couldn't be read").</summary>
    [ObservableProperty]
    public partial int SkippedFileCount { get; private set; }

    /// <summary>The project name.</summary>
    [ObservableProperty]
    public partial string Name { get; private set; }

    /// <summary>True while the project has changes not yet exported (#28 shows a dot).</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    /// <summary>"742 photos → 24.7 s at 30 fps" (#17).</summary>
    [ObservableProperty]
    public partial string TimingReadout { get; private set; }

    /// <summary>True when the project has at least one frame.</summary>
    public bool HasFrames => FrameCount > 0;

    /// <summary>True once <c>New project</c> (or the app) has disposed this view model.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Hands a per-project resource (the preview's frame source, the proxy cache, a running import) to this
    /// project: it is disposed with the project, last registered first. A resource registered after the
    /// project was disposed (a background build that finished late) is disposed at once.
    /// </summary>
    public void AddResource(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (IsDisposed)
        {
            resource.Dispose();
            return;
        }

        _resources.Add(resource);
    }

    /// <summary>
    /// Stops following the model and disposes every registered resource, newest first. Every resource is
    /// disposed even when one throws; the failures then surface together as an <see cref="AggregateException"/>.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        Model.Changed -= OnModelChanged;

        List<Exception>? failures = null;
        for (var i = _resources.Count - 1; i >= 0; i--)
        {
            try
            {
                _resources[i].Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        _resources.Clear();
        if (failures is not null)
        {
            throw new AggregateException("Some of the old project's resources failed to close.", failures);
        }
    }

    private void OnModelChanged(object? sender, ProjectChangedEventArgs e) => Refresh();

    // The generated setters compare before they notify, so refreshing every value raises only what changed.
    [MemberNotNull(nameof(Name), nameof(TimingReadout))]
    private void Refresh()
    {
        FrameCount = Model.Frames.Count;
        SkippedFileCount = Model.SkippedFiles.Count;
        Name = Model.Name;
        IsDirty = Model.IsDirty;
        TimingReadout = ProjectText.TimingReadout(Model.Frames.Count, Model.Timing);
    }
}
