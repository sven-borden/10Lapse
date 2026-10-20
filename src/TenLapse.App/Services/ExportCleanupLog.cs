using TenLapse.Core.Diagnostics;
using TenLapse.Media.Export;

namespace TenLapse.Services;

/// <summary>
/// The app's side of the export clean-up (#22): every clean-up step that failed or was abandoned goes to the log as
/// a Warning (it happens after the export has returned, so there is nobody to show an InfoBar to), the journal of
/// unfinished export files lives next to the log, and what an earlier run left is deleted at start.
/// </summary>
internal static class ExportCleanupLog
{
    /// <summary>Subscribes the log and sets the journal (<paramref name="journalFile"/>, <c>AppFolders.UnfinishedExportsFile</c>); then deletes the leftovers on the thread pool. Call once, before any export.</summary>
    public static void Start(ILog log, string journalFile)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalFile);
        ExportCleanup.Failed += (_, failure) => log.Warning(
            failure.Exception is null
                ? $"Export clean-up of {failure.Path}: gave up trying to {failure.Step}; an encoder call never returned."
                : $"Export clean-up of {failure.Path}: could not {failure.Step}.",
            failure.Exception);

        var journal = new PartialFileJournal(journalFile);
        ExportCleanup.Journal = journal;
        _ = Task.Run(() =>
        {
            try
            {
                if (journal.DeleteLeftovers() is var deleted and > 0)
                {
                    log.Info($"Deleted {deleted} unfinished export file(s) an earlier run left behind.");
                }
            }
            catch (Exception exception)
            {
                log.Warning("Deleting the unfinished export files an earlier run left behind failed.", exception);
            }
        });
    }
}
