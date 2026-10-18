using System.Diagnostics;
using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using TenLapse.Spike;

// Spike harness for #8. Usage:
//   TenLapse.Spike encoders
//   TenLapse.Spike generate --dir D [--frames 1000] [--source-size 6000x4000] [--noise 10]
//   TenLapse.Spike encode [--source synthetic|jpeg] [--dir D] [--distinct N] [--frames 1000] [--source-size 6000x4000]
//                         [--output-size 3840x2160] [--fps 30] [--out file.mp4] [--software]
//                         [--targets 4] [--decode-ahead 4] [--verify] [--format h264|hevc]
// --verify decodes the output and checks every frame's FrameCode. Results go to the console, the
// last line is a Markdown table row. Large files belong in %TEMP%, never in the repository.
// --noise adds grain so the JPEGs compress like real photos (#13); --distinct N cycles the export over the
// first N files of --dir, so a few realistic JPEGs make a 1,000-frame run.
//   TenLapse.Spike inspect FILE [--decode]
//   TenLapse.Spike probe [--cache file.json] [--fresh] [--software]   (#9: what this PC can encode, as Markdown;
//                         --software answers as a PC without a GPU encoder would, uncached)
var command = args.Length > 0 ? args[0] : "help";
var options = Options.Parse(args.Skip(1));
try
{
    return command switch
    {
        "encoders" => Encoders(),
        "generate" => await GenerateAsync(options),
        "encode" => await EncodeAsync(options),
        "inspect" => Inspect(options),
        "decode-bench" => await DecodeBench.RunAsync(
            options.Positional ?? throw new ArgumentException("decode-bench needs a photo."),
            options.Int("runs", 5),
            options.Size("output-size", (3840, 2160)).Width,
            options.Size("output-size", (3840, 2160)).Height),
        "probe" => await ProbeReport.RunAsync(options.Text("cache", Path.Combine(Path.GetTempPath(), "10lapse-encoders.json")), options.Flag("fresh"), options.Flag("software")),
        _ => Help(),
    };
}
catch (Exception exception) when (exception is ArgumentException or FormatException or FileNotFoundException or VideoExportException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

static int Help()
{
    Console.WriteLine("TenLapse.Spike encoders | generate --dir D | encode [options] | inspect FILE [--decode] | probe [--cache F] [--fresh]");
    return 1;
}

static int Encoders()
{
    foreach (var encoder in H264Encoders.Find())
    {
        Console.WriteLine($"{(encoder.IsHardware ? "hardware" : "software")}  {encoder.Name}");
    }

    return 0;
}

static async Task<int> GenerateAsync(Options options)
{
    var directory = options.Require("dir");
    var frames = options.Int("frames", 1000);
    var (width, height) = options.Size("source-size", (6000, 4000));
    var clock = Stopwatch.StartNew();
    await SyntheticJpegs.WriteAsync(directory, frames, width, height, options.Int("noise", 0));
    var bytes = Enumerable.Range(0, frames).Sum(i => new FileInfo(SyntheticJpegs.PathOf(directory, i)).Length);
    Console.WriteLine($"wrote {frames} JPEGs {width}x{height} to {directory} in {clock.Elapsed.TotalSeconds:0.0} s, {bytes / 1e9:0.00} GB");
    return 0;
}

static async Task<int> EncodeAsync(Options options)
{
    var mode = options.Text("source", "synthetic");
    var frames = options.Int("frames", 1000);
    var (sourceWidth, sourceHeight) = options.Size("source-size", (6000, 4000));
    var (width, height) = options.Size("output-size", (3840, 2160));
    var rate = FrameRates.TryParse(options.Text("fps", "30"), out var parsed) ? parsed : throw new FormatException("--fps is not a frame rate.");
    var output = Path.GetFullPath(options.Text("out", Path.Combine(Path.GetTempPath(), $"spike8-{mode}-{frames}-{width}x{height}.mp4")));
    var settings = new VideoExportSettings(width, height, rate)
    {
        HardwareAcceleration = !options.Flag("software"),
        Format = options.Text("format", "h264") switch
        {
            "h264" => VideoFormat.Mp4H264,
            "hevc" => VideoFormat.Mp4Hevc,
            var other => throw new ArgumentException($"--format must be h264 or hevc, not {other}."),
        },
        RenderTargets = options.Int("targets", 4),
        DecodeAhead = options.Int("decode-ahead", 4),
    };
    const string stage = "IMFSinkWriter";

    IFrameSource source = mode switch
    {
        "synthetic" => new SyntheticFrameSource(frames, sourceWidth, sourceHeight),
        "jpeg" => new PhotoFileSource([.. Enumerable.Range(0, frames).Select(i => SyntheticJpegs.PathOf(Path.GetFullPath(options.Require("dir")), i % options.Int("distinct", frames)))]),
        _ => throw new ArgumentException($"--source must be synthetic or jpeg, not {mode}."),
    };

    Console.WriteLine($"encode: {stage}, {settings.Format}, {mode}, {frames} frames, {sourceWidth}x{sourceHeight} -> {width}x{height} at {rate} fps, " +
        $"hardware {(settings.HardwareAcceleration ? "allowed" : "off")}, {settings.RenderTargets} targets, decode ahead {settings.DecodeAhead}, " +
        $"{settings.EffectiveBitrate / 1e6:0.0} Mbit/s");
    var encoders = H264Encoders.Find();
    Console.WriteLine($"encoders: {string.Join("; ", encoders.Select(e => $"{e.Name} ({(e.IsHardware ? "hw" : "sw")})"))}");

    var process = Process.GetCurrentProcess();
    process.Refresh();
    var startPeak = process.PeakWorkingSet64;
    VideoExportResult result;
    TimeSpan wall;
    (string Engine, double Max, double Mean)[] gpu;
    IReadOnlyCollection<string> instances;
    using (var monitor = new GpuEngineMonitor(Environment.ProcessId))
    {
        var lastReport = 0;
        var progress = new Progress<int>(done =>
        {
            if (done - lastReport >= 250 || done == frames)
            {
                lastReport = done;
                Console.WriteLine($"  {done} / {frames}");
            }
        });
        var clock = Stopwatch.StartNew();
        result = await VideoExporter.ExportAsync(source, output, settings, progress);
        wall = clock.Elapsed;
        monitor.Stop();
        gpu = monitor.Results();
        instances = monitor.Instances;
    }

    process.Refresh();
    var peak = process.PeakWorkingSet64;
    Console.WriteLine($"result: {result.FrameCount} frames in {wall.TotalSeconds:0.00} s = {result.FrameCount / wall.TotalSeconds:0.0} fps; " +
        $"peak working set {peak / 1048576.0:0} MB (before export {startPeak / 1048576.0:0} MB); file {new FileInfo(output).Length / 1e6:0.0} MB");
    Console.WriteLine($"encoder reported: {(result.Encoder is { } ran ? $"{ran.Name} ({(ran.IsHardware ? "hardware" : "software")})" : "not reported")}");
    Console.WriteLine($"gpu: {string.Join(", ", gpu.Select(g => $"{g.Engine} max {g.Max:0}% mean {g.Mean:0}%"))}");
    foreach (var instance in instances.Order())
    {
        Console.WriteLine($"  busy engine: {instance}");
    }

    var readBack = ReadBack(output, rate);
    var codeCheck = options.Flag("verify") ? VerifyFrameCodes(output) : "not run";
    var encodeEngine = gpu.First(g => g.Engine == "VideoEncode");
    Console.WriteLine();
    var encoderText = result.Encoder?.Name ?? (settings.HardwareAcceleration ? "hw allowed" : "sw forced");
    Console.WriteLine("| stage | source | frames | size | rate | wall | fps | peak WS | encoder (VideoEncode engine max/mean) | read-back | frame codes |");
    Console.WriteLine($"| {stage} | {mode} | {frames} | {sourceWidth}x{sourceHeight} to {width}x{height} | {rate} | {wall.TotalSeconds:0.0} s | {result.FrameCount / wall.TotalSeconds:0.0} | " +
        $"{peak / 1048576.0:0} MB | {encoderText} ({encodeEngine.Max:0}% / {encodeEngine.Mean:0}%) | {readBack} | {codeCheck} |");
    return 0;
}

static string ReadBack(string path, Fraction rate)
{
    var info = VideoFileReader.Read(path);
    var expected = Enumerable.Range(0, info.FrameCount).Select(i => FrameTiming.FrameStart(i, rate)).ToArray();
    var off = info.SampleTimes.Select((time, i) => Math.Abs(time - expected[i])).ToArray();
    var mismatches = off.Count(d => d != 0);
    var expectedDuration = FrameTiming.Duration(info.FrameCount, rate);
    Console.WriteLine($"read-back: {info.Codec} {info.Width}x{info.Height} rate {info.FrameRate}, {info.FrameCount} frames, " +
        $"duration {info.Duration} ticks ({info.Duration / 1e7:0.0000000} s, expected {expectedDuration}), " +
        $"timestamps off by 0: {info.FrameCount - mismatches}, worst {(off.Length > 0 ? off.Max() : 0)} ticks; " +
        $"first times {string.Join(", ", info.SampleTimes.Take(4))}; last {info.SampleTimes[^1]}; durations {string.Join(", ", info.SampleDurations.Distinct().Take(4))}");
    return $"{info.FrameCount} frames, {info.Duration / 1e7:0.0000000} s, {(mismatches == 0 ? "all times exact" : $"{mismatches} times off (worst {off.Max()} ticks)")}";
}

static string VerifyFrameCodes(string path)
{
    var bad = new List<string>();
    var clock = Stopwatch.StartNew();
    var count = VideoFileReader.ReadFrames(path, frame =>
    {
        var value = FrameCode.Read(frame.Pixels, frame.Width, frame.Height, frame.Stride);
        if (value != frame.Index && bad.Count < 20)
        {
            bad.Add($"{frame.Index}->{value}@{frame.Time}");
        }
    });
    Console.WriteLine($"frame codes: {count} decoded in {clock.Elapsed.TotalSeconds:0.0} s, mismatches: {(bad.Count == 0 ? "none" : string.Join(" ", bad))}");
    return bad.Count == 0 ? $"{count} of {count} match" : $"{bad.Count} wrong";
}

static int Inspect(Options options)
{
    var path = options.Positional ?? throw new ArgumentException("inspect needs a file.");
    var info = VideoFileReader.Read(path);
    Console.WriteLine($"{info.Codec} {info.Width}x{info.Height} rate {info.FrameRate} frames {info.FrameCount} duration {info.Duration} ticks");
    Console.WriteLine($"times: {string.Join(" ", info.SampleTimes)}");
    Console.WriteLine($"durations: {string.Join(" ", info.SampleDurations)}");
    if (options.Flag("decode"))
    {
        VideoFileReader.ReadFrames(path, frame =>
        {
            var value = FrameCode.Read(frame.Pixels, frame.Width, frame.Height, frame.Stride);
            Console.WriteLine($"frame {frame.Index} t={frame.Time} {frame.Width}x{frame.Height} stride {frame.Stride} code {value}{(value == frame.Index ? "" : "  <-- WRONG")}");
        });
    }

    return 0;
}

internal sealed class Options
{
    private readonly Dictionary<string, string?> _values = [];

    public string? Positional { get; private set; }

    public static Options Parse(IEnumerable<string> arguments)
    {
        var options = new Options();
        string? pending = null;
        foreach (var argument in arguments)
        {
            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (pending is not null)
                {
                    options._values[pending] = null;
                }

                pending = argument[2..];
            }
            else if (pending is not null)
            {
                options._values[pending] = argument;
                pending = null;
            }
            else
            {
                options.Positional = argument;
            }
        }

        if (pending is not null)
        {
            options._values[pending] = null;
        }

        return options;
    }

    public bool Flag(string name) => _values.ContainsKey(name);

    public string Text(string name, string fallback) => _values.TryGetValue(name, out var value) && value is not null ? value : fallback;

    public string Require(string name) => _values.TryGetValue(name, out var value) && value is not null ? value : throw new ArgumentException($"--{name} is required.");

    public int Int(string name, int fallback) => int.Parse(Text(name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

    public (int Width, int Height) Size(string name, (int Width, int Height) fallback)
    {
        if (!_values.TryGetValue(name, out var value) || value is null)
        {
            return fallback;
        }

        var parts = value.Split('x');
        return parts.Length == 2
            ? (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture))
            : throw new FormatException($"--{name} must look like 3840x2160.");
    }
}
