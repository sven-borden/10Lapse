using TenLapse.Presentation.Export;
using TenLapse.Presentation.FirstLaunch;

namespace TenLapse.Presentation.Motion;

/// <summary>The easing curves of DESIGN.md (Motion tokens).</summary>
public enum MotionEasing
{
    /// <summary>Things arriving: cubic-bezier(0.16, 1, 0.3, 1).</summary>
    Enter,

    /// <summary>Things leaving: cubic-bezier(0.7, 0, 0.84, 0).</summary>
    Exit,

    /// <summary>Pills, knobs, the gate and the photo's scale: cubic-bezier(0.65, 0, 0.35, 1).</summary>
    Move,

    /// <summary>Playback, progress and the film gate; the reduced-motion fades.</summary>
    Linear,
}

/// <summary>
/// The motion tokens of DESIGN.md and the motion spec (<c>docs/design/canvas/motion/Motion.dc.html</c>) for code: the one
/// source of every duration, delay, stagger and easing an animation in the app uses. <c>Themes/Motion.xaml</c> holds the
/// same durations and curves as XAML resources for Storyboards and templates, <c>.impeccable/design.json</c> as the design
/// sidecar; tests keep the three equal, and a rule test fails on a duration or curve written anywhere else.
/// </summary>
public static class MotionSpec
{
    /// <summary>Press and hover color, linear.</summary>
    public static readonly TimeSpan Tap = TimeSpan.FromMilliseconds(83);

    /// <summary>Toggles, exits and focus. Every exit runs this long, faster than the entrances.</summary>
    public static readonly TimeSpan Control = TimeSpan.FromMilliseconds(167);

    /// <summary>InfoBars, the readout and the segment pill.</summary>
    public static readonly TimeSpan Reveal = TimeSpan.FromMilliseconds(250);

    /// <summary>Screens, panels, thumbnails and the gate.</summary>
    public static readonly TimeSpan Scene = TimeSpan.FromMilliseconds(400);

    /// <summary>The preview developing on arrival.</summary>
    public static readonly TimeSpan Cinema = TimeSpan.FromMilliseconds(700);

    /// <summary>The theme change crossfades the colors over this long, linear.</summary>
    public static readonly TimeSpan ThemeCrossfade = TimeSpan.FromMilliseconds(250);

    /// <summary>With animations off, things arriving fade in over this long instead of moving.</summary>
    public static readonly TimeSpan ReducedFadeIn = TimeSpan.FromMilliseconds(150);

    /// <summary>With animations off, things leaving fade out over this long instead of moving.</summary>
    public static readonly TimeSpan ReducedFadeOut = TimeSpan.FromMilliseconds(90);

    /// <summary>The filmstrip's cascade on import: each thumbnail starts this much after the one before.</summary>
    public static readonly TimeSpan ThumbnailStagger = TimeSpan.FromMilliseconds(18);

    /// <summary>The first thumbnail of the cascade starts this long after the photos land.</summary>
    public static readonly TimeSpan ThumbnailCascadeStart = TimeSpan.FromMilliseconds(220);

    /// <summary>
    /// How long after the photos land a thumbnail the strip shows for the first time still joins the cascade (the
    /// prototype's cascade lasts 1.6 s); later ones, scrolled to, appear at rest.
    /// </summary>
    public static readonly TimeSpan ThumbnailCascadeWindow = TimeSpan.FromMilliseconds(1600);

    /// <summary>
    /// How long the preview's develop waits for the first picture to be decoded before it gives up and the picture simply
    /// appears (not a design token: the develop must start on a picture, not on the empty gate).
    /// </summary>
    public static readonly TimeSpan DevelopPatience = TimeSpan.FromSeconds(2);

    /// <summary>The inspector's sections on arrival: each starts this much after the one before.</summary>
    public static readonly TimeSpan SectionStagger = TimeSpan.FromMilliseconds(60);

    /// <summary>The first inspector section starts this long after the photos land.</summary>
    public static readonly TimeSpan SectionCascadeStart = TimeSpan.FromMilliseconds(160);

    /// <summary>The finished state's check draws its stroke over this long (motion spec, Wrap).</summary>
    public static readonly TimeSpan CheckDraw = TimeSpan.FromMilliseconds(500);

    /// <summary>The check starts drawing this long after the finished state arrives.</summary>
    public static readonly TimeSpan CheckDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>The premiere of the finished video starts this long after the finished state arrives.</summary>
    public static readonly TimeSpan Premiere = TimeSpan.FromMilliseconds(1100);

    /// <summary>The film gate's strip glides linearly to each new progress over the time to the next report.</summary>
    public static TimeSpan GateGlide => ExportViewModel.MinProgressInterval;

    /// <summary>Daybreak's loop, day to night, linear.</summary>
    public static TimeSpan DaybreakLoop => TimeSpan.FromTicks(Daybreak.LengthTicks);

    /// <summary>
    /// The step of the animations the app drives by hand on a timer (the film gate): one tick per frame at 60 Hz. Not a
    /// design token, the pace of the drawing.
    /// </summary>
    public static readonly TimeSpan HandDrivenTick = TimeSpan.FromMilliseconds(16);

    /// <summary>The cubic-bezier control points of <paramref name="easing"/> (Linear is the straight line).</summary>
    public static (float X1, float Y1, float X2, float Y2) Curve(MotionEasing easing) => easing switch
    {
        MotionEasing.Enter => (0.16f, 1f, 0.3f, 1f),
        MotionEasing.Exit => (0.7f, 0f, 0.84f, 0f),
        MotionEasing.Move => (0.65f, 0f, 0.35f, 1f),
        MotionEasing.Linear => (0f, 0f, 1f, 1f),
        _ => throw new ArgumentOutOfRangeException(nameof(easing), easing, "Unknown easing."),
    };

    /// <summary>
    /// How far along <paramref name="easing"/> is at <paramref name="time"/> (0 to 1 of the duration): the curve's y at x =
    /// time, solved by bisection, for animations drawn by hand (the preview's develop and reframe).
    /// </summary>
    public static double Ease(MotionEasing easing, double time)
    {
        time = Math.Clamp(time, 0, 1);
        if (easing == MotionEasing.Linear || time is 0 or 1)
        {
            return time;
        }

        var (x1, y1, x2, y2) = Curve(easing);
        double low = 0, high = 1, t = time;
        for (var i = 0; i < 40; i++)
        {
            t = (low + high) / 2;
            var x = Bezier(x1, x2, t);
            if (Math.Abs(x - time) < 1e-7)
            {
                break;
            }

            if (x < time)
            {
                low = t;
            }
            else
            {
                high = t;
            }
        }

        return Bezier(y1, y2, t);
    }

    // One coordinate of a cubic bezier from (0, 0) to (1, 1) with control points p1 and p2.
    private static double Bezier(double p1, double p2, double t)
    {
        var u = 1 - t;
        return (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t);
    }
}
