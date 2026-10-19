using System.Diagnostics;
using TenLapse.Core.Diagnostics;

namespace TenLapse.Presentation;

/// <summary>
/// One log line as each start-up phase begins (#90), so the last line of a log that stops at start-up names the phase
/// that was running. Each line carries the time since the process started, the clock of WER's "seconds after start".
/// Start-up has two ends: the first frame on screen and the encoder probe's answer in the Output panel, whichever comes
/// last. The trace runs until both have been reached, then goes quiet, so a long session's log holds only these lines.
/// </summary>
/// <remarks>Thread-safe: the probe's phases come from the thread pool, the window's from the UI thread.</remarks>
public sealed class StartupTrace
{
    private readonly ILog _log;
    private readonly Func<TimeSpan> _sinceStart;
    private readonly Lock _gate = new();
    private bool _firstFrame;
    private bool _probeApplied;

    /// <param name="log">Where the lines go, at Info.</param>
    /// <param name="sinceStart">The time since the process started.</param>
    public StartupTrace(ILog log, Func<TimeSpan> sinceStart)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(sinceStart);
        _log = log;
        _sinceStart = sinceStart;
    }

    /// <summary>A trace timed from this process's start (from the moment it is made, if Windows cannot say when that was).</summary>
    public static StartupTrace ForThisProcess(ILog log)
    {
        var made = Stopwatch.GetTimestamp();
        var offset = TimeSpan.Zero;
        try
        {
            using var process = Process.GetCurrentProcess();
            offset = DateTime.Now - process.StartTime;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // No start time: count from now.
        }

        return new StartupTrace(log, () => offset + Stopwatch.GetElapsedTime(made));
    }

    /// <summary>True once the first frame and the probe's answer have both been reached: nothing more is written.</summary>
    public bool IsDone
    {
        get
        {
            lock (_gate)
            {
                return _firstFrame && _probeApplied;
            }
        }
    }

    /// <summary>Writes that <paramref name="phase"/> begins (a lower-case phrase, "creating the window").</summary>
    public void Phase(string phase)
    {
        lock (_gate)
        {
            Write(phase);
        }
    }

    /// <summary>Writes that the first frame is being drawn: one of start-up's two ends.</summary>
    public void FirstFrame(string phase) => Reach(phase, firstFrame: true);

    /// <summary>Writes that the encoder probe's answer is in the Output panel: the other end of start-up.</summary>
    public void ProbeApplied(string phase) => Reach(phase, firstFrame: false);

    private void Reach(string phase, bool firstFrame)
    {
        lock (_gate)
        {
            Write(phase);
            if (firstFrame)
            {
                _firstFrame = true;
            }
            else
            {
                _probeApplied = true;
            }
        }
    }

    private void Write(string phase)
    {
        if (!(_firstFrame && _probeApplied))
        {
            _log.Info(ProjectText.StartupPhase(phase, _sinceStart().TotalMilliseconds));
        }
    }
}
