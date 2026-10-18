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

/// <summary>The kind of a <see cref="FrameChange"/>.</summary>
public enum FrameChangeKind
{
    /// <summary><see cref="FrameChange.Count"/> frames were inserted, the first now at <see cref="FrameChange.Index"/>.</summary>
    Added,

    /// <summary>The frames at <see cref="FrameChange.Indices"/> (positions before the change) were removed.</summary>
    Removed,

    /// <summary>The order changed (#15 sorts, #20 drags): rebind to the new list.</summary>
    Reset,
}

/// <summary>
/// What one mutation did to the frame list, so a view can update in place instead of rebuilding 5,000 rows.
/// Applying the changes in the order they were raised to the previous list gives the new one.
/// </summary>
public sealed class FrameChange
{
    private FrameChange(FrameChangeKind kind, int index, ImmutableArray<int> indices, ImmutableArray<Guid> ids)
    {
        Kind = kind;
        Index = index;
        Indices = indices;
        Ids = ids;
    }

    /// <summary>The order changed.</summary>
    public static FrameChange Reset { get; } = new(FrameChangeKind.Reset, -1, [], []);

    /// <summary>What happened.</summary>
    public FrameChangeKind Kind { get; }

    /// <summary><see cref="FrameChangeKind.Added"/>: where the first new frame now sits. Otherwise -1.</summary>
    public int Index { get; }

    /// <summary>How many frames were added or removed; 0 for a reset.</summary>
    public int Count => Ids.Length;

    /// <summary>
    /// <see cref="FrameChangeKind.Removed"/>: the removed frames' positions in the list before the change,
    /// ascending. <see cref="FrameChangeKind.Added"/>: <c>Index</c> to <c>Index + Count - 1</c>. Empty for a reset.
    /// </summary>
    public ImmutableArray<int> Indices { get; }

    /// <summary>The ids of the frames added or removed, in list order. Empty for a reset.</summary>
    public ImmutableArray<Guid> Ids { get; }

    internal static FrameChange Added(int index, IEnumerable<Frame> frames)
    {
        var ids = frames.Select(frame => frame.Id).ToImmutableArray();
        return new(FrameChangeKind.Added, index, [.. Enumerable.Range(index, ids.Length)], ids);
    }

    internal static FrameChange Removed(ImmutableArray<int> indices, ImmutableArray<Guid> ids) =>
        new(FrameChangeKind.Removed, -1, indices, ids);
}

/// <summary>Carries the <see cref="ProjectChanges"/> of one mutation.</summary>
public sealed class ProjectChangedEventArgs(ProjectChanges changes, ProjectSnapshot snapshot, FrameChange? frames = null) : EventArgs
{
    /// <summary>What changed.</summary>
    public ProjectChanges Changes { get; } = changes;

    /// <summary>The project as this change left it.</summary>
    public ProjectSnapshot Snapshot { get; } = snapshot;

    /// <summary>What happened to the frame list, when <see cref="Changes"/> has <see cref="ProjectChanges.Frames"/>; otherwise null.</summary>
    public FrameChange? Frames { get; } = frames;
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
    /// id or path is already in the project, or earlier in the same batch, is left out, so adding a folder
    /// twice does not double it. Paths compare case-insensitively in their canonical spelling
    /// (<see cref="Frame.NormalizePath"/>). The whole batch is read and checked before anything changes: if
    /// it holds a null, or enumerating it throws, the project stays as it was.
    /// </summary>
    /// <returns>How many frames were added.</returns>
    public int AddFrames(IEnumerable<Frame> frames)
    {
        EnsureWritable();
        return Insert(_state.Frames.Length, frames);
    }

    /// <summary>
    /// Inserts frames so the first sits at <paramref name="index"/>, in one change (#20's undo puts deleted
    /// frames back where they were: one call per contiguous run, in ascending order). Duplicates are left out
    /// as in <see cref="AddFrames"/>, and the batch is checked whole first.
    /// </summary>
    /// <param name="index">From 0 (before the first frame) to <c>Frames.Length</c> (after the last).</param>
    /// <param name="frames">The frames, in the order they should play.</param>
    /// <returns>How many frames were inserted.</returns>
    public int InsertFrames(int index, IEnumerable<Frame> frames)
    {
        EnsureWritable();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _state.Frames.Length);
        return Insert(index, frames);
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
        var indices = ImmutableArray.CreateBuilder<int>(doomed.Count);
        var removedIds = ImmutableArray.CreateBuilder<Guid>(doomed.Count);
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = frames[i];
            if (doomed.Contains(frame.Id))
            {
                indices.Add(i);
                removedIds.Add(frame.Id);
                _ids.Remove(frame.Id);
                _paths.Remove(frame.SourcePath);
            }
        }

        Commit(
            _state with { Frames = frames.RemoveAll(frame => doomed.Contains(frame.Id)) },
            ProjectChanges.Frames,
            FrameChange.Removed(indices.MoveToImmutable(), removedIds.MoveToImmutable()));
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

        Commit(_state with { Frames = [.. order.Select(id => byId[id])] }, ProjectChanges.Frames, FrameChange.Reset);
    }

    /// <summary>
    /// Appends files import could not read (#13). The whole batch is read and checked first: a null entry, or
    /// an enumeration that throws, leaves the project as it was.
    /// </summary>
    public void AddSkippedFiles(IEnumerable<SkippedFile> files)
    {
        EnsureWritable();
        var batch = Materialize(files, nameof(files));
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

    private static List<T> Materialize<T>(IEnumerable<T> items, string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items, parameterName);
        var batch = items.ToList();
        if (batch.Any(item => item is null))
        {
            throw new ArgumentNullException(parameterName, "The batch holds a null entry; nothing was changed.");
        }

        return batch;
    }

    // The frames of the batch that are new to the project, in order, without touching any state.
    private List<Frame> NewFrames(IEnumerable<Frame> frames)
    {
        var batch = Materialize(frames, nameof(frames));
        var ids = new HashSet<Guid>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. batch.Where(frame =>
            !_ids.Contains(frame.Id) && !_paths.Contains(frame.SourcePath) && ids.Add(frame.Id) && paths.Add(frame.SourcePath))];
    }

    private int Insert(int index, IEnumerable<Frame> frames)
    {
        var added = NewFrames(frames);
        if (added.Count == 0)
        {
            return 0;
        }

        foreach (var frame in added)
        {
            _ids.Add(frame.Id);
            _paths.Add(frame.SourcePath);
        }

        Commit(
            _state with { Frames = _state.Frames.InsertRange(index, added) },
            ProjectChanges.Frames,
            FrameChange.Added(index, added));
        return added.Count;
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
    private void Commit(ProjectSnapshot next, ProjectChanges changes, FrameChange? frames = null)
    {
        if (!_state.IsDirty)
        {
            changes |= ProjectChanges.Dirty;
        }

        Publish(next with { Version = _state.Version + 1, IsDirty = true }, changes, frames);
    }

    private void Publish(ProjectSnapshot next, ProjectChanges changes, FrameChange? frames = null)
    {
        _state = next;
        Changed?.Invoke(this, new ProjectChangedEventArgs(changes, next, frames));
    }
}
