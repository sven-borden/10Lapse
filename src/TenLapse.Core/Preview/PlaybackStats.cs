using System.Globalization;

namespace TenLapse.Core.Preview;

/// <summary>What one presented picture showed (<see cref="PlaybackStats.Record"/>).</summary>
public enum PresentedImage
{
    /// <summary>The frame itself from its proxy (or a full decode).</summary>
    Proxy,

    /// <summary>The frame itself, from a small decode while its proxy was not built yet.</summary>
    Quick,

    /// <summary>Not the frame: its image was not loaded in time, so the previous picture stayed (or a placeholder).</summary>
    Held,
}

/// <summary>
/// Frame pacing of a playback run (#19's measurement): which frames reached the screen, how late, for how long and
/// which were dropped. The render loop records every present; the Debug harness (<c>--preview-bench</c>) prints the
/// summary. Not thread-safe: one recorder.
/// </summary>
public sealed class PlaybackStats
{
    private readonly List<(long Timestamp, long Occurrence, Int128 OccurrenceStart, PresentedImage Image)> _presents = [];
    private readonly PlaybackClock _clock;

    /// <summary>Starts recording against <paramref name="clock"/>, the clock the run plays with.</summary>
    public PlaybackStats(PlaybackClock clock) => _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Records a present at <paramref name="timestamp"/> (the clock's units) of the frame <paramref name="reading"/> named.</summary>
    public void Record(long timestamp, PlaybackReading reading, PresentedImage image) =>
        _presents.Add((timestamp, reading.Occurrence, reading.OccurrenceStart, image));

    /// <summary>The summary so far.</summary>
    public PlaybackSummary Summarize()
    {
        var firsts = new List<(long Occurrence, long Timestamp, Int128 Start)>();
        var held = 0;
        foreach (var present in _presents)
        {
            if (present.Image == PresentedImage.Held)
            {
                held++;
                continue;
            }

            if (firsts.Count == 0 || firsts[^1].Occurrence != present.Occurrence)
            {
                firsts.Add((present.Occurrence, present.Timestamp, present.OccurrenceStart));
            }
        }

        var due = _presents.Count == 0 ? 0 : _presents[^1].Occurrence - _presents[0].Occurrence + 1;
        var shown = firsts.Select(first => first.Occurrence).Distinct().Count();
        var lateness = firsts
            .Select(first => Milliseconds(first.Timestamp - _clock.TimestampAt(first.Start)))
            .ToList();
        var onScreen = firsts.Zip(firsts.Skip(1), (a, b) => Milliseconds(b.Timestamp - a.Timestamp)).ToList();
        var quick = _presents.Count(present => present.Image == PresentedImage.Quick);
        return new PlaybackSummary(
            Presents: _presents.Count,
            FramesDue: due,
            FramesShown: shown,
            HeldPresents: held,
            QuickPresents: quick,
            IdealFrameMilliseconds: 1000.0 * _clock.Rate.Denominator / _clock.Rate.Numerator,
            Lateness: Distribution.Of(lateness),
            OnScreen: Distribution.Of(onScreen));
    }

    private double Milliseconds(long timestampDelta) => timestampDelta * 1000.0 / _clock.Frequency;
}

/// <summary>A playback run's pacing (<see cref="PlaybackStats.Summarize"/>).</summary>
/// <param name="Presents">Pictures presented.</param>
/// <param name="FramesDue">Frames the clock passed through, first to last present.</param>
/// <param name="FramesShown">Of those, the frames that reached the screen with their own image.</param>
/// <param name="HeldPresents">Presents that kept the previous picture because the frame's image was not loaded.</param>
/// <param name="QuickPresents">Presents of a small decode (no proxy yet).</param>
/// <param name="IdealFrameMilliseconds">How long each frame should stay on screen.</param>
/// <param name="Lateness">How long after its ideal start each frame first appeared (ms).</param>
/// <param name="OnScreen">How long each shown frame stayed before the next one appeared (ms).</param>
public sealed record PlaybackSummary(
    int Presents,
    long FramesDue,
    int FramesShown,
    int HeldPresents,
    int QuickPresents,
    double IdealFrameMilliseconds,
    Distribution Lateness,
    Distribution OnScreen)
{
    /// <summary>Frames the clock passed that never reached the screen.</summary>
    public long Dropped => Math.Max(0, FramesDue - FramesShown);

    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{FramesShown} of {FramesDue} frames shown, {Dropped} dropped, {HeldPresents} held presents, {QuickPresents} quick; " +
        $"on screen {OnScreen} ms (ideal {IdealFrameMilliseconds:0.00}); late {Lateness} ms");
}

/// <summary>Mean, percentiles and extremes of a set of measurements.</summary>
public sealed record Distribution(int Count, double Mean, double Min, double P5, double Median, double P95, double Max)
{
    /// <summary>The distribution of <paramref name="values"/>; all zero when there are none.</summary>
    public static Distribution Of(IReadOnlyCollection<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return new Distribution(0, 0, 0, 0, 0, 0, 0);
        }

        var sorted = values.Order().ToArray();
        return new Distribution(sorted.Length, sorted.Average(), sorted[0], Percentile(sorted, 0.05), Percentile(sorted, 0.5), Percentile(sorted, 0.95), sorted[^1]);
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"mean {Mean:0.00}, min {Min:0.00}, p5 {P5:0.00}, median {Median:0.00}, p95 {P95:0.00}, max {Max:0.00} (n {Count})");

    // Nearest rank.
    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];
}
