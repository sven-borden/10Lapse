using TenLapse.Core.Projects;

namespace TenLapse.Core.Ordering;

/// <summary>
/// How a <see cref="Project"/> gets its frame order: pick a <see cref="SortMode"/> and the frames are reordered by
/// it, or drag a frame and the mode becomes <see cref="SortMode.Manual"/>. Like every <see cref="Project"/> mutation these
/// run on the thread that created the project. Removing frames (<see cref="Project.RemoveFrames"/>) takes them out of
/// the project and never touches the files.
/// </summary>
public static class ProjectOrdering
{
    /// <summary>
    /// Sorts the project's frames by <paramref name="mode"/> and records the mode in <see cref="Project.SortMode"/>.
    /// <see cref="SortMode.Manual"/> only records the mode: the frames stay where they are.
    /// <para>
    /// <b>Events.</b> One <see cref="Project.Changed"/> event, with <see cref="ProjectChanges.Frames"/> and
    /// <see cref="ProjectChanges.SortMode"/> set as far as each really changed, so a listener never sees the new order under
    /// the old mode. No event at all when neither changes. A drag (<see cref="ReorderManually"/>) is one event the same way.
    /// </para>
    /// </summary>
    /// <returns>True when the order of the frames changed.</returns>
    public static bool ApplySort(this Project project, SortMode mode)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (mode == SortMode.Manual)
        {
            project.SortMode = mode;
            return false;
        }

        var before = project.Frames.Select(frame => frame.Id).ToArray();
        var sorted = FrameSorter.Sort(project.Frames, mode);
        var order = sorted.Select(frame => frame.Id).ToArray();
        project.ReorderAndSetMode(order, mode);
        return !order.AsSpan().SequenceEqual(before);
    }

    /// <summary>
    /// Sorts by the mode the frames call for (<see cref="FrameSorter.DefaultMode"/>: capture time when every frame has one,
    /// otherwise name). For a project that was empty before the import: it overrides the mode chosen earlier, a manual
    /// order included. When an import adds frames to a project that already had some, call <see cref="Resort"/> instead,
    /// which keeps the project's own mode and leaves a manual order alone (new frames at the end).
    /// </summary>
    /// <returns>The mode that was applied.</returns>
    public static SortMode ApplyDefaultSort(this Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var mode = FrameSorter.DefaultMode(project.Frames);
        project.ApplySort(mode);
        return mode;
    }

    /// <summary>
    /// Sorts again by the project's own mode: after frames were added, or after their capture times were filled in
    /// (<see cref="Project.UpdateFrames"/>). Does nothing in <see cref="SortMode.Manual"/>, where new frames stay at the end.
    /// </summary>
    /// <returns>True when the order of the frames changed.</returns>
    public static bool Resort(this Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.ApplySort(project.SortMode);
    }

    /// <summary>
    /// A drag in the filmstrip (#20): puts the frames in this order and switches the mode to
    /// <see cref="SortMode.Manual"/>, so a later import does not sort the user's order away. The order must list every
    /// frame id once (<see cref="Project.ReorderFrames"/>); if it does not, nothing changes, the mode included. The
    /// mode becomes manual even when the drop leaves the order as it was.
    /// </summary>
    public static void ReorderManually(this Project project, IReadOnlyList<Guid> order)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.ReorderAndSetMode(order, SortMode.Manual);
    }
}
