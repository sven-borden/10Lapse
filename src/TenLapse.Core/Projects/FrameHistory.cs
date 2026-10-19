using System.Collections.Immutable;

namespace TenLapse.Core.Projects;

/// <summary>What a <see cref="FrameHistory"/> entry did to the frames.</summary>
public enum FrameEditKind
{
    /// <summary>Frames were removed from the project (never from disk).</summary>
    Removal,

    /// <summary>Frames were dragged or moved to another place (the order became manual).</summary>
    Reorder,
}

/// <summary>An edit that can be undone: what it did, and to how many frames.</summary>
/// <param name="Kind">Removal or reorder.</param>
/// <param name="Count">The frames removed, or the frames moved.</param>
public sealed record FrameEdit(FrameEditKind Kind, int Count);

/// <summary>What <see cref="FrameHistory.Undo"/> did.</summary>
/// <param name="Kind">The kind of edit undone.</param>
/// <param name="Frames">The frames put back (a removal) or moved back (a reorder) that are in the project now, in playback order.</param>
public sealed record FrameUndo(FrameEditKind Kind, ImmutableArray<Guid> Frames);

/// <summary>
/// The filmstrip's undo (#20): the smallest history that fits the project model. It records the two frame edits the user
/// makes in the filmstrip, removal and reorder, and undoes the latest one with Ctrl+Z as one project change. Nothing else
/// is undoable: timing, shape and output settings, imports, sorting by a mode, and the frames an import drops because
/// they cannot be read. There is no redo.
/// </summary>
/// <remarks>
/// <para>
/// An entry keeps the frame list from before the edit (an immutable snapshot array, shared with the project, so an
/// entry costs one reference), and undo works from it against the project as it is then. Undoing a removal puts each
/// frame back right after the frame that preceded it before the removal, or the nearest earlier one still there, so with
/// nothing changed in between every frame returns to its exact place, and after other changes (an import, another
/// removal) it still lands beside its old neighbours. Undoing a reorder restores the earlier order and sort mode; frames
/// added since keep their places at the end. A frame whose file was imported again meanwhile is not put back twice.
/// </para>
/// <para>
/// One history per project, on the project's owner thread (the UI thread), like every <see cref="Project"/> mutation. It
/// keeps the latest <see cref="Capacity"/> edits.
/// </para>
/// </remarks>
public sealed class FrameHistory
{
    /// <summary>The most edits kept; older ones are forgotten.</summary>
    public const int Capacity = 100;

    private readonly Project _project;
    private readonly LinkedList<Entry> _entries = new();

    /// <summary>A history for <paramref name="project"/>.</summary>
    public FrameHistory(Project project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    /// <summary>The project the edits apply to.</summary>
    public Project Project => _project;

    /// <summary>True when there is an edit to undo.</summary>
    public bool CanUndo => _entries.Count > 0;

    /// <summary>The edit <see cref="Undo"/> would undo, or null.</summary>
    public FrameEdit? NextUndo => _entries.Last?.Value.Edit;

    /// <summary>
    /// Removes the frames with these ids from the project (<see cref="Project.RemoveFrames"/>, never from disk) and records
    /// the removal. Unknown ids are ignored; nothing is recorded when nothing was removed.
    /// </summary>
    /// <returns>How many frames were removed.</returns>
    public int Remove(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.ToHashSet();
        var before = _project.Frames;
        var removed = before.Where(frame => wanted.Contains(frame.Id)).ToImmutableArray();
        if (removed.IsEmpty)
        {
            return 0;
        }

        var count = _project.RemoveFrames(wanted);
        if (count > 0)
        {
            Push(new Entry(new FrameEdit(FrameEditKind.Removal, count), before, _project.SortMode, removed, []));
        }

        return count;
    }

    /// <summary>
    /// A reorder by the user (a drag, or Alt+arrow): puts the frames in <paramref name="order"/> and switches the sort mode
    /// to <see cref="SortMode.Manual"/> in one change (as <c>ProjectOrdering.ReorderManually</c>), and records it. The order
    /// must list every frame id once; otherwise it throws and nothing changes. Nothing is recorded when neither the order
    /// nor the mode changed.
    /// </summary>
    /// <param name="order">Every frame id, in the new order.</param>
    /// <param name="moved">The frames the user moved, so the undo can select them again; empty for none in particular.</param>
    /// <returns>True when the project changed.</returns>
    public bool Reorder(IReadOnlyList<Guid> order, IEnumerable<Guid>? moved = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        var before = _project.Frames;
        var mode = _project.SortMode;
        var version = _project.Version;
        _project.ReorderAndSetMode(order, SortMode.Manual);
        if (_project.Version == version)
        {
            return false;
        }

        var movedIds = (moved ?? []).ToImmutableArray();
        Push(new Entry(new FrameEdit(FrameEditKind.Reorder, movedIds.Length), before, mode, [], movedIds));
        return true;
    }

    /// <summary>
    /// Undoes the latest edit as one project change and forgets it. Null when there is nothing to undo. An undo that finds
    /// nothing left to do (every removed frame is back already) still uses up its entry and says so with an empty
    /// <see cref="FrameUndo.Frames"/>.
    /// </summary>
    public FrameUndo? Undo()
    {
        if (_entries.Last is not { } last)
        {
            return null;
        }

        var entry = last.Value;

        // The entry goes first: an undo that throws (a closed project) is not tried again and again.
        _entries.RemoveLast();
        return entry.Edit.Kind == FrameEditKind.Removal ? UndoRemoval(entry) : UndoReorder(entry);
    }

    /// <summary>Forgets every edit.</summary>
    public void Clear() => _entries.Clear();

    private FrameUndo UndoRemoval(Entry entry)
    {
        var current = _project.Frames;
        var present = current.Select(frame => frame.Id).ToHashSet();
        var paths = current.Select(frame => frame.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var back = entry.Removed.Where(frame => !present.Contains(frame.Id) && !paths.Contains(frame.SourcePath)).ToList();
        if (back.Count == 0)
        {
            return new FrameUndo(FrameEditKind.Removal, []);
        }

        // Each frame goes after the last frame before it (in the old order) that is still in the project; frames with
        // none go first. Frames of one run stay together, in their old order.
        var returning = back.Select(frame => frame.Id).ToHashSet();
        var atStart = new List<Guid>();
        var after = new Dictionary<Guid, List<Guid>>();
        Guid? anchor = null;
        foreach (var frame in entry.Before)
        {
            if (present.Contains(frame.Id))
            {
                anchor = frame.Id;
            }
            else if (returning.Contains(frame.Id))
            {
                if (anchor is not { } id)
                {
                    atStart.Add(frame.Id);
                }
                else if (after.TryGetValue(id, out var run))
                {
                    run.Add(frame.Id);
                }
                else
                {
                    after[id] = [frame.Id];
                }
            }
        }

        var order = new List<Guid>(current.Length + back.Count);
        order.AddRange(atStart);
        foreach (var frame in current)
        {
            order.Add(frame.Id);
            if (after.TryGetValue(frame.Id, out var run))
            {
                order.AddRange(run);
            }
        }

        _project.RestoreFrames(back, order);
        var now = _project.Frames.Select(frame => frame.Id).ToHashSet();
        return new FrameUndo(FrameEditKind.Removal, [.. order.Where(id => returning.Contains(id) && now.Contains(id))]);
    }

    private FrameUndo UndoReorder(Entry entry)
    {
        var current = _project.Frames;
        var present = current.Select(frame => frame.Id).ToHashSet();
        var earlier = entry.Before.Select(frame => frame.Id).ToHashSet();
        var order = entry.Before.Select(frame => frame.Id).Where(present.Contains)
            .Concat(current.Select(frame => frame.Id).Where(id => !earlier.Contains(id)))
            .ToList();
        _project.ReorderAndSetMode(order, entry.Mode);
        var moved = entry.Moved.Where(present.Contains).ToHashSet();
        return new FrameUndo(FrameEditKind.Reorder, [.. order.Where(moved.Contains)]);
    }

    private void Push(Entry entry)
    {
        _entries.AddLast(entry);
        while (_entries.Count > Capacity)
        {
            _entries.RemoveFirst();
        }
    }

    private sealed record Entry(FrameEdit Edit, ImmutableArray<Frame> Before, SortMode Mode, ImmutableArray<Frame> Removed, ImmutableArray<Guid> Moved);
}
