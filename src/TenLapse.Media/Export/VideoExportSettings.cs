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

    /// <summary>Target bitrate in bits per second; null uses <see cref="DefaultBitrate"/>.</summary>
    public uint? Bitrate { get; init; }

    /// <summary>
    /// Lets Media Foundation use a GPU encoder when there is one (the default;
    /// <c>MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS</c>). Off
    /// forces the Microsoft software encoder, for comparison.
    /// </summary>
    public bool HardwareAcceleration { get; init; } = true;

    /// <summary>How many source frames may be loading or decoded and waiting at once.</summary>
    public int DecodeAhead { get; init; } = 4;

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
