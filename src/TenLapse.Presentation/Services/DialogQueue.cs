namespace TenLapse.Presentation.Services;

/// <summary>
/// Lets one modal question show at a time and makes the others wait their turn, in order. WinUI allows one
/// open <c>ContentDialog</c> per window and throws on a second, so the App's dialog service shows every
/// question through this queue: no question is dropped or read as "declined" because another was open.
/// </summary>
public sealed class DialogQueue
{
    private readonly SemaphoreSlim _turn = new(1, 1);

    /// <summary>
    /// Waits for this queue's turn, then for <paramref name="waitUntilFree"/> (dialogs this queue does not
    /// own, such as a system or third-party dialog), then shows the question with <paramref name="show"/>. The
    /// turn passes on when the question closes, also when showing it throws. Continuations stay on the
    /// caller's context (the UI thread).
    /// </summary>
    public async Task<T> RunAsync<T>(Func<Task> waitUntilFree, Func<Task<T>> show)
    {
        ArgumentNullException.ThrowIfNull(waitUntilFree);
        ArgumentNullException.ThrowIfNull(show);

        await _turn.WaitAsync();
        try
        {
            await waitUntilFree();
            return await show();
        }
        finally
        {
            _turn.Release();
        }
    }
}
