using TenLapse.Core.Timing;

namespace TenLapse.Core.Projects;

/// <summary>How crop and pan moves from the start framing to the end framing (#46).</summary>
public enum CropEasing
{
    /// <summary>Starts gently, moves fastest in the middle and settles into the end framing (smoothstep).</summary>
    Smooth,

    /// <summary>The same speed from the first frame to the last.</summary>
    Linear,
}

/// <summary>
/// The crop and pan settings (#46): on or off, the framing the video starts with, the one it ends with, and how it moves
/// between them. Immutable; a <c>with</c> expression validates too. Project only, like the look: a new project starts
/// with the whole frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>The timeline.</b> The move runs over the frames the photos fill (<see cref="FrameSequence.CoreLength"/>): the first
/// of them shows <see cref="Start"/>, the last <see cref="End"/>, and a hold keeps the framing of the frame it holds, so a
/// held first or last frame stands still as a hold should. Plays and a boomerang are inside the core, so the move spans
/// all of them, once; it follows the video, not the photos, so a photo the boomerang shows twice is framed where the
/// video is at each showing.
/// </para>
/// <para>
/// Turning it off keeps both framings, so turning it on again brings them back.
/// </para>
/// </remarks>
public sealed record CropSettings
{
    /// <summary>Off, both framings the whole frame, smooth: a project from before #46 has no crop group and reads as this.</summary>
    public static CropSettings Default { get; } = new(false, Framing.Whole, Framing.Whole, CropEasing.Smooth);

    /// <summary>Creates crop and pan settings.</summary>
    /// <param name="enabled">Whether the video is framed by <paramref name="start"/> and <paramref name="end"/>.</param>
    /// <param name="start">The framing of the first frame the photos fill.</param>
    /// <param name="end">The framing of the last.</param>
    /// <param name="easing">How the framing moves between them.</param>
    public CropSettings(bool enabled, Framing start, Framing end, CropEasing easing)
    {
        Enabled = enabled;
        Start = start;
        End = end;
        Easing = easing;
    }

    /// <summary>Whether the video is framed by <see cref="Start"/> and <see cref="End"/>; off shows the whole frame.</summary>
    public bool Enabled { get; init; }

    /// <summary>The framing of the first frame the photos fill (and of a start hold).</summary>
    public Framing Start { get; init; }

    /// <summary>The framing of the last frame the photos fill (and of an end hold).</summary>
    public Framing End { get; init; }

    /// <summary>How the framing moves from <see cref="Start"/> to <see cref="End"/>.</summary>
    public CropEasing Easing
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown easing.");
    }

    /// <summary>True when it changes the picture: on, and at least one framing is not the whole frame.</summary>
    public bool IsActive => Enabled && !(Start.IsWhole && End.IsWhole);

    /// <summary>The most any frame is enlarged: 1 unless <see cref="IsActive"/>. The export decodes for it.</summary>
    public double MaxZoom => IsActive ? Math.Max(Start.Zoom, End.Zoom) : 1;

    /// <summary>
    /// The framing a share <paramref name="progress"/> (0 to 1) of the way through the move, after <see cref="Easing"/>:
    /// <see cref="Framing.Whole"/> unless <see cref="IsActive"/>.
    /// </summary>
    public Framing At(double progress)
    {
        if (!IsActive)
        {
            return Framing.Whole;
        }

        var t = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        var eased = Easing == CropEasing.Smooth ? t * t * (3 - (2 * t)) : t;
        return Framing.Between(Start, End, eased);
    }

    /// <summary>The framing of frame <paramref name="position"/> of <paramref name="sequence"/> (see the remarks: the holds stand still).</summary>
    public Framing At(FrameSequence sequence, int position) => At(Progress(sequence, position));

    /// <summary>
    /// How far through the move frame <paramref name="position"/> of the video is: 0 up to the first frame the photos fill
    /// (the start hold), 1 from the last one on (the end hold), evenly between. A video whose photos fill one frame is at 0.
    /// </summary>
    public static double Progress(FrameSequence sequence, int position)
    {
        var last = sequence.CoreLength - 1;
        if (last <= 0)
        {
            return 0;
        }

        return Math.Clamp(position - sequence.HoldStartFrames, 0, last) / (double)last;
    }
}
