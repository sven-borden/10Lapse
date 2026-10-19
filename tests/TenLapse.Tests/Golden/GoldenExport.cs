using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;

namespace TenLapse.Tests.Golden;

/// <summary>
/// What the golden render tests (#32) share: the project they export, the export itself at a small size, the
/// skips, and the folders for scratch files and for the evidence a failure leaves.
/// </summary>
/// <remarks>
/// The export goes through <see cref="ProjectExporter"/> from a <see cref="ProjectSnapshot"/>, exactly as the app
/// runs it, with one change: the frame is scaled down to a <see cref="ShortSide"/> of 320 px (720p is the smallest
/// resolution a project offers), which keeps the gate fast. The shape, the rate, the fit, the codec and the bitrate
/// are the snapshot's own (<see cref="ProjectExporter.SettingsFor"/>). Keeping the 720p bitrate at the smaller size
/// leaves little codec noise, so a geometry fault cannot hide in it.
/// </remarks>
internal static class GoldenExport
{
    /// <summary>The short side of every golden export, in pixels.</summary>
    public const int ShortSide = 320;

    /// <summary>
    /// Where a failing case leaves its evidence: <c>GoldenFailures\&lt;case&gt;</c> beside the test host. Called at
    /// the start of the case, it removes what an earlier run left, so the folder only ever shows the latest failure.
    /// </summary>
    public static string EvidenceFolder(string caseName)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "GoldenFailures", caseName);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        return folder;
    }

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

    /// <summary>A new scratch folder beside the test host, for one test class's photos and videos.</summary>
    public static string NewScratchFolder(string name)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "GoldenScratch", $"{name}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static void DeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file still held; the next build's clean or the next run leaves it harmless.
        }
    }

    /// <summary>A project of <paramref name="photos"/> in this order, with their sizes known as an import reads them.</summary>
    public static ProjectSnapshot Snapshot(
        IEnumerable<(string Path, PixelSize Size)> photos,
        TimingSettings timing,
        FrameAspect aspect,
        FitMode fit,
        VideoFormat format = VideoFormat.Mp4H264)
    {
        var project = new Project(
            timing,
            new FrameSettings(aspect, fit),
            new OutputSettings(OutputResolution.Hd720, format, OutputQuality.High),
            SortMode.Manual);
        project.AddFrames(photos.Select(photo => new Frame(photo.Path, new FileInfo(photo.Path).Length, File.GetLastWriteTimeUtc(photo.Path)) { PixelSize = photo.Size }));
        return project.Snapshot;
    }

    /// <summary>The snapshot's video size scaled to a <see cref="ShortSide"/> of 320 px, the long side rounded to even.</summary>
    public static PixelSize SmallSize(PixelSize video)
    {
        var (shortSide, longSide) = (Math.Min(video.Width, video.Height), Math.Max(video.Width, video.Height));
        var scaled = (int)Math.Round(longSide * (double)ShortSide / shortSide / 2, MidpointRounding.AwayFromZero) * 2;
        return video.Width >= video.Height ? new PixelSize(scaled, ShortSide) : new PixelSize(ShortSide, scaled);
    }

    /// <summary>
    /// Exports the snapshot at <see cref="SmallSize"/> through the project exporter. Skips, saying why, when this PC
    /// has no encoder for the format or no usable graphics device.
    /// </summary>
    public static async Task<ProjectExportResult> ExportOrSkipAsync(ProjectSnapshot snapshot, string path)
    {
        var size = SmallSize(snapshot.VideoSize);
        try
        {
            return await ProjectExporter.ExportAsync(
                snapshot,
                path,
                progress: null,
                TestContext.Current.CancellationToken,
                new ProjectExportOptions { Tune = settings => Resized(settings, size) });
        }
        catch (VideoExportException exception) when (exception.Failure is VideoExportFailure.EncoderUnavailable or VideoExportFailure.NoGraphicsDevice)
        {
            Assert.Skip($"The {snapshot.Output.Format} export cannot run on this PC ({exception.Message}). Run the golden tests on a PC with one before merging.");
            throw;
        }
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
    /// The most a sample time may lie after <see cref="FrameTiming.FrameStart"/>: Media Foundation's MP4 sink keeps
    /// floor(rate × 1000) time units a second, so a time lands on that grid at most one unit late, and exactly on
    /// it when rate × 1000 is whole (PR #65). The container duration and the sample durations are within one unit.
    /// </summary>
    public static long TimescaleUnit(Fraction rate)
    {
        var unitsPerSecond = rate.Numerator * 1000 / rate.Denominator;
        return (FrameTiming.TicksPerSecond + unitsPerSecond - 1) / unitsPerSecond;
    }

    /// <summary>True when rate × 1000 is whole: every sample time is then exactly <see cref="FrameTiming.FrameStart"/>.</summary>
    public static bool HasExactTimescale(Fraction rate) => rate.Numerator * 1000 % rate.Denominator == 0;

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

    // Everything the snapshot asked for, at another size.
    private static VideoExportSettings Resized(VideoExportSettings settings, PixelSize size) =>
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
        };
}
