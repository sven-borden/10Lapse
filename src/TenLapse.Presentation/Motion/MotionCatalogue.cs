using static TenLapse.Presentation.Motion.MotionSpec;

namespace TenLapse.Presentation.Motion;

/// <summary>
/// Every transition of the motion spec (<c>docs/design/canvas/motion/Motion.dc.html</c>) and the prototype
/// (<c>Main.dc.html</c>), with its timing and its reduced-motion fallback. Nothing animates on launch; every one is
/// interruptible (a newer change jumps it to its end state), none blocks input, and none changes state: the view sets the
/// end state first and the animation only draws the way there.
/// </summary>
public static class MotionCatalogue
{
    // ---- Screens: Empty, Editing and Settings (prototype, go) ----

    /// <summary>The screen that leaves (drop zone, editor, Settings): fades out and shrinks to 98.5 %, 167 ms Exit.</summary>
    public static readonly MotionTransition SceneOut = new("Scene.Out", Control, MotionEasing.Exit, MotionDirection.Out, Moves: true, ReducedMotion.Fade, "scales to 0.985")
    {
        HoldsInfoBars = true,
    };

    /// <summary>The screen that arrives, once the old one is gone: rises 8 px and fades in, 400 ms Enter.</summary>
    public static readonly MotionTransition SceneIn = new("Scene.In", Scene, MotionEasing.Enter, MotionDirection.In, Moves: true, ReducedMotion.Fade, "rises 8 px")
    {
        HoldsInfoBars = true,
        Delay = Control,
        ReducedDelay = ReducedFadeOut,
    };

    // ---- Arrival: the photos land (motion spec, Arrival) ----

    /// <summary>The editor's surface (the Layer, the transport, the inspector's frame) fades in after the drop zone's exit.</summary>
    public static readonly MotionTransition ArrivalSurface = new("Arrival.Surface", Scene, MotionEasing.Enter, MotionDirection.In, Moves: false, ReducedMotion.Fade, "fades in")
    {
        HoldsInfoBars = true,
        Delay = Control,
        ReducedDelay = ReducedFadeOut,
    };

    /// <summary>The first frame develops like a print: blur 14 to 0 px, scale 1.03 to 1, opacity in, 700 ms Enter.</summary>
    public static readonly MotionTransition ArrivalDevelop = new("Arrival.Develop", Cinema, MotionEasing.Enter, MotionDirection.In, Moves: true, ReducedMotion.Fade, "blur 14 to 0 px, scale 1.03 to 1")
    {
        HoldsInfoBars = true,
        Delay = Control,
        ReducedDelay = ReducedFadeOut,
    };

    /// <summary>The thumbnails rise 8 px into place, 400 ms Enter, 18 ms apart from 220 ms.</summary>
    public static readonly MotionTransition ArrivalFilmstrip = new("Arrival.Filmstrip", Scene, MotionEasing.Enter, MotionDirection.In, Moves: true, ReducedMotion.Fade, "rises 8 px")
    {
        HoldsInfoBars = true,
        Delay = ThumbnailCascadeStart,
        Stagger = ThumbnailStagger,
    };

    /// <summary>The inspector's sections slide 16 px in from the right, 400 ms Enter, 60 ms apart from 160 ms.</summary>
    public static readonly MotionTransition ArrivalInspector = new("Arrival.Inspector", Scene, MotionEasing.Enter, MotionDirection.In, Moves: true, ReducedMotion.Fade, "slides 16 px")
    {
        HoldsInfoBars = true,
        Delay = SectionCascadeStart,
        Stagger = SectionStagger,
    };

    // ---- Retime: change the speed ----

    /// <summary>A segmented control's pill slides to the new option, 250 ms Move.</summary>
    public static readonly MotionTransition SegmentPill = new("Retime.Pill", Reveal, MotionEasing.Move, MotionDirection.Change, Moves: true, ReducedMotion.Cut, "slides");

    /// <summary>The new length rises 6 px into place and fades in, 250 ms Enter.</summary>
    public static readonly MotionTransition RetimeReadout = new("Retime.Readout", Reveal, MotionEasing.Enter, MotionDirection.In, Moves: true, ReducedMotion.Fade, "rises 6 px");

    /// <summary>The length bar grows or shrinks to the new length (scale X), 400 ms Enter.</summary>
    public static readonly MotionTransition RetimeLengthBar = new("Retime.LengthBar", Scene, MotionEasing.Enter, MotionDirection.Change, Moves: true, ReducedMotion.Cut, "scale X");

    // ---- Reframe: change the shape ----

    /// <summary>The gate closes around the new shape (its clip), 400 ms Move.</summary>
    public static readonly MotionTransition ReframeGate = new("Reframe.Gate", Scene, MotionEasing.Move, MotionDirection.Change, Moves: true, ReducedMotion.Cut, "clip inset");

    /// <summary>The photo scales for Fit or Fill inside the gate, 400 ms Move.</summary>
    public static readonly MotionTransition ReframePhoto = new("Reframe.Photo", Scene, MotionEasing.Move, MotionDirection.Change, Moves: true, ReducedMotion.Cut, "scale");

    // ---- Gate: the export runs ----

    /// <summary>The inspector's part that leaves for the export panel or back: to -12 px, 167 ms Exit.</summary>
    public static readonly MotionTransition PanelOut = new("Export.PanelOut", Control, MotionEasing.Exit, MotionDirection.Out, Moves: true, ReducedMotion.Fade, "to -12 px")
    {
        HoldsInfoBars = true,
    };

    /// <summary>The part that arrives: from +16 px, 400 ms Enter.</summary>
    public static readonly MotionTransition PanelIn = new("Export.PanelIn", Scene, MotionEasing.Enter, MotionDirection.In, Moves: true, ReducedMotion.Fade, "from +16 px")
    {
        HoldsInfoBars = true,
    };

    /// <summary>The film gate's strip glides linearly to each new progress; with animations off it stands still.</summary>
    public static readonly MotionTransition GateStrip = new("Export.GateStrip", GateGlide, MotionEasing.Linear, MotionDirection.Change, Moves: true, ReducedMotion.Cut, "slides with the progress");

    /// <summary>A gate sample changes strength (pending 30 % to written), 167 ms linear; a fade, so it stays with animations off.</summary>
    public static readonly MotionTransition GateSample = new("Export.GateSample", Control, MotionEasing.Linear, MotionDirection.Change, Moves: false, ReducedMotion.Same, "fades");

    /// <summary>The Export button dims to 0.6 while the export panel shows, and back, 167 ms linear; a fade, so it stays with animations off.</summary>
    public static readonly MotionTransition ExportButtonDim = new("Export.ButtonDim", Control, MotionEasing.Linear, MotionDirection.Change, Moves: false, ReducedMotion.Same, "fades");

    // ---- Wrap: the video is ready ----

    /// <summary>The check's stroke draws, 500 ms Enter after 200 ms; drawn at once with animations off.</summary>
    public static readonly MotionTransition FinishedCheck = new("Finished.Check", CheckDraw, MotionEasing.Enter, MotionDirection.In, Moves: true, ReducedMotion.Cut, "stroke draws")
    {
        Delay = CheckDelay,
    };

    /// <summary>The finished title rises 6 px into place, 400 ms Enter.</summary>
    public static readonly MotionTransition FinishedTitle = new("Finished.Title", Scene, MotionEasing.Enter, MotionDirection.In, Moves: true, ReducedMotion.Fade, "rises 6 px");

    /// <summary>The preview premieres the finished video 1.1 s after the finished state arrives (a wait, not motion).</summary>
    public static readonly MotionTransition FinishedPremiere = new("Finished.Premiere", Premiere, MotionEasing.Linear, MotionDirection.Change, Moves: false, ReducedMotion.Same, "waits")
    {
        IsWait = true,
    };

    // ---- InfoBar: something to know ----

    /// <summary>An InfoBar floats in from -8 px, 250 ms Enter.</summary>
    public static readonly MotionTransition InfoBarIn = new("InfoBar.In", Reveal, MotionEasing.Enter, MotionDirection.In, Moves: true, ReducedMotion.Fade, "from -8 px");

    /// <summary>An InfoBar leaves to -8 px, 167 ms Exit.</summary>
    public static readonly MotionTransition InfoBarOut = new("InfoBar.Out", Control, MotionEasing.Exit, MotionDirection.Out, Moves: true, ReducedMotion.Fade, "to -8 px");

    /// <summary>An InfoBar's detail list expands under it from -4 px, 250 ms Enter.</summary>
    public static readonly MotionTransition InfoBarDetails = new("InfoBar.Details", Reveal, MotionEasing.Enter, MotionDirection.In, Moves: true, ReducedMotion.Fade, "from -4 px");

    // ---- Theme: Windows changes mode ----

    /// <summary>The colors crossfade to the new theme, 250 ms linear; the photos never change. A fade, so it stays with animations off.</summary>
    public static readonly MotionTransition ThemeChange = new("Theme.Crossfade", ThemeCrossfade, MotionEasing.Linear, MotionDirection.Change, Moves: false, ReducedMotion.Same, "crossfades")
    {
        HoldsInfoBars = true,
    };

    // ---- First launch (Daybreak, the drop zone) ----

    /// <summary>The drop zone's drag-over look fades in, 167 ms Enter.</summary>
    public static readonly MotionTransition DragLookIn = new("FirstLaunch.DragLookIn", Control, MotionEasing.Enter, MotionDirection.In, Moves: false, ReducedMotion.Fade, "fades in");

    /// <summary>The drop zone's drag-over look fades out, 167 ms Enter (the canvas eases both ways alike).</summary>
    public static readonly MotionTransition DragLookOut = new("FirstLaunch.DragLookOut", Control, MotionEasing.Enter, MotionDirection.Out, Moves: false, ReducedMotion.Fade, "fades out");

    /// <summary>Daybreak lifts 6 px while a drag is over the drop zone, and settles back, 250 ms Enter.</summary>
    public static readonly MotionTransition DragLift = new("FirstLaunch.DragLift", Reveal, MotionEasing.Enter, MotionDirection.Change, Moves: true, ReducedMotion.Cut, "lifts 6 px");

    /// <summary>Daybreak's 20 s loop, day to night, linear; a still frame at golden hour with animations off.</summary>
    public static readonly MotionTransition Daybreak = new("FirstLaunch.Daybreak", DaybreakLoop, MotionEasing.Linear, MotionDirection.Change, Moves: true, ReducedMotion.Cut, "plays the loop");

    /// <summary>Every transition, in the spec's order.</summary>
    public static IReadOnlyList<MotionTransition> All { get; } =
    [
        SceneOut, SceneIn,
        ArrivalSurface, ArrivalDevelop, ArrivalFilmstrip, ArrivalInspector,
        SegmentPill, RetimeReadout, RetimeLengthBar,
        ReframeGate, ReframePhoto,
        PanelOut, PanelIn, GateStrip, GateSample, ExportButtonDim,
        FinishedCheck, FinishedTitle, FinishedPremiere,
        InfoBarIn, InfoBarOut, InfoBarDetails,
        ThemeChange,
        DragLookIn, DragLookOut, DragLift, Daybreak,
    ];
}
