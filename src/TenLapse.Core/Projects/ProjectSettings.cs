using TenLapse.Core.Timing;

namespace TenLapse.Core.Projects;

/// <summary>How the user sets the speed in the Timing panel (#17). Each mode keeps its own input.</summary>
public enum SpeedMode
{
    /// <summary>Frames per second.</summary>
    PerSecond,

    /// <summary>A target total length; the rate follows from it and the frame count.</summary>
    Length,

    /// <summary>Seconds each photo stays on screen, for slideshows.</summary>
    PerPhoto,
}

/// <summary>Which bound a <see cref="PlaybackRate"/> was held at, if any.</summary>
public enum RateClamp
{
    /// <summary>The rate is what the user's input asks for.</summary>
    None,

    /// <summary>The input asks for less than <see cref="FrameRates.Min"/> (a long length for few photos, or no photos); the rate is the minimum.</summary>
    Min,

    /// <summary>The input asks for more than <see cref="FrameRates.Max"/> (a short length for many photos); the rate is the maximum.</summary>
    Max,
}

/// <summary>The rate a project plays and exports at for its current frame count, and whether it was clamped (#17 says so).</summary>
/// <param name="Rate">Frames per second, exact, always in <see cref="FrameRates.Min"/> to <see cref="FrameRates.Max"/>.</param>
/// <param name="Clamp">The bound the rate was held at.</param>
public readonly record struct PlaybackRate(Fraction Rate, RateClamp Clamp)
{
    /// <summary>True when the input asked for a rate outside the range, so the clip length differs from a Length target.</summary>
    public bool IsClamped => Clamp != RateClamp.None;
}

/// <summary>
/// The project's speed: the mode the user chose and each mode's own input, so switching modes keeps what the
/// user typed in the others. Immutable; a <c>with</c> expression validates too, so every input is always in
/// its #17 range. The rate itself depends on the frame count in Length mode: ask
/// <see cref="EffectiveRate"/> (or <see cref="ProjectSnapshot.EffectiveRate"/>), never store it.
/// </summary>
public sealed record TimingSettings
{
    /// <summary>Per second: from 1 fps (#17).</summary>
    public static Fraction MinFramesPerSecond { get; } = new(1, 1);

    /// <summary>Per second: up to 120 fps (#17), <see cref="FrameRates.Max"/>.</summary>
    public static Fraction MaxFramesPerSecond => FrameRates.Max;

    /// <summary>Length: from 0.1 s.</summary>
    public static Fraction MinLengthSeconds { get; } = new(1, 10);

    /// <summary>Length: up to 10 h (36,000 s), past what 5,000 photos fill even at <see cref="FrameRates.Min"/>.</summary>
    public static Fraction MaxLengthSeconds { get; } = new(36_000, 1);

    /// <summary>Per photo: from 0.1 s (#17).</summary>
    public static Fraction MinSecondsPerPhoto { get; } = new(1, 10);

    /// <summary>Per photo: up to 10 s (#17).</summary>
    public static Fraction MaxSecondsPerPhoto { get; } = new(10, 1);

    /// <summary>30 fps set per second, with a 30 s length and 0.5 s per photo waiting in the other modes.</summary>
    public static TimingSettings Default { get; } = new(SpeedMode.PerSecond, new Fraction(30, 1), new Fraction(30, 1), new Fraction(1, 2));

    /// <summary>Creates timing settings; each input must be in its range, whatever the mode.</summary>
    /// <param name="mode">The mode the Timing panel shows, which decides the rate.</param>
    /// <param name="framesPerSecond">Per second input, <see cref="MinFramesPerSecond"/> to <see cref="MaxFramesPerSecond"/>.</param>
    /// <param name="lengthSeconds">Length input in seconds, <see cref="MinLengthSeconds"/> to <see cref="MaxLengthSeconds"/>.</param>
    /// <param name="secondsPerPhoto">Per photo input in seconds, <see cref="MinSecondsPerPhoto"/> to <see cref="MaxSecondsPerPhoto"/>.</param>
    public TimingSettings(SpeedMode mode, Fraction framesPerSecond, Fraction lengthSeconds, Fraction secondsPerPhoto)
    {
        Mode = mode;
        FramesPerSecond = framesPerSecond;
        LengthSeconds = lengthSeconds;
        SecondsPerPhoto = secondsPerPhoto;
    }

    /// <summary>The mode the Timing panel shows; it decides which input sets the rate.</summary>
    public SpeedMode Mode
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown speed mode.");
    }

    /// <summary>The Per second input, exact (29.97 is 30000/1001: convert user input once with <see cref="FrameRates.TryParse"/>).</summary>
    public Fraction FramesPerSecond
    {
        get;
        init => field = InRange(value, MinFramesPerSecond, MaxFramesPerSecond, "The frame rate must be from 1 to 120 fps.");
    }

    /// <summary>The Length input: the target clip length in seconds.</summary>
    public Fraction LengthSeconds
    {
        get;
        init => field = InRange(value, MinLengthSeconds, MaxLengthSeconds, "The length must be from 0.1 s to 10 h.");
    }

    /// <summary>The Per photo input: the seconds each photo stays on screen.</summary>
    public Fraction SecondsPerPhoto
    {
        get;
        init => field = InRange(value, MinSecondsPerPhoto, MaxSecondsPerPhoto, "A photo must stay from 0.1 to 10 s.");
    }

    /// <summary>Per second at <paramref name="framesPerSecond"/>, the other inputs at their defaults.</summary>
    public static TimingSettings PerSecond(Fraction framesPerSecond) =>
        Default with { Mode = SpeedMode.PerSecond, FramesPerSecond = framesPerSecond };

    /// <summary>Length mode with a target of <paramref name="lengthSeconds"/>, the other inputs at their defaults.</summary>
    public static TimingSettings Length(Fraction lengthSeconds) =>
        Default with { Mode = SpeedMode.Length, LengthSeconds = lengthSeconds };

    /// <summary>Per photo at <paramref name="secondsPerPhoto"/>, the other inputs at their defaults.</summary>
    public static TimingSettings PerPhoto(Fraction secondsPerPhoto) =>
        Default with { Mode = SpeedMode.PerPhoto, SecondsPerPhoto = secondsPerPhoto };

    /// <summary>
    /// The rate for <paramref name="frameCount"/> frames. Per second and Per photo give their input (always in
    /// range). Length gives <c>count / length</c> (<see cref="FrameRates.FromTotalLength"/>), held to
    /// <see cref="FrameRates.Min"/> or <see cref="FrameRates.Max"/> with <see cref="PlaybackRate.Clamp"/> set
    /// when it falls outside; no frames at all gives the minimum, clamped. It never throws for a count of 0 or more.
    /// </summary>
    public PlaybackRate EffectiveRate(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        switch (Mode)
        {
            case SpeedMode.PerSecond:
                return new PlaybackRate(FramesPerSecond, RateClamp.None);
            case SpeedMode.PerPhoto:
                return new PlaybackRate(FrameRates.FromSecondsPerPhoto(SecondsPerPhoto), RateClamp.None);
        }

        if (frameCount == 0)
        {
            return new PlaybackRate(FrameRates.Min, RateClamp.Min);
        }

        var rate = FrameRates.FromTotalLength(frameCount, LengthSeconds);
        return rate < FrameRates.Min ? new PlaybackRate(FrameRates.Min, RateClamp.Min)
            : rate > FrameRates.Max ? new PlaybackRate(FrameRates.Max, RateClamp.Max)
            : new PlaybackRate(rate, RateClamp.None);
    }

    private static Fraction InRange(Fraction value, Fraction min, Fraction max, string message) =>
        value >= min && value <= max ? value : throw new ArgumentOutOfRangeException(nameof(value), value, message);
}

/// <summary>The output shape (#18).</summary>
public enum FrameAspect
{
    /// <summary>The most common photo size in the set.</summary>
    MatchPhotos,

    /// <summary>16:9 landscape.</summary>
    Ratio16x9,

    /// <summary>9:16 portrait, for phones.</summary>
    Ratio9x16,

    /// <summary>1:1 square.</summary>
    Ratio1x1,

    /// <summary>4:5 portrait.</summary>
    Ratio4x5,

    /// <summary>4:3 landscape.</summary>
    Ratio4x3,
}

/// <summary>How a photo that does not match the shape is placed (#18).</summary>
public enum FitMode
{
    /// <summary>Letterbox: the whole photo shows, with bars.</summary>
    Fit,

    /// <summary>Centre crop: the frame is filled, edges are cut.</summary>
    Fill,
}

/// <summary>The Frame panel's settings. Immutable.</summary>
public sealed record FrameSettings
{
    /// <summary>16:9, filled, as the prototype starts.</summary>
    public static FrameSettings Default { get; } = new(FrameAspect.Ratio16x9, FitMode.Fill);

    /// <summary>Creates frame settings.</summary>
    public FrameSettings(FrameAspect aspect, FitMode fit)
    {
        Aspect = aspect;
        Fit = fit;
    }

    /// <summary>The output shape.</summary>
    public FrameAspect Aspect
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown aspect.");
    }

    /// <summary>Fit or fill.</summary>
    public FitMode Fit
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown fit mode.");
    }
}

/// <summary>
/// The output resolution (#23), named by the <b>short side</b> of the frame: the long side follows the frame
/// shape. 1080p is 1920 × 1080 at 16:9 and 1080 × 1920 at 9:16, as phones and video sites name portrait video.
/// <see cref="OutputSize.For"/> gives both sides.
/// </summary>
public enum OutputResolution
{
    /// <summary>720p: the short side is 720 px.</summary>
    Hd720,

    /// <summary>1080p: the short side is 1080 px.</summary>
    Hd1080,

    /// <summary>1440p: the short side is 1440 px.</summary>
    Qhd1440,

    /// <summary>2160p (4K): the short side is 2160 px.</summary>
    Uhd2160,
}

/// <summary>The output frame size in pixels, from the resolution (the short side) and the frame shape.</summary>
public static class OutputSize
{
    /// <summary>The short side in pixels: 720, 1080, 1440 or 2160.</summary>
    public static int ShortSide(OutputResolution resolution) => resolution switch
    {
        OutputResolution.Hd720 => 720,
        OutputResolution.Hd1080 => 1080,
        OutputResolution.Qhd1440 => 1440,
        OutputResolution.Uhd2160 => 2160,
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown resolution."),
    };

    /// <summary>
    /// The width and height of the exported video, so #9 and #23 can check both sides against what the
    /// encoder supports. The short side is <see cref="ShortSide"/>; the long side follows the shape, rounded
    /// to the nearest even number (4:2:0 video needs even sides). 1080p gives 1920 × 1080 at 16:9,
    /// 1080 × 1920 at 9:16, 1080 × 1080 at 1:1, 1080 × 1350 at 4:5 and 1440 × 1080 at 4:3.
    /// </summary>
    /// <param name="resolution">The resolution.</param>
    /// <param name="aspect">The frame shape.</param>
    /// <param name="photoSize">For <see cref="FrameAspect.MatchPhotos"/>: the most common photo size, whose shape the video takes.</param>
    /// <exception cref="ArgumentException"><see cref="FrameAspect.MatchPhotos"/> without a photo size.</exception>
    public static PixelSize For(OutputResolution resolution, FrameAspect aspect, PixelSize? photoSize = null)
    {
        var (width, height) = aspect switch
        {
            FrameAspect.Ratio16x9 => (16, 9),
            FrameAspect.Ratio9x16 => (9, 16),
            FrameAspect.Ratio1x1 => (1, 1),
            FrameAspect.Ratio4x5 => (4, 5),
            FrameAspect.Ratio4x3 => (4, 3),
            FrameAspect.MatchPhotos => photoSize is { } size
                ? (size.Width, size.Height)
                : throw new ArgumentException("Match photos needs the photos' size.", nameof(photoSize)),
            _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "Unknown aspect."),
        };

        var shortSide = ShortSide(resolution);
        var longSide = LongSide(shortSide, Math.Max(width, height), Math.Min(width, height));
        return width >= height ? new PixelSize(longSide, shortSide) : new PixelSize(shortSide, longSide);
    }

    // shortSide × longRatio / shortRatio, rounded half up to an even number.
    private static int LongSide(int shortSide, long longRatio, long shortRatio) =>
        (int)(((shortSide * longRatio) + shortRatio) / (2 * shortRatio) * 2);
}

/// <summary>Container and codec (#23).</summary>
public enum VideoFormat
{
    /// <summary>MP4 with H.264, available everywhere.</summary>
    Mp4H264,

    /// <summary>MP4 with HEVC, when the codec is installed.</summary>
    Mp4Hevc,
}

/// <summary>Quality level, mapped to a bitrate per resolution and rate (#23).</summary>
public enum OutputQuality
{
    /// <summary>Smaller files.</summary>
    Standard,

    /// <summary>The default.</summary>
    High,

    /// <summary>Largest files.</summary>
    Maximum,
}

/// <summary>The Output panel's settings. Immutable. #23 narrows them to what the hardware supports.</summary>
public sealed record OutputSettings
{
    /// <summary>1080p (short side) H.264 at High: every PC can export it, so #23 only ever raises it.</summary>
    public static OutputSettings Default { get; } = new(OutputResolution.Hd1080, VideoFormat.Mp4H264, OutputQuality.High);

    /// <summary>Creates output settings.</summary>
    public OutputSettings(OutputResolution resolution, VideoFormat format, OutputQuality quality)
    {
        Resolution = resolution;
        Format = format;
        Quality = quality;
    }

    /// <summary>The resolution: the short side of the output (see <see cref="OutputSize.For"/>).</summary>
    public OutputResolution Resolution
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown resolution.");
    }

    /// <summary>Container and codec.</summary>
    public VideoFormat Format
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown format.");
    }

    /// <summary>Quality level.</summary>
    public OutputQuality Quality
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown quality.");
    }
}

/// <summary>How the frames are ordered (#15). The project stores the choice; #15 computes the order.</summary>
public enum SortMode
{
    /// <summary>EXIF capture time, ties broken by name.</summary>
    CaptureTime,

    /// <summary>File name in Explorer order (IMG_9 before IMG_10).</summary>
    Name,

    /// <summary>File date modified, ties broken by name.</summary>
    DateModified,

    /// <summary>The order the user dragged them into.</summary>
    Manual,
}
