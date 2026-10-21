using System.Collections.Immutable;
using TenLapse.Core.Text;

namespace TenLapse.Core.Projects;

/// <summary>What a <see cref="Project.Changed"/> notification covers.</summary>
[Flags]
public enum ProjectChanges
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Frames were added, removed, reordered or updated; <see cref="ProjectChangedEventArgs.Frames"/> says which.</summary>
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

    /// <summary><see cref="Project.FilePath"/> changed (#52: the project was opened from, saved to or restored for a file).</summary>
    File = 1 << 8,

    /// <summary><see cref="Project.Look"/> changed (#45: deflicker and its strength).</summary>
    /// <remarks>Bit 10: #49 takes bit 9 for its sequence settings.</remarks>
    Look = 1 << 10,
}

/// <summary>The kind of a <see cref="FrameChange"/>.</summary>
public enum FrameChangeKind
{
    /// <summary>
    /// <see cref="FrameChange.Count"/> frames were inserted, at the positions <see cref="FrameChange.Indices"/> (after the
    /// change, ascending), the first at <see cref="FrameChange.Index"/>. An import's are contiguous; an undo of a removal
    /// (#20, <see cref="FrameHistory"/>) puts frames back where each was, so its positions may be scattered. A consumer
    /// inserts at each of the <see cref="FrameChange.Indices"/> in order, never a run from <see cref="FrameChange.Index"/>.
    /// </summary>
    Added,

    /// <summary>The frames at <see cref="FrameChange.Indices"/> (positions before the change) were removed.</summary>
    Removed,

    /// <summary>The order changed (#15 sorts, #20 drags): rebind to the new list.</summary>
    Reset,

    /// <summary>
    /// The frames at <see cref="FrameChange.Indices"/> were replaced by new values with the same id and path
    /// (#13 and #15 fill in the pixel size and capture time). The order did not change.
    /// </summary>
    Updated,
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

    /// <summary><see cref="FrameChangeKind.Added"/>: where the first new frame now sits (<c>Indices[0]</c>). Otherwise -1.</summary>
    public int Index { get; }

    /// <summary>How many frames were added, removed or updated; 0 for a reset.</summary>
    public int Count => Ids.Length;

    /// <summary>
    /// <see cref="FrameChangeKind.Removed"/>: the removed frames' positions in the list before the change,
    /// ascending. <see cref="FrameChangeKind.Added"/>: the new frames' positions in the list after the change, ascending:
    /// <c>Index</c> to <c>Index + Count - 1</c> for <see cref="Project.AddFrames"/> and <see cref="Project.InsertFrames"/>,
    /// and possibly scattered when an undo puts removed frames back where each was (#20, <see cref="FrameHistory"/>).
    /// <see cref="FrameChangeKind.Updated"/>: the updated frames' positions, ascending. Empty for a reset.
    /// </summary>
    public ImmutableArray<int> Indices { get; }

    /// <summary>The ids of the frames added, removed or updated, in list order. Empty for a reset.</summary>
    public ImmutableArray<Guid> Ids { get; }

    internal static FrameChange Added(int index, IEnumerable<Frame> frames)
    {
        var ids = frames.Select(frame => frame.Id).ToImmutableArray();
        return new(FrameChangeKind.Added, index, [.. Enumerable.Range(index, ids.Length)], ids);
    }

    // Frames put back at scattered places in one change (FrameHistory's undo of a removal): still an Added delta.
    internal static FrameChange Restored(ImmutableArray<int> indices, ImmutableArray<Guid> ids) =>
        new(FrameChangeKind.Added, indices[0], indices, ids);

    internal static FrameChange Removed(ImmutableArray<int> indices, ImmutableArray<Guid> ids) =>
        new(FrameChangeKind.Removed, -1, indices, ids);

    internal static FrameChange Updated(ImmutableArray<int> indices, ImmutableArray<Guid> ids) =>
        new(FrameChangeKind.Updated, -1, indices, ids);
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

    /// <summary>The Look section (#45): deflicker and its strength. The correction it gives each photo is <see cref="Look.ExposureCurve.For"/>.</summary>
    public LookSettings Look { get; internal init; } = LookSettings.Default;

    /// <summary>True after any change since the project was created, opened, saved or (while it has no file) exported.</summary>
    public bool IsDirty { get; internal init; }

    /// <summary>The <c>.10lapse</c> file this project was opened from or last saved to (#52), or null while it has none.</summary>
    public string? FilePath { get; internal init; }

    /// <summary>The rate these frames play and export at: <see cref="TimingSettings.EffectiveRate"/> for <see cref="Frames"/>.</summary>
    public PlaybackRate EffectiveRate => Timing.EffectiveRate(Frames.Length);

    /// <summary>
    /// The video's width and height in pixels, which the export writes and the preview draws: the resolution
    /// (short side) and shape, with Match photos taking the most common photo size (<see cref="OutputSize.For(OutputResolution, FrameAspect, IEnumerable{Frame})"/>).
    /// A GIF (#51) takes its own size preset instead (<see cref="GifOutput.Size(GifSize, FrameAspect, IEnumerable{Frame})"/>).
    /// </summary>
    public PixelSize VideoSize => Output.Format == VideoFormat.Gif
        ? GifOutput.Size(Output.Gif.Size, FrameSettings.Aspect, Frames)
        : OutputSize.For(Output.Resolution, FrameSettings.Aspect, Frames);

    /// <summary>
    /// This snapshot with <paramref name="output"/> in place of <see cref="Output"/>, and nothing else changed (the same
    /// <see cref="Version"/>): what #24 hands the exporter, with the Output panel's effective output (#23), so the
    /// export writes that resolution, codec and quality and its <see cref="VideoSize"/>, never the stored preference.
    /// </summary>
    public ProjectSnapshot WithOutput(OutputSettings output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output == Output ? this : this with { Output = output };
    }
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
/// A project is never cleared: <c>New project</c> replaces the whole instance and <see cref="Close"/>s the old
/// one, so nothing of the old one can survive into the new one and nothing late can be added to the old one.
/// Removing a frame removes it from the project, never from disk.
/// </para>
/// </remarks>
public sealed class Project
{
    /// <summary>The name of a project that has no folder to be named after ("Timelapse", from <c>Strings/CoreText.resx</c>).</summary>
    public static string DefaultName => CoreText.Strings.Get();

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    // Owner-thread bookkeeping for the duplicate checks; readers never touch these.
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _ids = [];

    private volatile ProjectSnapshot _state;
    private volatile bool _closed;

    /// <summary>Creates an empty, clean project with the default settings, owned by the calling thread.</summary>
    public Project()
        : this(TimingSettings.Default, FrameSettings.Default, OutputSettings.Default, SortMode.CaptureTime)
    {
    }

    /// <summary>
    /// Creates an empty, clean project that starts with these settings (#21 passes the user's defaults), owned
    /// by the calling thread. Setting the properties afterwards (or in an object initializer) is an edit and
    /// makes the project dirty; this constructor does not.
    /// </summary>
    public Project(TimingSettings timing, FrameSettings frameSettings, OutputSettings output, SortMode sortMode)
        : this(timing, frameSettings, output, sortMode, LookSettings.Default)
    {
    }

    /// <summary>
    /// Creates an empty, clean project that starts with these settings and this look (#45; a project file opens with its
    /// own), owned by the calling thread.
    /// </summary>
    public Project(TimingSettings timing, FrameSettings frameSettings, OutputSettings output, SortMode sortMode, LookSettings look)
    {
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(frameSettings);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(look);
        if (!Enum.IsDefined(sortMode))
        {
            throw new ArgumentOutOfRangeException(nameof(sortMode), sortMode, "Unknown sort mode.");
        }

        _state = ProjectSnapshot.Empty with { Timing = timing, FrameSettings = frameSettings, Output = output, SortMode = sortMode, Look = look };
    }

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

    /// <summary>The rate the current frames play at (see <see cref="ProjectSnapshot.EffectiveRate"/>).</summary>
    public PlaybackRate EffectiveRate => _state.EffectiveRate;

    /// <summary>The speed (#17): the mode and each mode's input. The rate is <see cref="EffectiveRate"/>.</summary>
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

    /// <summary>The Look section (#45): deflicker and its strength.</summary>
    public LookSettings Look
    {
        get => _state.Look;
        set
        {
            if (Changes(_state.Look, value))
            {
                Commit(_state with { Look = value }, ProjectChanges.Look);
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

    /// <summary>True after any change since the project was created, opened, saved or (while it has no file) exported.</summary>
    public bool IsDirty => _state.IsDirty;

    /// <summary>The <c>.10lapse</c> file this project was opened from or last saved to (#52), or null while it has none.</summary>
    public string? FilePath => _state.FilePath;

    /// <summary>True once <see cref="Close"/> ran: the project was replaced and takes no more changes.</summary>
    public bool IsClosed => _closed;

    /// <summary>
    /// True when replacing this project would lose something the user did: it changed since it was created, opened or
    /// saved, or exported while it had no file. A fresh project, a saved one, and one without a file that was exported
    /// since its last change, have nothing to lose.
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
        return Remove(ids, []);
    }

    /// <summary>
    /// Removes the frames with these ids and appends <paramref name="skipped"/> to <see cref="SkippedFiles"/> in one
    /// commit (#12: files that turn out unreadable while an import reads them), so a reader never sees a frame gone
    /// that is not listed as skipped yet. One <see cref="Changed"/> event with <see cref="ProjectChanges.Frames"/> (a
    /// <see cref="FrameChangeKind.Removed"/> delta) and <see cref="ProjectChanges.SkippedFiles"/>, each as far as it
    /// really changes; none when nothing does. Unknown ids are ignored; the skipped files are checked whole first, so a
    /// null entry changes nothing. The files stay on disk.
    /// </summary>
    /// <returns>How many frames were removed.</returns>
    public int RemoveFramesAndSkip(IEnumerable<Guid> ids, IEnumerable<SkippedFile> skipped)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ids);
        return Remove(ids, Materialize(skipped, nameof(skipped)));
    }

    private int Remove(IEnumerable<Guid> ids, List<SkippedFile> skipped)
    {
        var doomed = ids.Where(_ids.Contains).ToHashSet();
        if (doomed.Count == 0)
        {
            if (skipped.Count > 0)
            {
                Commit(_state with { SkippedFiles = _state.SkippedFiles.AddRange(skipped) }, ProjectChanges.SkippedFiles);
            }

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

        var next = _state with { Frames = frames.RemoveAll(frame => doomed.Contains(frame.Id)) };
        var changes = ProjectChanges.Frames;
        if (skipped.Count > 0)
        {
            next = next with { SkippedFiles = next.SkippedFiles.AddRange(skipped) };
            changes |= ProjectChanges.SkippedFiles;
        }

        Commit(next, changes, FrameChange.Removed(indices.MoveToImmutable(), removedIds.MoveToImmutable()));
        return doomed.Count;
    }

    /// <summary>
    /// Puts frames back in one change (#20: <see cref="FrameHistory"/> undoes a removal): adds <paramref name="frames"/>
    /// and leaves the frames in the order of <paramref name="order"/>, which lists every frame of the project and every
    /// frame put back, once each. The frames already in the project keep their order among themselves, so the change is
    /// one <see cref="FrameChangeKind.Added"/> delta whose <see cref="FrameChange.Indices"/> need not be contiguous. A
    /// frame whose id or path is in the project already is left out (and its id in <paramref name="order"/> ignored), as
    /// in <see cref="AddFrames"/>. An invalid order throws and changes nothing.
    /// </summary>
    /// <returns>How many frames were put back.</returns>
    internal int RestoreFrames(IReadOnlyList<Frame> frames, IReadOnlyList<Guid> order)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(order);
        var batch = Materialize(frames, nameof(frames));
        var added = NewFrames(batch);
        var addedById = added.ToDictionary(frame => frame.Id);
        var leftOut = batch.Select(frame => frame.Id).Where(id => !addedById.ContainsKey(id)).ToHashSet();
        var current = _state.Frames;
        var currentById = current.ToDictionary(frame => frame.Id);
        var next = ImmutableArray.CreateBuilder<Frame>(current.Length + added.Count);
        var indices = ImmutableArray.CreateBuilder<int>(added.Count);
        var ids = ImmutableArray.CreateBuilder<Guid>(added.Count);
        var seen = new HashSet<Guid>();
        var kept = 0;
        foreach (var id in order)
        {
            if (!seen.Add(id))
            {
                throw new ArgumentException("The order lists a frame twice; nothing was changed.", nameof(order));
            }

            if (currentById.TryGetValue(id, out var frame))
            {
                if (current[kept].Id != id)
                {
                    throw new ArgumentException("The frames already in the project must keep their order; nothing was changed.", nameof(order));
                }

                kept++;
                next.Add(frame);
            }
            else if (addedById.TryGetValue(id, out frame))
            {
                indices.Add(next.Count);
                ids.Add(id);
                next.Add(frame);
            }
            else if (!leftOut.Contains(id))
            {
                throw new ArgumentException($"The order lists {id}, which is neither in the project nor put back; nothing was changed.", nameof(order));
            }
        }

        if (kept != current.Length || ids.Count != added.Count)
        {
            throw new ArgumentException("The order must list every frame of the project and every frame put back; nothing was changed.", nameof(order));
        }

        if (added.Count == 0)
        {
            return 0;
        }

        foreach (var frame in added)
        {
            _ids.Add(frame.Id);
            _paths.Add(frame.SourcePath);
        }

        Commit(_state with { Frames = next.MoveToImmutable() }, ProjectChanges.Frames, FrameChange.Restored(indices.MoveToImmutable(), ids.MoveToImmutable()));
        return added.Count;
    }

    /// <summary>
    /// Replaces frames by id in one change, keeping their positions: #13 writes back the
    /// <see cref="Frame.PixelSize"/> and #15 the <see cref="Frame.CaptureTime"/> with
    /// <c>frame with { ... }</c>. Each frame must already be in the project with the same
    /// <see cref="Frame.SourcePath"/>, and appear once in the batch; otherwise nothing changes. A value equal
    /// to the current one is not a change.
    /// </summary>
    /// <returns>How many frames changed.</returns>
    /// <exception cref="ArgumentException">An id is unknown or repeated, or a path differs.</exception>
    public int UpdateFrames(IEnumerable<Frame> frames)
    {
        EnsureWritable();
        var batch = Materialize(frames, nameof(frames));
        var current = _state.Frames;
        var positions = new Dictionary<Guid, int>(current.Length);
        for (var i = 0; i < current.Length; i++)
        {
            positions[current[i].Id] = i;
        }

        var replacements = new SortedDictionary<int, Frame>();
        foreach (var frame in batch)
        {
            if (!positions.TryGetValue(frame.Id, out var index))
            {
                throw new ArgumentException($"No frame with id {frame.Id} is in the project; nothing was changed.", nameof(frames));
            }

            if (!string.Equals(current[index].SourcePath, frame.SourcePath, StringComparison.Ordinal))
            {
                throw new ArgumentException($"An update keeps the frame's path ({current[index].SourcePath}); nothing was changed.", nameof(frames));
            }

            if (!replacements.TryAdd(index, frame))
            {
                throw new ArgumentException($"The frame {frame.Id} is in the batch twice; nothing was changed.", nameof(frames));
            }
        }

        var changed = replacements.Where(pair => !pair.Value.Equals(current[pair.Key])).ToList();
        if (changed.Count == 0)
        {
            return 0;
        }

        var next = current.ToBuilder();
        foreach (var (index, frame) in changed)
        {
            next[index] = frame;
        }

        Commit(
            _state with { Frames = next.MoveToImmutable() },
            ProjectChanges.Frames,
            FrameChange.Updated([.. changed.Select(pair => pair.Key)], [.. changed.Select(pair => pair.Value.Id)]));
        return changed.Count;
    }

    /// <summary>
    /// Puts the frames in the order of <paramref name="order"/>, which must list every frame id exactly once
    /// (#15 sorts, #20 drags). Raises nothing when the order is unchanged.
    /// </summary>
    public void ReorderFrames(IReadOnlyList<Guid> order)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(order);
        ReorderAndSetMode(order, _state.SortMode);
    }

    /// <summary>
    /// <see cref="ReorderFrames"/> and <see cref="SortMode"/> in one commit, so a listener never sees the new order under the
    /// old mode (or the new mode over the old order). One <see cref="Changed"/> event with <see cref="ProjectChanges.Frames"/>
    /// and <see cref="ProjectChanges.SortMode"/> as far as each really changes; none when neither does. An invalid order
    /// or mode throws and changes nothing. For <c>TenLapse.Core.Ordering</c>.
    /// </summary>
    internal void ReorderAndSetMode(IReadOnlyList<Guid> order, SortMode mode)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(order);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown sort mode.");
        }

        var frames = _state.Frames;
        var byId = frames.ToDictionary(frame => frame.Id);
        if (order.Count != frames.Length || order.Distinct().Count() != order.Count || !order.All(byId.ContainsKey))
        {
            throw new ArgumentException("The order must list every frame id in the project exactly once.", nameof(order));
        }

        var next = _state;
        var changes = ProjectChanges.None;
        if (!order.SequenceEqual(frames.Select(frame => frame.Id)))
        {
            next = next with { Frames = [.. order.Select(id => byId[id])] };
            changes |= ProjectChanges.Frames;
        }

        if (mode != _state.SortMode)
        {
            next = next with { SortMode = mode };
            changes |= ProjectChanges.SortMode;
        }

        if (changes != ProjectChanges.None)
        {
            Commit(next, changes, (changes & ProjectChanges.Frames) != 0 ? FrameChange.Reset : null);
        }
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

    /// <summary>
    /// Records a finished export (#24): a project without a file has no unsaved work until it changes again, but only if
    /// nothing changed while the export ran. #24 reads <see cref="Version"/> (from the snapshot it encodes)
    /// when the export starts and passes it here when it finishes; a change made meanwhile is not in the file,
    /// so the project stays dirty. Clearing the flag does not change <see cref="Version"/>. A project with a
    /// <see cref="FilePath"/> (#52) keeps its flag: the video does not hold the project, only Save writes the file.
    /// </summary>
    /// <param name="versionAtExportStart">The <see cref="ProjectSnapshot.Version"/> the export encoded.</param>
    /// <returns>True when the project is clean afterwards.</returns>
    public bool MarkExported(long versionAtExportStart)
    {
        EnsureWritable();
        return _state.FilePath is null ? MarkClean(versionAtExportStart) : !_state.IsDirty;
    }

    /// <summary>
    /// Records a save (#52): the project has no unsaved work until it changes again, but only if nothing changed since
    /// the snapshot that was written (<paramref name="versionWritten"/>, read before the write). Call
    /// <see cref="SetFile"/> first. Does not change <see cref="Version"/>.
    /// </summary>
    /// <returns>True when the project is clean afterwards.</returns>
    public bool MarkSaved(long versionWritten)
    {
        EnsureWritable();
        return MarkClean(versionWritten);
    }

    /// <summary>
    /// Ties the project to its <c>.10lapse</c> file (#52: opened from, saved to, or restored for it): <see cref="FilePath"/>
    /// becomes <paramref name="path"/> (made full) and <see cref="Name"/> the file's name without its extension, as a
    /// document takes its file's name. Not an edit: it neither marks the project dirty nor cleans it, nor changes
    /// <see cref="Version"/>. One <see cref="Changed"/> with <see cref="ProjectChanges.File"/> (and
    /// <see cref="ProjectChanges.Name"/> when the name changes); none when nothing does.
    /// </summary>
    public void SetFile(string path)
    {
        EnsureWritable();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var name = Path.GetFileNameWithoutExtension(full) is { } stem && !string.IsNullOrWhiteSpace(stem) ? stem.Trim() : DefaultName;
        var changes = ProjectChanges.None;
        if (!string.Equals(full, _state.FilePath, StringComparison.Ordinal))
        {
            changes |= ProjectChanges.File;
        }

        if (name != _state.Name)
        {
            changes |= ProjectChanges.Name;
        }

        if (changes != ProjectChanges.None)
        {
            Publish(_state with { FilePath = full, Name = name }, changes);
        }
    }

    private bool MarkClean(long version)
    {
        if (version != _state.Version)
        {
            return false;
        }

        if (_state.IsDirty)
        {
            Publish(_state with { IsDirty = false }, ProjectChanges.Dirty);
        }

        return true;
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

    /// <summary>
    /// Ends this project's life: <c>New project</c> closes the project it replaces. From then on every
    /// mutation throws <see cref="ObjectDisposedException"/>, so a batch from an import that was still running
    /// can never land in a project the user left (#12 treats that exception as "cancelled"). Reading keeps
    /// working. Safe on any thread, and a second call does nothing.
    /// </summary>
    public void Close() => _closed = true;

    private bool Changes<T>(T current, T value)
        where T : class
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(value);
        return !EqualityComparer<T>.Default.Equals(current, value);
    }

    private void EnsureWritable()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
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
