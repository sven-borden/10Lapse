using Microsoft.UI.Dispatching;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// The only way the app enqueues work on a <see cref="DispatcherQueue"/> (a test in <c>tests/TenLapse.Tests/Code</c>
/// fails on a raw <c>TryEnqueue</c> anywhere else).
/// </summary>
/// <remarks>
/// A <c>DispatcherQueue</c> callback that throws ends the process with STATUS_STOWED_EXCEPTION and reaches no
/// handler, not even <c>Application.UnhandledException</c> (checked in #26). So every callback runs inside
/// <see cref="ErrorPath.RunLogged"/>: a failure is written to the log through the sink's <c>Report</c> and nothing
/// else. It is never shown, because the callbacks that run here include the InfoBar presenter's own, and showing an
/// InfoBar for its failure would run the broken code again.
/// </remarks>
internal static class DispatcherQueueExtensions
{
    /// <summary>Enqueues <paramref name="action"/>; a failure in it is logged through <paramref name="sink"/> only.</summary>
    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="sink">Takes the failure (its <see cref="IErrorSink.Report"/>, which writes to the log only).</param>
    /// <param name="action">The work, run on the dispatcher's thread.</param>
    /// <param name="context">What it does, for the log (see <see cref="Presentation.ProjectText"/>).</param>
    /// <returns>False when the queue is shutting down and the work will not run.</returns>
    public static bool TryEnqueueGuarded(this DispatcherQueue dispatcher, IErrorSink sink, Action action, string context)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(action);
        return dispatcher.TryEnqueue(() => sink.RunLogged(action, context));
    }
}
