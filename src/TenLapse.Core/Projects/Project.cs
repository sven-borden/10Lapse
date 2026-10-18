using System.Collections.Immutable;

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
public sealed class ProjectChangedEventArgs(ProjectChanges changes, ProjectSnapshot snapshot) : EventArgs
{
    /// <summary>What changed.</summary>
    public ProjectChanges Changes { get; } = changes;

    /// <summary>The project as this change left it.</summary>
    public ProjectSnapshot Snapshot { get; } = snapshot;
}

/// <summary>
/// The whole state of a <see cref="Project"/> at one <see cref="Version"/>. Immutable, so any thread can hold
/// one for as long as it likes (the preview's draw thread, #19; the encoder, #22): it never changes under it.
/// </summary>
public sealed record ProjectSnapshot
{
    internal static ProjectSnapshot Empty { get; } = new();

    private ProjectSnapshot()
    {
    }

    /// <summary>Counts the changes: 0 for a new project, one more after every mutation that changed something.</summary>
    public long Version { get; internal init; }

    /// <summary>The frames in playback order.</summary>
    public ImmutableArray<Frame> Frames { get; internal init; } = [];

    /// <summary>The files import could not read, in the order they were reported.</summary>
    public ImmutableArray<SkippedFile> SkippedFiles { get; internal init; } = [];

    /// <summary>The project name.</summary>
    public string Name { get; internal init; } = Project.DefaultName;

    /// <summary>The speed.</summary>
    public TimingSettings Timing { get; internal init; } = TimingSettings.Default;

    /// <summary>The output shape.</summary>
    public FrameSettings FrameSettings { get; internal init; } = FrameSettings.Default;

    /// <summary>Resolution, format and quality.</summary>
    public OutputSettings Output { get; internal init; } = OutputSettings.Default;

    /// <summary>The order the user chose.</summary>
    public SortMode SortMode { get; internal init; } = SortMode.CaptureTime;

    /// <summary>True after any change since the project was created or last exported.</summary>
    public bool IsDirty { get; internal init; }
}

/// <summary>
/// One timelapse being edited: the ordered frames, the files import skipped, and the timing, frame, output
/// and sort settings. Every mutation goes through a method or setter here, publishes a new
/// <see cref="ProjectSnapshot"/>, marks the project dirty and raises <see cref="Changed"/> once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Single writer, many readers.</b> Only the thread that created the project (the UI thread) may change
/// it; every mutating method and setter throws <see cref="InvalidOperationException"/> on any other thread
/// (#12 marshals its import batches to the UI thread). Any thread may read: <see cref="Snapshot"/>,
/// <see cref="Frames"/> and the other getters read one immutable snapshot that a mutation replaces in a
/// single reference write, so a reader never sees a half-made change. A reader that needs several values to
/// agree (the frames and the timing, say) takes <see cref="Snapshot"/> once and reads from it.
/// </para>
/// <para>
/// A project is never cleared: <c>New project</c> replaces the whole instance, so nothing of the old one can
/// survive into the new one. Removing a frame removes it from the project, never from disk.
/// </para>
/// </remarks>
public sealed class Project
{
    /// <summary>The name of a project that has no folder to be named after.</summary>
    public const string DefaultName = "Timelapse";

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    // Owner-thread bookkeeping for the duplicate checks; readers never touch these.
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _ids = [];

    private volatile ProjectSnapshot _state = ProjectSnapshot.Empty;

    /// <summary>Raised after every mutation, once per call, on the owner thread.</summary>
    public event EventHandler<ProjectChangedEventArgs>? Changed;

    /// <summary>The current state, whole. Safe on any thread.</summary>
    public ProjectSnapshot Snapshot => _state;

    /// <summary>See <see cref="ProjectSnapshot.Version"/>.</summary>
    public long Version => _state.Version;

    /// <summary>The frames in playback order: an immutable snapshot, safe on any thread.</summary>
    public ImmutableArray<Frame> Frames => _state.Frames;

    /// <summary>The files import could not read, in the order they were reported: an immutable snapshot.</summary>
    public ImmutableArray<SkippedFile> SkippedFiles => _state.SkippedFiles;

    /// <summary>
    /// The project name, for the title bar and the suggested export file name. Setting null, empty or
    /// white space gives <see cref="DefaultName"/>; the value is trimmed.
    /// </summary>
    public string Name
    {
        get => _state.Name;
        set
        {
            EnsureWritable();
            var name = string.IsNullOrWhiteSpace(value) ? DefaultName : value.Trim();
            if (name != _state.Name)
            {
                Commit(_state with { Name = name }, ProjectChanges.Name);
            }
        }
    }

    /// <summary>The speed (#17).</summary>
    public TimingSettings Timing
    {
        get => _state.Timing;
        set
        {
            if (Changes(_state.Timing, value))
            {
                Commit(_state with { Timing = value }, ProjectChanges.Timing);
            }
        }
    }

    /// <summary>The output shape (#18).</summary>
    public FrameSettings FrameSettings
    {
        get => _state.FrameSettings;
        set
        {
            if (Changes(_state.FrameSettings, value))
            {
                Commit(_state with { FrameSettings = value }, ProjectChanges.FrameSettings);
            }
        }
    }

    /// <summary>Resolution, format and quality (#23).</summary>
    public OutputSettings Output
    {
        get => _state.Output;
        set
        {
            if (Changes(_state.Output, value))
            {
                Commit(_state with { Output = value }, ProjectChanges.Output);
            }
        }
    }

    /// <summary>The order the user chose (#15). Setting it does not reorder; #15 calls <see cref="ReorderFrames"/>.</summary>
    public SortMode SortMode
    {
        get => _state.SortMode;
        set
        {
            EnsureWritable();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown sort mode.");
            }

            if (value != _state.SortMode)
            {
                Commit(_state with { SortMode = value }, ProjectChanges.SortMode);
            }
        }
    }

    /// <summary>True after any change since the project was created or last exported.</summary>
    public bool IsDirty => _state.IsDirty;

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
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(frames);

        var added = new List<Frame>();
        foreach (var frame in frames)
        {
            ArgumentNullException.ThrowIfNull(frame, nameof(frames));
            if (_ids.Contains(frame.Id) || !_paths.Add(frame.SourcePath))
            {
                continue;
            }

            _ids.Add(frame.Id);
            added.Add(frame);
        }

        if (added.Count > 0)
        {
            Commit(_state with { Frames = _state.Frames.AddRange(added) }, ProjectChanges.Frames);
        }

        return added.Count;
    }

    /// <summary>Removes the frames with these ids from the project; the files stay on disk. Unknown ids are ignored.</summary>
    /// <returns>How many frames were removed.</returns>
    public int RemoveFrames(IEnumerable<Guid> ids)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ids);

        var doomed = ids.Where(_ids.Contains).ToHashSet();
        if (doomed.Count == 0)
        {
            return 0;
        }

        var frames = _state.Frames;
        foreach (var frame in frames.Where(frame => doomed.Contains(frame.Id)))
        {
            _ids.Remove(frame.Id);
            _paths.Remove(frame.SourcePath);
        }

        Commit(_state with { Frames = frames.RemoveAll(frame => doomed.Contains(frame.Id)) }, ProjectChanges.Frames);
        return doomed.Count;
    }

    /// <summary>
    /// Puts the frames in the order of <paramref name="order"/>, which must list every frame id exactly once
    /// (#15 sorts, #20 drags). Raises nothing when the order is unchanged.
    /// </summary>
    public void ReorderFrames(IReadOnlyList<Guid> order)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(order);

        var frames = _state.Frames;
        var byId = frames.ToDictionary(frame => frame.Id);
        if (order.Count != frames.Length || order.Distinct().Count() != order.Count || !order.All(byId.ContainsKey))
        {
            throw new ArgumentException("The order must list every frame id in the project exactly once.", nameof(order));
        }

        if (order.SequenceEqual(frames.Select(frame => frame.Id)))
        {
            return;
        }

        Commit(_state with { Frames = [.. order.Select(id => byId[id])] }, ProjectChanges.Frames);
    }

    /// <summary>Appends files import could not read (#13).</summary>
    public void AddSkippedFiles(IEnumerable<SkippedFile> files)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(files);

        var batch = files.ToList();
        foreach (var file in batch)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(files));
        }

        if (batch.Count > 0)
        {
            Commit(_state with { SkippedFiles = _state.SkippedFiles.AddRange(batch) }, ProjectChanges.SkippedFiles);
        }
    }

    /// <summary>Records a finished export (#24): the project has no unsaved work until it changes again.</summary>
    public void MarkExported()
    {
        EnsureWritable();
        if (_state.IsDirty)
        {
            Publish(_state with { IsDirty = false }, ProjectChanges.Dirty);
        }
    }

    private bool Changes<T>(T current, T value)
        where T : class
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(value);
        return !EqualityComparer<T>.Default.Equals(current, value);
    }

    private void EnsureWritable()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "A project is changed only on the thread that created it (the UI thread). Other threads read its Snapshot.");
        }
    }

    // A real change: the next version, dirty.
    private void Commit(ProjectSnapshot next, ProjectChanges changes)
    {
        if (!_state.IsDirty)
        {
            changes |= ProjectChanges.Dirty;
        }

        Publish(next with { Version = _state.Version + 1, IsDirty = true }, changes);
    }

    private void Publish(ProjectSnapshot next, ProjectChanges changes)
    {
        _state = next;
        Changed?.Invoke(this, new ProjectChangedEventArgs(changes, next));
    }
}
