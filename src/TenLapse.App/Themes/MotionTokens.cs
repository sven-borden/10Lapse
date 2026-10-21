using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media.Animation;
using TenLapse.Presentation.Motion;
using Windows.UI.ViewManagement;

namespace TenLapse.Themes;

/// <summary>
/// The motion tokens for code, and the one place that decides whether the app animates. The values are
/// <see cref="MotionSpec"/>'s (Presentation, tested against DESIGN.md, the motion spec and Themes/Motion.xaml); this class
/// adds the reduced-motion decision, the motion log and the Composition and Storyboard forms of the curves.
/// </summary>
/// <remarks>
/// Every animation starts with <see cref="Plan"/>: it reads <see cref="AnimationsEnabled"/> at that moment (Windows has no
/// change event for it, so it is polled at each start, never cached), writes the step to the Debug motion log, and returns
/// what to animate. A rule test (tests/TenLapse.Tests/Design/MotionRuleTests.cs) fails on a duration or a curve written
/// anywhere but the token files.
/// </remarks>
public static class MotionTokens
{
    /// <summary>Press and hover color, linear.</summary>
    public static TimeSpan Tap => MotionSpec.Tap;

    /// <summary>Toggles, exits and focus, with the Move easing.</summary>
    public static TimeSpan Control => MotionSpec.Control;

    /// <summary>InfoBars, the readout and the segment pill, with the Enter easing.</summary>
    public static TimeSpan Reveal => MotionSpec.Reveal;

    /// <summary>Screens, panels, thumbnails and the gate, with the Enter easing.</summary>
    public static TimeSpan Scene => MotionSpec.Scene;

    /// <summary>The preview developing on arrival, with the Enter easing.</summary>
    public static TimeSpan Cinema => MotionSpec.Cinema;

    /// <summary>The theme change crossfades colors over this long, linear.</summary>
    public static TimeSpan ThemeCrossfade => MotionSpec.ThemeCrossfade;

    /// <summary>With animations off, things arriving fade in over this long instead of moving.</summary>
    public static TimeSpan ReducedFadeIn => MotionSpec.ReducedFadeIn;

    /// <summary>With animations off, things leaving fade out over this long instead of moving.</summary>
    public static TimeSpan ReducedFadeOut => MotionSpec.ReducedFadeOut;

    private static readonly UISettings _settings = new();

    /// <summary>
    /// Whether the app animates: Windows Settings, Accessibility, Visual effects, Animation effects (and, in Debug, no
    /// <c>--reduce-motion</c>). Read it each time an animation starts; when false, movement becomes a short fade or nothing.
    /// It is polled at each start, not observed: an animation already running when the user flips the Windows setting
    /// finishes as it began, and the next one follows the new value. Never <c>AccessibilitySettings</c>, which crashes the
    /// unpackaged app (PR #67).
    /// </summary>
    public static bool AnimationsEnabled => _settings.AnimationsEnabled && !ReduceMotionSwitch;

    /// <summary>Debug <c>--reduce-motion</c> acts as Windows' animation setting off.</summary>
    internal static bool ReduceMotionSwitch =>
#if DEBUG
        Debugging.DebugLaunch.ReduceMotion;
#else
        false;
#endif

    /// <summary>
    /// When the running screen, arrival, panel and theme transitions end: an InfoBar does not come in before (motion spec,
    /// InfoBar). <see cref="Plan"/> pushes it forward.
    /// </summary>
    public static MotionQuiet Quiet { get; } = new(TimeProvider.System);

    /// <summary>
    /// What <paramref name="transition"/> does now: the spec's timing with animations on, the reduced fade or nothing with
    /// them off. The <paramref name="index"/>-th of a cascade starts its stagger later. Written to the Debug motion log
    /// (<c>--motion-log</c>), and held in <see cref="Quiet"/> when an InfoBar must wait for it. Any thread.
    /// </summary>
    public static MotionStep Plan(MotionTransition transition, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var step = transition.Plan(AnimationsEnabled, index);
        Quiet.Hold(step);
#if DEBUG
        Debugging.DebugMotion.Record(step);
#endif
        return step;
    }

    /// <summary>A new Composition easing for <paramref name="easing"/>.</summary>
    public static CompositionEasingFunction CreateEasing(Compositor compositor, MotionEasing easing)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        if (easing == MotionEasing.Linear)
        {
            return compositor.CreateLinearEasingFunction();
        }

        var (x1, y1, x2, y2) = MotionSpec.Curve(easing);
        return compositor.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2));
    }

    /// <summary>A new easing that holds the start value to the end, then jumps: something that appears at once, after a wait.</summary>
    public static CompositionEasingFunction CreateCut(Compositor compositor)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        return compositor.CreateStepEasingFunction(1);
    }

    /// <summary>A new Enter easing for arrivals.</summary>
    public static CompositionEasingFunction CreateEnter(Compositor compositor) => CreateEasing(compositor, MotionEasing.Enter);

    /// <summary>A new Exit easing for departures.</summary>
    public static CompositionEasingFunction CreateExit(Compositor compositor) => CreateEasing(compositor, MotionEasing.Exit);

    /// <summary>A new Move easing for pills, knobs and the gate.</summary>
    public static CompositionEasingFunction CreateMove(Compositor compositor) => CreateEasing(compositor, MotionEasing.Move);

    /// <summary>A new linear easing for playback, progress and the reduced fades.</summary>
    public static CompositionEasingFunction CreateLinear(Compositor compositor) => CreateEasing(compositor, MotionEasing.Linear);

    /// <summary>
    /// The key frame that ends a Storyboard animation at <paramref name="value"/> after <paramref name="duration"/>, on
    /// <paramref name="easing"/> (a straight line for Linear).
    /// </summary>
    public static DoubleKeyFrame KeyFrame(double value, TimeSpan duration, MotionEasing easing)
    {
        if (easing == MotionEasing.Linear)
        {
            return new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(duration), Value = value };
        }

        var (x1, y1, x2, y2) = MotionSpec.Curve(easing);
        return new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(duration),
            Value = value,
            KeySpline = new KeySpline { ControlPoint1 = new(x1, y1), ControlPoint2 = new(x2, y2) },
        };
    }
}
