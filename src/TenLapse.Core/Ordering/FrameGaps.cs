using System.Collections.Immutable;
using TenLapse.Core.Projects;

namespace TenLapse.Core.Ordering;

/// <summary>Why a gap marker sits between two frames.</summary>
[Flags]
public enum FrameGapKinds
{
    /// <summary>Not a gap.</summary>
    None = 0,

    /// <summary>The file numbers jump: <c>IMG_0041</c> is followed by <c>IMG_0045</c> and three photos are missing.</summary>
    Numbering = 1,

    /// <summary>The time between two shots is much longer than the shots around it.</summary>
    CaptureInterval = 2,
}

/// <summary>
/// A place in the frame order where photos are probably missing. The marker sits between frame
/// <see cref="AfterIndex"/> and the next one (#20 draws it in that seam).
/// </summary>
/// <param name="AfterIndex">The index of the frame before the gap; the gap is between it and <c>AfterIndex + 1</c>.</param>
/// <param name="Kinds">What was seen: a numbering jump, a long interval, or both.</param>
/// <param name="MissingFrames">
/// With <see cref="FrameGapKinds.Numbering"/>, how many numbers are skipped (IMG_0041 to IMG_0045 is 3); otherwise 0.
/// </param>
/// <param name="Interval">With <see cref="FrameGapKinds.CaptureInterval"/>, the time between the two frames; otherwise null.</param>
/// <param name="TypicalInterval">With <see cref="FrameGapKinds.CaptureInterval"/>, the median interval around it; otherwise null.</param>
public sealed record FrameGap(
    int AfterIndex,
    FrameGapKinds Kinds,
    int MissingFrames,
    TimeSpan? Interval,
    TimeSpan? TypicalInterval)
{
    /// <summary>
    /// For an interval gap, about how many shots fit in it: the interval divided by the typical interval, less the
    /// one that is there. At least 1. Zero when this is a numbering gap only.
    /// </summary>
    public int EstimatedMissingByTime => Interval is { } interval && TypicalInterval is { } typical && typical > TimeSpan.Zero
        ? Math.Max(1, (int)Math.Round(interval / typical) - 1)
        : 0;
}

/// <summary>
/// Finds places in an ordered list of frames where photos are probably missing, for the filmstrip's gap markers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Numbering.</b> A name is split at its last run of digits (before the extension): <c>IMG_0041.jpg</c> is the
/// prefix <c>IMG_</c>, the number 41 and an empty suffix. Frames with the same prefix and suffix (case ignored, any
/// extension, any folder) form one sequence, so two cameras with different prefixes are separate sequences even when
/// their frames are interleaved by capture time. Walking the frames in order, a frame whose number is more than one
/// above the previous number of its sequence has a gap in front of it, of the numbers in between. A number that is
/// not higher is no gap (duplicates, a RAW and a JPEG of one shot, a second folder), except a wrap: when the previous
/// number is in the top 1 percent of its counter range and the next in the bottom 1 percent (the range is the next power
/// of ten above the previous number, at least 1000: 9900 to 9999, then 0 to 100) the numbers left at the top and those
/// skipped after the bottom are the gap. 9999 to 0001 is none, 9999 to 0005 is 4 (0001 to 0004), 9998 to 0002 is 2
/// (9999 and 0001). The marker goes in front of the frame whose
/// number jumped, also when frames of other sequences sit between it and the previous frame of its own. A name made from
/// the time the photo was taken (<c>IMG_20240101_123456</c>, <c>PXL_20240101_123456789</c>, <c>20240101_123456(1)</c>) has no
/// frame counter, so it is not numbered: see <c>LooksLikeTimestamp</c> for the rule.
/// </para>
/// <para>
/// <b>Interval.</b> For each pair of neighbours that both have a capture time, in order, the interval is an outlier when
/// it is at least one second and more than three times the median of up to five intervals on each side of it (a median
/// under one second counts as one second, so whole-second EXIF times at several shots a second cannot flag every step). Fewer
/// than three intervals around it, or a pair out of time order, never flag. The one-second floor keeps a burst
/// (a tenth of a second between shots, a pause of half a second) from flagging.
/// </para>
/// <para>
/// <b>Modes.</b> A marker says "a shot is missing between these two", which only holds when neighbours are meant to be
/// consecutive. <see cref="Detect"/> therefore takes the project's <see cref="SortMode"/>: numbering is looked for in name
/// and capture-time order, intervals in capture-time order only, and a manual or date-modified order gets no markers.
/// </para>
/// </remarks>
public static class FrameGaps
{
    /// <summary>An interval must be at least this long to be an outlier.</summary>
    public static TimeSpan MinimumInterval { get; } = TimeSpan.FromSeconds(1);

    /// <summary>An interval must exceed this many times the median of its neighbours to be an outlier.</summary>
    public const int OutlierFactor = 3;

    private const int NeighboursEachSide = 5;
    private const int MinimumNeighbours = 3;
    private const int MaxDigits = 18;
    private const int MinimumTimeDigits = 6;

    /// <summary>
    /// The gaps in <paramref name="frames"/>, in playback order, at most one per place. Which kinds are looked for depends
    /// on the order the frames are in (<paramref name="mode"/>, the project's <see cref="Project.SortMode"/>): numbering
    /// jumps only in <see cref="SortMode.Name"/> and <see cref="SortMode.CaptureTime"/>, where neighbours are meant to be
    /// consecutive shots; interval outliers only in <see cref="SortMode.CaptureTime"/>. In <see cref="SortMode.Manual"/> and
    /// <see cref="SortMode.DateModified"/> there are no markers. #20 passes the project's mode and draws what comes back.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined mode.</exception>
    public static ImmutableArray<FrameGap> Detect(IReadOnlyList<Frame> frames, SortMode mode = SortMode.CaptureTime)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown sort mode.");
        }

        if (frames.Count < 2 || mode is SortMode.Manual or SortMode.DateModified)
        {
            return [];
        }

        var numbering = DetectNumbering(frames);
        var intervals = mode == SortMode.CaptureTime ? DetectIntervals(frames) : [];

        var gaps = ImmutableArray.CreateBuilder<FrameGap>();
        for (var after = 0; after < frames.Count - 1; after++)
        {
            numbering.TryGetValue(after, out var missing);
            IntervalOutlier? interval = intervals.TryGetValue(after, out var outlier) ? outlier : null;
            if (missing == 0 && interval is null)
            {
                continue;
            }

            gaps.Add(new FrameGap(
                after,
                (missing > 0 ? FrameGapKinds.Numbering : FrameGapKinds.None) | (interval is null ? FrameGapKinds.None : FrameGapKinds.CaptureInterval),
                missing,
                interval?.Interval,
                interval?.Typical));
        }

        return gaps.ToImmutable();
    }

    // The gap in front of frame i is reported "after" frame i - 1.
    private static Dictionary<int, int> DetectNumbering(IReadOnlyList<Frame> frames)
    {
        var missingAfter = new Dictionary<int, int>();
        var last = new Dictionary<(string Prefix, string Suffix), long>(SequenceKeyComparer.Instance);
        for (var i = 0; i < frames.Count; i++)
        {
            if (!TryParse(frames[i].FileName, out var key, out var current))
            {
                continue;
            }

            if (last.TryGetValue(key, out var previous))
            {
                var missing = MissingBetween(previous, current);
                if (missing > 0)
                {
                    missingAfter[i - 1] = (int)Math.Min(missing, int.MaxValue);
                }
            }

            last[key] = current;
        }

        return missingAfter;
    }

    private static long MissingBetween(long previous, long current)
    {
        if (current > previous)
        {
            return current - previous - 1;
        }

        // The counter wrapped (9999 to 0001): the numbers left before the top, plus those skipped after the bottom.
        if (current < previous && IsWrap(previous, current, out var top))
        {
            return (top - previous) + Math.Max(0, current - 1);
        }

        return 0;
    }

    // A wrap is the previous number in the top 1 percent of its counter's range and the next in the bottom 1 percent.
    // The range is the smallest power of ten above the previous number: for 9998 it is 10,000, so the wrap is from
    // 9900 to 9999 and on to 0 to 100, and 9998 to 0002 has 9999 and 0001 missing. (Going by the value, not the zero
    // padding, makes Sony's five-digit DSC09999 to DSC00001 a wrap too.) A counter needs three digits to count: one of
    // 9 or 99 is more likely a short run of copies than a camera. `top` is the last number of the range.
    private static bool IsWrap(long previous, long current, out long top)
    {
        top = 0;
        if (previous < 990)
        {
            return false;
        }

        var range = 1000L;
        while (range <= previous)
        {
            range *= 10;
        }

        top = range - 1;
        var onePercent = range / 100;
        return previous >= range - onePercent && current <= onePercent;
    }

    private readonly record struct IntervalOutlier(TimeSpan Interval, TimeSpan Typical);

    private static Dictionary<int, IntervalOutlier> DetectIntervals(IReadOnlyList<Frame> frames)
    {
        // intervals[i] is the time from frame i - 1 to frame i; null when either has no time or they are out of order.
        var intervals = new TimeSpan?[frames.Count];
        for (var i = 1; i < frames.Count; i++)
        {
            if (frames[i - 1].CaptureTime is { } before && frames[i].CaptureTime is { } after && after >= before)
            {
                intervals[i] = after - before;
            }
        }

        var found = new Dictionary<int, IntervalOutlier>();
        var neighbours = new List<TimeSpan>(2 * NeighboursEachSide);
        for (var i = 1; i < frames.Count; i++)
        {
            if (intervals[i] is not { } interval || interval < MinimumInterval)
            {
                continue;
            }

            neighbours.Clear();
            Collect(intervals, i - 1, -1, neighbours);
            Collect(intervals, i + 1, 1, neighbours);
            if (neighbours.Count < MinimumNeighbours)
            {
                continue;
            }

            neighbours.Sort();
            var middle = neighbours.Count / 2;
            var median = neighbours.Count % 2 == 1
                ? neighbours[middle]
                : TimeSpan.FromTicks((neighbours[middle - 1].Ticks + neighbours[middle].Ticks) / 2);

            // Whole-second times with several shots a second give a median of 0 (or under a second): measure against
            // the one-second floor then, or every 1 s step would be an outlier.
            median = median < MinimumInterval ? MinimumInterval : median;
            if (interval > median * OutlierFactor)
            {
                found[i - 1] = new IntervalOutlier(interval, median);
            }
        }

        return found;

        static void Collect(TimeSpan?[] all, int start, int step, List<TimeSpan> into)
        {
            var taken = 0;
            for (var j = start; j >= 1 && j < all.Length && taken < NeighboursEachSide; j += step)
            {
                if (all[j] is { } value)
                {
                    into.Add(value);
                    taken++;
                }
            }
        }
    }

    // A name made from the time the photo was taken has no frame counter in it: IMG_20240101_123456, Pixel's
    // PXL_20240101_123456789, 20240101_123456 and Samsung's 20240101_123456(1) would all give a "jump" at nearly every
    // seam. Numbering is skipped for such a name when
    //  - it holds a date run (8 digits starting 19 or 20) followed by _ or - and a time run of 6 or more digits, or
    //  - it holds a run of exactly 14 digits starting 19 or 20 (yyyyMMddHHmmss), or
    //  - the counter run (the last one) has 6 or more digits and the text before it already has a digit.
    // A short counter after a date (IMG_20240101_0041) is still a counter.
    private static bool LooksLikeTimestamp(ReadOnlySpan<char> stem, int counterStart, int counterEnd)
    {
        if (counterEnd - counterStart >= MinimumTimeDigits && stem[..counterStart].IndexOfAnyInRange('0', '9') >= 0)
        {
            return true;
        }

        var i = 0;
        while (i < stem.Length)
        {
            if (!char.IsAsciiDigit(stem[i]))
            {
                i++;
                continue;
            }

            var runStart = i;
            while (i < stem.Length && char.IsAsciiDigit(stem[i]))
            {
                i++;
            }

            var run = stem[runStart..i];
            if (run.Length < 8 || !(run.StartsWith("19", StringComparison.Ordinal) || run.StartsWith("20", StringComparison.Ordinal)))
            {
                continue;
            }

            if (run.Length == 14)
            {
                return true;
            }

            if (run.Length == 8 && i < stem.Length && stem[i] is '_' or '-')
            {
                var timeEnd = i + 1;
                while (timeEnd < stem.Length && char.IsAsciiDigit(stem[timeEnd]))
                {
                    timeEnd++;
                }

                if (timeEnd - (i + 1) >= MinimumTimeDigits)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // "IMG_0041 (2).jpg" -> prefix "IMG_0041 (", number 2, suffix ")". The extension is not part of the key.
    private static bool TryParse(string fileName, out (string Prefix, string Suffix) key, out long number)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName.AsSpan());
        var end = stem.Length;
        while (end > 0 && !char.IsAsciiDigit(stem[end - 1]))
        {
            end--;
        }

        var start = end;
        while (start > 0 && char.IsAsciiDigit(stem[start - 1]))
        {
            start--;
        }

        if (end == 0 || end - start > MaxDigits || LooksLikeTimestamp(stem, start, end))
        {
            key = default;
            number = default;
            return false;
        }

        key = (stem[..start].ToString(), stem[end..].ToString());
        number = long.Parse(stem[start..end], System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private sealed class SequenceKeyComparer : IEqualityComparer<(string Prefix, string Suffix)>
    {
        public static SequenceKeyComparer Instance { get; } = new();

        public bool Equals((string Prefix, string Suffix) x, (string Prefix, string Suffix) y) =>
            string.Equals(x.Prefix, y.Prefix, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Suffix, y.Suffix, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Prefix, string Suffix) key) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Prefix), StringComparer.OrdinalIgnoreCase.GetHashCode(key.Suffix));
    }
}
