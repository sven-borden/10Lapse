using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TenLapse.Presentation;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Shell;
using TenLapse.Themes;

namespace TenLapse.Debugging;

/// <summary>
/// The motion log (#35): <c>--motion-log</c> writes every transition the app starts, as <see cref="MotionTokens.Plan"/>
/// decides it, to <c>motion.log</c> in the log folder (<c>--log-folder=</c>), or to <c>--motion-log=PATH</c>. Each line has
/// the milliseconds since the process started, the transition's name, what it does (moved, faded, waited or none), its
/// duration, easing and delay, and whether animations were on. It proves the timings without real input: run once as is
/// and once with <c>--reduce-motion</c>. Not built in Release.
/// </summary>
internal static class DebugMotion
{
    private static readonly Lock _gate = new();
    private static readonly long _start = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
    private static readonly string? _path = FindPath();

    // Held, or the collector may take the timers before they fire.
    private static readonly List<DispatcherQueueTimer> _timers = [];

    /// <summary>The motion log's file, or null when the switch is not given.</summary>
    public static string? Path => _path;

    /// <summary>
    /// The switches that start transitions without real input (UI thread, once the shell is built):
    /// <c>--motion-import=FOLDER</c> imports FOLDER's photos (through the same view model as Choose folder) after
    /// <c>--motion-import-delay=S</c> seconds (3 by default), so the arrival plays on the Empty screen;
    /// <c>--theme-flip-after=S[,S...]</c> crossfades the shell to the other theme at each of those seconds after launch, as a
    /// Windows mode change would, without touching Windows' setting; <c>--motion-infobar-after=S[,S...]</c> raises an InfoBar at
    /// each of those seconds after launch (to see it wait for a running transition).
    /// </summary>
    public static void Apply(ShellView shell, ImportViewModel import, IInfoBarService infoBars, IErrorSink log)
    {
        var args = Environment.GetCommandLineArgs();
        if (Value(args, "--motion-import=") is { Length: > 0 } folder)
        {
            var delay = Seconds(Value(args, "--motion-import-delay=")) ?? 3;
            After(shell.DispatcherQueue, delay, log, () => _ = import.ImportAsync([System.IO.Path.GetFullPath(folder)]));
        }

        // --motion-drop=FOLDER drops FOLDER on the window after --motion-drop-delay=S seconds (5 by default), as a real drop
        // would, so a project that has photos by then asks the drop question (#114: scripts/pseudo-check.ps1 captures it).
        if (Value(args, "--motion-drop=") is { Length: > 0 } dropped)
        {
            var delay = Seconds(Value(args, "--motion-drop-delay=")) ?? 5;
            After(shell.DispatcherQueue, delay, log, () => _ = import.DropAsync([System.IO.Path.GetFullPath(dropped)]));
        }

        if (Value(args, "--theme-flip-after=") is { Length: > 0 } flips)
        {
            foreach (var at in flips.Split(',').Select(Seconds).OfType<double>())
            {
                After(shell.DispatcherQueue, at, log, () => _ = shell.ThemeFade.SwitchAsync(shell.ThemeFade.Current == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark));
            }
        }

        if (Value(args, "--motion-infobar-after=") is { Length: > 0 } raises)
        {
            foreach (var at in raises.Split(',').Select(Seconds).OfType<double>())
            {
                After(shell.DispatcherQueue, at, log, () =>
                {
                    Note(string.Create(CultureInfo.InvariantCulture, $"InfoBar raised (--motion-infobar-after={at})"));
                    infoBars.Show(InfoBarSeverity.Neutral, string.Create(CultureInfo.InvariantCulture, $"Motion check: an InfoBar raised at {at} s."));
                });
            }
        }
    }

    private static void After(DispatcherQueue dispatcher, double seconds, IErrorSink log, Action action)
    {
        var timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(seconds);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => log.RunLogged(action, "A Debug motion switch failed.");
        _timers.Add(timer);
        timer.Start();
    }

    private static double? Seconds(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : null;

    private static string? Value(string[] args, string prefix) =>
        args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    /// <summary>Writes one started transition (any thread). A failure to write is dropped: this is a Debug aid.</summary>
    public static void Record(MotionStep step) => Note(step.ToLogLine());

    /// <summary>Writes one line about the motion that is not a transition (an InfoBar held for one). Any thread.</summary>
    public static void Note(string text)
    {
        if (_path is null)
        {
            return;
        }

        var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _start).TotalMilliseconds;
        var line = string.Create(CultureInfo.InvariantCulture, $"{elapsed,9:0} {text}{Environment.NewLine}");
        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? FindPath()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.FirstOrDefault(arg => arg.StartsWith("--motion-log=", StringComparison.OrdinalIgnoreCase)) is { } given)
        {
            return System.IO.Path.GetFullPath(given["--motion-log=".Length..]);
        }

        if (!args.Contains("--motion-log", StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var folder = DebugLaunch.LogFolder ?? AppEnvironment.LogFolder();
        Directory.CreateDirectory(folder);
        return System.IO.Path.Combine(folder, "motion.log");
    }
}
