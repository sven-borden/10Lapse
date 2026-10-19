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
    /// counts until it is drawn. This bounds the decoded bitmaps in memory. At least 1. Null (the default) lets
    /// the export choose for the PC (<see cref="DecodeFor"/>): 8, or 4 where memory is tight.
    /// </summary>
    public int? DecodeAhead { get; init; }

    /// <summary>
    /// How many loads may run at once, at most the decode depth: the decode concurrency, bounded separately from
    /// the queue depth. Each running WIC decode holds its CPU buffer. Null (the default) lets the export choose
    /// for the PC (<see cref="DecodeFor"/>): half the logical processors, 1 to 6, or 1 to 3 where memory is tight.
    /// </summary>
    public int? DecodeThreads { get; init; }

    /// <summary>
    /// The decode depth and concurrency an export uses: <see cref="DecodeAhead"/> and <see cref="DecodeThreads"/>
    /// when set, otherwise the defaults for the PC. Measured on a dedicated GPU (16 threads, 24 MP JPEGs to 4K):
    /// 6 threads and 8 ahead gave 36 fps at 487 MB peak, against 23 fps for 4 and 6. On an integrated GPU the
    /// decoded textures (about 40 MB each at 4K) live in system memory too, which puts the same defaults at about
    /// 1.0 to 1.1 GB, over the 1 GB target; there, and on a PC with 8 GB of RAM or less, 4 ahead and at most 3
    /// threads halve the decoded frames in flight.
    /// </summary>
    /// <param name="tightMemory">True on an integrated GPU or with 8 GB of RAM or less (<see cref="IsTightMemory"/>).</param>
    public (int Ahead, int Threads) DecodeFor(bool tightMemory)
    {
        var ahead = Math.Max(1, DecodeAhead ?? (tightMemory ? 4 : 8));
        var threads = DecodeThreads ?? Math.Clamp(Environment.ProcessorCount / 2, 1, tightMemory ? 3 : 6);
        return (ahead, Math.Clamp(threads, 1, ahead));
    }

    /// <summary>
    /// True when the export should keep fewer decoded frames: the graphics adapter shares system memory (an
    /// integrated GPU, D3D11 UnifiedMemoryArchitecture) or the PC has 8 GB of RAM or less.
    /// </summary>
    public static bool IsTightMemory(bool unifiedMemory, long physicalMemoryBytes) =>
        unifiedMemory || physicalMemoryBytes <= TightMemoryBytes;

    // 8 GB of RAM reports a little less than 8 GiB (memory the firmware keeps); half a GiB of headroom.
    private const long TightMemoryBytes = 17L * 512 * 1024 * 1024;

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
