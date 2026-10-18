using System.Collections.Concurrent;
using System.Diagnostics;

namespace TenLapse.Media.Export;

/// <summary>A clean-up step after an export that went wrong, for the log.</summary>
/// <param name="Path">The output file the clean-up was for (the <c>.partial</c> file for a project export).</param>
/// <param name="Step">What failed: "release the encoder", "delete the unfinished file", ...</param>
/// <param name="Exception">Why, or null when the step was abandoned (an encoder call that never returned).</param>
public sealed record ExportCleanupFailure(string Path, string Step, Exception? Exception);

/// <summary>
/// The clean-up a cancelled or failed export leaves running after it has returned: releasing the encoder (an
/// unfinished Media Foundation sink writer can take about 5 s to let go), deleting the unfinished file and
/// disposing the export's graphics device, in that order. An export returns within about a quarter of a second of
/// stopping whatever the clean-up does; this class lets a caller wait for it, and reports what it could not do.
/// </summary>
/// <remarks>
/// Clean-ups never throw: a failure is reported through <see cref="Failed"/> (the app logs it) and to
/// <see cref="Trace"/>. A successful export has finished its clean-up before it returns.
/// </remarks>
public static class ExportCleanup
{
    private static readonly ConcurrentDictionary<string, Task> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised on a thread pool thread for each clean-up step that failed or was abandoned. Handlers must not throw.</summary>
    public static event EventHandler<ExportCleanupFailure>? Failed;

    /// <summary>
    /// Completes once every clean-up started so far for <paramref name="path"/> has finished (successfully or
    /// not; it never faults). Already complete when there is none. An export of the same file waits for this
    /// first, so it does not trip over the previous one's unfinished file.
    /// </summary>
    public static Task WhenDone(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return _pending.TryGetValue(Path.GetFullPath(path), out var task) ? task : Task.CompletedTask;
    }

    /// <summary>Records a clean-up of <paramref name="path"/>, which must not fault (it reports instead).</summary>
    internal static void Track(string path, Task cleanup)
    {
        var key = Path.GetFullPath(path);
        var tracked = _pending.AddOrUpdate(key, cleanup, (_, prior) => Task.WhenAll(prior, cleanup));
        tracked.ContinueWith(
            done => _pending.TryRemove(new KeyValuePair<string, Task>(key, done)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Runs <paramref name="step"/>, reporting instead of throwing; true when it worked.</summary>
    internal static bool Try(string path, string step, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception exception)
        {
            Report(new ExportCleanupFailure(path, step, exception));
            return false;
        }
    }

    /// <summary>Deletes <paramref name="path"/> once every clean-up of it already running is done, reporting a failure.</summary>
    internal static void DeleteWhenDone(string path)
    {
        var prior = WhenDone(path);
        Track(path, prior.ContinueWith(
            _ => Try(path, "delete the unfinished file", () => DeleteIfThere(path)),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default));
    }

    /// <summary>Deletes a file; one that is not there (or whose folder is gone) is fine.</summary>
    internal static void DeleteIfThere(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    internal static void Report(ExportCleanupFailure failure)
    {
        Trace.TraceWarning($"Export clean-up of {failure.Path}: could not {failure.Step}: {failure.Exception?.ToString() ?? "abandoned"}");
        var handlers = Failed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<ExportCleanupFailure>>())
        {
            try
            {
                handler(null, failure);
            }
            catch (Exception exception)
            {
                Trace.TraceError($"An export clean-up handler threw: {exception}");
            }
        }
    }
}
