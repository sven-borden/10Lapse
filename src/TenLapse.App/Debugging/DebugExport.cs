using Microsoft.UI.Dispatching;
using TenLapse.Presentation;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Services;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only switches for the export (#24), since the Save dialog needs real input: <c>--export-to=PATH</c> answers the
/// Save dialog with PATH instead of showing it, and starts one export as soon as Export can run (with
/// <c>--sample-frames=N</c>, once all N samples are in). Not built in Release.
/// </summary>
internal static class DebugExport
{
    private static readonly string[] _args = Environment.GetCommandLineArgs();

    // Held, or the collector may take the timer before it fires.
    private static DispatcherQueueTimer? _timer;

    /// <summary>The path the export goes to without the Save dialog, or null.</summary>
    public static string? Target => Value("--export-to=");

    /// <summary>The Save dialog, or a stand-in that answers <see cref="Target"/> when the switch is given.</summary>
    public static ISaveVideoPicker Picker(ISaveVideoPicker picker) => Target is { Length: > 0 } target ? new FixedPicker(Path.GetFullPath(target)) : picker;

    /// <summary>
    /// With <c>--export-to</c>, starts one export once the project is ready for it: enough frames, and no import running
    /// (an import the Debug <c>--motion-import</c> started, say).
    /// </summary>
    public static void AutoStart(ExportViewModel export, ShellViewModel shell, IErrorSink log, Func<bool> importing)
    {
        if (Target is not { Length: > 0 })
        {
            return;
        }

        var wanted = int.TryParse(Value("--sample-frames="), out var count) ? count : 1;
        var timer = _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.IsRepeating = true;
        timer.Tick += (sender, _) => log.RunLogged(
            () =>
            {
                if (!export.CanStartExport || shell.CurrentProject.FrameCount < wanted || importing())
                {
                    return;
                }

                sender.Stop();
                if (export.ExportCommand.CanExecute(null))
                {
                    export.ExportCommand.Execute(null);
                }
            },
            "Starting the Debug export failed.");
        timer.Start();
    }

    private static string? Value(string prefix) =>
        _args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private sealed class FixedPicker(string path) : ISaveVideoPicker
    {
        public Task<string?> PickVideoAsync(string suggestedName, string? startFolder) => Task.FromResult<string?>(path);

        public void Discard(string path)
        {
            // This stand-in creates nothing.
        }
    }
}
