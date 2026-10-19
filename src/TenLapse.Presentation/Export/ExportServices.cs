using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Export;

/// <summary>What a running export is doing (Media's <c>ExportStage</c>, which Presentation cannot see).</summary>
public enum ExportPhase
{
    /// <summary>Checking the folder and opening the encoder: no frame written yet.</summary>
    Preparing,

    /// <summary>Decoding, drawing and encoding frames.</summary>
    Encoding,

    /// <summary>Every frame is in; the file is being completed and moved into place.</summary>
    Finishing,
}

/// <summary>A progress report of a running export (Media's <c>ExportProgress</c>).</summary>
/// <param name="Phase">What the export is doing.</param>
/// <param name="Frame">Frames the encoder has taken, 0 to <paramref name="FrameCount"/>; never goes down.</param>
/// <param name="FrameCount">Frames in the export.</param>
/// <param name="FramesPerSecond">Encoding speed, smoothed; null until measured.</param>
/// <param name="TimeLeft">Time to the last frame at that speed; null until a few frames are done.</param>
public sealed record ExportStatus(ExportPhase Phase, int Frame, int FrameCount, double? FramesPerSecond, TimeSpan? TimeLeft)
{
    /// <summary>The share of frames done, 0 to 1.</summary>
    public double Fraction => FrameCount <= 0 ? 0 : Math.Clamp((double)Frame / FrameCount, 0, 1);
}

/// <summary>What an export wrote (Media's <c>ProjectExportResult</c>), for the finished state.</summary>
/// <param name="Path">The video file: the one asked for, or a free name beside it when another app held that one (<see cref="TargetInUse"/>).</param>
/// <param name="FrameCount">Frames written.</param>
/// <param name="Duration">The clip length in 100 ns ticks.</param>
/// <param name="Size">Width and height in pixels.</param>
/// <param name="Rate">The exact frame rate.</param>
/// <param name="Format">The codec.</param>
/// <param name="FileBytes">The file's real size on disk.</param>
public sealed record ExportedVideo(string Path, int FrameCount, long Duration, PixelSize Size, Fraction Rate, VideoFormat Format, long FileBytes)
{
    /// <summary>The file asked for, when another app held it to the end and the video went beside it; null otherwise.</summary>
    public string? TargetInUse { get; init; }
}

/// <summary>
/// Writes a project's video. The App implements it over Media's <c>ProjectExporter</c> and <c>ExportCleanup</c>;
/// tests use a fake. <see cref="ExportViewModel"/> drives it.
/// </summary>
public interface IVideoExporter
{
    /// <summary>
    /// Exports <paramref name="snapshot"/> (its frames, rate, frame settings and <see cref="ProjectSnapshot.Output"/>) to
    /// <paramref name="path"/>, replacing a file there only once the video is complete. Runs off the caller's thread;
    /// <paramref name="progress"/> is reported from worker threads.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ExportedVideo> ExportAsync(ProjectSnapshot snapshot, string path, IProgress<ExportStatus> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Completes when the clean-up of a cancelled or failed export of <paramref name="path"/> has finished, with true when
    /// none of the unfinished files there when it was called is left (false: the next launch deletes them). Call it as
    /// that export ends, before another export of the same file can start: a later export's file does not count. Never
    /// faults.
    /// </summary>
    Task<bool> CleanUpAsync(string path);

    /// <summary>Completes when every export clean-up started so far has finished. Never faults.</summary>
    Task WhenAllCleanedUp();
}

/// <summary>The Save dialog for the video.</summary>
public interface ISaveVideoPicker
{
    /// <summary>
    /// Asks where to save the video, suggesting <paramref name="suggestedName"/> (with its <c>.mp4</c>) in
    /// <paramref name="startFolder"/>, or in Videos when that is null or gone.
    /// </summary>
    /// <returns>The full path chosen, or null when the user cancelled.</returns>
    Task<string?> PickVideoAsync(string suggestedName, string? startFolder);

    /// <summary>
    /// The export to <paramref name="path"/> was cancelled or failed: if this dialog itself created that file (an empty
    /// one, in this session) and it is still empty, it is deleted, so no 0-byte video is left behind. Anything else is
    /// left alone. Never throws.
    /// </summary>
    void Discard(string path);
}

/// <summary>Where the Save dialog opens: the folder of the last successful export, across launches (#21).</summary>
public interface IExportFolderMemory
{
    /// <summary>The folder, or null for Videos.</summary>
    string? ExportFolder { get; set; }
}

/// <summary>Opens File Explorer on a file (Show in folder).</summary>
public interface IFileReveal
{
    /// <summary>Opens the file's folder with the file selected.</summary>
    void ShowInFolder(string path);
}

/// <summary>
/// What the window does for an export beyond the panel: taskbar progress (<c>ITaskbarList3</c>) and a Windows notification
/// when the export ends while the window is in the background. Every call is on the UI thread and must not throw.
/// </summary>
public interface IExportWindow
{
    /// <summary>Taskbar progress: <paramref name="fraction"/> 0 to 1, or null while the export is getting ready (indeterminate).</summary>
    void ShowProgress(double? fraction);

    /// <summary>The export ended well or was cancelled: no taskbar progress.</summary>
    void ClearProgress();

    /// <summary>The export failed: the taskbar button shows it until the user is back in the window.</summary>
    void ShowFailed();

    /// <summary>A notification with <paramref name="message"/> (it names the file and nothing else), only when the window is in the background.</summary>
    void NotifyIfInBackground(string message);
}

/// <summary>The services <see cref="ExportViewModel"/> works through; the App builds them, tests pass fakes.</summary>
/// <param name="Exporter">Writes the video.</param>
/// <param name="Picker">The Save dialog.</param>
/// <param name="Folders">Where the Save dialog opens.</param>
/// <param name="Dialogs">Asks before a close stops the export.</param>
/// <param name="InfoBars">Says that the export was cancelled, and that edits wait.</param>
/// <param name="Reveal">Show in folder.</param>
/// <param name="Window">Taskbar progress and the notification.</param>
public sealed record ExportServices(
    IVideoExporter Exporter,
    ISaveVideoPicker Picker,
    IExportFolderMemory Folders,
    IDialogService Dialogs,
    IInfoBarService InfoBars,
    IFileReveal Reveal,
    IExportWindow Window);

/// <summary>How an export ended.</summary>
public enum ExportOutcome
{
    /// <summary>The video is written.</summary>
    Succeeded,

    /// <summary>The user cancelled it (Cancel export, or closing the window).</summary>
    Cancelled,

    /// <summary>It failed; the error path says why.</summary>
    Failed,
}

/// <summary>An export ended (<see cref="ExportViewModel.Ended"/>).</summary>
public sealed class ExportEndedEventArgs(ExportOutcome outcome, ExportedVideo? video) : EventArgs
{
    /// <summary>How it ended.</summary>
    public ExportOutcome Outcome { get; } = outcome;

    /// <summary>The video, when it <see cref="ExportOutcome.Succeeded"/>.</summary>
    public ExportedVideo? Video { get; } = video;
}
