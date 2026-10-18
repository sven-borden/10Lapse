using System.Collections.ObjectModel;

namespace TenLapse.Core.Projects;

/// <summary>What a <see cref="Project.Changed"/> notification covers.</summary>
[Flags]
public enum ProjectChanges
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Frames were added, removed or reordered.</summary>
    Frames = 1 << 0,

    /// <summary>The skipped-files list grew.</summary>
    SkippedFiles = 1 << 1,

    /// <summary><see cref="Project.Timing"/> changed.</summary>
    Timing = 1 << 2,

    /// <summary><see cref="Project.FrameSettings"/> changed.</summary>
    FrameSettings = 1 << 3,

    /// <summary><see cref="Project.Output"/> changed.</summary>
    Output = 1 << 4,

    /// <summary><see cref="Project.SortMode"/> changed.</summary>
    SortMode = 1 << 5,

    /// <summary><see cref="Project.Name"/> changed.</summary>
    Name = 1 << 6,

    /// <summary><see cref="Project.IsDirty"/> flipped.</summary>
    Dirty = 1 << 7,
}

/// <summary>Carries the <see cref="ProjectChanges"/> of one mutation.</summary>
public sealed class ProjectChangedEventArgs(ProjectChanges changes) : EventArgs
{
    /// <summary>What changed.</summary>
    public ProjectChanges Changes { get; } = changes;
}

/// <summary>
/// One timelapse being edited: the ordered frames, the files import skipped, and the timing, frame, output
/// and sort settings. A small mutable aggregate: every mutation goes through a method or setter here, marks
/// the project dirty and raises <see cref="Changed"/> once. The frame and setting values themselves are
/// immutable records.
/// </summary>
/// <remarks>
/// Not thread-safe: mutate it on one thread (the UI thread; #12 marshals its import batches there). A
/// project is never cleared: <c>New project</c> replaces the whole instance, so nothing of the old one can
/// survive into the new one. Removing a frame removes it from the project, never from disk.
/// </remarks>
public sealed class Project
{
    /// <summary>The name of a project that has no folder to be named after.</summary>
    public const string DefaultName = "Timelapse";

    private readonly List<Frame> _frames = [];
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _ids = [];
    private readonly List<SkippedFile> _skippedFiles = [];

    /// <summary>Creates an empty, clean project with the default settings.</summary>
    public Project()
    {
        Frames = new ReadOnlyCollection<Frame>(_frames);
        SkippedFiles = new ReadOnlyCollection<SkippedFile>(_skippedFiles);
    }

    /// <summary>Raised after every mutation, once per call, on the mutating thread.</summary>
    public event EventHandler<ProjectChangedEventArgs>? Changed;

    /// <summary>The frames in playback order. A live read-only view.</summary>
    public IReadOnlyList<Frame> Frames { get; }

    /// <summary>The files import could not read, in the order they were reported. A live read-only view.</summary>
    public IReadOnlyList<SkippedFile> SkippedFiles { get; }

    /// <summary>
    /// The project name, for the title bar and the suggested export file name. Setting null, empty or
    /// white space gives <see cref="DefaultName"/>; the value is trimmed.
    /// </summary>
    public string Name
    {
        get;
        set
        {
            var name = string.IsNullOrWhiteSpace(value) ? DefaultName : value.Trim();
            if (name != field)
            {
                field = name;
                Commit(ProjectChanges.Name);
            }
        }
    } = DefaultName;

    /// <summary>The speed (#17).</summary>
    public TimingSettings Timing
    {
        get;
        set => Set(ref field, value, ProjectChanges.Timing);
    } = TimingSettings.Default;

    /// <summary>The output shape (#18).</summary>
    public FrameSettings FrameSettings
    {
        get;
        set => Set(ref field, value, ProjectChanges.FrameSettings);
    } = FrameSettings.Default;

    /// <summary>Resolution, format and quality (#23).</summary>
    public OutputSettings Output
    {
        get;
        set => Set(ref field, value, ProjectChanges.Output);
    } = OutputSettings.Default;

    /// <summary>The order the user chose (#15). Setting it does not reorder; #15 calls <see cref="ReorderFrames"/>.</summary>
    public SortMode SortMode
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown sort mode.");
            }

            if (value != field)
            {
                field = value;
                Commit(ProjectChanges.SortMode);
            }
        }
    } = SortMode.CaptureTime;

    /// <summary>True after any change since the project was created or last exported.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// True when replacing this project would lose something the user did: it changed since it was created
    /// or last exported. A fresh project, and one exported since its last change, have nothing to lose.
    /// </summary>
    public bool HasWorkToLose => IsDirty;

    /// <summary>
    /// The name a project imported from <paramref name="folderPath"/> gets: the folder's own name, or
    /// <see cref="DefaultName"/> for a drive root, an empty path or null.
    /// </summary>
    public static string NameFromFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return DefaultName;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath.Trim()));
        return string.IsNullOrWhiteSpace(name) || name.EndsWith(Path.VolumeSeparatorChar) ? DefaultName : name.Trim();
    }

    /// <summary>
    /// Appends frames in the given order (#12 calls this for every batch, also for Add photos). A frame whose
    /// id or path (case-insensitive) is already in the project is left out, so adding a folder twice does
    /// not double it.
    /// </summary>
    /// <returns>How many frames were added.</returns>
    public int AddFrames(IEnumerable<Frame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var added = 0;
        foreach (var frame in frames)
        {
            ArgumentNullException.ThrowIfNull(frame, nameof(frames));
            if (_ids.Contains(frame.Id) || !_paths.Add(frame.SourcePath))
            {
                continue;
            }

            _ids.Add(frame.Id);
            _frames.Add(frame);
            added++;
        }

        if (added > 0)
        {
            Commit(ProjectChanges.Frames);
        }

        return added;
    }

    /// <summary>Removes the frames with these ids from the project; the files stay on disk. Unknown ids are ignored.</summary>
    /// <returns>How many frames were removed.</returns>
    public int RemoveFrames(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var doomed = ids.Where(_ids.Contains).ToHashSet();
        if (doomed.Count == 0)
        {
            return 0;
        }

        foreach (var frame in _frames.Where(frame => doomed.Contains(frame.Id)))
        {
            _ids.Remove(frame.Id);
            _paths.Remove(frame.SourcePath);
        }

        var removed = _frames.RemoveAll(frame => doomed.Contains(frame.Id));
        Commit(ProjectChanges.Frames);
        return removed;
    }

    /// <summary>
    /// Puts the frames in the order of <paramref name="order"/>, which must list every frame id exactly once
    /// (#15 sorts, #20 drags). Raises nothing when the order is unchanged.
    /// </summary>
    public void ReorderFrames(IReadOnlyList<Guid> order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var byId = _frames.ToDictionary(frame => frame.Id);
        if (order.Count != _frames.Count || order.Distinct().Count() != order.Count || !order.All(byId.ContainsKey))
        {
            throw new ArgumentException("The order must list every frame id in the project exactly once.", nameof(order));
        }

        if (order.SequenceEqual(_frames.Select(frame => frame.Id)))
        {
            return;
        }

        _frames.Clear();
        _frames.AddRange(order.Select(id => byId[id]));
        Commit(ProjectChanges.Frames);
    }

    /// <summary>Appends files import could not read (#13).</summary>
    public void AddSkippedFiles(IEnumerable<SkippedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var before = _skippedFiles.Count;
        foreach (var file in files)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(files));
            _skippedFiles.Add(file);
        }

        if (_skippedFiles.Count > before)
        {
            Commit(ProjectChanges.SkippedFiles);
        }
    }

    /// <summary>Records a finished export (#24): the project has no unsaved work until it changes again.</summary>
    public void MarkExported()
    {
        if (IsDirty)
        {
            IsDirty = false;
            Changed?.Invoke(this, new ProjectChangedEventArgs(ProjectChanges.Dirty));
        }
    }

    private void Set<T>(ref T field, T value, ProjectChanges change)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            Commit(change);
        }
    }

    private void Commit(ProjectChanges changes)
    {
        if (!IsDirty)
        {
            IsDirty = true;
            changes |= ProjectChanges.Dirty;
        }

        Changed?.Invoke(this, new ProjectChangedEventArgs(changes));
    }
}
