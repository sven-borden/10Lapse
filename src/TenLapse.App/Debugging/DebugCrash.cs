using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using TenLapse.Presentation.Services;

namespace TenLapse.Debugging;

/// <summary>
/// Debug only (this folder is not built in Release): <c>--crash=KIND</c> makes one failure of each kind the error
/// path handles, once the window is up, so a screenshot and the log show what the user would get.
/// <list type="bullet">
/// <item><c>command</c>: a command throws; a Critical InfoBar with Copy diagnostics, the app keeps running.</item>
/// <item><c>task</c>: a task faults and nobody awaits it; the finalizer reports it, an InfoBar.</item>
/// <item><c>ui</c>: a callback on the UI thread throws; Application.UnhandledException marks it handled, an InfoBar.</item>
/// <item><c>unhandled</c>: a thread of its own throws; the process ends, after the log has the exception.</item>
/// </list>
/// </summary>
internal static partial class DebugLaunch
{
    /// <summary>Starts the failure <c>--crash=</c> asks for, after the window has been activated.</summary>
    public static void Crash(IErrorPresenter errors, DispatcherQueue dispatcher)
    {
        switch (Value("--crash=")?.ToLowerInvariant())
        {
            case "command":
                dispatcher.TryEnqueue(() => _ = errors
                    .AsyncCommand(() => throw new InvalidOperationException("Debug --crash=command: a command threw."), "Debug --crash=command.")
                    .ExecuteAsync(null));
                break;
            case "task":
                StartUnobservedFailure();
                _ = Task.Run(async () =>
                {
                    // Let the faulted task become garbage, then have the finalizer find it.
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                });
                break;
            case "ui":
                // The way an async void method's exception comes back: posted to the UI thread's synchronization context.
                dispatcher.TryEnqueue(() => SynchronizationContext.Current!.Post(
                    _ => throw new InvalidOperationException("Debug --crash=ui: an exception posted to the UI thread."), null));
                break;
            case "unhandled":
                new Thread(() => throw new InvalidOperationException("Debug --crash=unhandled: a background thread threw.")) { IsBackground = true }.Start();
                break;
        }
    }

    // Its own method, so no local keeps the faulted task alive for the finalizer.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartUnobservedFailure() =>
        _ = Task.Run(() => throw new InvalidOperationException("Debug --crash=task: a task nobody awaited faulted."));
}
