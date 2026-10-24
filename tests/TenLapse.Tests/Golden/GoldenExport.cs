using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using TenLapse.Tests.Media;

namespace TenLapse.Tests.Golden;

/// <summary>
/// What the golden render tests (#32) share, and the export helpers the other export tests use too: a project
/// snapshot of photos, the export itself at a small size, the MP4 timescale bounds, and the folders for scratch
/// files and for the evidence a failure leaves.
/// </summary>
/// <remarks>
/// The export goes through <see cref="ProjectExporter"/> from a <see cref="ProjectSnapshot"/>, exactly as the app
/// runs it, with one change: the frame size <see cref="ProjectExporter.SettingsFor"/> chose is scaled down to a
/// <see cref="ShortSide"/> of 320 px (720p is the smallest resolution a project offers), which keeps the gate fast.
/// The shape, the rate, the fit, the codec and the bitrate are the snapshot's own. Keeping the 720p bitrate at the
/// smaller size leaves little codec noise, so a geometry fault cannot hide in it.
/// </remarks>
internal static class GoldenExport
{
    /// <summary>The short side of every golden export, in pixels.</summary>
    public const int ShortSide = 320;

    /// <summary>
    /// Set to <c>1</c> to export with hardware acceleration off, through Microsoft's software encoder, to compare
    /// it with the GPU's (#41). The per-case lines in the test output name the encoder that ran.
    /// </summary>
    public const string SoftwareVariable = "TENLAPSE_GOLDEN_SOFTWARE";

    // Scratch folders a killed run left behind are swept once they are this old.
    private static readonly TimeSpan _leftoverAge = TimeSpan.FromHours(1);

    private static string ScratchRoot => Path.Combine(AppContext.BaseDirectory, "GoldenScratch");

    private static string FailuresRoot => Path.Combine(AppContext.BaseDirectory, "GoldenFailures");

    private static bool SoftwareOnly => Environment.GetEnvironmentVariable(SoftwareVariable) == "1";

    /// <summary>
    /// Starts a test class (from its fixture): sweeps the scratch folders a killed run left behind, clears this
    /// class's evidence from the last run, so <c>GoldenFailures</c> holds only the cases that failed in the latest
    /// one, and makes a new scratch folder for the class's photos and videos. Touches the disk only, no GPU.
    /// </summary>
    public static string StartClass(string className)
    {
        if (Directory.Exists(ScratchRoot))
        {
            foreach (var leftover in Directory.GetDirectories(ScratchRoot).Where(folder => DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > _leftoverAge))
            {
                DeleteFolder(leftover);
            }
        }

        if (Directory.Exists(FailuresRoot))
        {
            foreach (var earlier in Directory.GetDirectories(FailuresRoot, $"{className}-*"))
            {
                DeleteFolder(earlier);
            }
        }

        var folder = Path.Combine(ScratchRoot, $"{className}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Ends a test class: deletes its scratch folder, and the parent folders when nothing else is in them.</summary>
    public static void EndClass(string scratch)
    {
        DeleteFolder(scratch);
        RemoveIfEmpty(ScratchRoot);
        RemoveIfEmpty(FailuresRoot);
    }

    /// <summary>Where a failing case of <paramref name="className"/> leaves its evidence: <c>GoldenFailures\&lt;class&gt;-&lt;case&gt;</c> beside the test host.</summary>
    public static string EvidenceFolder(string className, string caseName) =>
        Path.Combine(FailuresRoot, $"{className}-{caseName}");

    /// <summary>The failure message: what failed, the first problems (a geometry fault repeats on every line), and where the evidence is.</summary>
    public static string Report(string heading, IReadOnlyList<string> problems, string evidence)
    {
        const int Shown = 12;
        var lines = problems.Take(Shown).Select(problem => "- " + problem).ToList();
        if (problems.Count > Shown)
        {
            lines.Add($"- and {problems.Count - Shown} more");
        }

        return $"{heading}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{Environment.NewLine}Evidence: {evidence}";
    }

    /// <summary>A photo as an import adds it: its file's length and last write time (none for a missing file), and its pixel size when known.</summary>
    public static Frame FrameOf(string path, PixelSize? size = null)
    {
        var file = new FileInfo(path);
        return file.Exists
            ? new Frame(path, file.Length, file.LastWriteTimeUtc) { PixelSize = size }
            : new Frame(path, 0, DateTime.UtcNow) { PixelSize = size };
    }

    /// <summary>A project of <paramref name="frames"/> in this order, at 720p.</summary>
    public static ProjectSnapshot Snapshot(
        IEnumerable<Frame> frames,
        TimingSettings timing,
        FrameAspect aspect,
        FitMode fit,
        VideoFormat format = VideoFormat.Mp4H264,
        OutputQuality quality = OutputQuality.High,
        SequenceSettings? sequence = null,
        LookSettings? look = null,
        CropSettings? crop = null)
    {
        var project = new Project(
            timing,
            new FrameSettings(aspect, fit),
            new OutputSettings(OutputResolution.Hd720, format, quality),
            SortMode.Manual,
            sequence ?? SequenceSettings.Default,
            look ?? LookSettings.Default,
            crop ?? CropSettings.Default);
        project.AddFrames(frames);
        return project.Snapshot;
    }

    /// <summary>A size scaled to a <see cref="ShortSide"/> of 320 px, the long side rounded to even.</summary>
    public static PixelSize SmallSize(PixelSize video)
    {
        var (shortSide, longSide) = (Math.Min(video.Width, video.Height), Math.Max(video.Width, video.Height));
        var scaled = (int)Math.Round(longSide * (double)ShortSide / shortSide / 2, MidpointRounding.AwayFromZero) * 2;
        return video.Width >= video.Height ? new PixelSize(scaled, ShortSide) : new PixelSize(ShortSide, scaled);
    }

    /// <summary>
    /// Exports the snapshot through the project exporter, at the size <see cref="ProjectExporter.SettingsFor"/>
    /// chose scaled by <see cref="SmallSize"/>, and records every frame's time and duration as the exporter hands
    /// them to the real encoder. Skips only when this PC has no encoder for the format or no usable graphics device
    /// (<see cref="MediaSkips.CannotRunHere"/>).
    /// </summary>
    public static async Task<(ProjectExportResult Result, EncoderRecord Record)> ExportSmallOrSkipAsync(ProjectSnapshot snapshot, string path)
    {
        var record = new EncoderRecord();
        var result = await MediaSkips.ExportOrSkipAsync(
            snapshot,
            path,
            options: new ProjectExportOptions
            {
                Tune = settings =>
                {
                    var small = Resized(settings, SmallSize(new PixelSize(settings.Width, settings.Height)));
                    return SoftwareOnly ? small with { HardwareAcceleration = false } : small;
                },
                CreateEncoder = (file, settings, device) => new RecordingEncoder(new SinkWriterEncoder(file, settings, device), record),
            });
        return (result, record);
    }

    /// <summary>
    /// Skips when this PC cannot write and read back HEVC: no HEVC encoder, or no HEVC decoder (the app offers
    /// HEVC only with both, #9).
    /// </summary>
    public static void SkipWithoutHevc()
    {
        Assert.SkipWhen(
            EncoderCatalog.Find(VideoFormat.Mp4Hevc).Count == 0,
            "No HEVC encoder is installed (Media Foundation lists none), so the HEVC case cannot run on this PC; the H.264 cases still ran.");
        Assert.SkipWhen(
            EncoderCatalog.FindDecoders(VideoFormat.Mp4Hevc).Count == 0,
            "No HEVC decoder is installed (the HEVC Video Extensions), so the app does not offer HEVC here and the file could not be read back; the H.264 cases still ran.");
    }

    /// <summary>
    /// One unit of a timescale of floor(<paramref name="timescaleRate"/> × 1000) units a second, in whole ticks
    /// rounded up: 334 at 30 and 29.97 fps, 4,287 at 7/3, 23,365 at 3/7. For an export's own bound, use
    /// <see cref="SinkTimescaleUnit"/>.
    /// </summary>
    public static long TimescaleUnit(Fraction timescaleRate)
    {
        var unitsPerSecond = timescaleRate.Numerator * 1000 / timescaleRate.Denominator;
        return (FrameTiming.TicksPerSecond + unitsPerSecond - 1) / unitsPerSecond;
    }

    /// <summary>
    /// One unit of the timescale Media Foundation's MP4 sink keeps for an export at <paramref name="rate"/>:
    /// floor(nominal × 1000) units a second, where the nominal rate is the one the encoder writes into the media
    /// types (<see cref="SinkWriterEncoder.NominalFrameRate"/>: the rate itself at 1 fps and above, a whole multiple
    /// of it below).
    /// </summary>
    public static long SinkTimescaleUnit(Fraction rate) => TimescaleUnit(SinkWriterEncoder.NominalFrameRate(rate));

    /// <summary>True when the sink's timescale holds every <see cref="FrameTiming.FrameStart"/> exactly: nominal rate × 1000 is whole.</summary>
    public static bool HasExactTimescale(Fraction rate)
    {
        var nominal = SinkWriterEncoder.NominalFrameRate(rate);
        return nominal.Numerator * 1000 % nominal.Denominator == 0;
    }

    /// <summary>
    /// How far a sample time in the file may lie from <see cref="FrameTiming.FrameStart"/>, in ticks: exactly on it
    /// with an exact timescale; otherwise up to one <see cref="SinkTimescaleUnit"/> late at 1 fps and above (the
    /// sink rounds up onto its grid, PR #65), and one unit either side below 1 fps, where the grid is the nominal
    /// rate's (#22).
    /// </summary>
    public static (long Earliest, long Latest) SampleTimeBound(Fraction rate)
    {
        if (HasExactTimescale(rate))
        {
            return (0, 0);
        }

        var unit = SinkTimescaleUnit(rate);
        return SinkWriterEncoder.NominalFrameRate(rate) == rate ? (0, unit) : (-unit, unit);
    }

    /// <summary>The frames of a video, decoded to BGRA by Media Foundation, with their presentation times.</summary>
    public static (List<GoldenImage> Frames, List<long> Times) ReadAllFrames(string path)
    {
        var frames = new List<GoldenImage>();
        var times = new List<long>();
        VideoFileReader.ReadFrames(path, frame =>
        {
            frames.Add(GoldenImage.Copy(frame.Pixels, frame.Width, frame.Height, frame.Stride));
            times.Add(frame.Time);
        });
        return (frames, times);
    }

    /// <summary>
    /// Everything the snapshot asked for, at another size. A property <see cref="VideoExportSettings"/> gains must be
    /// copied here; <c>GoldenExportTests.ResizedCopiesEverySettingButTheSize</c> fails until it is.
    /// </summary>
    internal static VideoExportSettings Resized(VideoExportSettings settings, PixelSize size) =>
        new(size.Width, size.Height, settings.FrameRate)
        {
            Format = settings.Format,
            Bitrate = settings.Bitrate,
            HardwareAcceleration = settings.HardwareAcceleration,
            Fit = settings.Fit,
            DecodeAhead = settings.DecodeAhead,
            DecodeThreads = settings.DecodeThreads,
            StallTimeout = settings.StallTimeout,
            RenderTargets = settings.RenderTargets,
            DecodeZoom = settings.DecodeZoom,
        };

    private static void DeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file still held; the next class start sweeps it once it is old.
        }
    }

    private static void RemoveIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Another class just put something in it.
        }
    }
}
