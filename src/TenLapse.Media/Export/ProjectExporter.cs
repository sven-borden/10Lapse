using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Inspection;

namespace TenLapse.Media.Export;

/// <summary>What a project export wrote, for #24's finished state ("Harbor sunset.mp4 is ready · 24.7 s · 4K · H.264 · 173 MB").</summary>
/// <param name="Path">
/// The video file, full path: the target asked for, or, when another app held that file to the end, a free name
/// beside it (<c>Harbor (2).mp4</c>; <see cref="TargetInUse"/> then names the target).
/// </param>
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
    VideoEncoderInfo? Encoder)
{
    /// <summary>
    /// The file the export was asked to write, when another app kept it open (or it was read-only) through the
    /// final move: the finished video is at <see cref="Path"/> instead, and the old file is untouched. Null when
    /// the video is where it was asked to be. The finished state says so (<c>ProjectText.ExportSavedBeside</c>).
    /// </summary>
    public string? TargetInUse { get; init; }
}

/// <summary>
/// Exports a <see cref="ProjectSnapshot"/>: its frames in order at its effective rate, with its frame settings
/// (shape, Fit or Fill) and output settings (resolution, codec, quality), through <see cref="VideoExporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Safe writing.</b> Each export writes its own hidden unfinished file next to the target
/// (<c>Harbor.mp4.1f3a9c0e.partial</c>, <see cref="PartialSuffix"/>) and moves it over the target only once it is
/// complete, so an existing file is replaced only by a finished video, atomically. A cancel or a failure deletes the
/// partial file (in the background when the encoder is slow to let go: <see cref="ExportCleanup.WhenDone"/> of the
/// target). Because every export has its own partial file, an export never waits for, trips over or deletes another
/// one's, even of the same target. The folder is checked before anything else: a folder that is missing or cannot
/// be written to, or a target another app holds open, fails at once with its <see cref="VideoExportFailure"/>.
/// </para>
/// <para>
/// May be called from any thread; it runs on the thread pool. Progress is reported from worker threads.
/// </para>
/// </remarks>
public static class ProjectExporter
{
    /// <summary>The end of every unfinished file's name: <c>&lt;target name&gt;.&lt;8 hex digits&gt;.partial</c>.</summary>
    public const string PartialSuffix = ".partial";

    /// <summary>A new unfinished file name for an export of <paramref name="target"/>, unique to that export.</summary>
    internal static string NewPartialPath(string target) =>
        $"{target}.{Guid.NewGuid().ToString("N")[..8]}{PartialSuffix}";

    /// <summary>The unfinished files of exports of <paramref name="outputPath"/> in its folder now (running, or left behind).</summary>
    public static IReadOnlyList<string> PartialFilesOf(string outputPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        var target = Path.GetFullPath(outputPath);
        var folder = Path.GetDirectoryName(target);
        return folder is not null && Directory.Exists(folder)
            ? Directory.GetFiles(folder, $"{Path.GetFileName(target)}.*{PartialSuffix}")
            : [];
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
        var partial = NewPartialPath(target);
        await PrepareOutputAsync(target, partial, settings.Format, options.InUsePatience, cancellationToken).ConfigureAwait(false);
        try
        {
            var createEncoder = options.CreateEncoder ?? (static (path, s, device) => new SinkWriterEncoder(path, s, device));
            var written = await VideoExporter.ExportAsync(
                source,
                partial,
                settings,
                tracker,
                cancellationToken,
                (path, s, device) =>
                {
                    // Hidden once the encoder has it open: the sink writer creates its file with CREATE_ALWAYS,
                    // which Windows refuses on an existing hidden file.
                    var encoder = createEncoder(path, s, device);
                    SetHidden(path, true);
                    return encoder;
                }).ConfigureAwait(false);

            tracker.Enter(ExportStage.Finishing);
            SetHidden(partial, false);
            var saved = await PlaceAsync(partial, target, settings.Format, options.ReplacePatience, cancellationToken).ConfigureAwait(false);
            return new ProjectExportResult(
                saved,
                written.FrameCount,
                written.Duration,
                new PixelSize(settings.Width, settings.Height),
                settings.FrameRate,
                settings.Format,
                new FileInfo(saved).Length,
                written.Encoder)
            {
                TargetInUse = saved == target ? null : target,
            };
        }
        catch
        {
            // The exporter deletes the partial file once its encoder has opened it; one it never opened (the
            // encoder failed to start) or one a failed move left is deleted here, after any clean-up still holding
            // it. WhenDone(target) waits for it.
            ExportCleanup.Track(target, ExportCleanup.DeleteWhenDone(partial));
            throw;
        }
    }

    // Fails early, before a long export, for what would otherwise fail at the end: a folder that is not there or
    // cannot be written to, or a target another app has open. A target held only for a moment (Explorer making a
    // thumbnail) is waited out. The partial file's name is new, so this creates it.
    private static async Task PrepareOutputAsync(string target, string partial, VideoFormat format, TimeSpan patience, CancellationToken cancellationToken)
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
                Exception? held = null;
                await RetryAsync(
                    () =>
                    {
                        using (new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                        }
                    },
                    patience,
                    exception => held = exception,
                    cancellationToken).ConfigureAwait(false);
                if (held is not null)
                {
                    throw held;
                }
            }

            using (new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw ExportFailures.Classify(exception, VideoExportFailure.OutputFolderNotWritable, format);
        }
    }

    // Hides the unfinished file from Explorer, and shows it again before it becomes the video. Best effort: a
    // visible partial file is untidy, not wrong.
    private static void SetHidden(string path, bool hidden)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            File.SetAttributes(path, hidden ? attributes | FileAttributes.Hidden : attributes & ~FileAttributes.Hidden);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ExportCleanup.Report(new ExportCleanupFailure(path, hidden ? "hide the unfinished file" : "show the finished file", exception));
        }
    }

    // Moves the finished video over the target in one step: an existing file is replaced only now. Another app
    // that opened the target since the early check (a player, Explorer's thumbnailer) makes the move fail with
    // access denied even when it shares everything; a short hold is waited out. One that holds on gets the video
    // beside the target ("Harbor (2).mp4") rather than losing a finished export. Returns where the video is.
    private static async Task<string> PlaceAsync(string partial, string target, VideoFormat format, TimeSpan patience, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            await RetryAsync(
                () => File.Move(partial, target, overwrite: true),
                patience,
                exception => failure = exception,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw ExportFailures.Classify(exception, VideoExportFailure.OutputFolderNotWritable, format);
        }

        if (failure is null)
        {
            return target;
        }

        if (!File.Exists(target) || !File.Exists(partial))
        {
            throw ExportFailures.Classify(failure, VideoExportFailure.OutputFolderNotWritable, format);
        }

        var folder = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);
        for (var n = 2; n < 100; n++)
        {
            var beside = Path.Combine(folder, $"{name} ({n}){extension}");
            try
            {
                File.Move(partial, beside, overwrite: false);
                return beside;
            }
            catch (IOException) when (File.Exists(beside))
            {
                // Taken: try the next number.
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                break;
            }
        }

        // Nowhere to put it: the target is still held, so that is what the user can fix.
        throw ExportFailures.Wrap(failure, VideoExportFailure.OutputFileInUse, format);
    }

    // Runs a file operation, retrying for up to `patience` while it fails with a sharing or access violation
    // (another process holding the file for a moment); hands the last failure to `failed` instead of throwing it.
    private static async Task RetryAsync(Action operation, TimeSpan patience, Action<Exception> failed, CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var delay = TimeSpan.FromMilliseconds(50);
        while (true)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && IsHeld(exception))
            {
                if (clock.Elapsed + delay > patience)
                {
                    failed(exception);
                    return;
                }
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMilliseconds(800).Ticks));
        }
    }

    // E_ACCESSDENIED, ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION, ERROR_USER_MAPPED_FILE: another process has the file.
    private static bool IsHeld(Exception exception) =>
        unchecked((uint)exception.HResult) is 0x80070005 or 0x80070020 or 0x80070021 or 0x800704C8;
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

    /// <summary>How long the early check waits for another app to let go of an existing target.</summary>
    public TimeSpan InUsePatience { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>How long the final move waits for another app to let go of the target before the video goes beside it.</summary>
    public TimeSpan ReplacePatience { get; init; } = TimeSpan.FromSeconds(5);
}
