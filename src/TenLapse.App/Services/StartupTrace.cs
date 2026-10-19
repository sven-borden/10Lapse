using System.Diagnostics;
using TenLapse.Core.Diagnostics;
using TenLapse.Presentation;

namespace TenLapse.Services;

/// <summary>
/// One log line as each start-up phase begins, from the remembered settings to the first frame on screen (#90), so
/// the last line of a log that stops at start-up names the phase that was running. Each line carries the time since
/// the process started. It goes quiet after the first frame, so a long session's log holds only these few lines.
/// </summary>
/// <remarks>UI thread only, as the start-up itself.</remarks>
internal sealed class StartupTrace(ILog log)
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private bool _done;

    /// <summary>Writes that <paramref name="phase"/> begins (a lower-case phrase, "creating the window").</summary>
    public void Phase(string phase)
    {
        if (!_done)
        {
            log.Info(ProjectText.StartupPhase(phase, Stopwatch.GetElapsedTime(_started).TotalMilliseconds));
        }
    }

    /// <summary>Writes the last phase line; the trace is silent from then on.</summary>
    public void Done(string phase)
    {
        Phase(phase);
        _done = true;
    }
}
