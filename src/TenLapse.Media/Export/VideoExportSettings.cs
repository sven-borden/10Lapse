using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Media.Export;

/// <summary>What to write and how much to keep in flight while writing it.</summary>
public sealed record VideoExportSettings
{
    /// <summary>Bits per pixel per frame for <see cref="DefaultBitrate"/>: about 35 Mbit/s at 4K 30 fps, 8.7 at 1080p 30.</summary>
    public const double DefaultBitsPerPixel = 0.14;

    /// <param name="width">Output width in pixels, positive and even (H.264 4:2:0).</param>
    /// <param name="height">Output height in pixels, positive and even.</param>
    /// <param name="frameRate">The exact frame rate; sample times come from <see cref="FrameTiming"/>.</param>
    public VideoExportSettings(int width, int height, Fraction frameRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width % 2 != 0 || height % 2 != 0)
        {
            throw new ArgumentException($"H.264 needs an even width and height; got {width}x{height}.");
        }

        if (frameRate.Numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameRate), frameRate, "The frame rate must be positive.");
        }

        Width = width;
        Height = height;
        FrameRate = frameRate;
    }

    /// <summary>Output width in pixels.</summary>
    public int Width { get; }

    /// <summary>Output height in pixels.</summary>
    public int Height { get; }

    /// <summary>The exact output frame rate.</summary>
    public Fraction FrameRate { get; }

    /// <summary>
    /// The codec: H.264 (the default) or HEVC. Offer HEVC only where <see cref="Encoding.EncoderCapabilities"/>
    /// says it works; without an HEVC encoder the export fails as <see cref="VideoExportFailure.EncoderUnavailable"/>.
    /// </summary>
    public VideoFormat Format
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown video format.");
    }

    /// <summary>Target bitrate in bits per second; null uses <see cref="DefaultBitrate"/>.</summary>
    public uint? Bitrate { get; init; }

    /// <summary>
    /// Lets Media Foundation use a GPU encoder when there is one (the default;
    /// <c>MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS</c>). Off
    /// forces the Microsoft software encoder, for comparison.
    /// </summary>
    public bool HardwareAcceleration { get; init; } = true;

    /// <summary>
    /// How frames are placed (#18): Fill (a centred cover crop, the default) or Fit (letterboxed). The frame source
    /// must decode for the same mode (<see cref="PhotoFileSource.Fit"/>, <see cref="Rendering.FrameRenderer.DecodeOptions"/>).
    /// </summary>
    public FitMode Fit
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown fit mode.");
    } = FitMode.Fill;

    /// <summary>
    /// How many source frames may be loading, or loaded and waiting to be drawn, at once: the frame being drawn
    /// counts until it is drawn. This bounds the decoded bitmaps in memory. At least 1.
    /// </summary>
    public int DecodeAhead { get; init; } = 8;

    /// <summary>
    /// How many loads may run at once, at most <see cref="DecodeAhead"/>: the decode concurrency, bounded
    /// separately from the queue depth. Each running WIC decode holds its CPU buffer. Defaults to half the
    /// logical processors, 1 to 6 (measured on 16 threads, 24 MP to 4K: 4 threads 23 fps, 6 threads 36 fps, 8 threads
    /// 42 fps at 100 MB more; 6 keeps an iGPU, whose textures share system memory, under 1 GB).
    /// </summary>
    public int DecodeThreads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 6);

    /// <summary>
    /// How long one encoder call (opening, writing one frame, finishing) may take before the export fails as
    /// <see cref="VideoExportFailure.EncoderStalled"/> and abandons the call: <see cref="VideoExporter.EncoderStallTimeout"/>.
    /// </summary>
    public TimeSpan StallTimeout
    {
        get;
        init => field = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The stall timeout must be positive.");
    } = VideoExporter.EncoderStallTimeout;

    /// <summary>
    /// How many drawn output frames may wait for the encoder at once. Each frame
    /// gets a new render target; none is ever drawn twice (see <see cref="VideoExporter"/>).
    /// </summary>
    public int RenderTargets { get; init; } = 4;

    /// <summary>The bitrate the export uses.</summary>
    public uint EffectiveBitrate => Bitrate ?? DefaultBitrate(Width, Height, FrameRate);

    /// <summary><see cref="DefaultBitsPerPixel"/> times pixels times frames per second, between 1 and 200 Mbit/s.</summary>
    public static uint DefaultBitrate(int width, int height, Fraction frameRate)
    {
        var bits = DefaultBitsPerPixel * width * height * frameRate.ToDouble();
        return (uint)Math.Clamp(bits, 1_000_000, 200_000_000);
    }
}
