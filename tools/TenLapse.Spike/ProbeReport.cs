using System.Diagnostics;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;

namespace TenLapse.Spike;

/// <summary>The #9 capability report: loads (or probes) twice and prints the answers as Markdown.</summary>
internal static class ProbeReport
{
    /// <param name="cachePath">The cache file to load or write.</param>
    /// <param name="fresh">Deletes the cache first, so the first load probes.</param>
    /// <param name="software">Probes with hardware transforms off, as a PC without a GPU encoder would, without the cache.</param>
    public static async Task<int> RunAsync(string cachePath, bool fresh, bool software)
    {
        if (fresh)
        {
            File.Delete(cachePath);
        }

        var hadCache = File.Exists(cachePath);
        var clock = Stopwatch.StartNew();
        var capabilities = software ? await EncoderProbe.ProbeAsync(hardwareAcceleration: false) : await EncoderProbe.LoadOrProbeAsync(cachePath);
        var first = clock.Elapsed;
        clock.Restart();
        var encoders = EncoderCatalog.Find();
        var catalog = clock.Elapsed;
        var key = EncoderCacheKey.ForThisMachine(encoders);
        var keyTime = clock.Elapsed - catalog;
        clock.Restart();
        if (!software)
        {
            await EncoderProbe.LoadOrProbeAsync(cachePath);
        }

        var second = clock.Elapsed;
        Console.WriteLine($"Listing the encoders: {catalog.TotalMilliseconds:0} ms; the rest of the key: {keyTime.TotalMilliseconds:0} ms");
        Console.WriteLine($"Adapter: {key.AdapterName} ({key.Adapter}), driver {key.Driver}, Windows {key.OsBuild}");
        Console.WriteLine($"First load ({(software ? "software probe" : hadCache ? "from the cache" : "probe, no cache")}): {first.TotalMilliseconds:0} ms; second (cache, same process): {second.TotalMilliseconds:0} ms; probe itself: {capabilities.ProbeDuration.TotalMilliseconds:0} ms");
        if (capabilities.DeviceFailure is { } failure)
        {
            Console.WriteLine($"No probe ran: {failure}");
        }

        Console.WriteLine();
        Console.WriteLine("| Encoder MFT | Codec | Kind | CLSID |");
        Console.WriteLine("|---|---|---|---|");
        foreach (var encoder in capabilities.Encoders)
        {
            Console.WriteLine($"| {encoder.Name} | {EncoderText.CodecName(encoder.Format)} | {(encoder.IsHardware ? "hardware" : "software")} | {encoder.Clsid:B} |");
        }

        Console.WriteLine();
        Console.WriteLine("| Test encode | Codec | fps | Result | Encoder | ms |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (var probe in capabilities.Probes)
        {
            Console.WriteLine($"| {probe.Width}×{probe.Height} | {EncoderText.CodecName(probe.Format)} | {probe.FramesPerSecond} | {probe.Failure?.ToString() ?? probe.Encoder.ToString()} | {probe.EncoderName ?? "-"} | {probe.Milliseconds:0} |");
        }

        Console.WriteLine();
        Console.WriteLine("| Codec | fps | Shape | Max (hardware) | Max (any) | Note |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (var format in new[] { VideoFormat.Mp4H264, VideoFormat.Mp4Hevc })
        {
            foreach (var fps in EncoderProbe.FramesPerSecond)
            {
                foreach (var shape in EncoderProbe.Shapes)
                {
                    var rate = new Fraction(fps, 1);
                    var hardware = capabilities.MaxResolution(format, shape, rate, minimum: EncoderKind.Hardware);
                    var any = capabilities.MaxResolution(format, shape, rate);
                    Console.WriteLine($"| {EncoderText.CodecName(format)} | {fps} | {shape} | {Name(hardware)} | {Name(any)} | {capabilities.Summary(format, shape, rate)} |");
                }
            }
        }

        return 0;
    }

    private static string Name(OutputResolution? resolution) => resolution is { } value ? EncoderText.ResolutionName(value) : "none";
}
