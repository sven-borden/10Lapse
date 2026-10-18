using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Inspection;

namespace TenLapse.Media.Export;

/// <summary>What a project export wrote, for #24's finished state ("Harbor sunset.mp4 is ready · 24.7 s · 4K · H.264 · 173 MB").</summary>
/// <param name="Path">The video file, full path.</param>
/// <param name="FrameCount">Frames written.</param>
/// <param name="Duration">The clip duration in 100 ns ticks (<see cref="FrameTiming.Duration"/>).</param>
/// <param name="Size">Width and height in pixels.</param>
/// <param name="Rate">The exact frame rate.</param>
/// <param name="Format">The codec.</param>
/// <param name="FileBytes">The file's size on disk.</param>
/// <param name="Encoder">The encoder that ran, if it could tell.</param>
public sealed record ProjectExportResult(
    string Path,
    int FrameCount,
    long Duration,
    PixelSize Size,
    Fraction Rate,
    VideoFormat Format,
    long FileBytes,
    VideoEncoderInfo? Encoder);

/// <summary>
/// Exports a <see cref="ProjectSnapshot"/>: its frames in order at its effective rate, with its frame settings
/// (shape, Fit or Fill) and output settings (resolution, codec, quality), through <see cref="VideoExporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Safe writing.</b> The video is written to <c>&lt;name&gt;.partial</c> next to the target (<see cref="PartialPathFor"/>;
/// <c>Harbor.mp4.partial</c>) and moved over the target only once it is complete, so an existing file is replaced
/// only by a finished video, atomically. A cancel or a failure deletes the partial file (in the background when the
/// encoder is slow to let go: <see cref="ExportCleanup"/>). A partial file left by a crash is overwritten by the
/// next export to the same name. The folder is checked before anything else: a folder that is missing or cannot be
/// written to, or a target another app holds open, fails at once with its <see cref="VideoExportFailure"/>.
/// </para>
/// <para>
/// May be called from any thread; it runs on the thread pool. Progress is reported from worker threads.
/// </para>
/// </remarks>
public static class ProjectExporter
{
    /// <summary>How long an export waits for an earlier export's clean-up of the same partial file before it goes ahead.</summary>
    internal static readonly TimeSpan EarlierCleanupWait = TimeSpan.FromSeconds(10);

    /// <summary>The unfinished file an export of <paramref name="outputPath"/> writes: the same path with <c>.partial</c> added.</summary>
    public static string PartialPathFor(string outputPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        return Path.GetFullPath(outputPath) + ".partial";
    }

    /// <summary>
    /// The export settings for a snapshot: <see cref="ProjectSnapshot.VideoSize"/>, the effective rate, the fit mode,
    /// the codec and <see cref="ExportBitrate.For"/> of the quality.
    /// </summary>
    public static VideoExportSettings SettingsFor(ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var size = snapshot.VideoSize;
        var rate = snapshot.EffectiveRate.Rate;
        return new VideoExportSettings(size.Width, size.Height, rate)
        {
            Format = snapshot.Output.Format,
            Fit = snapshot.FrameSettings.Fit,
            Bitrate = ExportBitrate.For(size, rate, snapshot.Output.Format, snapshot.Output.Quality),
        };
    }

    /// <summary>Exports <paramref name="snapshot"/> to <paramref name="outputPath"/>, replacing a file there only once the new one is complete.</summary>
    /// <param name="snapshot">The project as it is when the export starts; later edits do not reach this export.</param>
    /// <param name="outputPath">The video file to write, normally <c>.mp4</c>.</param>
    /// <param name="progress">Receives <see cref="ExportProgress"/> about 10 times a second, from worker threads.</param>
    /// <exception cref="ArgumentException">The snapshot has no frames.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled; it returns within about a second.</exception>
    /// <exception cref="VideoExportException">The export failed for the reason its <see cref="VideoExportException.Failure"/> names.</exception>
    public static Task<ProjectExportResult> ExportAsync(
        ProjectSnapshot snapshot,
        string outputPath,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExportAsync(snapshot, outputPath, progress, cancellationToken, new ProjectExportOptions());

    /// <summary>The export with test seams: the encoder, the settings, the clock.</summary>
    internal static Task<ProjectExportResult> ExportAsync(
        ProjectSnapshot snapshot,
        string outputPath,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken,
        ProjectExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentNullException.ThrowIfNull(options);
        if (snapshot.Frames.IsEmpty)
        {
            throw new ArgumentException("The project has no frames to export.", nameof(snapshot));
        }

        var target = Path.GetFullPath(outputPath);
        var settings = options.Tune(SettingsFor(snapshot));
        var source = new PhotoFileSource([.. snapshot.Frames.Select(frame => frame.SourcePath)], settings.Fit);
        var tracker = new ExportProgressTracker(source.FrameCount, progress, options.Clock);
        tracker.Enter(ExportStage.Preparing);
        return Task.Run(() => RunAsync(source, target, settings, tracker, options, cancellationToken), CancellationToken.None);
    }

    private static async Task<ProjectExportResult> RunAsync(
        PhotoFileSource source,
        string target,
        VideoExportSettings settings,
        ExportProgressTracker tracker,
        ProjectExportOptions options,
        CancellationToken cancellationToken)
    {
        var partial = PartialPathFor(target);

        // The previous export of this file may still be releasing its partial file (cancelled moments ago).
        await ExportCleanup.WhenDone(partial)
            .WaitAsync(EarlierCleanupWait, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        cancellationToken.ThrowIfCancellationRequested();

        PrepareOutput(target, partial, settings.Format);
        try
        {
            var written = await VideoExporter.ExportAsync(
                source,
                partial,
                settings,
                tracker,
                cancellationToken,
                options.CreateEncoder ?? (static (path, s, device) => new SinkWriterEncoder(path, s, device))).ConfigureAwait(false);

            tracker.Enter(ExportStage.Finishing);
            Replace(partial, target, settings.Format);
            return new ProjectExportResult(
                target,
                written.FrameCount,
                written.Duration,
                new PixelSize(settings.Width, settings.Height),
                settings.FrameRate,
                settings.Format,
                new FileInfo(target).Length,
                written.Encoder);
        }
        catch
        {
            // The exporter deletes the partial file once its encoder has opened it; one it never opened (the
            // encoder failed to start) or one a failed move left is deleted here, after any clean-up still holding it.
            ExportCleanup.DeleteWhenDone(partial);
            throw;
        }
    }

    // Fails early, before a long export, for what would otherwise fail at the end: a folder that is not there or
    // cannot be written to, or a target another app has open. Creating the partial file overwrites a stale one.
    private static void PrepareOutput(string target, string partial, VideoFormat format)
    {
        var folder = Path.GetDirectoryName(target);
        if (folder is null || !Directory.Exists(folder))
        {
            throw new VideoExportException(
                $"{ExportFailures.Summary(VideoExportFailure.OutputFolderMissing, format)}: {folder ?? target}.",
                VideoExportFailure.OutputFolderMissing);
        }

        try
        {
            if (File.Exists(target))
            {
                // Opened for writing with no sharing: fails when a player has it open or it is read-only.
                using (new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
            }

            using (new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw ExportFailures.Classify(exception, VideoExportFailure.OutputFolderNotWritable, format);
        }
    }

    // Moves the finished video over the target in one step: an existing file is replaced only now.
    private static void Replace(string partial, string target, VideoFormat format)
    {
        try
        {
            File.Move(partial, target, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw ExportFailures.Classify(exception, VideoExportFailure.OutputFolderNotWritable, format);
        }
    }
}

/// <summary>Test seams of <see cref="ProjectExporter"/>.</summary>
internal sealed record ProjectExportOptions
{
    /// <summary>The encode stage; null for the sink writer.</summary>
    public VideoEncoderFactory? CreateEncoder { get; init; }

    /// <summary>Adjusts the settings the snapshot gives (the stall limit, the queue depths).</summary>
    public Func<VideoExportSettings, VideoExportSettings> Tune { get; init; } = static settings => settings;

    /// <summary>The progress clock; null for a stopwatch.</summary>
    public Func<TimeSpan>? Clock { get; init; }
}
