using System.Collections.Immutable;
using System.Runtime.InteropServices;
using TenLapse.Core.Projects;

namespace TenLapse.Core.Ordering;

/// <summary>
/// Puts frames in the order of a <see cref="SortMode"/>. The sort is stable and total: ties on the mode's own key
/// are broken by file name in Explorer order (<see cref="NaturalNameComparer"/>), then by the full path the same
/// way, then by the position the frames came in, so the same frames always give the same order.
/// </summary>
public static class FrameSorter
{
    /// <summary>
    /// The mode a new project starts in: <see cref="SortMode.CaptureTime"/> when there is at least one frame and
    /// every frame has a capture time, otherwise <see cref="SortMode.Name"/>. One photo without EXIF would
    /// otherwise land at an arbitrary place among the timed ones.
    /// </summary>
    public static SortMode DefaultMode(IEnumerable<Frame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var any = false;
        foreach (var frame in frames)
        {
            if (frame.CaptureTime is null)
            {
                return SortMode.Name;
            }

            any = true;
        }

        return any ? SortMode.CaptureTime : SortMode.Name;
    }

    /// <summary>
    /// The capture time of each frame as ticks, null for a frame without one: the UTC instant (wall clock minus offset) when every timed
    /// frame has a <see cref="Frame.CaptureUtcOffset"/>, otherwise the wall-clock time. Mixing the two would put a camera without
    /// offsets hours away from one with them. The capture-time sort and the interval gaps both measure with this.
    /// </summary>
    internal static long?[] CaptureTicks(IReadOnlyList<Frame> frames)
    {
        var useOffsets = false;
        foreach (var frame in frames)
        {
            if (frame.CaptureTime is null)
            {
                continue;
            }

            if (frame.CaptureUtcOffset is null)
            {
                useOffsets = false;
                break;
            }

            useOffsets = true;
        }

        var ticks = new long?[frames.Count];
        for (var i = 0; i < ticks.Length; i++)
        {
            if (frames[i].CaptureTime is { } time)
            {
                ticks[i] = useOffsets ? time.Ticks - frames[i].CaptureUtcOffset!.Value.Ticks : time.Ticks;
            }
        }

        return ticks;
    }

    /// <summary>
    /// The frames in the order of <paramref name="mode"/>.
    /// <list type="bullet">
    /// <item><see cref="SortMode.CaptureTime"/>: earliest first, to the tick (100 ns), so burst shots keep their order. When every
    /// timed frame has a <see cref="Frame.CaptureUtcOffset"/> the time is the UTC instant (wall clock minus offset), which keeps
    /// the order right across a daylight-saving change or a trip across zones; if even one timed frame has none, all are
    /// compared by their wall-clock <see cref="Frame.CaptureTime"/>. Frames without a
    /// capture time come after all the timed ones, in name order, so choosing this mode by hand never loses a frame.</item>
    /// <item><see cref="SortMode.Name"/>: the file name as Explorer sorts it, IMG_9 before IMG_10.</item>
    /// <item><see cref="SortMode.DateModified"/>: the file's last write time, oldest first.</item>
    /// <item><see cref="SortMode.Manual"/>: the order given; there is nothing to compute.</item>
    /// </list>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined mode.</exception>
    public static ImmutableArray<Frame> Sort(IEnumerable<Frame> frames, SortMode mode)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown sort mode.");
        }

        var input = frames.ToArray();
        if (mode == SortMode.Manual || input.Length < 2)
        {
            return ImmutableCollectionsMarshal.AsImmutableArray(input);
        }

        var names = new string[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            names[i] = input[i].FileName;
        }

        var natural = NaturalNameComparer.Instance;
        int ByName(int a, int b)
        {
            var byName = natural.Compare(names[a], names[b]);
            if (byName != 0)
            {
                return byName;
            }

            var byPath = natural.Compare(input[a].SourcePath, input[b].SourcePath);
            return byPath != 0 ? byPath : a.CompareTo(b);
        }

        // UTC instants when every timed frame has an offset, the cameras' wall clocks otherwise (see CaptureTicks).
        var ticks = mode == SortMode.CaptureTime ? CaptureTicks(input) : [];
        Comparison<int> comparison = mode switch
        {
            SortMode.CaptureTime => (a, b) =>
            {
                var left = ticks[a];
                var right = ticks[b];
                if (left is { } l && right is { } r)
                {
                    var byTime = l.CompareTo(r);
                    return byTime != 0 ? byTime : ByName(a, b);
                }

                // Timed frames first; the untimed ones follow in name order.
                return left is null && right is null ? ByName(a, b) : left is null ? 1 : -1;
            },
            SortMode.DateModified => (a, b) =>
            {
                var byTime = input[a].LastWriteTimeUtc.CompareTo(input[b].LastWriteTimeUtc);
                return byTime != 0 ? byTime : ByName(a, b);
            },
            _ => ByName,
        };

        var order = new int[input.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        // Every comparison ends in the input position, so no two items are equal and an unstable sort is still stable.
        Array.Sort(order, comparison);

        var sorted = new Frame[input.Length];
        for (var i = 0; i < order.Length; i++)
        {
            sorted[i] = input[order[i]];
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(sorted);
    }
}
