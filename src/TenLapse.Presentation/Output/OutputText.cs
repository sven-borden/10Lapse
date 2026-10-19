using System.Globalization;
using TenLapse.Core.Projects;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible word of the Output panel (#23), as whole format strings for #39. The file size is the one
/// number here written in the user's culture (a decimal comma in German or French); the sizes of the video are
/// plain integers.
/// </summary>
public static class OutputText
{
    /// <summary>The section's heading.</summary>
    public static string Heading => "Output";

    /// <summary>The resolution list's label.</summary>
    public static string ResolutionLabel => "Resolution";

    /// <summary>The format list's label.</summary>
    public static string FormatLabel => "Format";

    /// <summary>The quality list's label.</summary>
    public static string QualityLabel => "Quality";

    /// <summary>The name a screen reader gives the estimated size.</summary>
    public static string EstimateName => "Estimated file size";

    /// <summary>Shown in place of the lists until the startup probe has said what this PC can encode.</summary>
    public static string Checking => "Checking what this PC can encode…";

    /// <summary>The note when the probe failed or timed out. It stands in for <c>EncoderCapabilities.Summary</c>.</summary>
    public static string ConservativeNote => "10Lapse couldn't check this PC's video encoders, so it offers H.264 up to 1080p.";

    /// <summary>A format was replaced because this PC cannot export it. {0}: the format's name ("HEVC"); {1}: the one selected now.</summary>
    public static string FormatReplaced => "{0} can't be exported on this PC, so {1} is selected.";

    /// <summary>A resolution was replaced because this PC cannot export it with the project's settings. {0}: the old one ("4K"); {1}: the new one.</summary>
    public static string ResolutionReplaced => "{0} can't be exported on this PC with these settings, so {1} is selected.";

    /// <summary>The estimate. {0}: the size from <see cref="Size"/>.</summary>
    public static string EstimateAbout => "About {0}";

    /// <summary>A size in megabytes. {0}: the number.</summary>
    public static string Megabytes => "{0} MB";

    /// <summary>A size in gigabytes. {0}: the number.</summary>
    public static string Gigabytes => "{0} GB";

    /// <summary>The estimate when the file stays under a megabyte.</summary>
    public static string UnderOneMegabyte => "Less than 1 MB";

    /// <summary>Log context: a setting could not be written to the project.</summary>
    public static string ChangeFailed => "Changing the output settings failed.";

    /// <summary>Log context: the panel could not follow a change of the project.</summary>
    public static string RefreshFailed => "The output panel could not update.";

    /// <summary>Log context: the startup probe of the encoders failed.</summary>
    public static string ProbeFailed => "Checking this PC's video encoders failed.";

    /// <summary>The resolution's usual name: 720p, 1080p, 1440p or 4K.</summary>
    public static string ResolutionName(OutputResolution resolution) => resolution switch
    {
        OutputResolution.Hd720 => "720p",
        OutputResolution.Hd1080 => "1080p",
        OutputResolution.Qhd1440 => "1440p",
        OutputResolution.Uhd2160 => "4K",
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown resolution."),
    };

    /// <summary>The list entry for a resolution: "1080p · 1920 × 1080" (the size follows the frame shape).</summary>
    public static string ResolutionOption(OutputResolution resolution, PixelSize size) =>
        string.Create(CultureInfo.InvariantCulture, $"{ResolutionName(resolution)} · {size.Width} × {size.Height}");

    /// <summary>The codec's name: H.264 or HEVC.</summary>
    public static string CodecName(VideoFormat format) => format switch
    {
        VideoFormat.Mp4H264 => "H.264",
        VideoFormat.Mp4Hevc => "HEVC",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown video format."),
    };

    /// <summary>The list entry for a format: "MP4 · H.264".</summary>
    public static string FormatOption(VideoFormat format) => "MP4 · " + CodecName(format);

    /// <summary>The list entry for a quality.</summary>
    public static string QualityOption(OutputQuality quality) => quality switch
    {
        OutputQuality.Standard => "Standard",
        OutputQuality.High => "High",
        OutputQuality.Maximum => "Maximum",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Unknown quality."),
    };

    /// <summary>"About 180 MB": see <see cref="Size"/>.</summary>
    /// <param name="bytes">The estimated size in bytes.</param>
    /// <param name="culture">The culture to write the number in; the current one when null.</param>
    public static string Estimate(long bytes, CultureInfo? culture = null)
    {
        var size = Size(bytes, culture);
        return bytes < Megabyte ? size : string.Format(culture ?? CultureInfo.CurrentCulture, EstimateAbout, size);
    }

    /// <summary>
    /// A file size without false precision: two significant figures ("4.2 MB", "37 MB", "180 MB", "1.8 GB"),
    /// in units of 1,024 as Explorer counts them, and "Less than 1 MB" below a megabyte. The number is written in
    /// <paramref name="culture"/> ("1,8 GB" in German).
    /// </summary>
    public static string Size(long bytes, CultureInfo? culture = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < Megabyte)
        {
            return UnderOneMegabyte;
        }

        var megabytes = TwoFigures((double)bytes / Megabyte);
        if (megabytes < 1000)
        {
            return string.Format(culture, Megabytes, Number(megabytes, culture));
        }

        return string.Format(culture, Gigabytes, Number(TwoFigures((double)bytes / Gigabyte), culture));
    }

    private const long Megabyte = 1L << 20;
    private const long Gigabyte = 1L << 30;

    // Rounded to two significant figures: 4.23 to 4.2, 37.4 to 37, 183 to 180, 0.97 stays.
    private static double TwoFigures(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var scale = Math.Pow(10, Math.Floor(Math.Log10(value)) - 1);
        return Math.Round(value / scale, MidpointRounding.AwayFromZero) * scale;
    }

    // One decimal under 10 ("4.2", "5" for 5.0), none from 10 up; always the culture's own separator.
    private static string Number(double value, CultureInfo culture) =>
        value < 10 ? value.ToString("0.##", culture) : value.ToString("0", culture);
}
