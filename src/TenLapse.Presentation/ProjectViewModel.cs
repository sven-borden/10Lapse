using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// The bindable face of one <see cref="Project"/>, and the owner of everything built for it. The preview
/// player and the proxy cache (#19) register themselves with <see cref="AddResource"/>; <c>New project</c>
/// disposes this view model, which closes the project and disposes them, so nothing of the old project
/// outlives it.
/// </summary>
public sealed partial class ProjectViewModel : ObservableObject, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<IDisposable> _resources = [];
    private readonly IErrorSink _errors;
    private bool _disposed;

    /// <summary>Wraps <paramref name="model"/> and follows its changes until disposed.</summary>
    /// <param name="model">The project; this view model closes it when disposed.</param>
    /// <param name="errors">Takes the failures of resources that throw while they are disposed.</param>
    public ProjectViewModel(Project model, IErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(errors);
        Model = model;
        _errors = errors;
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

    /// <summary>True when the project has at least one frame.</summary>
    public bool HasFrames => FrameCount > 0;

    /// <summary>True once <c>New project</c> (or the app) has disposed this view model.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed);

    /// <summary>
    /// Hands a per-project resource (the preview's frame source, the proxy cache, a running import) to this
    /// project: it is disposed with the project, last registered first. A resource registered after the
    /// project was disposed (a background build that finished late) is disposed at once. Safe on any thread,
    /// also while <see cref="Dispose"/> runs on another.
    /// </summary>
    public void AddResource(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            if (!_disposed)
            {
                _resources.Add(resource);
                return;
            }
        }

        DisposeResource(resource);
    }

    /// <summary>
    /// Takes back a resource that ended on its own (a finished import), so the project no longer holds it and does not
    /// dispose it. It is not disposed here. Unknown resources are ignored. Safe on any thread.
    /// </summary>
    /// <returns>True when it was registered.</returns>
    public bool RemoveResource(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            return _resources.Remove(resource);
        }
    }

    /// <summary>How many resources the project holds (tests).</summary>
    internal int ResourceCount
    {
        get
        {
            lock (_gate)
            {
                return _resources.Count;
            }
        }
    }

    /// <summary>
    /// Stops following the model, closes it (it takes no more changes) and disposes every registered resource,
    /// newest first. Never throws: a resource that throws is reported to the error sink and the others are
    /// still disposed. The resources are disposed outside the lock, so one that registers another resource
    /// from any thread while it closes cannot deadlock; that late one is disposed at once.
    /// </summary>
    public void Dispose()
    {
        IDisposable[] resources;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Volatile.Write(ref _disposed, true);
            resources = [.. _resources];
            _resources.Clear();
        }

        Model.Changed -= OnModelChanged;
        Model.Close();
        for (var i = resources.Length - 1; i >= 0; i--)
        {
            DisposeResource(resources[i]);
        }
    }

    private void DisposeResource(IDisposable resource)
    {
        try
        {
            resource.Dispose();
        }
        catch (Exception exception)
        {
            _errors.Report(exception, ProjectText.ClosingProjectFailed);
        }
    }

    private void OnModelChanged(object? sender, ProjectChangedEventArgs e) => Refresh();

    // The generated setters compare before they notify, so refreshing every value raises only what changed.
    [MemberNotNull(nameof(Name))]
    private void Refresh()
    {
        var snapshot = Model.Snapshot;
        FrameCount = snapshot.Frames.Length;
        SkippedFileCount = snapshot.SkippedFiles.Length;
        Name = snapshot.Name;
        IsDirty = snapshot.IsDirty;
    }
}
