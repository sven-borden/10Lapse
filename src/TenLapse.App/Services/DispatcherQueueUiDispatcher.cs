using Microsoft.UI.Dispatching;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// <see cref="IUiDispatcher"/> on the window's <see cref="DispatcherQueue"/> (#12's import batches). Each piece of work
/// goes through <see cref="DispatcherQueueExtensions.TryEnqueueGuarded"/>; its own exception is caught inside the
/// callback and handed to the awaiting task, so the caller decides what the user hears and the callback itself never
/// throws.
/// </summary>
/// <param name="queue">The UI thread's queue.</param>
/// <param name="sink">Logs a failure of the callback wrapper itself (log only).</param>
internal sealed class DispatcherQueueUiDispatcher(DispatcherQueue queue, IErrorSink sink) : IUiDispatcher
{
    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Continuations run on the thread pool, never inline on the UI thread.
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = queue.TryEnqueueGuarded(
            sink,
            () =>
            {
                try
                {
                    result.TrySetResult(work());
                }
                catch (Exception exception)
                {
                    result.TrySetException(exception);
                }
            },
            ProjectText.ImportUiStepFailed);
        if (!queued)
        {
            // The window is closing: the work will never run.
            result.TrySetCanceled();
        }

        return result.Task;
    }
}
