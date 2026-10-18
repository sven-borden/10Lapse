using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// The Encoders section of Copy diagnostics (#9's capabilities): what this PC exports, per codec, shape and frame
/// rate, and on which encoder. It reads what is already known (the session's probe result, or the cache file the
/// last probe wrote) and never starts a probe: a probe test-encodes for seconds, which Copy diagnostics must not do.
/// </summary>
/// <remarks>
/// Compiled into the tests too (a linked file), so it uses nothing of the App's but the cache path it is given.
/// </remarks>
internal sealed class EncoderDiagnostics : IDiagnosticsContributor
{
    /// <summary>The line when no probe result exists yet.</summary>
    public const string NotProbed = "Encoder capabilities not probed yet.";

    /// <summary>The line when a cache file exists but was written for this PC as it was before.</summary>
    public const string CacheOutdated = "Encoder capabilities not probed yet for this PC as it is now (the cached result is from another graphics driver, Windows build, codec or app version).";

    private readonly string _cachePath;
    private readonly Func<EncoderCapabilities?> _loaded;
    private readonly Func<string, EncoderCapabilities?> _readCache;

    /// <param name="cachePath">The cache file <c>EncoderProbe.LoadOrProbeAsync</c> writes.</param>
    /// <param name="loaded">
    /// The result this session already has, if any (#23 hands the startup probe's result here); asked first.
    /// </param>
    /// <param name="readCache">
    /// Reads the cache file for this PC as it is now; by default with the key <see cref="EncoderCacheKey.ForThisMachine"/>
    /// makes from the encoders and decoders Media Foundation lists (a listing, not a probe: about 0.1 s).
    /// </param>
    public EncoderDiagnostics(string cachePath, Func<EncoderCapabilities?>? loaded = null, Func<string, EncoderCapabilities?>? readCache = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(cachePath);
        _cachePath = cachePath;
        _loaded = loaded ?? (() => null);
        _readCache = readCache ?? ReadCacheForThisPc;
    }

    /// <inheritdoc />
    public string Title => "Encoders";

    /// <inheritdoc />
    public void Contribute(DiagnosticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (_loaded() is { } loaded)
        {
            report.Add("Source", "this session's probe");
            Write(report, loaded);
            return;
        }

        if (!File.Exists(_cachePath))
        {
            report.AddLine(NotProbed);
            return;
        }

        if (_readCache(_cachePath) is not { } cached)
        {
            report.AddLine(CacheOutdated);
            return;
        }

        report.Add("Source", "the cache of the last probe");
        Write(report, cached);
    }

    // The summary per codec, the matrix per codec, shape and rate, then what the answers rest on.
    private static void Write(DiagnosticsReport report, EncoderCapabilities capabilities)
    {
        var standard = new Fraction(30, 1);
        foreach (var format in Formats)
        {
            var name = EncoderText.CodecName(format);
            report.Add(name, $"{capabilities.Summary(format, FrameAspect.Ratio16x9, standard)} (16:9 at 30 fps)");
            report.Add($"{name} available", capabilities.IsAvailable(format)
                ? $"yes, {(capabilities.HasHardware(format) ? "hardware" : "software only")}, {capabilities.EncoderName(format) ?? "unnamed encoder"}"
                : capabilities.CanPlayBack(format) ? "no" : "no (no decoder: Windows could not play it back)");
        }

        foreach (var format in Formats)
        {
            foreach (var aspect in EncoderProbe.Shapes)
            {
                var cells = EncoderProbe.FramesPerSecond.Select(fps => $"{fps} fps {Cell(capabilities, format, aspect, new Fraction(fps, 1))}");
                report.Add($"{EncoderText.CodecName(format)} {ShapeName(aspect)}", string.Join("; ", cells));
            }
        }

        report.Add("Probe", string.Create(
            CultureInfo.InvariantCulture,
            $"{capabilities.ProbeDuration.TotalMilliseconds:0} ms, {capabilities.Probes.Count} test encodes{(capabilities.IsProvisional ? ", provisional (not cached)" : string.Empty)}"));
        if (capabilities.DeviceFailure is { } failure)
        {
            report.Add("Probe failure", failure.ToString());
        }

        for (var i = 0; i < capabilities.Encoders.Count; i++)
        {
            var encoder = capabilities.Encoders[i];
            report.Add($"Encoder {i + 1}", $"{EncoderText.CodecName(encoder.Format)}, {encoder.Name}, {(encoder.IsHardware ? "hardware" : "software")}");
        }

        for (var i = 0; i < capabilities.Decoders.Count; i++)
        {
            var decoder = capabilities.Decoders[i];
            report.Add($"Decoder {i + 1}", $"{EncoderText.CodecName(decoder.Format)}, {decoder.Name}, {(decoder.IsHardware ? "hardware" : "software")}");
        }

        for (var i = 0; i < capabilities.Probes.Count; i++)
        {
            var probe = capabilities.Probes[i];
            var result = probe.Encoder == EncoderKind.None
                ? $"failed ({probe.Failure?.ToString() ?? "no cause"})"
                : $"{probe.Encoder.ToString().ToLowerInvariant()}, {probe.EncoderName ?? "unnamed encoder"}";
            report.Add($"Test encode {i + 1}", string.Create(
                CultureInfo.InvariantCulture,
                $"{EncoderText.CodecName(probe.Format)} {probe.Width} x {probe.Height} at {probe.FramesPerSecond} fps: {result}, {probe.Milliseconds:0} ms"));
        }
    }

    private static VideoFormat[] Formats => [VideoFormat.Mp4H264, VideoFormat.Mp4Hevc];

    // "up to 4K, hardware", "up to 1440p hardware, 4K software", "up to 1080p, software", "none".
    private static string Cell(EncoderCapabilities capabilities, VideoFormat format, FrameAspect aspect, Fraction rate)
    {
        var any = capabilities.MaxResolution(format, aspect, rate);
        var hardware = capabilities.MaxResolution(format, aspect, rate, minimum: EncoderKind.Hardware);
        return (hardware, any) switch
        {
            (_, null) => "none",
            (null, { } max) => $"up to {EncoderText.ResolutionName(max)}, software",
            ({ } fast, { } max) when fast >= max => $"up to {EncoderText.ResolutionName(max)}, hardware",
            ({ } fast, { } max) => $"up to {EncoderText.ResolutionName(fast)} hardware, {EncoderText.ResolutionName(max)} software",
        };
    }

    private static string ShapeName(FrameAspect aspect) => aspect switch
    {
        FrameAspect.Ratio16x9 => "16:9",
        FrameAspect.Ratio9x16 => "9:16",
        FrameAspect.Ratio4x3 => "4:3",
        FrameAspect.Ratio1x1 => "1:1",
        FrameAspect.Ratio4x5 => "4:5",
        _ => aspect.ToString(),
    };

    private static EncoderCapabilities? ReadCacheForThisPc(string path)
    {
        var key = EncoderCacheKey.ForThisMachine(EncoderCatalog.Find(), EncoderCatalog.FindDecoders(VideoFormat.Mp4Hevc));
        return EncoderCapabilitiesCache.TryRead(path, key);
    }
}
