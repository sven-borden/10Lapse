using TenLapse.Core.Look;
using TenLapse.Core.Projects;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Look;

/// <summary>
/// Deflicker's curve (#45) on synthetic sequences with known flicker and known exposure steps: the flicker goes, a step
/// becomes a ramp with no visible jump, and the evening still gets darker.
/// </summary>
public sealed class ExposureCurveTests(ITestOutputHelper output)
{
    // The smallest change between two frames an eye notices on a smooth sequence is about 1/30 of a stop; the tests hold
    // the corrected curve well under it.
    private const double Unnoticeable = 1.0 / 30;

    private static double[] Zeros(int count) => new double[count];

    private static double[] Corrected(IReadOnlyList<double> brightness, IReadOnlyList<double> exposure, double strength = 1)
    {
        var corrections = ExposureCurve.Corrections(brightness, exposure, strength);
        return [.. brightness.Select((value, i) => value + corrections[i])];
    }

    private static double MaxJump(IReadOnlyList<double> values) =>
        Enumerable.Range(1, values.Count - 1).Max(i => Math.Abs(values[i] - values[i - 1]));

    private static double Rms(IEnumerable<double> values)
    {
        var list = values.ToList();
        return Math.Sqrt(list.Sum(value => value * value) / list.Count);
    }

    // A normal deviate from the seeded generator (Box-Muller).
    private static double Normal(Random random) =>
        Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

    [Fact]
    public void AFlickeringSequencePlaysSteadyAndKeepsItsDarkening()
    {
        // 300 photos of a scene that darkens 2 stops, with 0.15 stop of flicker (aperture and auto exposure), no Exif.
        var random = new Random(45);
        var scene = Enumerable.Range(0, 300).Select(i => -2 - (2.0 * i / 299)).ToArray();
        var brightness = scene.Select(value => value + (0.15 * Normal(random))).ToArray();

        var corrected = Corrected(brightness, Zeros(300));

        var before = Rms(brightness.Select((value, i) => value - scene[i]));
        var after = Rms(corrected.Select((value, i) => value - scene[i]));
        var jumpBefore = MaxJump(brightness);
        var jumpAfter = MaxJump(corrected);
        output.WriteLine($"Flicker: residual {before:F3} -> {after:F3} stops RMS, largest jump {jumpBefore:F3} -> {jumpAfter:F3} stops, change over the sequence {corrected[^1] - corrected[0]:F3} (scene {scene[^1] - scene[0]:F3}).");

        Assert.True(after < before / 3, $"Residual flicker {after:F3} is not a third of {before:F3}.");
        Assert.True(jumpAfter < Unnoticeable, $"A jump of {jumpAfter:F3} stops is left.");
        Assert.Equal(scene[^1] - scene[0], corrected[^1] - corrected[0], 0.15);
    }

    [Fact]
    public void AManualSunsetDarkensSmoothlyWithoutAJumpAtAStep()
    {
        // 480 photos across a sunset: the scene loses 9.6 stops (0.02 a photo); the photographer opens up one stop every
        // 80 photos (shutter 1/250, 1/125, ...), so the photos darken and brighten in a sawtooth, 1 stop jumps included.
        var random = new Random(55);
        const int count = 480;
        var scene = Enumerable.Range(0, count).Select(i => -1 - (0.02 * i)).ToArray();
        var exposure = Enumerable.Range(0, count).Select(i => (double)(i / 80)).ToArray();
        var brightness = scene.Select((value, i) => value + exposure[i] + (0.02 * Normal(random))).ToArray();

        var corrected = Corrected(brightness, exposure);

        var jumpBefore = MaxJump(brightness);
        var jumpAfter = MaxJump(corrected);
        var brightest = Enumerable.Range(1, count - 1).Max(i => corrected[i] - corrected[i - 1]);
        output.WriteLine($"Manual sunset: largest jump {jumpBefore:F3} -> {jumpAfter:F3} stops; largest brightening {brightest:F4}; {corrected[0]:F2} -> {corrected[^1]:F2} stops (photos {brightness[0]:F2} -> {brightness[^1]:F2}); largest correction {ExposureCurve.Corrections(brightness, exposure, 1).Max(Math.Abs):F2}.");

        Assert.True(jumpBefore > 0.9);
        Assert.True(jumpAfter < Unnoticeable, $"A jump of {jumpAfter:F3} stops is left.");

        // Smooth, never flattened: it darkens about as much as the photos do overall, and never brightens noticeably.
        Assert.True(brightest < 0.01, $"The curve brightens by {brightest:F4} stops between two frames.");
        Assert.Equal(brightness[^1] - brightness[0], corrected[^1] - corrected[0], 0.4);
        Assert.True(corrected[^1] < corrected[0] - 2);
    }

    [Fact]
    public void WithoutExifAJumpIsSmoothedOverTheShortWindowOnly()
    {
        // A one-stop jump with no exposure settings to say it was the camera's: it may be the street lights coming on, so
        // it is eased over a few photos (a quarter second at 30 fps), never stretched into a long ramp.
        var brightness = Enumerable.Range(0, 100).Select(i => i < 50 ? -3.0 : -2.0).ToArray();

        var corrected = Corrected(brightness, Zeros(100));

        Assert.InRange(MaxJump(corrected), 0.05, 0.12);
        Assert.Equal(-3, corrected[30], 0.01);
        Assert.Equal(-2, corrected[70], 0.01);
    }

    [Fact]
    public void TheStrengthScalesTheCorrectionAndItIsHeldToTwoStops()
    {
        double[] brightness = [-2, -2.4, -1.6, -2, -2.2, -1.8];
        var full = ExposureCurve.Corrections(brightness, Zeros(6), 1);
        var half = ExposureCurve.Corrections(brightness, Zeros(6), 0.5);

        Assert.All(full.Zip(half), pair => Assert.Equal(pair.First / 2, pair.Second, 1e-12));

        // A photo five stops off its neighbours is brought at most two stops toward them.
        double[] outlier = [-2, -2, -2, -7, -2, -2, -2];
        Assert.Equal(ExposureCurve.MaxCorrection, ExposureCurve.Corrections(outlier, Zeros(7), 1)[3], 1e-12);
    }

    [Fact]
    public void EachPhotoKeepsItsCorrectionWhereverItPlays()
    {
        // Keyed by photo: #49's holds, loops and boomerangs show a photo again and must give it the same gain.
        var frames = Measured(-2, -2.3, -1.7, -2.1, -1.9);
        var curve = ExposureCurve.Compute(frames, new LookSettings(true, 100));

        Assert.Equal(5, curve.Count);
        foreach (var frame in frames)
        {
            Assert.Equal(Math.Pow(2, curve.Correction(frame.Id)), curve.Gain(frame.Id), 1e-6);
        }

        Assert.Equal(0, curve.Correction(Guid.NewGuid()));
        Assert.Equal(1f, curve.Gain(Guid.NewGuid()));
    }

    [Fact]
    public void PhotosNotMeasuredYetAreLeftOutOfTheCurve()
    {
        var frames = Measured(-2, -2.3, -1.7).ToList();
        frames.Insert(1, TestFrames.At(@"C:\Photos\Look\unmeasured.jpg"));

        var withGap = ExposureCurve.Compute(frames, new LookSettings(true, 100));
        var without = ExposureCurve.Compute(frames.Where(frame => frame.Exposure is not null).ToList(), new LookSettings(true, 100));

        Assert.Equal(0, withGap.Correction(frames[1].Id));
        Assert.Equal(without.Correction(frames[2].Id), withGap.Correction(frames[2].Id), 1e-12);
    }

    [Fact]
    public void AnExposureFactorCountsOnlyWhenEveryPhotoHasIt()
    {
        // One stop more shutter on the second half: a step the curve ramps. With a photo lacking its shutter time the
        // factor is ignored and the same jump is eased as flicker instead.
        var frames = Enumerable.Range(0, 60)
            .Select(i => TestFrames.At($@"C:\Photos\Look\IMG_{i:D4}.jpg") with
            {
                Exposure = new PhotoExposure(i < 30 ? -3 : -2) { ExposureTime = i < 30 ? 1.0 / 250 : 1.0 / 125, FNumber = 8, Iso = 100 },
            })
            .ToList();
        var steps = ExposureCurve.Compute(frames, new LookSettings(true, 100));
        frames[0] = frames[0] with { Exposure = frames[0].Exposure! with { ExposureTime = null } };
        var flicker = ExposureCurve.Compute(frames, new LookSettings(true, 100));

        // The ramp spreads the step over the long window: ten photos before it, a photo is already lifted a good part of
        // the way, while the short window has not reached it yet.
        Assert.True(steps.Correction(frames[20].Id) > 0.2, $"Ramp: {steps.Correction(frames[20].Id):F3}");
        Assert.True(flicker.Correction(frames[20].Id) < 0.05, $"Flicker: {flicker.Correction(frames[20].Id):F3}");
    }

    [Fact]
    public void OffOrAtZeroOrWithOnePhotoThereIsNoCurve()
    {
        Assert.Same(ExposureCurve.None, ExposureCurve.Compute(Measured(-2, -3), new LookSettings(false, 100)));
        Assert.Same(ExposureCurve.None, ExposureCurve.Compute(Measured(-2, -3), new LookSettings(true, 0)));
        Assert.Same(ExposureCurve.None, ExposureCurve.Compute(Measured(-2), new LookSettings(true, 100)));
    }

    [Fact]
    public void ASnapshotsCurveIsComputedOnceAndFollowsTheLook()
    {
        var project = new Project();
        project.AddFrames(Measured(-2, -2.5, -1.5, -2));
        Assert.Same(ExposureCurve.None, ExposureCurve.For(project.Snapshot));

        project.Look = new LookSettings(true, 100);
        var snapshot = project.Snapshot;
        Assert.Same(ExposureCurve.For(snapshot), ExposureCurve.For(snapshot));
        Assert.Equal(4, ExposureCurve.For(snapshot).Count);
    }

    private static Frame[] Measured(params double[] brightness) =>
        [.. brightness.Select((value, i) => TestFrames.At($@"C:\Photos\Look\IMG_{i:D4}.jpg") with { Exposure = new PhotoExposure(value) })];
}
