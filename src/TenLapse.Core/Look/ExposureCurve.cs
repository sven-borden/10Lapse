using System.Collections.Frozen;
using System.Runtime.CompilerServices;
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
/// photos), which removes flicker and keeps the scene's own darkening; and the staircase through a long Gaussian
/// (<see cref="RampSigma"/> photos), which turns each step into a ramp. Each photo's correction is the target minus
/// <c>m</c>, held to ±<see cref="MaxCorrection"/> stops and scaled by the strength. Nothing is flattened: the darkening
/// the scene and the photographer's own steps add up to stays in the curve, only its jumps go.
/// </para>
/// <para>
/// Both smoothings are local linear fits with Gaussian weights: in the middle of the sequence a plain Gaussian mean, at
/// either end a fit that follows a trend instead of bending it toward the mean. Photos are spaced by their place in the
/// project, not their capture time: a photo is one frame on screen.
/// </para>
/// <para>
/// An exposure factor is used only when every measured photo has it (a lens without electronic contacts writes no
/// f-number; it is then taken as unchanged). Without Exif exposure the staircase is flat, so a jump in brightness is
/// smoothed over the short window only: a real event (street lights coming on) is never stretched into a long ramp on a
/// guess. Photos not measured yet, or that could not be, have no correction and are left out of the curve.
/// </para>
/// <para>
/// Keyed by photo (<see cref="Frame.Id"/>), never by an output frame: a photo held, looped or played back and forth
/// gets the same correction every time. The curve follows the project's order; a reorder computes a new one.
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

    private const int MedianRadius = 2;

    // One curve per snapshot, computed by whichever thread asks first (the preview's render thread, the export).
    private static readonly ConditionalWeakTable<ProjectSnapshot, ExposureCurve> _bySnapshot = [];

    private readonly FrozenDictionary<Guid, double> _corrections;

    private ExposureCurve(FrozenDictionary<Guid, double> corrections)
    {
        _corrections = corrections;
    }

    /// <summary>No correction for any photo: deflicker is off, or fewer than two photos are measured.</summary>
    public static ExposureCurve None { get; } = new(FrozenDictionary<Guid, double>.Empty);

    /// <summary>How many photos have a correction.</summary>
    public int Count => _corrections.Count;

    /// <summary>The curve of <paramref name="snapshot"/>'s photos and look, computed once per snapshot. <see cref="None"/> while deflicker is off.</summary>
    public static ExposureCurve For(ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Look.Deflicker ? _bySnapshot.GetValue(snapshot, static s => Compute(s.Frames, s.Look)) : None;
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

        var measured = frames.Where(frame => frame.Exposure is not null).ToList();
        if (measured.Count < 2)
        {
            return None;
        }

        var brightness = measured.Select(frame => frame.Exposure!.Luminance).ToArray();
        var exposure = Exposures(measured);
        var corrections = Corrections(brightness, exposure, look.Strength / 100.0);
        var byId = new Dictionary<Guid, double>(measured.Count);
        for (var i = 0; i < measured.Count; i++)
        {
            byId[measured[i].Id] = corrections[i];
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
        for (var i = 0; i < count; i++)
        {
            scene[i] = brightness[i] - exposure[i];
        }

        var smoothScene = LocalLinear(Median(scene, MedianRadius), FlickerSigma);
        var ramp = LocalLinear([.. exposure], RampSigma);
        var corrections = new double[count];
        for (var i = 0; i < count; i++)
        {
            var target = smoothScene[i] + ramp[i];
            corrections[i] = Math.Clamp(target - brightness[i], -MaxCorrection, MaxCorrection) * strength;
        }

        return corrections;
    }

    /// <summary>The photo's correction in stops; 0 for a photo without one.</summary>
    public double Correction(Guid frameId) => _corrections.GetValueOrDefault(frameId);

    /// <summary>The photo's correction as a factor on linear light: 2 to the power of <see cref="Correction"/>, 1 for none.</summary>
    public float Gain(Guid frameId) => _corrections.TryGetValue(frameId, out var stops) ? (float)Math.Pow(2, stops) : 1f;

    // Each photo's exposure in stops from the factors every measured photo has; zeros when none has any.
    private static double[] Exposures(List<Frame> measured)
    {
        var useTime = measured.All(frame => frame.Exposure!.ExposureTime is not null);
        var useAperture = measured.All(frame => frame.Exposure!.FNumber is not null);
        var useIso = measured.All(frame => frame.Exposure!.Iso is not null);
        return [.. measured.Select(frame =>
        {
            var exposure = frame.Exposure!;
            var stops = 0.0;
            if (useTime)
            {
                stops += Math.Log2(exposure.ExposureTime!.Value);
            }

            if (useIso)
            {
                stops += Math.Log2(exposure.Iso!.Value / 100);
            }

            if (useAperture)
            {
                stops -= 2 * Math.Log2(exposure.FNumber!.Value);
            }

            return stops;
        })];
    }

    // A running median over 2r + 1 values, narrower at the ends: one odd frame (a flash, a passing car) moves nothing.
    private static double[] Median(double[] values, int radius)
    {
        var result = new double[values.Length];
        var window = new double[(2 * radius) + 1];
        for (var i = 0; i < values.Length; i++)
        {
            var from = Math.Max(0, i - radius);
            var to = Math.Min(values.Length - 1, i + radius);
            var n = to - from + 1;
            Array.Copy(values, from, window, 0, n);
            Array.Sort(window, 0, n);
            result[i] = n % 2 == 1 ? window[n / 2] : (window[(n / 2) - 1] + window[n / 2]) / 2;
        }

        return result;
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
}
