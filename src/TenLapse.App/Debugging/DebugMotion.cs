using System.Diagnostics;
using System.Globalization;
using TenLapse.Presentation.Motion;
using TenLapse.Services;

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

    /// <summary>The motion log's file, or null when the switch is not given.</summary>
    public static string? Path => _path;

    /// <summary>Writes one started transition (any thread). A failure to write is dropped: this is a Debug aid.</summary>
    public static void Record(MotionStep step)
    {
        if (_path is null)
        {
            return;
        }

        var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _start).TotalMilliseconds;
        var line = string.Create(CultureInfo.InvariantCulture, $"{elapsed,9:0} {step.ToLogLine()}{Environment.NewLine}");
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
