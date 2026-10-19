using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Presentation;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only stand-in for the encoder probe (this folder is not built in Release), to look at the Output panel's
/// states on a PC that does everything: <c>--max-resolution=720|1080|1440|2160</c> hides larger sizes (the
/// graphics hardware stops there), <c>--no-hevc</c> hides HEVC, <c>--probe-delay=SECONDS</c> keeps the panel pending
/// that long, and <c>--probe-failed</c> makes the probe fail (the conservative answer and an InfoBar).
/// </summary>
internal sealed class DebugOutputCapabilities(OutputResolution maxResolution, bool hasHevc) : IOutputCapabilities
{
    /// <inheritdoc />
    public bool IsAvailable(VideoFormat format) => format == VideoFormat.Mp4H264 || hasHevc;

    /// <inheritdoc />
    public bool Supports(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) =>
        IsAvailable(format) && resolution <= maxResolution;

    /// <inheritdoc />
    public bool UsesHardware(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) =>
        Supports(format, resolution, aspect, frameRate, photoSize);

    /// <inheritdoc />
    public string Summary(VideoFormat format, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) =>
        EncoderText.Summary(format, maxResolution, maxResolution);

    /// <summary>The probe the switches ask for, or null when none is given and the real probe should run.</summary>
    /// <param name="args">The command line.</param>
    public static Task<IOutputCapabilities>? FromSwitches(string[] args)
    {
        string? Value(string prefix) =>
            args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

        var max = Value("--max-resolution=") switch
        {
            "720" => OutputResolution.Hd720,
            "1080" => OutputResolution.Hd1080,
            "1440" => OutputResolution.Qhd1440,
            "2160" => OutputResolution.Uhd2160,
            _ => (OutputResolution?)null,
        };
        var noHevc = args.Contains("--no-hevc", StringComparer.OrdinalIgnoreCase);
        var failed = args.Contains("--probe-failed", StringComparer.OrdinalIgnoreCase);
        var delay = double.TryParse(Value("--probe-delay="), System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
        if (max is null && !noHevc && !failed && delay <= 0)
        {
            return null;
        }

        return Run();

        async Task<IOutputCapabilities> Run()
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, delay))).ConfigureAwait(false);
            return failed
                ? throw new InvalidOperationException("Debug: the encoder probe was made to fail (--probe-failed).")
                : new DebugOutputCapabilities(max ?? OutputResolution.Uhd2160, hasHevc: !noHevc);
        }
    }
}
