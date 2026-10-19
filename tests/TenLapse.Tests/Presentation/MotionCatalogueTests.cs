using TenLapse.Presentation.Motion;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// The transition catalogue against the motion spec's numbers (docs/design/canvas/motion/Motion.dc.html and the prototype,
/// Main.dc.html), and what each becomes with animations off: "Every transition listed in the spec runs at its specified
/// timing, and none run with animations turned off in Windows" (#35).
/// </summary>
public sealed class MotionCatalogueTests
{
    // Name, duration ms, easing, delay ms, stagger ms, moves, with animations off: (kind, duration ms, delay ms).
    public static TheoryData<string, int, MotionEasing, int, int, bool, MotionKind, int, int> Spec() => new()
    {
        // Screens (prototype, go): out 167 Exit, in 400 Enter rising 8 px once the old one is gone (170 ms; 90 reduced).
        { "Scene.Out", 167, MotionEasing.Exit, 0, 0, true, MotionKind.Faded, 90, 0 },
        { "Scene.In", 400, MotionEasing.Enter, 167, 0, true, MotionKind.Faded, 150, 90 },

        // Arrival: drop zone fades out 167 exit (Scene.Out), and the editor's frame appears at once (no transition of its
        // own, as in the prototype); preview blur 14 to 0, scale 1.03 to 1, 700 ms enter; filmstrip rises 8 px, 400 ms, 18 ms
        // stagger from 220 ms; inspector slides 16 px, 400 ms, 60 ms stagger from 160 ms.
        { "Arrival.Develop", 700, MotionEasing.Enter, 167, 0, true, MotionKind.Faded, 150, 90 },
        { "Arrival.Filmstrip", 400, MotionEasing.Enter, 220, 18, true, MotionKind.Faded, 150, 0 },
        { "Arrival.Inspector", 400, MotionEasing.Enter, 160, 60, true, MotionKind.Faded, 150, 0 },

        // Retime: pill slides 250 ms move; readout rises 6 px and fades in, 250 ms enter; length bar scaleX, 400 ms enter.
        { "Retime.Pill", 250, MotionEasing.Move, 0, 0, true, MotionKind.None, 0, 0 },
        { "Retime.Readout", 250, MotionEasing.Enter, 0, 0, true, MotionKind.Faded, 150, 0 },
        { "Retime.LengthBar", 400, MotionEasing.Enter, 0, 0, true, MotionKind.None, 0, 0 },

        // Reframe: gate clip-path inset, 400 ms move; photo scale for fit or fill, 400 ms move; reduced cuts.
        { "Reframe.Gate", 400, MotionEasing.Move, 0, 0, true, MotionKind.None, 0, 0 },
        { "Reframe.Photo", 400, MotionEasing.Move, 0, 0, true, MotionKind.None, 0, 0 },

        // Gate: panel swap out 167 ms -12 px, in 400 ms from +16 px; strip linear, tied to progress (reduced: still);
        // pending thumbnails fade 167 ms linear; the Export button dims over 167 ms linear.
        { "Export.PanelOut", 167, MotionEasing.Exit, 0, 0, true, MotionKind.Faded, 90, 0 },
        { "Export.PanelIn", 400, MotionEasing.Enter, 0, 0, true, MotionKind.Faded, 150, 0 },
        { "Export.GateStrip", 100, MotionEasing.Linear, 0, 0, true, MotionKind.None, 0, 0 },
        { "Export.GateSample", 167, MotionEasing.Linear, 0, 0, false, MotionKind.Faded, 167, 0 },
        { "Export.ButtonDim", 167, MotionEasing.Linear, 0, 0, false, MotionKind.Faded, 167, 0 },

        // Wrap: check stroke draws, 500 ms enter after 200 ms; title rises 6 px, 400 ms enter; premiere after 1.1 s.
        { "Finished.Check", 500, MotionEasing.Enter, 200, 0, true, MotionKind.None, 0, 0 },
        { "Finished.Title", 400, MotionEasing.Enter, 0, 0, true, MotionKind.Faded, 150, 0 },
        { "Finished.Premiere", 1100, MotionEasing.Linear, 0, 0, false, MotionKind.Waited, 1100, 0 },

        // InfoBar: in from -8 px, 250 ms enter; out to -8 px, 167 ms exit; detail list expands, 250 ms enter.
        { "InfoBar.In", 250, MotionEasing.Enter, 0, 0, true, MotionKind.Faded, 150, 0 },
        { "InfoBar.Out", 167, MotionEasing.Exit, 0, 0, true, MotionKind.Faded, 90, 0 },
        { "InfoBar.Details", 250, MotionEasing.Enter, 0, 0, true, MotionKind.Faded, 150, 0 },

        // Theme: colors crossfade, 250 ms linear (a fade: the same with animations off, as the prototype keeps it).
        { "Theme.Crossfade", 250, MotionEasing.Linear, 0, 0, false, MotionKind.Faded, 250, 0 },

        // First launch: drag look 167 ms (canvas: border and fill, 167 ms enter), Daybreak lifts 6 px over 250 ms enter,
        // Daybreak loops 20 s linear (reduced: a still frame at golden hour).
        { "FirstLaunch.DragLookIn", 167, MotionEasing.Enter, 0, 0, false, MotionKind.Faded, 150, 0 },
        { "FirstLaunch.DragLookOut", 167, MotionEasing.Enter, 0, 0, false, MotionKind.Faded, 90, 0 },
        { "FirstLaunch.DragLift", 250, MotionEasing.Enter, 0, 0, true, MotionKind.None, 0, 0 },
        { "FirstLaunch.Daybreak", 20_000, MotionEasing.Linear, 0, 0, true, MotionKind.None, 0, 0 },
    };

    [Theory]
    [MemberData(nameof(Spec))]
    public void EachTransitionHasTheSpecsTimingAndItsReducedFallback(
        string name, int duration, MotionEasing easing, int delay, int stagger, bool moves, MotionKind reducedKind, int reducedDuration, int reducedDelay)
    {
        var transition = MotionCatalogue.All.Single(entry => entry.Name == name);
        Assert.Equal(TimeSpan.FromMilliseconds(duration), transition.Duration);
        Assert.Equal(easing, transition.Easing);
        Assert.Equal(TimeSpan.FromMilliseconds(delay), transition.Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(stagger), transition.Stagger);
        Assert.Equal(moves, transition.Moves);

        var on = transition.Plan(animationsEnabled: true);
        Assert.Equal(transition.IsWait ? MotionKind.Waited : moves ? MotionKind.Moved : MotionKind.Faded, on.Kind);
        Assert.Equal(transition.Duration, on.Duration);
        Assert.Equal(transition.Delay, on.Delay);
        Assert.Equal(easing, on.Easing);

        var off = transition.Plan(animationsEnabled: false);
        Assert.Equal(reducedKind, off.Kind);
        Assert.Equal(TimeSpan.FromMilliseconds(reducedDuration), off.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(reducedDelay), off.Delay);
    }

    [Fact]
    public void TheCatalogueHoldsEveryTransitionOfTheSpecOnce()
    {
        var names = MotionCatalogue.All.Select(transition => transition.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Spec().Select(row => (string)((ITheoryDataRow)row).GetData()[0]!).Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NothingMovesWithAnimationsOff()
    {
        // "None run with animations turned off": a short linear fade at most, a wait, or the end state at once. The only
        // longer fades are the ones that move nothing and stay (the gate samples' strength, the Export button's dim, the
        // theme's crossfade).
        foreach (var transition in MotionCatalogue.All)
        {
            for (var index = 0; index < 20; index++)
            {
                var step = transition.Plan(animationsEnabled: false, index);
                Assert.False(step.Moves, transition.Name);
                Assert.NotEqual(MotionKind.Moved, step.Kind);
                if (step.Kind == MotionKind.Faded && transition.Reduced == ReducedMotion.Fade)
                {
                    Assert.Equal(MotionEasing.Linear, step.Easing);
                    Assert.True(step.Duration <= MotionSpec.ReducedFadeIn, transition.Name);
                    Assert.True(step.Delay <= MotionSpec.ReducedFadeOut, transition.Name);
                }
            }
        }
    }

    [Fact]
    public void OnlyFadesAndWaitsStayTheSameWithAnimationsOff()
    {
        Assert.All(MotionCatalogue.All.Where(transition => transition.Reduced == ReducedMotion.Same), transition => Assert.False(transition.Moves, transition.Name));
    }

    [Fact]
    public void ExitsRunAtControlOnTheExitCurveFasterThanEntrances()
    {
        // The motion spec: "Exits run at 167 ms, faster than entrances." The drop zone's drag look is the canvas's own
        // exception: its border and fill ease out on the Enter curve, as they ease in.
        foreach (var exit in MotionCatalogue.All.Where(transition => transition.Direction == MotionDirection.Out && transition != MotionCatalogue.DragLookOut))
        {
            Assert.Equal(MotionSpec.Control, exit.Duration);
            Assert.Equal(MotionEasing.Exit, exit.Easing);
        }

        Assert.All(MotionCatalogue.All.Where(transition => transition.Direction == MotionDirection.In), entry => Assert.True(entry.Duration > MotionSpec.Control || entry == MotionCatalogue.DragLookIn, entry.Name));
    }

    [Fact]
    public void ACascadeStaggersEachNextOneWithAnimationsOn()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(220 + (18 * 5)), MotionCatalogue.ArrivalFilmstrip.Plan(true, 5).Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(160 + (60 * 2)), MotionCatalogue.ArrivalInspector.Plan(true, 2).Delay);
        Assert.Equal(TimeSpan.Zero, MotionCatalogue.ArrivalFilmstrip.Plan(false, 5).Delay);
    }

    [Theory]
    [InlineData(MotionEasing.Linear, 0.25, 0.25)]
    [InlineData(MotionEasing.Move, 0.5, 0.5)]
    [InlineData(MotionEasing.Enter, 0, 0)]
    [InlineData(MotionEasing.Exit, 1, 1)]
    public void EaseFollowsTheCurve(MotionEasing easing, double time, double expected) =>
        Assert.Equal(expected, MotionSpec.Ease(easing, time), 4);

    [Fact]
    public void EnterRisesFastAndExitStartsSlow()
    {
        Assert.True(MotionSpec.Ease(MotionEasing.Enter, 0.2) > 0.6);
        Assert.True(MotionSpec.Ease(MotionEasing.Exit, 0.2) < 0.05);
        for (var t = 0.0; t < 1; t += 0.05)
        {
            Assert.True(MotionSpec.Ease(MotionEasing.Enter, t + 0.05) >= MotionSpec.Ease(MotionEasing.Enter, t));
        }
    }

    [Fact]
    public void TheLogLineSaysWhatRanForHowLong()
    {
        Assert.Equal(
            "Arrival.Filmstrip #3 moved 400 ms Enter after 274 ms (rises 8 px; animations on)",
            MotionCatalogue.ArrivalFilmstrip.Plan(true, 3).ToLogLine());
        Assert.Equal("Arrival.Filmstrip #3 faded 150 ms Linear (animations off)", MotionCatalogue.ArrivalFilmstrip.Plan(false, 3).ToLogLine());
        Assert.Equal("Reframe.Gate none (animations off)", MotionCatalogue.ReframeGate.Plan(false).ToLogLine());
        Assert.Equal("Finished.Premiere waited 1100 ms (animations off)", MotionCatalogue.FinishedPremiere.Plan(false).ToLogLine());
        Assert.Equal("Scene.In faded 150 ms Linear after 90 ms (animations off)", MotionCatalogue.SceneIn.Plan(false).ToLogLine());
    }
}
