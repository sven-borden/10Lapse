using System.Numerics;
using Microsoft.UI.Composition;
using Windows.UI.ViewManagement;

namespace TenLapse.Themes;

/// <summary>
/// The motion tokens of DESIGN.md for code: durations, easings for Composition animations, and the one
/// reduced-motion check. The same values exist as XAML resources in Themes/Motion.xaml for Storyboards.
/// </summary>
public static class MotionTokens
{
    /// <summary>Press and hover color, linear.</summary>
    public static readonly TimeSpan Tap = TimeSpan.FromMilliseconds(83);

    /// <summary>Toggles, exits and focus, with the Move easing.</summary>
    public static readonly TimeSpan Control = TimeSpan.FromMilliseconds(167);

    /// <summary>InfoBars, the readout and the segment pill, with the Enter easing.</summary>
    public static readonly TimeSpan Reveal = TimeSpan.FromMilliseconds(250);

    /// <summary>Screens, panels, thumbnails and the gate, with the Enter easing.</summary>
    public static readonly TimeSpan Scene = TimeSpan.FromMilliseconds(400);

    /// <summary>The preview developing on arrival, with the Enter easing.</summary>
    public static readonly TimeSpan Cinema = TimeSpan.FromMilliseconds(700);

    /// <summary>The theme change crossfades colors over this long, linear.</summary>
    public static readonly TimeSpan ThemeCrossfade = TimeSpan.FromMilliseconds(250);

    /// <summary>With animations off, things arriving fade in over this long instead of moving.</summary>
    public static readonly TimeSpan ReducedFadeIn = TimeSpan.FromMilliseconds(150);

    /// <summary>With animations off, things leaving fade out over this long instead of moving.</summary>
    public static readonly TimeSpan ReducedFadeOut = TimeSpan.FromMilliseconds(90);

    /// <summary>Enter, for things arriving: cubic-bezier(0.16, 1, 0.3, 1).</summary>
    public static readonly (Vector2 ControlPoint1, Vector2 ControlPoint2) Enter = (new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

    /// <summary>Exit, for things leaving: cubic-bezier(0.7, 0, 0.84, 0).</summary>
    public static readonly (Vector2 ControlPoint1, Vector2 ControlPoint2) Exit = (new Vector2(0.7f, 0f), new Vector2(0.84f, 0f));

    /// <summary>Move, for pills, knobs and the gate: cubic-bezier(0.65, 0, 0.35, 1).</summary>
    public static readonly (Vector2 ControlPoint1, Vector2 ControlPoint2) Move = (new Vector2(0.65f, 0f), new Vector2(0.35f, 1f));

    private static readonly UISettings _settings = new();

    /// <summary>
    /// Whether the user wants animation (Windows Settings, Accessibility, Visual effects, Animation effects).
    /// Read it each time an animation starts. When false, movement becomes a short fade or nothing.
    /// </summary>
    public static bool AnimationsEnabled => _settings.AnimationsEnabled;

    /// <summary>A new Enter easing for arrivals.</summary>
    public static CubicBezierEasingFunction CreateEnter(Compositor compositor) => CreateBezier(compositor, Enter);

    /// <summary>A new Exit easing for departures.</summary>
    public static CubicBezierEasingFunction CreateExit(Compositor compositor) => CreateBezier(compositor, Exit);

    /// <summary>A new Move easing for pills, knobs and the gate.</summary>
    public static CubicBezierEasingFunction CreateMove(Compositor compositor) => CreateBezier(compositor, Move);

    /// <summary>A new linear easing for playback and progress.</summary>
    public static LinearEasingFunction CreateLinear(Compositor compositor)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        return compositor.CreateLinearEasingFunction();
    }

    private static CubicBezierEasingFunction CreateBezier(Compositor compositor, (Vector2 ControlPoint1, Vector2 ControlPoint2) curve)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        return compositor.CreateCubicBezierEasingFunction(curve.ControlPoint1, curve.ControlPoint2);
    }
}
