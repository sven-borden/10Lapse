using TenLapse.Core.Timing;

namespace TenLapse.Core.Projects;

/// <summary>
/// The bitrate each <see cref="OutputQuality"/> exports at, and the file size that gives, from one formula so the
/// Output panel's estimate (#23) and the export agree.
/// </summary>
/// <remarks>
/// <para>
/// Bits per second = bits per pixel × pixels × a rate factor. The rate factor is the frame rate from 24 fps up;
/// below 24 fps it is √(24 × rate), so slow clips get more bits per frame: at 1 fps each frame is nearly a new
/// picture (a slideshow), and it stays on screen long enough to be looked at.
/// </para>
/// <para>
/// H.264 bits per pixel: Standard 0.08, High 0.14 (about 8.7 Mbit/s at 1080p 30 fps and 35 Mbit/s at 4K 30 fps,
/// what video sites recommend), Maximum 0.24. HEVC gets 60 % of that for the same look. The result is held to
/// <see cref="MinBitrate"/> to <see cref="MaxBitrate"/>.
/// </para>
/// </remarks>
public static class ExportBitrate
{
    /// <summary>The lowest bitrate an export uses: 250 kbit/s.</summary>
    public const uint MinBitrate = 250_000;

    /// <summary>The highest bitrate an export uses: 200 Mbit/s, within what H.264 level 5.2 and every hardware encoder take.</summary>
    public const uint MaxBitrate = 200_000_000;

    /// <summary>HEVC's share of the H.264 bitrate for the same quality.</summary>
    public const double HevcShare = 0.6;

    // Bytes the MP4 container adds: the header, and per frame its index entries (size, time, sync, chunk offset).
    private const long ContainerBytes = 4_096;
    private const long ContainerBytesPerFrame = 16;

    /// <summary>The bits per pixel per frame of a quality and codec, at 24 fps and above.</summary>
    public static double BitsPerPixel(VideoFormat format, OutputQuality quality)
    {
        var h264 = quality switch
        {
            OutputQuality.Standard => 0.08,
            OutputQuality.High => 0.14,
            OutputQuality.Maximum => 0.24,
            _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Unknown quality."),
        };
        return format switch
        {
            VideoFormat.Mp4H264 => h264,
            VideoFormat.Mp4Hevc => h264 * HevcShare,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
        };
    }

    /// <summary>The bitrate in bits per second for a video of <paramref name="size"/> at <paramref name="rate"/>.</summary>
    public static uint For(PixelSize size, Fraction rate, VideoFormat format, OutputQuality quality)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Width, nameof(size));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Height, nameof(size));
        if (rate.Numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "The frame rate must be positive.");
        }

        var fps = rate.ToDouble();
        var rateFactor = fps >= 24 ? fps : Math.Sqrt(24 * fps);
        var bits = BitsPerPixel(format, quality) * size.Width * size.Height * rateFactor;
        return (uint)Math.Clamp(Math.Round(bits), MinBitrate, MaxBitrate);
    }

    /// <summary>
    /// The expected size in bytes of an export of <paramref name="frameCount"/> frames: the bitrate over the clip's
    /// exact duration plus the MP4 container. An estimate: the encoder spends less on still scenes and may
    /// overshoot a little on busy ones.
    /// </summary>
    public static long EstimateBytes(int frameCount, Fraction rate, PixelSize size, VideoFormat format, OutputQuality quality)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        var bitrate = For(size, rate, format, quality);
        if (frameCount == 0)
        {
            return 0;
        }

        var seconds = (double)FrameTiming.Duration(frameCount, rate) / FrameTiming.TicksPerSecond;
        return (long)Math.Round(bitrate * seconds / 8) + ContainerBytes + (ContainerBytesPerFrame * frameCount);
    }
}
