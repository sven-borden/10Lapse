using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// An InfoBar never appears during a transition (motion spec, InfoBar; #35): the screen, arrival, panel and theme
/// transitions hold its entrance until they end, with animations off only for their short fade, and nothing waiting is lost
/// or reordered.
/// </summary>
public sealed class InfoBarHoldTests
{
    [Fact]
    public void NothingRunningMeansQuiet()
    {
        var quiet = new MotionQuiet(new Clock());
        Assert.Equal(TimeSpan.Zero, quiet.Remaining);
    }

    [Fact]
    public void ATransitionThatHoldsInfoBarsHoldsThemUntilItsDelayAndDurationEnd()
    {
        var clock = new Clock();
        var quiet = new MotionQuiet(clock);
        quiet.Hold(MotionCatalogue.SceneIn.Plan(animationsEnabled: true));
        Assert.Equal(MotionSpec.Control + MotionSpec.Scene, quiet.Remaining);

        clock.Advance(MotionSpec.Scene);
        Assert.Equal(MotionSpec.Control, quiet.Remaining);
        clock.Advance(MotionSpec.Control);
        Assert.Equal(TimeSpan.Zero, quiet.Remaining);
    }

    [Fact]
    public void WithAnimationsOffTheHoldIsTheShortFade()
    {
        var quiet = new MotionQuiet(new Clock());
        quiet.Hold(MotionCatalogue.SceneIn.Plan(animationsEnabled: false));
        Assert.Equal(MotionSpec.ReducedFadeOut + MotionSpec.ReducedFadeIn, quiet.Remaining);
    }

    [Fact]
    public void ALaterEndPushesTheHoldForwardAndAnEarlierOneDoesNot()
    {
        var clock = new Clock();
        var quiet = new MotionQuiet(clock);
        quiet.Hold(MotionCatalogue.ArrivalDevelop.Plan(animationsEnabled: true));
        quiet.Hold(MotionCatalogue.SceneOut.Plan(animationsEnabled: true));
        Assert.Equal(MotionSpec.Control + MotionSpec.Cinema, quiet.Remaining);

        clock.Advance(MotionSpec.Cinema);
        quiet.Hold(MotionCatalogue.PanelIn.Plan(animationsEnabled: true));
        Assert.Equal(MotionSpec.Scene, quiet.Remaining);
    }

    [Fact]
    public void OnlyScreenArrivalPanelAndThemeTransitionsHold()
    {
        var quiet = new MotionQuiet(new Clock());
        foreach (var transition in new[] { MotionCatalogue.InfoBarIn, MotionCatalogue.InfoBarOut, MotionCatalogue.SegmentPill, MotionCatalogue.ReframeGate, MotionCatalogue.FinishedPremiere, MotionCatalogue.Daybreak })
        {
            quiet.Hold(transition.Plan(animationsEnabled: true));
        }

        Assert.Equal(TimeSpan.Zero, quiet.Remaining);
        Assert.All(
            [MotionCatalogue.SceneOut, MotionCatalogue.SceneIn, MotionCatalogue.ArrivalDevelop, MotionCatalogue.ArrivalFilmstrip, MotionCatalogue.ArrivalInspector, MotionCatalogue.PanelOut, MotionCatalogue.PanelIn, MotionCatalogue.ThemeChange],
            transition => Assert.True(transition.HoldsInfoBars, transition.Name));
    }

    [Fact]
    public void AnEntranceWaitsButALeaveDoesNot()
    {
        var queue = new InfoBarQueue(new FakeErrorSink());
        var first = queue.Show(InfoBarSeverity.Caution, "3 photos couldn't be read and were skipped.");
        var busy = TimeSpan.FromMilliseconds(300);

        Assert.Equal(InfoBarMove.Wait, InfoBarSync.Next(queue.Current, shown: null, leaving: false, busy));
        Assert.Equal(InfoBarMove.Enter, InfoBarSync.Next(queue.Current, shown: null, leaving: false, TimeSpan.Zero));

        // On screen and dismissed during a transition: it leaves at once; while it leaves nothing else happens.
        queue.Dismiss(first);
        Assert.Equal(InfoBarMove.Leave, InfoBarSync.Next(queue.Current, first, leaving: false, busy));
        Assert.Equal(InfoBarMove.Stay, InfoBarSync.Next(queue.Current, first, leaving: true, busy));
        Assert.Equal(InfoBarMove.Stay, InfoBarSync.Next(queue.Current, shown: null, leaving: false, busy));
    }

    [Fact]
    public void HeldInfoBarsAreAllShownInTheirOrderOnceTheTransitionsEnd()
    {
        // The presenter's loop, with the queue and the quiet: InfoBars raised during an arrival, then a panel swap.
        var clock = new Clock();
        var quiet = new MotionQuiet(clock);
        var queue = new InfoBarQueue(new FakeErrorSink());
        InfoBarTicket? shown = null;
        var seen = new List<string>();

        void Sync()
        {
            switch (InfoBarSync.Next(queue.Current, shown, leaving: false, quiet.Remaining))
            {
                case InfoBarMove.Leave:
                    shown = null;
                    Sync();
                    break;
                case InfoBarMove.Enter:
                    shown = queue.Current;
                    seen.Add(shown!.Request.Message);
                    break;
            }
        }

        quiet.Hold(MotionCatalogue.ArrivalDevelop.Plan(animationsEnabled: true));
        var a = queue.Show(InfoBarSeverity.Caution, "A");
        queue.Show(InfoBarSeverity.Neutral, "B");
        Sync();
        Assert.Empty(seen);

        clock.Advance(MotionSpec.Control + MotionSpec.Cinema);
        Sync();
        Assert.Equal(["A"], seen);

        // A panel swap starts, then A is dismissed: A leaves at once, B waits for the swap.
        quiet.Hold(MotionCatalogue.PanelIn.Plan(animationsEnabled: true));
        queue.Dismiss(a);
        Sync();
        Assert.Null(shown);
        Assert.Equal(["A"], seen);

        clock.Advance(MotionSpec.Scene);
        Sync();
        Assert.Equal(["A", "B"], seen);
    }

    // A clock the test moves, in 100 ns ticks.
    private sealed class Clock : TimeProvider
    {
        private long _now = 1_000;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now;

        public void Advance(TimeSpan by) => _now += by.Ticks;
    }
}
