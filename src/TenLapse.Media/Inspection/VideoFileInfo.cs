using TenLapse.Core.Timing;

namespace TenLapse.Media.Inspection;

/// <summary>
/// What a video file holds, as Media Foundation reads it back: the first video
/// stream's samples in presentation order, and the container's duration. All
/// times are 100 ns ticks, like <see cref="FrameTiming"/>.
/// </summary>
/// <param name="Codec">The stream's subtype, such as <c>H264</c>.</param>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="FrameRate">The nominal frame rate the stream declares, or null if it declares none.</param>
/// <param name="Duration">The presentation duration (<c>MF_PD_DURATION</c>).</param>
/// <param name="SampleTimes">Every sample's presentation time, ascending.</param>
/// <param name="SampleDurations">Each sample's duration, in the same order; -1 where the file gives none.</param>
public sealed record VideoFileInfo(
    string Codec,
    int Width,
    int Height,
    Fraction? FrameRate,
    long Duration,
    IReadOnlyList<long> SampleTimes,
    IReadOnlyList<long> SampleDurations)
{
    /// <summary>The number of video samples, which for H.264 is the number of frames.</summary>
    public int FrameCount => SampleTimes.Count;
}
