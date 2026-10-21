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

    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

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
        var ends = Rms(new[] { 0, 1, 298, 299 }.Select(i => corrected[i] - scene[i]));
        output.WriteLine($"Flicker: residual {before:F3} -> {after:F3} stops RMS ({ends:F3} at the two photos at either end), largest jump {jumpBefore:F3} -> {jumpAfter:F3} stops, change over the sequence {corrected[^1] - corrected[0]:F3} (scene {scene[^1] - scene[0]:F3}).");

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
    public void APhotoNotMeasuredTakesItsCorrectionFromItsMeasuredNeighbours()
    {
        // Not measured yet, or it could not be (#45 review): between corrected photos it must not flash at no correction.
        var frames = Measured(-2, -2.3, -1.7, -2.4, -1.8).ToList();
        frames.Insert(2, TestFrames.At(@"C:\Photos\Look\unmeasured.jpg"));
        frames.Insert(0, TestFrames.At(@"C:\Photos\Look\first.jpg"));
        frames.Add(TestFrames.At(@"C:\Photos\Look\last.jpg"));

        var withGaps = ExposureCurve.Compute(frames, new LookSettings(true, 100));
        var measured = frames.Where(frame => frame.Exposure is not null).ToList();
        var without = ExposureCurve.Compute(measured, new LookSettings(true, 100));

        Assert.Equal(frames.Count, withGaps.Count);
        Assert.All(measured, frame => Assert.Equal(without.Correction(frame.Id), withGaps.Correction(frame.Id), 1e-12));
        Assert.Equal((without.Correction(frames[2].Id) + without.Correction(frames[4].Id)) / 2, withGaps.Correction(frames[3].Id), 1e-12);
        Assert.Equal(without.Correction(frames[1].Id), withGaps.Correction(frames[0].Id), 1e-12);
        Assert.Equal(without.Correction(frames[^2].Id), withGaps.Correction(frames[^1].Id), 1e-12);
        Assert.NotEqual(0, withGaps.Correction(frames[3].Id));
    }

    [Fact]
    public void APhotoWithoutAFactorTakesItFromItsNeighbourAndTheRampStays()
    {
        // One stop more shutter on the second half: a step the curve ramps. A photo lacking its shutter time takes its
        // neighbour's, so the factor still counts and the step is still a ramp (before the review it fell back to flicker).
        var frames = Enumerable.Range(0, 60)
            .Select(i => TestFrames.At($@"C:\Photos\Look\IMG_{i:D4}.jpg") with
            {
                Exposure = new PhotoExposure(i < 30 ? -3 : -2) { ExposureTime = i < 30 ? 1.0 / 250 : 1.0 / 125, FNumber = 8, Iso = 100 },
            })
            .ToList();
        var steps = ExposureCurve.Compute(frames, new LookSettings(true, 100));
        frames[0] = frames[0] with { Exposure = frames[0].Exposure! with { ExposureTime = null } };
        frames[30] = frames[30] with { Exposure = frames[30].Exposure! with { Iso = null } };
        var gaps = ExposureCurve.Compute(frames, new LookSettings(true, 100));

        // The ramp spreads the step over the long window: ten photos before it, a photo is already lifted a good part of
        // the way, while the short window has not reached it yet.
        Assert.True(steps.Correction(frames[20].Id) > 0.2, $"Ramp: {steps.Correction(frames[20].Id):F3}");
        Assert.Equal(steps.Correction(frames[20].Id), gaps.Correction(frames[20].Id), 0.01);
    }

    [Fact]
    public void AManualSunsetWithOnePhotoStrippedOfItsExifStaysSmooth()
    {
        // The review's case: 480 photos, +1 stop by hand every 80; one photo without its shutter time used to turn the
        // ramp off for all of them (largest jump 0.080 stop). The worst place for the gap is the first photo of a step,
        // whose neighbours disagree: the earlier one's value is a stop off, and the median takes that one photo out.
        var (frames, _) = Sunset(480);
        var whole = Jumps(frames);
        foreach (var stripped in new[] { 200, 240 })
        {
            var gap = frames.ToArray();
            gap[stripped] = gap[stripped] with { Exposure = gap[stripped].Exposure! with { ExposureTime = null } };
            var jump = Jumps(gap);
            output.WriteLine($"Manual sunset, photo {stripped} without a shutter time: largest jump {whole:F3} -> {jump:F3} stops.");
            Assert.True(jump < Unnoticeable, $"A jump of {jump:F3} stops is left.");
            Assert.Equal(whole, jump, 0.01);
        }
    }

    [Fact]
    public void ExposureSettingsOutOfRangeAreRejectedAndOneWildValueDoesNotSwellTheRamp()
    {
        // One photo's Exif misread (4294967295 s) or hand-edited (3,000 s, in range but 19 stops off): before the review the
        // long fit spread it over ±60 photos (+0.59 stop next to it). The range check drops the first, the median the second.
        var (frames, _) = Sunset(240);
        var clean = ExposureCurve.Compute(frames, new LookSettings(true, 100));
        foreach (var wild in new[] { 4294967295.0, 3000.0 })
        {
            var edited = frames.ToArray();
            edited[120] = edited[120] with { Exposure = edited[120].Exposure! with { ExposureTime = wild } };
            var curve = ExposureCurve.Compute(edited, new LookSettings(true, 100));
            var swell = edited.Where((_, i) => i != 120).Max(frame => Math.Abs(curve.Correction(frame.Id) - clean.Correction(frame.Id)));
            output.WriteLine($"Shutter time {wild} s on photo 120: largest change to another photo's correction {swell:F4} stops.");
            Assert.True(swell < 0.01, $"The other photos move by up to {swell:F3} stops.");
        }
    }

    [Fact]
    public void MixedCamerasHalfWithoutExifStaySmooth()
    {
        // The first half from a camera shot by hand (+1 stop every 80), the second from one that writes no exposure (a
        // phone, a manual lens) and follows the light itself. The second half takes the first half's last exposure: a flat
        // staircase, so it is smoothed as flicker only, as a sequence without Exif is, with no jump where the cameras meet.
        var (frames, brightness) = Sunset(320);
        var mixed = frames.Select((frame, i) => i < 160 ? frame : frame with { Exposure = new PhotoExposure(brightness[159] - (0.02 * (i - 159))) }).ToArray();
        var curve = ExposureCurve.Compute(mixed, new LookSettings(true, 100));
        var corrected = mixed.Select(frame => frame.Exposure!.Luminance + curve.Correction(frame.Id)).ToArray();

        var jump = MaxJump(corrected);
        output.WriteLine($"Mixed cameras: largest jump {jump:F3} stops.");
        Assert.True(jump < Unnoticeable, $"A jump of {jump:F3} stops is left.");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(12)]
    public void AShortSequenceKeepsItsRealDarkening(int count)
    {
        // The review's case: 3 photos darkening half a stop each came out flat ([-0.5, 0, +0.5]), 5 lost half their
        // darkening. Below seven photos there is no median, and from seven the median follows the trend to both ends.
        var brightness = Enumerable.Range(0, count).Select(i => -2 - (0.5 * i)).ToArray();

        var corrections = ExposureCurve.Corrections(brightness, Zeros(count), 1);

        output.WriteLine($"{count} photos darkening 0.5 stop each: corrections [{string.Join(", ", corrections.Select(value => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}].");
        Assert.All(corrections, correction => Assert.Equal(0, correction, 1e-9));
    }

    [Fact]
    public void SevenPhotosStillLoseAFlash()
    {
        // From seven photos on the median runs: one photo a stop too bright (a flash) is brought back to the others.
        double[] brightness = [-2, -2, -2, -1, -2, -2, -2];

        var corrections = ExposureCurve.Corrections(brightness, Zeros(7), 1);

        Assert.Equal(-1, corrections[3], 0.05);
        Assert.All(corrections.Where((_, i) => i != 3), correction => Assert.Equal(0, correction, 0.05));
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

    [Fact]
    public async Task TheCurveIsKeptForEditsThatChangeNeitherFramesNorLookAndTryGetNeverWaits()
    {
        var project = new Project();
        project.AddFrames(Measured(-2, -2.5, -1.5, -2));
        project.Look = new LookSettings(true, 100);
        var first = project.Snapshot;

        var ready = new TaskCompletionSource();
        if (!ExposureCurve.TryGet(first, () => ready.TrySetResult(), out var curve))
        {
            Assert.Same(ExposureCurve.None, curve);
            await ready.Task.WaitAsync(_patience, TestContext.Current.CancellationToken);
            Assert.True(ExposureCurve.TryGet(first, null, out curve));
        }

        Assert.Equal(4, curve.Count);
        Assert.Same(curve, ExposureCurve.For(first));

        // A new name is a new snapshot with the same frames and look: the same curve, nothing computed.
        project.Name = "Harbor";
        Assert.NotSame(first, project.Snapshot);
        Assert.Same(curve, ExposureCurve.For(project.Snapshot));

        project.Look = new LookSettings(true, 50);
        Assert.NotSame(curve, ExposureCurve.For(project.Snapshot));
    }

    [Fact]
    public void AskingForANewCurveOf5000PhotosDoesNotHoldTheRenderThread()
    {
        // The preview's render thread used to compute the curve itself the first time it drew a snapshot (17 ms at 5,000
        // photos on the dev PC, every 250 ms while measuring). It now asks with TryGet, which hands the work to the pool.
        var project = new Project();
        project.AddFrames(TestFrames.Folder("bench", 5000).Select((frame, i) => frame with { Exposure = new PhotoExposure(-3 + Math.Sin(i * 0.37)) }));
        var inline = new List<double>();
        var asked = new List<double>();
        for (var strength = 60; strength < 70; strength++)
        {
            project.Look = new LookSettings(true, strength);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _ = ExposureCurve.Compute(project.Snapshot.Frames, project.Snapshot.Look);
            inline.Add(clock.Elapsed.TotalMilliseconds);

            clock.Restart();
            var known = ExposureCurve.TryGet(project.Snapshot, null, out _);
            asked.Add(clock.Elapsed.TotalMilliseconds);
            Assert.False(known);
            _ = ExposureCurve.For(project.Snapshot);
        }

        output.WriteLine($"5,000 photos, a new curve on the render thread: computed there {inline.Order().ElementAt(5):F2} ms (median of 10) -> asked with TryGet {asked.Order().ElementAt(5):F3} ms.");
        Assert.True(asked.Order().ElementAt(5) < inline.Order().ElementAt(5), "TryGet took as long as computing the curve.");
    }

    // The hand-shot sunset as frames with Exif: the scene loses 0.02 stop a photo, the shutter time doubles every 80
    // photos, a little noise. Returns the frames and their brightness.
    private static (Frame[] Frames, double[] Brightness) Sunset(int count)
    {
        var random = new Random(55);
        var brightness = new double[count];
        var frames = new Frame[count];
        for (var i = 0; i < count; i++)
        {
            var steps = i / 80;
            brightness[i] = -1 - (0.02 * i) + steps + (0.02 * Normal(random));
            frames[i] = TestFrames.At($@"C:\Photos\Look\Sunset_{i:D4}.jpg") with
            {
                Exposure = new PhotoExposure(brightness[i]) { ExposureTime = Math.Pow(2, steps) / 250, FNumber = 8, Iso = 100 },
            };
        }

        return (frames, brightness);
    }

    // The largest frame-to-frame jump of the frames' brightness after their correction.
    private static double Jumps(IReadOnlyList<Frame> frames)
    {
        var curve = ExposureCurve.Compute(frames, new LookSettings(true, 100));
        return MaxJump([.. frames.Select(frame => frame.Exposure!.Luminance + curve.Correction(frame.Id))]);
    }

    private static Frame[] Measured(params double[] brightness) =>
        [.. brightness.Select((value, i) => TestFrames.At($@"C:\Photos\Look\IMG_{i:D4}.jpg") with { Exposure = new PhotoExposure(value) })];
}
