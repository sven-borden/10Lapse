using TenLapse.Core.Timing;

namespace TenLapse.Core.Projects;

/// <summary>How the user sets the speed in the Timing panel (#17). The rate is the truth; the mode is the view.</summary>
public enum SpeedMode
{
    /// <summary>Frames per second.</summary>
    PerSecond,

    /// <summary>A target total length; the rate is computed from it.</summary>
    Length,

    /// <summary>Seconds each photo stays on screen, for slideshows.</summary>
    PerPhoto,
}

/// <summary>
/// The project's speed: an exact rational frame rate and the mode the user chose to set it in. Immutable; a
/// <c>with</c> expression validates too, so a <see cref="TimingSettings"/> always holds a rate in
/// <see cref="FrameRates.Min"/> to <see cref="FrameRates.Max"/>.
/// </summary>
public sealed record TimingSettings
{
    /// <summary>30 fps, set per second.</summary>
    public static TimingSettings Default { get; } = new(new Fraction(30, 1), SpeedMode.PerSecond);

    /// <summary>Creates timing settings; the rate must pass <see cref="FrameRates.IsInRange"/>.</summary>
    public TimingSettings(Fraction rate, SpeedMode mode)
    {
        Rate = rate;
        Mode = mode;
    }

    /// <summary>Frames per second, exact. Convert user input once (see <see cref="FrameRates"/>), never from display text.</summary>
    public Fraction Rate
    {
        get;
        init
        {
            if (!FrameRates.IsInRange(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"The frame rate must be from {FrameRates.Min} to {FrameRates.Max} fps.");
            }

            field = value;
        }
    }

    /// <summary>The mode the Timing panel shows.</summary>
    public SpeedMode Mode
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown speed mode.");
    }
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

/// <summary>Output height (#23); the width follows the frame shape.</summary>
public enum OutputResolution
{
    /// <summary>720 p.</summary>
    Hd720,

    /// <summary>1080 p.</summary>
    Hd1080,

    /// <summary>1440 p.</summary>
    Qhd1440,

    /// <summary>2160 p (4K).</summary>
    Uhd2160,
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
    /// <summary>1080 p H.264 at High: every PC can export it, so #23 only ever raises it.</summary>
    public static OutputSettings Default { get; } = new(OutputResolution.Hd1080, VideoFormat.Mp4H264, OutputQuality.High);

    /// <summary>Creates output settings.</summary>
    public OutputSettings(OutputResolution resolution, VideoFormat format, OutputQuality quality)
    {
        Resolution = resolution;
        Format = format;
        Quality = quality;
    }

    /// <summary>The output height.</summary>
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
