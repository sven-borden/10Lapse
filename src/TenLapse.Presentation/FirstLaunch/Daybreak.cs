using TenLapse.Core.Timing;

namespace TenLapse.Presentation.FirstLaunch;

/// <summary>
/// Daybreak, the small timelapse beside the first-launch drop zone (motion spec, docs/design/canvas/motion): a made-up
/// sequence of <see cref="FrameCount"/> photos of a harbor from day to night, played at <see cref="Rate"/> in a loop of
/// exactly 20 s, with a strip of <see cref="ThumbCount"/> thumbnails and a caption ("Frame 0181 of 0600",
/// "00:06.00 / 00:20.00"). Every time on screen comes from the frame's index through <see cref="FrameTiming"/>, as the
/// app's own preview will.
/// </summary>
public static class Daybreak
{
    /// <summary>The photos in the made-up sequence: 20 s at 30 fps, the motion spec's loop.</summary>
    public const int FrameCount = 600;

    /// <summary>The thumbnails in the strip under the frame.</summary>
    public const int ThumbCount = 12;

    /// <summary>
    /// The frame shown with animations off (motion spec: "a still frame at golden hour"), and where the loop starts:
    /// the sun low over the water, the horizon golden.
    /// </summary>
    public const int StillFrame = 180;

    /// <summary>The sequence's frame rate.</summary>
    public static Fraction Rate { get; } = new(30, 1);

    /// <summary>The loop's length in 100 ns ticks (20 s).</summary>
    public static long LengthTicks => FrameTiming.Duration(FrameCount, Rate);

    /// <summary>
    /// The frame on screen after <paramref name="played"/> of playback: the last frame whose start is not later,
    /// looping after the last one.
    /// </summary>
    public static int FrameAt(TimeSpan played)
    {
        var ticks = Math.Max(0, played.Ticks) % LengthTicks;

        // Estimate from the rate, then settle on the frame whose start interval holds the time (FrameStart floors).
        var index = (long)((Int128)ticks * Rate.Numerator / ((Int128)Rate.Denominator * FrameTiming.TicksPerSecond));
        while (index + 1 < FrameCount && FrameTiming.FrameStart(index + 1, Rate) <= ticks)
        {
            index++;
        }

        while (index > 0 && FrameTiming.FrameStart(index, Rate) > ticks)
        {
            index--;
        }

        return (int)index;
    }

    /// <summary>The playback time at which <paramref name="frame"/> starts.</summary>
    public static TimeSpan StartOf(int frame) => TimeSpan.FromTicks(FrameTiming.FrameStart(Check(frame), Rate));

    /// <summary>How far <paramref name="frame"/> is from day (0) to night (1).</summary>
    public static double Phase(int frame) => Check(frame) / (double)(FrameCount - 1);

    /// <summary>The thumbnail that marks <paramref name="frame"/> in the strip (the nearest one by phase).</summary>
    public static int SelectedThumb(int frame) => (int)Math.Round(Phase(frame) * (ThumbCount - 1), MidpointRounding.AwayFromZero);

    /// <summary>How far thumbnail <paramref name="thumb"/> is from day to night.</summary>
    public static double ThumbPhase(int thumb)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(thumb);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(thumb, ThumbCount);
        return thumb / (double)(ThumbCount - 1);
    }

    /// <summary>"Frame 0181 of 0600".</summary>
    public static string FrameCaption(int frame) => ProjectText.FrameCaption(Check(frame) + 1, FrameCount);

    /// <summary>"00:06.00 / 00:20.00": the frame's start and the loop's length.</summary>
    public static string TimeCaption(int frame) =>
        ProjectText.TimecodeCaption(FrameTiming.FrameStart(Check(frame), Rate), LengthTicks);

    private static int Check(int frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, FrameCount);
        return frame;
    }
}
