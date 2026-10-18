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
    /// Runs <paramref name="action"/> and writes any failure to the log through <see cref="IErrorSink.Report"/>,
    /// never to an InfoBar. Never throws. For callbacks the UI framework runs where an exception would end the
    /// process (a <c>DispatcherQueue</c> callback: the App's <c>TryEnqueueGuarded</c> wraps every one in this), and
    /// where a failure must not show an InfoBar because the InfoBar machinery itself may be what failed: showing
    /// one would run the broken code again, and again.
    /// </summary>
    /// <param name="sink">Where the failure is logged; its <see cref="IErrorSink.Report"/> only, whatever else it is.</param>
    /// <param name="action">The work.</param>
    /// <param name="context">What it does, for the log (see <see cref="ProjectText"/>).</param>
    public static void RunLogged(this IErrorSink sink, Action action, string context)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
        }
        catch (Exception exception)
        {
            try
            {
                sink.Report(exception, context);
            }
            catch (Exception)
            {
                // A sink must not throw; if one does, the callback still must not end the process.
            }
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
    /// Like <see cref="GuardAsync(IErrorPresenter, Func{Task}, string)"/> for work that <paramref name="cancellation"/>
    /// can stop: a cancellation counts as one (logged, nothing shown) only when that token was cancelled. An
    /// <see cref="OperationCanceledException"/> from anything else (a timeout, another component's token) is a
    /// failure the user did not ask for, and shows as an unexpected problem (<see cref="UnexpectedCancellationException"/>).
    /// Never throws.
    /// </summary>
    public static async Task GuardCancellableAsync(this IErrorPresenter errors, Func<Task> action, string context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            errors.Show(
                ErrorMessages.IsCancellation(exception) && !cancellation.IsCancellationRequested
                    ? new UnexpectedCancellationException(exception)
                    : exception,
                context);
        }
    }

    /// <summary>
    /// A cancellable async command (#22's export): <c>Cancel()</c> on it cancels the token, and that cancellation
    /// shows nothing. A cancellation the command's own token did not ask for is an error and shows.
    /// </summary>
    public static IAsyncRelayCommand AsyncCommand(this IErrorPresenter errors, Func<CancellationToken, Task> execute, string context, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(execute);
        return new AsyncRelayCommand(token => errors.GuardCancellableAsync(() => execute(token), context, token), canExecute ?? Always);
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

/// <summary>
/// Work stopped with an <see cref="OperationCanceledException"/> that its own token did not ask for (a timeout,
/// another component's token): not a cancellation by the user, so the error path shows it as an unexpected problem
/// and logs the original exception inside it.
/// </summary>
public sealed class UnexpectedCancellationException : Exception
{
    /// <summary>Wraps the cancellation that nobody asked for.</summary>
    public UnexpectedCancellationException(Exception cancellation)
        : base("The work was cancelled, but its own cancellation token was not: a timeout or another component stopped it.", cancellation)
    {
    }

    /// <summary>Creates one with no inner exception.</summary>
    public UnexpectedCancellationException()
        : base("The work was cancelled, but its own cancellation token was not.")
    {
    }

    /// <summary>Creates one with a message.</summary>
    public UnexpectedCancellationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates one with a message and the cancellation inside it.</summary>
    public UnexpectedCancellationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
