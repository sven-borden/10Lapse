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
    /// Sorts the project's frames by <paramref name="mode"/> (one <see cref="Project.ReorderFrames"/> commit, none when the
    /// order does not change) and records the mode in <see cref="Project.SortMode"/>. <see cref="SortMode.Manual"/> only
    /// records the mode: the frames stay where they are.
    /// </summary>
    /// <returns>True when the order of the frames changed.</returns>
    public static bool ApplySort(this Project project, SortMode mode)
    {
        ArgumentNullException.ThrowIfNull(project);
        var before = project.Version;
        if (mode != SortMode.Manual)
        {
            var sorted = FrameSorter.Sort(project.Frames, mode);
            project.ReorderFrames([.. sorted.Select(frame => frame.Id)]);
        }

        var reordered = project.Version != before;
        project.SortMode = mode;
        return reordered;
    }

    /// <summary>
    /// Sorts by the mode the frames call for (<see cref="FrameSorter.DefaultMode"/>: capture time when every frame has one,
    /// otherwise name). For a project that was just filled by an import; it overrides a mode chosen earlier.
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
        project.ReorderFrames(order);
        project.SortMode = SortMode.Manual;
    }
}
