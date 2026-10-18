using CommunityToolkit.Mvvm.Input;

namespace TenLapse.Presentation.Services;

/// <summary>
/// How code reaches the one error path (#26). Every command is made here, so none can throw into WinUI (an
/// <c>AsyncRelayCommand</c> run by a button rethrows on the UI thread, which ends the process):
/// <code>
/// NewProjectCommand = errors.AsyncCommand(NewProjectAsync, ProjectText.NewProjectFailed);
/// </code>
/// and an event handler that must be <c>async void</c> awaits exactly one guarded call:
/// <code>
/// private async void OnDrop(object? sender, DragEventArgs e) => await _errors.GuardAsync(() => ImportAsync(e), ProjectText.ImportFailed);
/// </code>
/// A test (<c>tests/TenLapse.Tests/Code</c>) fails on a <c>[RelayCommand]</c>, a command built anywhere else, or any
/// other <c>async void</c>.
/// </summary>
public static class ErrorPath
{
    /// <summary>
    /// Runs <paramref name="action"/> and hands any failure to <see cref="IErrorPresenter.Show"/> (a cancellation
    /// shows nothing). Never throws, so it is safe to await from an <c>async void</c> event handler.
    /// </summary>
    /// <param name="errors">The error path.</param>
    /// <param name="action">The work.</param>
    /// <param name="context">What it does, for the log (see <see cref="ProjectText"/>).</param>
    public static async Task GuardAsync(this IErrorPresenter errors, Func<Task> action, string context)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            errors.Show(exception, context);
        }
    }

    /// <summary>Runs <paramref name="action"/> and hands any failure to <see cref="IErrorPresenter.Show"/>. Never throws.</summary>
    public static void Guard(this IErrorPresenter errors, Action action, string context)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Show(exception, context);
        }
    }

    /// <summary>
    /// An async command whose failures go to the error path. Like <c>[RelayCommand]</c> on an async method, it
    /// cannot run again while it runs (bound buttons disable).
    /// </summary>
    public static IAsyncRelayCommand AsyncCommand(this IErrorPresenter errors, Func<Task> execute, string context, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(execute);
        return new AsyncRelayCommand(() => errors.GuardAsync(execute, context), canExecute ?? Always);
    }

    /// <summary>
    /// A cancellable async command (#22's export): <c>Cancel()</c> on it cancels the token, and the cancellation
    /// shows nothing.
    /// </summary>
    public static IAsyncRelayCommand AsyncCommand(this IErrorPresenter errors, Func<CancellationToken, Task> execute, string context, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(execute);
        return new AsyncRelayCommand(token => errors.GuardAsync(() => execute(token), context), canExecute ?? Always);
    }

    /// <summary>An async command with a parameter whose failures go to the error path.</summary>
    public static IAsyncRelayCommand<T> AsyncCommand<T>(this IErrorPresenter errors, Func<T?, Task> execute, string context, Predicate<T?>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(execute);
        return new AsyncRelayCommand<T>(parameter => errors.GuardAsync(() => execute(parameter), context), canExecute ?? (_ => true));
    }

    /// <summary>A synchronous command whose failures go to the error path.</summary>
    public static IRelayCommand Command(this IErrorPresenter errors, Action execute, string context, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(execute);
        return new RelayCommand(() => errors.Guard(execute, context), canExecute ?? Always);
    }

    private static bool Always() => true;
}
