using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Text;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible word of the Output panel (#23), as whole format strings read from <c>Strings/OutputText.resx</c>
/// (#39); log contexts stay here in English. The resolution and codec names are technical names and stay in code. The
/// file size is written in the user's culture (a decimal comma in German or French), through the one size formatter
/// (<see cref="Size(long, int, CultureInfo?)"/>) the export and the OneDrive question use too; the pixel sizes of the
/// video are plain integers, invariant on purpose.
/// </summary>
public static class OutputText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.OutputText", typeof(OutputText).Assembly);

    /// <summary>The section's heading.</summary>
    public static string Heading => _strings.Get();

    /// <summary>The resolution list's label.</summary>
    public static string ResolutionLabel => _strings.Get();

    /// <summary>The format list's label.</summary>
    public static string FormatLabel => _strings.Get();

    /// <summary>The quality list's label.</summary>
    public static string QualityLabel => _strings.Get();

    /// <summary>Shown in place of the lists until the startup probe has said what this PC can encode.</summary>
    public static string Checking => _strings.Get();

    /// <summary>Said to a screen reader when the lists appear in place of <see cref="Checking"/>.</summary>
    public static string Ready => _strings.Get();

    /// <summary>The note when the probe failed or timed out. It stands in for <c>EncoderCapabilities.Summary</c>.</summary>
    public static string ConservativeNote => _strings.Get();

    /// <summary>A format was replaced because this PC cannot export it. {0}: the format's name ("HEVC"); {1}: the one selected now.</summary>
    public static string FormatReplaced => _strings.Get();

    /// <summary>A resolution was replaced because this PC cannot export it with the project's settings. {0}: the old one ("4K"); {1}: the new one.</summary>
    public static string ResolutionReplaced => _strings.Get();

    /// <summary>
    /// A resolution was replaced because this PC cannot export it in the current frame shape; the choice returns with
    /// the old shape. {0}: the chosen one ("4K"); {1}: the one selected now.
    /// </summary>
    public static string ResolutionReplacedByShape => _strings.Get();

    /// <summary>The button under a replacement notice that stores the selected settings in place of the earlier choice.</summary>
    public static string KeepSelected => _strings.Get();

    /// <summary>The estimate. {0}: the size from <see cref="Size"/>.</summary>
    public static string EstimateAbout => _strings.Get();

    /// <summary>A size in megabytes. {0}: the number.</summary>
    public static string Megabytes => _strings.Get();

    /// <summary>A size in gigabytes. {0}: the number.</summary>
    public static string Gigabytes => _strings.Get();

    /// <summary>The estimate when the file stays under a megabyte.</summary>
    public static string UnderOneMegabyte => _strings.Get();

    /// <summary>Log context: a setting could not be written to the project.</summary>
    public static string ChangeFailed => "Changing the output settings failed.";

    /// <summary>Log context: the panel could not follow a change of the project.</summary>
    public static string RefreshFailed => "The output panel could not update.";

    /// <summary>Log context: the startup probe of the encoders failed.</summary>
    public static string ProbeFailed => "Checking this PC's video encoders failed.";

    /// <summary>The list entry for a resolution. {0} its name ("1080p"), {1} the width, {2} the height, in pixels.</summary>
    public static string ResolutionOptionFormat => _strings.Get();

    /// <summary>The list entry for a format. {0} the codec ("H.264", "HEVC").</summary>
    public static string FormatOptionFormat => _strings.Get();

    /// <summary>The quality list's lowest bitrate.</summary>
    public static string QualityStandard => _strings.Get();

    /// <summary>The quality list's default bitrate.</summary>
    public static string QualityHigh => _strings.Get();

    /// <summary>The quality list's highest bitrate.</summary>
    public static string QualityMaximum => _strings.Get();

    /// <summary>The resolution's usual name: 720p, 1080p, 1440p or 4K, the same in every language (not in the resource file).</summary>
    public static string ResolutionName(OutputResolution resolution) => resolution switch
    {
        OutputResolution.Hd720 => "720p",
        OutputResolution.Hd1080 => "1080p",
        OutputResolution.Qhd1440 => "1440p",
        OutputResolution.Uhd2160 => "4K",
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown resolution."),
    };

    /// <summary>
    /// The list entry for a resolution: "1080p · 1920 × 1080" (the size follows the frame shape). Invariant on purpose:
    /// a pixel size is written without grouping in every language, as Windows' display settings write it.
    /// </summary>
    public static string ResolutionOption(OutputResolution resolution, PixelSize size) =>
        string.Format(CultureInfo.InvariantCulture, ResolutionOptionFormat, ResolutionName(resolution), size.Width, size.Height);

    /// <summary>The codec's name: H.264 or HEVC, the same in every language (not in the resource file).</summary>
    public static string CodecName(VideoFormat format) => format switch
    {
        VideoFormat.Mp4H264 => "H.264",
        VideoFormat.Mp4Hevc => "HEVC",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown video format."),
    };

    /// <summary>The list entry for a format: "MP4 · H.264".</summary>
    public static string FormatOption(VideoFormat format) => string.Format(CultureInfo.CurrentCulture, FormatOptionFormat, CodecName(format));

    /// <summary>The list entry for a quality.</summary>
    public static string QualityOption(OutputQuality quality) => quality switch
    {
        OutputQuality.Standard => QualityStandard,
        OutputQuality.High => QualityHigh,
        OutputQuality.Maximum => QualityMaximum,
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
    /// An estimated file size without false precision: two significant figures ("4.2 MB", "37 MB", "180 MB", "1.8 GB");
    /// see <see cref="Size(long, int, CultureInfo?)"/>.
    /// </summary>
    public static string Size(long bytes, CultureInfo? culture = null) => Size(bytes, 2, culture);

    /// <summary>
    /// The one file-size formatter (#114): <paramref name="figures"/> significant figures, trailing zeros left out ("4.2 MB",
    /// "173 MB", "1.23 GB"), in units of 1,024 as Explorer counts them, gigabytes from what rounds to 1000 MB, and "Less
    /// than 1 MB" below a megabyte. The number is written in <paramref name="culture"/> ("1,8 GB" in German), the current
    /// culture when null. The Output panel's estimate and the OneDrive question use two figures, the finished file three.
    /// </summary>
    public static string Size(long bytes, int figures, CultureInfo? culture = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(figures, 1);
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < Megabyte)
        {
            return UnderOneMegabyte;
        }

        var megabytes = Significant((double)bytes / Megabyte, figures);
        if (megabytes < 1000)
        {
            return string.Format(culture, Megabytes, Number(megabytes, culture));
        }

        // A size that rounds up to 1000 MB is nearly a gigabyte: say "1 GB", never "0.97 GB" next to figures in MB.
        return string.Format(culture, Gigabytes, Number(Math.Max(1, Significant((double)bytes / Gigabyte, figures)), culture));
    }

    private const long Megabyte = 1L << 20;
    private const long Gigabyte = 1L << 30;

    // Rounded half away from zero to that many significant figures: with two, 4.23 to 4.2, 37.4 to 37, 183 to 180.
    private static double Significant(double value, int figures)
    {
        if (value <= 0)
        {
            return 0;
        }

        var scale = Math.Pow(10, Math.Floor(Math.Log10(value)) - (figures - 1));
        return Math.Round(value / scale, MidpointRounding.AwayFromZero) * scale;
    }

    // Up to two decimals, trailing zeros left out ("4.2", "5" for 5.0, "1.23"); always the culture's own separator.
    private static string Number(double value, CultureInfo culture) => value.ToString("0.##", culture);
}
