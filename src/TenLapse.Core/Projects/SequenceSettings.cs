using TenLapse.Core.Timing;

namespace TenLapse.Core.Projects;

/// <summary>
/// How the photos play out in the video (#49): hold the first frame and the last one for a while, play the photos several
/// times, and play them forward then backward (a boomerang). Immutable; a <c>with</c> expression validates too. The
/// resolved sequence, which photo each video frame shows, is <see cref="ProjectSnapshot.Playback"/> (a
/// <see cref="FrameSequence"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>How they combine.</b> One play is the photos forward, or out and back for a boomerang; the plays follow each other;
/// the holds come once, before the first frame and after the last (<see cref="FrameSequence"/>). A boomerang's play is
/// 0 … P−1 … 1, so a looping player goes on to photo 0 without showing it twice; its end hold shows the first photo,
/// home again.
/// </para>
/// <para>
/// <b>Holds are seconds, shown as frames.</b> A hold is the nearest whole number of frames at the video's rate
/// (<see cref="HoldFrames"/>: half a frame rounds up), and at least one frame when it is not zero, so a hold the user set
/// always shows. The video's frames start where <see cref="FrameTiming.FrameStart"/> puts them, so the length is exact for
/// that frame count; the hold itself is within half a frame of what was asked.
/// </para>
/// <para>
/// <b>A GIF plays once.</b> A GIF loops forever by itself, so repeating the photos inside it would only make the file
/// bigger: for a GIF the plays are 1 (<see cref="For"/>), and the Timing panel leaves the choice out. It is kept for when
/// the format goes back to a video.
/// </para>
/// </remarks>
public sealed record SequenceSettings
{
    /// <summary>The longest hold: 10 s, as long as the longest time per photo.</summary>
    public static TimeSpan MaxHold { get; } = TimeSpan.FromSeconds(10);

    /// <summary>Holds are whole tenths of a second, as the Timing panel's box keeps them.</summary>
    public static TimeSpan HoldStep { get; } = TimeSpan.FromTicks(FrameTiming.TicksPerSecond / 10);

    /// <summary>The photos play at least once.</summary>
    public const int MinPlays = 1;

    /// <summary>At most five times: a loop for a chat or a post, not a screensaver.</summary>
    public const int MaxPlays = 5;

    /// <summary>No holds, once, forward: the photos as they are.</summary>
    public static SequenceSettings Default { get; } = new(TimeSpan.Zero, TimeSpan.Zero, 1, false);

    /// <summary>Creates sequence settings; each must be in its range.</summary>
    /// <param name="holdStart">How long the first frame stays before the photos play, 0 to <see cref="MaxHold"/> in <see cref="HoldStep"/>s.</param>
    /// <param name="holdEnd">How long the last frame stays after they played.</param>
    /// <param name="plays">How many times the photos play, <see cref="MinPlays"/> to <see cref="MaxPlays"/>.</param>
    /// <param name="boomerang">True to play each time forward and then backward.</param>
    public SequenceSettings(TimeSpan holdStart, TimeSpan holdEnd, int plays, bool boomerang)
    {
        HoldStart = holdStart;
        HoldEnd = holdEnd;
        Plays = plays;
        Boomerang = boomerang;
    }

    /// <summary>How long the video's first frame stays on screen before the photos play.</summary>
    public TimeSpan HoldStart
    {
        get;
        init => field = CheckHold(value);
    }

    /// <summary>How long the video's last frame stays on screen after the photos played.</summary>
    public TimeSpan HoldEnd
    {
        get;
        init => field = CheckHold(value);
    }

    /// <summary>How many times the photos play, 1 to 5.</summary>
    public int Plays
    {
        get;
        init => field = value is >= MinPlays and <= MaxPlays ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The photos play from 1 to 5 times.");
    }

    /// <summary>True when each play goes forward and then backward.</summary>
    public bool Boomerang { get; init; }

    /// <summary>True when nothing is held, repeated or reversed.</summary>
    public bool IsPlain => HoldStart == TimeSpan.Zero && HoldEnd == TimeSpan.Zero && Plays == 1 && !Boomerang;

    /// <summary>These settings as a video of <paramref name="format"/> plays them: a GIF plays once (it loops by itself).</summary>
    public SequenceSettings For(VideoFormat format) => format == VideoFormat.Gif && Plays != 1 ? this with { Plays = 1 } : this;

    /// <summary>The frames the photos themselves fill, without the holds (<see cref="FrameSequence.CoreLengthFor"/>).</summary>
    public int PhotoFrames(int photoCount) => FrameSequence.CoreLengthFor(photoCount, Plays, Boomerang);

    /// <summary>The sequence for <paramref name="photoCount"/> photos at <paramref name="rate"/>: the holds in frames at that rate.</summary>
    public FrameSequence Resolve(int photoCount, Fraction rate) =>
        new(photoCount, HoldFrames(HoldStart, rate), HoldFrames(HoldEnd, rate), Plays, Boomerang);

    /// <summary>
    /// A hold in frames at <paramref name="rate"/>: the nearest whole number of frames (half a frame rounds up), and at least
    /// one for a hold that is not zero. 1 s at 29.97 fps is 30 frames; 0.5 s at 2 s per photo is one frame (2 s).
    /// </summary>
    public static int HoldFrames(TimeSpan hold, Fraction rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hold.Ticks);
        if (rate.Numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "The frame rate must be positive.");
        }

        if (hold == TimeSpan.Zero)
        {
            return 0;
        }

        // round(ticks × num / (T × den)) half up = floor((2 × ticks × num + T × den) / (2 × T × den)).
        var scale = (Int128)FrameTiming.TicksPerSecond * rate.Denominator;
        var frames = ((2 * (Int128)hold.Ticks * rate.Numerator) + scale) / (2 * scale);
        return (int)Int128.Max(1, Int128.Min(frames, int.MaxValue));
    }

    /// <summary>The seconds the holds add together, for Length mode (<see cref="TimingSettings.EffectiveRate(int, SequenceSettings)"/>).</summary>
    public TimeSpan Holds => HoldStart + HoldEnd;

    private static TimeSpan CheckHold(TimeSpan value) =>
        value >= TimeSpan.Zero && value <= MaxHold && value.Ticks % HoldStep.Ticks == 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A hold is 0 to 10 s, in tenths of a second.");
}
