using System.Globalization;
using TenLapse.Core.Projects;

namespace TenLapse.Media.Encoding;

/// <summary>
/// Every user-visible word about what this PC can encode, in one place for localization (#39). Whole format
/// strings, so a translation can reorder them.
/// </summary>
public static class EncoderText
{
    /// <summary>{0}: the largest resolution, such as "1440p". Every size up to it uses the graphics hardware.</summary>
    public const string HardwareUpTo = "This PC's graphics hardware exports up to {0}.";

    /// <summary>{0}: the largest resolution the hardware takes; larger ones work, slower, in software.</summary>
    public const string HardwareUpToThenSoftware = "This PC's graphics hardware exports up to {0}; larger sizes use the slower software encoder.";

    /// <summary>{0}: the codec, such as "H.264"; {1}: the largest resolution the software encoder takes.</summary>
    public const string SoftwareOnly = "This PC's graphics hardware cannot encode {0}; the slower software encoder exports up to {1}.";

    /// <summary>{0}: the codec, such as "HEVC".</summary>
    public const string Unavailable = "This PC cannot export {0} video at this shape and frame rate.";

    /// <summary>{0}: the codec, "HEVC": Windows has no decoder for it (the HEVC Video Extensions are not installed).</summary>
    public const string NoPlayback = "This PC cannot play {0} video, so it does not export it.";

    /// <summary>The resolution's usual name: 720p, 1080p, 1440p or 4K.</summary>
    public static string ResolutionName(OutputResolution resolution) => resolution switch
    {
        OutputResolution.Hd720 => "720p",
        OutputResolution.Hd1080 => "1080p",
        OutputResolution.Qhd1440 => "1440p",
        OutputResolution.Uhd2160 => "4K",
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown resolution."),
    };

    /// <summary>The codec's name: H.264 or HEVC.</summary>
    public static string CodecName(VideoFormat format) => format switch
    {
        VideoFormat.Mp4H264 => "H.264",
        VideoFormat.Mp4Hevc => "HEVC",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown video format."),
    };

    /// <summary>The note for the output panel, from the largest resolution the hardware and any encoder take.</summary>
    public static string Summary(VideoFormat format, OutputResolution? hardwareMax, OutputResolution? anyMax) =>
        (hardwareMax, anyMax) switch
        {
            (_, null) => Format(Unavailable, CodecName(format)),
            (null, { } max) => Format(SoftwareOnly, CodecName(format), ResolutionName(max)),
            ({ } hardware, { } max) when hardware >= max => Format(HardwareUpTo, ResolutionName(max)),
            ({ } hardware, _) => Format(HardwareUpToThenSoftware, ResolutionName(hardware)),
        };

    /// <summary>The note when Windows cannot play the codec back.</summary>
    public static string NoPlaybackSummary(VideoFormat format) => Format(NoPlayback, CodecName(format));

    private static string Format(string format, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);
}
