using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TenLapse.Core.Projects;

namespace TenLapse.Core.Look;

/// <summary>
/// Deflicker's correction (#45): for each photo of a project, the exposure change in stops that puts it on a smooth
/// brightness curve. One pass for both problems of a sunset sequence: frame-to-frame flicker, and the steps of an
/// exposure changed by hand (or by the camera) across the day-to-night change, which become a ramp while the evening
/// still gets darker.
/// </summary>
/// <remarks>
/// <para>
/// From each photo's measured brightness <c>m</c> (<see cref="PhotoExposure.Luminance"/>) and, where its Exif block has
/// them, its exposure <c>e = log2(t × ISO / 100 / N²)</c> (shutter time, ISO, f-number): the scene's own light is
/// <c>s = m − e</c>, smooth apart from flicker, and the exposure <c>e</c> is a staircase. The target brightness is
/// <c>s'</c> + <c>e'</c>: the scene curve through a median of five and a short Gaussian (<see cref="FlickerSigma"/>
/// photos), which removes flicker and keeps the scene's own darkening; and the staircase through the same median and a
/// long Gaussian (<see cref="RampSigma"/> photos), which turns each step into a ramp (a step survives a median; one
/// wrong value does not, so it cannot swell the ramp). Each photo's correction is the target minus <c>m</c>, held to
/// ±<see cref="MaxCorrection"/> stops and scaled by the strength. Nothing is flattened: the darkening the scene and the
/// photographer's own steps add up to stays in the curve, only its jumps go.
/// </para>
/// <para>
/// Both smoothings are local linear fits with Gaussian weights: in the middle of the sequence a plain Gaussian mean, at
/// either end a fit that follows a trend instead of bending it toward the mean. The median continues each end along the
/// line through its first (last) twelve values, so a trend is not bent there either. Below <see cref="MinimumForMedian"/>
/// photos there is no median: a few photos cannot tell flicker from the scene's own change, and the local linear fits
/// alone keep any steady darkening whole. Photos are spaced by their place in the project, not their capture time: a
/// photo is one frame on screen.
/// </para>
/// <para>
/// The exposure is decided photo by photo, factor by factor. A shutter time, f-number or ISO outside a sane range
/// (<see cref="MinExposureTime"/> to <see cref="MaxExposureTime"/> s, f/<see cref="MinFNumber"/> to
/// f/<see cref="MaxFNumber"/>, ISO <see cref="MinIso"/> to <see cref="MaxIso"/>) is a misread or a hand-edited file and is
/// rejected. A photo without a factor (a copy whose Exif was stripped, a frame from another camera) takes the value of
/// its nearer neighbour that has it, the earlier on a tie: exposure changes in steps, so a gap is filled with a step,
/// never a ramp, and if the fill is wrong it is one photo off, which the median removes. A factor no photo has (a lens
/// without electronic contacts writes no f-number) is taken as unchanged. Without Exif exposure the staircase is flat, so
/// a jump in brightness is smoothed over the short window only: a real event (street lights coming on) is never
/// stretched into a long ramp on a guess.
/// </para>
/// <para>
/// A photo not measured yet, or that could not be, takes its correction from its measured neighbours, interpolated by its
/// place between them (the nearer one's at either end), so it never flashes between corrected photos.
/// </para>
/// <para>
/// Keyed by photo (<see cref="Frame.Id"/>), never by an output frame: a photo held, looped or played back and forth
/// gets the same correction every time. The curve follows the project's order; a reorder computes a new one. It is
/// remembered per frame list and look, so an edit that changes neither (the timing, the output) reuses it, and the
/// preview's render thread can ask without waiting (<see cref="TryGet"/>).
/// </para>
/// </remarks>
public sealed class ExposureCurve
{
    /// <summary>The short smoothing, in photos: the Gaussian's sigma for flicker.</summary>
    public const double FlickerSigma = 4;

    /// <summary>The long smoothing, in photos: the Gaussian's sigma that ramps an exposure step.</summary>
    public const double RampSigma = 20;

    /// <summary>The largest correction, in stops, either way (4× brighter amplifies noise as much).</summary>
    public const double MaxCorrection = 2;

    /// <summary>The fewest photos the median of five runs on (2 × 2 + 3); shorter sequences are only fitted.</summary>
    public const int MinimumForMedian = (2 * MedianRadius) + 3;

    /// <summary>The shortest shutter time taken as real, in seconds (1/100,000 s).</summary>
    public const double MinExposureTime = 1e-5;

    /// <summary>The longest shutter time taken as real, in seconds (an hour).</summary>
    public const double MaxExposureTime = 3600;

    /// <summary>The smallest f-number taken as real.</summary>
    public const double MinFNumber = 0.5;

    /// <summary>The largest f-number taken as real.</summary>
    public const double MaxFNumber = 128;

    /// <summary>The lowest ISO taken as real.</summary>
    public const double MinIso = 1;

    /// <summary>The highest ISO taken as real.</summary>
    public const double MaxIso = 10_000_000;

    private const int MedianRadius = 2;

    // The values each end's line is fitted to before the median continues along it: the short smoothing's reach (3 sigma),
    // so the line is as steady as the curve it feeds.
    private const int EndFit = 12;

    // One curve per frame list (the snapshot's array) and look, computed once by whichever thread asks first.
    private static readonly ConditionalWeakTable<Frame[], Cache> _byFrames = [];
    private static readonly Frame[] _noFrames = [];

    private readonly FrozenDictionary<Guid, double> _corrections;

    private ExposureCurve(FrozenDictionary<Guid, double> corrections)
    {
        _corrections = corrections;
    }

    /// <summary>No correction for any photo: deflicker is off, or fewer than two photos are measured.</summary>
    public static ExposureCurve None { get; } = new(FrozenDictionary<Guid, double>.Empty);

    /// <summary>How many photos have a correction (measured or interpolated).</summary>
    public int Count => _corrections.Count;

    /// <summary>
    /// The curve of <paramref name="snapshot"/>'s photos and look, computed once per frame list and look (waiting for a
    /// computation <see cref="TryGet"/> started). <see cref="None"/> while deflicker is off. The export's call.
    /// </summary>
    public static ExposureCurve For(ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Look.Deflicker ? Entry(snapshot, background: false).GetAwaiter().GetResult() : None;
    }

    /// <summary>
    /// The curve of <paramref name="snapshot"/> without waiting: true with it when it is known (or deflicker is off);
    /// otherwise false, and it is computed on the thread pool, which calls <paramref name="ready"/> when it is done. The
    /// preview's render thread asks this way, and draws with the curve it had until then.
    /// </summary>
    public static bool TryGet(ProjectSnapshot snapshot, Action? ready, out ExposureCurve curve)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.Look.Deflicker)
        {
            curve = None;
            return true;
        }

        var task = Entry(snapshot, background: true);
        if (task.IsCompleted)
        {
            curve = task.GetAwaiter().GetResult();
            return true;
        }

        if (ready is not null)
        {
            _ = task.ContinueWith(_ => ready(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        curve = None;
        return false;
    }

    /// <summary>The curve of <paramref name="frames"/> (in playback order) with <paramref name="look"/>.</summary>
    public static ExposureCurve Compute(IReadOnlyList<Frame> frames, LookSettings look)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(look);
        if (!look.Deflicker || look.Strength == 0)
        {
            return None;
        }

        var places = new List<int>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i].Exposure is not null)
            {
                places.Add(i);
            }
        }

        if (places.Count < 2)
        {
            return None;
        }

        var measured = places.Select(i => frames[i].Exposure!).ToArray();
        var brightness = measured.Select(exposure => exposure.Luminance).ToArray();
        var corrections = Corrections(brightness, Exposures(measured), look.Strength / 100.0);
        var byId = new Dictionary<Guid, double>(frames.Count);
        var next = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            while (next < places.Count && places[next] < i)
            {
                next++;
            }

            byId[frames[i].Id] = next < places.Count && places[next] == i
                ? corrections[next]
                : Between(places, corrections, next, i);
        }

        return new ExposureCurve(byId.ToFrozenDictionary());
    }

    /// <summary>
    /// The corrections in stops for a sequence of brightnesses <paramref name="brightness"/> and camera exposures
    /// <paramref name="exposure"/> (log2, any origin; zeros for none), at <paramref name="strength"/> (0 to 1). The core of
    /// <see cref="Compute"/>, for tests on synthetic sequences.
    /// </summary>
    public static double[] Corrections(IReadOnlyList<double> brightness, IReadOnlyList<double> exposure, double strength)
    {
        ArgumentNullException.ThrowIfNull(brightness);
        ArgumentNullException.ThrowIfNull(exposure);
        if (brightness.Count != exposure.Count)
        {
            throw new ArgumentException("One exposure per brightness.", nameof(exposure));
        }

        var count = brightness.Count;
        var scene = new double[count];
        var steps = new double[count];
        for (var i = 0; i < count; i++)
        {
            scene[i] = brightness[i] - exposure[i];
            steps[i] = exposure[i];
        }

        var median = count >= MinimumForMedian;
        var smoothScene = LocalLinear(median ? Median(scene, MedianRadius) : scene, FlickerSigma);
        var ramp = LocalLinear(median ? Median(steps, MedianRadius) : steps, RampSigma);
        var corrections = new double[count];
        for (var i = 0; i < count; i++)
        {
            var target = smoothScene[i] + ramp[i];
            corrections[i] = Math.Clamp(target - brightness[i], -MaxCorrection, MaxCorrection) * strength;
        }

        return corrections;
    }

    /// <summary>The photo's correction in stops; 0 for a photo the curve does not know.</summary>
    public double Correction(Guid frameId) => _corrections.GetValueOrDefault(frameId);

    /// <summary>The photo's correction as a factor on linear light: 2 to the power of <see cref="Correction"/>, 1 for none.</summary>
    public float Gain(Guid frameId) => _corrections.TryGetValue(frameId, out var stops) ? (float)Math.Pow(2, stops) : 1f;

    private static Task<ExposureCurve> Entry(ProjectSnapshot snapshot, bool background)
    {
        var frames = snapshot.Frames;
        var look = snapshot.Look;
        var cache = _byFrames.GetValue(ImmutableCollectionsMarshal.AsArray(frames) ?? _noFrames, static _ => new Cache());
        lock (cache)
        {
            if (cache.Curve is { } known && look.Equals(cache.Look))
            {
                return known;
            }

            cache.Look = look;
            return cache.Curve = background
                ? Task.Run(() => Compute(frames, look))
                : Task.FromResult(Compute(frames, look));
        }
    }

    // The correction of the unmeasured photo at place i, from the measured photos around it: places[next] is the first
    // measured one after it (next == places.Count when there is none).
    private static double Between(List<int> places, double[] corrections, int next, int i)
    {
        if (next == 0)
        {
            return corrections[0];
        }

        if (next == places.Count)
        {
            return corrections[^1];
        }

        var (from, to) = (places[next - 1], places[next]);
        var share = (i - from) / (double)(to - from);
        return corrections[next - 1] + ((corrections[next] - corrections[next - 1]) * share);
    }

    // Each photo's exposure in stops, factor by factor (see the remarks); zeros when no photo has any.
    private static double[] Exposures(PhotoExposure[] photos)
    {
        var stops = new double[photos.Length];
        AddFactor(stops, photos, photo => photo.ExposureTime, MinExposureTime, MaxExposureTime, Math.Log2);
        AddFactor(stops, photos, photo => photo.Iso, MinIso, MaxIso, iso => Math.Log2(iso / 100));
        AddFactor(stops, photos, photo => photo.FNumber, MinFNumber, MaxFNumber, fNumber => -2 * Math.Log2(fNumber));
        return stops;
    }

    // Adds one factor in stops to each photo: its own value when it is in range, else the nearer neighbour's (the earlier
    // on a tie); nothing when no photo has it.
    private static void AddFactor(double[] stops, PhotoExposure[] photos, Func<PhotoExposure, double?> read, double min, double max, Func<double, double> toStops)
    {
        var count = photos.Length;
        var values = new double?[count];
        var any = false;
        for (var i = 0; i < count; i++)
        {
            if (read(photos[i]) is { } value && value >= min && value <= max)
            {
                values[i] = toStops(value);
                any = true;
            }
        }

        if (!any)
        {
            return;
        }

        var after = new int[count];
        var found = -1;
        for (var i = count - 1; i >= 0; i--)
        {
            found = values[i] is null ? found : i;
            after[i] = found;
        }

        var before = -1;
        for (var i = 0; i < count; i++)
        {
            if (values[i] is { } own)
            {
                before = i;
                stops[i] += own;
                continue;
            }

            var next = after[i];
            var useBefore = before >= 0 && (next < 0 || i - before <= next - i);
            stops[i] += values[useBefore ? before : next]!.Value;
        }
    }

    // A running median over 2r + 1 values. Each end is continued along the least-squares line through its first (last)
    // EndFit values, so a steady trend passes unchanged to the last photo while one odd frame (a flash, a passing car, a
    // misread exposure) moves nothing. Needs at least 2r + 1 values.
    private static double[] Median(double[] values, int radius)
    {
        var count = values.Length;
        var width = (2 * radius) + 1;
        var padded = new double[count + (2 * radius)];
        Array.Copy(values, 0, padded, radius, count);
        var fit = Math.Min(count, EndFit);
        var (startA, startB) = Line(values, 0, fit);
        var (endA, endB) = Line(values, count - fit, fit);
        for (var k = 1; k <= radius; k++)
        {
            padded[radius - k] = startA - (startB * k);
            padded[radius + count - 1 + k] = endA + (endB * (count - 1 + k));
        }

        var result = new double[count];
        var window = new double[width];
        for (var i = 0; i < count; i++)
        {
            Array.Copy(padded, i, window, 0, width);
            Array.Sort(window);
            result[i] = window[radius];
        }

        return result;
    }

    // The least-squares line through values[from .. from + count - 1], as value = a + b × index.
    private static (double A, double B) Line(double[] values, int from, int count)
    {
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var j = from; j < from + count; j++)
        {
            sx += j;
            sy += values[j];
            sxx += (double)j * j;
            sxy += j * values[j];
        }

        var b = ((count * sxy) - (sx * sy)) / ((count * sxx) - (sx * sx));
        return ((sy - (b * sx)) / count, b);
    }

    // A local linear fit with Gaussian weights (sigma in samples, cut at 3 sigma), evaluated at each sample.
    private static double[] LocalLinear(double[] values, double sigma)
    {
        var result = new double[values.Length];
        var reach = (int)Math.Ceiling(3 * sigma);
        var weights = new double[reach + 1];
        for (var d = 0; d <= reach; d++)
        {
            weights[d] = Math.Exp(-(d * d) / (2 * sigma * sigma));
        }

        for (var i = 0; i < values.Length; i++)
        {
            double s0 = 0, s1 = 0, s2 = 0, t0 = 0, t1 = 0;
            var from = Math.Max(0, i - reach);
            var to = Math.Min(values.Length - 1, i + reach);
            for (var j = from; j <= to; j++)
            {
                var d = j - i;
                var w = weights[Math.Abs(d)];
                s0 += w;
                s1 += w * d;
                s2 += w * d * d;
                t0 += w * values[j];
                t1 += w * d * values[j];
            }

            var determinant = (s0 * s2) - (s1 * s1);
            result[i] = determinant > 1e-9 ? ((s2 * t0) - (s1 * t1)) / determinant : t0 / s0;
        }

        return result;
    }

    // The curve last asked for of one frame list: its look and its computation (finished or under way).
    private sealed class Cache
    {
        public LookSettings? Look { get; set; }

        public Task<ExposureCurve>? Curve { get; set; }
    }
}
