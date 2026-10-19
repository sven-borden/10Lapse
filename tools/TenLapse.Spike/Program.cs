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
//                         [--targets 4] [--decode-ahead 6] [--decode-threads N] [--fit fill|fit] [--verify] [--format h264|hevc]
//   TenLapse.Spike export-project --dir D [--distinct N] [--frames 1000] [--resolution 2160] [--aspect 16x9]
//                         [--fit fill|fit] [--quality high] [--format h264|hevc] [--fps 30] [--out file.mp4]
//                         (#22: ProjectExporter, the product's entry point, through the .partial file)
// --verify decodes the output and checks every frame's FrameCode. Results go to the console, the
// last line is a Markdown table row. Large files belong in %TEMP%, never in the repository.
// --noise adds grain so the JPEGs compress like real photos (#13); --distinct N cycles the export over the
// first N files of --dir, so a few realistic JPEGs make a 1,000-frame run.
//   TenLapse.Spike inspect FILE [--decode]
//   TenLapse.Spike decode-bench PHOTO [--runs 5] [--output-size 3840x2160]   (#13: ms per decode for each choice)
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
        "export-project" => await ExportProjectAsync(options),
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
        DecodeAhead = options.Flag("decode-ahead") ? options.Int("decode-ahead", 0) : null,
        DecodeThreads = options.Flag("decode-threads") ? options.Int("decode-threads", 0) : null,
        Fit = Fit(options),
    };
    const string stage = "IMFSinkWriter";

    IFrameSource source = mode switch
    {
        "synthetic" => new SyntheticFrameSource(frames, sourceWidth, sourceHeight),
        "jpeg" => new PhotoFileSource([.. Enumerable.Range(0, frames).Select(i => SyntheticJpegs.PathOf(Path.GetFullPath(options.Require("dir")), i % options.Int("distinct", frames)))], settings.Fit),
        _ => throw new ArgumentException($"--source must be synthetic or jpeg, not {mode}."),
    };

    Console.WriteLine($"encode: {stage}, {settings.Format}, {mode}, {frames} frames, {sourceWidth}x{sourceHeight} -> {width}x{height} at {rate} fps, " +
        $"hardware {(settings.HardwareAcceleration ? "allowed" : "off")}, {settings.Fit}, {settings.RenderTargets} targets, decode ahead {settings.DecodeAhead?.ToString(CultureInfo.InvariantCulture) ?? "auto"}, " +
        $"{settings.DecodeThreads?.ToString(CultureInfo.InvariantCulture) ?? "auto"} decode threads, " +
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

static FitMode Fit(Options options) => options.Text("fit", "fill") switch
{
    "fill" => FitMode.Fill,
    "fit" => FitMode.Fit,
    var other => throw new ArgumentException($"--fit must be fill or fit, not {other}."),
};

// #22: the product's entry point. A project over --frames files of --dir (cycling over --distinct), exported
// through the .partial file with the bitrate of --quality; prints wall time, speed, peak working set and progress.
static async Task<int> ExportProjectAsync(Options options)
{
    var directory = Path.GetFullPath(options.Require("dir"));
    var frames = options.Int("frames", 1000);
    var distinct = options.Int("distinct", frames);
    var rate = FrameRates.TryParse(options.Text("fps", "30"), out var parsed) ? parsed : throw new FormatException("--fps is not a frame rate.");
    var resolution = options.Int("resolution", 2160) switch
    {
        720 => OutputResolution.Hd720,
        1080 => OutputResolution.Hd1080,
        1440 => OutputResolution.Qhd1440,
        2160 => OutputResolution.Uhd2160,
        var other => throw new ArgumentException($"--resolution must be 720, 1080, 1440 or 2160, not {other}."),
    };
    var aspect = options.Text("aspect", "16x9") switch
    {
        "16x9" => FrameAspect.Ratio16x9,
        "9x16" => FrameAspect.Ratio9x16,
        "1x1" => FrameAspect.Ratio1x1,
        "4x5" => FrameAspect.Ratio4x5,
        "4x3" => FrameAspect.Ratio4x3,
        var other => throw new ArgumentException($"--aspect must be 16x9, 9x16, 1x1, 4x5 or 4x3, not {other}."),
    };
    var quality = Enum.Parse<OutputQuality>(options.Text("quality", "High"), ignoreCase: true);
    var format = options.Text("format", "h264") == "hevc" ? VideoFormat.Mp4Hevc : VideoFormat.Mp4H264;
    var output = Path.GetFullPath(options.Text("out", Path.Combine(Path.GetTempPath(), $"spike22-{frames}.mp4")));

    // A project holds each file once: hard-link the distinct files under as many names as there are frames
    // (symbolic links need Developer Mode).
    var links = Path.Combine(directory, "project");
    Directory.CreateDirectory(links);
    var project = new Project(TimingSettings.PerSecond(rate), new FrameSettings(aspect, Fit(options)), new OutputSettings(resolution, format, quality), SortMode.Manual);
    var paths = new List<string>();
    for (var i = 0; i < frames; i++)
    {
        var link = Path.Combine(links, $"f{i:D5}.jpg");
        if (!File.Exists(link))
        {
            if (!Windows.Win32.PInvoke.CreateHardLink(link, SyntheticJpegs.PathOf(directory, i % distinct)))
            {
                throw new IOException($"Could not link {link}: error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}.");
            }
        }

        paths.Add(link);
    }

    project.AddFrames(paths.Select(path => new Frame(path, 1, DateTime.UtcNow)));
    var snapshot = project.Snapshot;
    var settings = ProjectExporter.SettingsFor(snapshot);
    Console.WriteLine($"export-project: {frames} frames ({distinct} distinct) to {settings.Width}x{settings.Height} {settings.Fit} at {rate} fps, {format} {quality} " +
        $"{settings.EffectiveBitrate / 1e6:0.0} Mbit/s, estimate {ExportBitrate.EstimateBytes(frames, rate, snapshot.VideoSize, format, quality) / 1e6:0.0} MB");

    var process = Process.GetCurrentProcess();
    var last = DateTime.MinValue;
    var reports = 0;
    var progress = new Progress<ExportProgress>(report =>
    {
        reports++;
        if (DateTime.UtcNow - last > TimeSpan.FromSeconds(5) || report.Stage == ExportStage.Finishing)
        {
            last = DateTime.UtcNow;
            Console.WriteLine($"  {report.Stage} {report.Frame} / {report.FrameCount}, {report.FramesPerSecond:0.0} fps, {report.TimeLeft?.TotalSeconds:0} s left");
        }
    });
    var clock = Stopwatch.StartNew();
    var result = await ProjectExporter.ExportAsync(snapshot, output, progress);
    var wall = clock.Elapsed;
    process.Refresh();
    Console.WriteLine($"result: {result.FrameCount} frames in {wall.TotalSeconds:0.00} s = {result.FrameCount / wall.TotalSeconds:0.0} fps; peak working set " +
        $"{process.PeakWorkingSet64 / 1048576.0:0} MB; file {result.FileBytes / 1e6:0.0} MB; {reports} progress reports; encoder {result.Encoder?.Name}");
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
