namespace TenLapse.Presentation.Services;

/// <summary>
/// Runs work on the UI thread from any thread and hands back its result, so background work (#12's import) can
/// change a <see cref="TenLapse.Core.Projects.Project"/>, which only its UI thread may change, and the view models
/// bound to it. The App implements it on the window's <c>DispatcherQueue</c> through <c>TryEnqueueGuarded</c>; tests
/// use a thread of their own.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Queues <paramref name="work"/> on the UI thread. The task completes, never on the UI thread itself, with the
    /// work's result, or faults with the exception it threw (so the caller decides what the user hears), or is
    /// cancelled when the UI thread is shutting down and the work will not run.
    /// </summary>
    Task<T> InvokeAsync<T>(Func<T> work);
}

/// <summary>Helpers for <see cref="IUiDispatcher"/>.</summary>
public static class UiDispatcherExtensions
{
    /// <summary>Queues <paramref name="work"/> on the UI thread; see <see cref="IUiDispatcher.InvokeAsync"/>.</summary>
    public static Task RunAsync(this IUiDispatcher dispatcher, Action work)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(work);
        return dispatcher.InvokeAsync(() =>
        {
            work();
            return true;
        });
    }
}
