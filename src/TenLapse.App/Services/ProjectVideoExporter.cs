using TenLapse.Core.Projects;
using TenLapse.Media.Export;
using TenLapse.Presentation.Export;

namespace TenLapse.Services;

/// <summary>
/// <see cref="IVideoExporter"/> over Media's <see cref="ProjectExporter"/> and <see cref="ExportCleanup"/>: the export
/// view model (#24) never sees Media's types. Failures pass through as they are (<see cref="VideoExportException"/>),
/// so the error path words them with <see cref="VideoExportErrorMapper"/>.
/// </summary>
internal sealed class ProjectVideoExporter : IVideoExporter
{
    /// <inheritdoc />
    public async Task<ExportedVideo> ExportAsync(ProjectSnapshot snapshot, string path, IProgress<ExportStatus> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var result = await ProjectExporter.ExportAsync(snapshot, path, new Relay(progress), cancellationToken).ConfigureAwait(false);
        return new ExportedVideo(result.Path, result.FrameCount, result.Duration, result.Size, result.Rate, result.Format, result.FileBytes)
        {
            TargetInUse = result.TargetInUse,
        };
    }

    /// <inheritdoc />
    public async Task<bool> CleanUpAsync(string path)
    {
        // The unfinished files there now, as the cancelled export ends: an export of the same file started after it
        // writes a new unique name, which must not count as this one's leftover (#24 review, minor 7).
        IReadOnlyList<string> left;
        try
        {
            left = ProjectExporter.PartialFilesOf(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The folder cannot be listed now (a drive that went away): nothing can be said, and the journal deletes the file later.
            await ExportCleanup.WhenDone(path).ConfigureAwait(false);
            return false;
        }

        await ExportCleanup.WhenDone(path).ConfigureAwait(false);
        try
        {
            return !left.Any(File.Exists);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task WhenAllCleanedUp() => ExportCleanup.WhenAllDone();

    // Media's report in Presentation's words, on the same (worker) thread: the view model's Progress<T> posts it to the UI.
    internal sealed class Relay(IProgress<ExportStatus> target) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => target.Report(new ExportStatus(
            value.Stage switch
            {
                ExportStage.Preparing => ExportPhase.Preparing,
                ExportStage.Encoding => ExportPhase.Encoding,
                _ => ExportPhase.Finishing,
            },
            value.Frame,
            value.FrameCount,
            value.FramesPerSecond,
            value.TimeLeft));
    }
}
