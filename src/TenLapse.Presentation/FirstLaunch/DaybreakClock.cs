using System.Diagnostics;

namespace TenLapse.Presentation.FirstLaunch;

/// <summary>
/// The playback time of the Daybreak timelapse: it advances only while running, so a timelapse stopped in the
/// background picks up where it was. Read the frame with <see cref="Daybreak.FrameAt"/>(<see cref="Played"/>);
/// the frame is computed from the time, never counted per tick, so a late or skipped tick changes nothing.
/// </summary>
public sealed class DaybreakClock
{
    private readonly Func<TimeSpan> _now;
    private TimeSpan _played;
    private TimeSpan? _runningSince;

    /// <summary>A stopped clock at <paramref name="start"/>, on the system's monotonic clock unless <paramref name="now"/> is given (tests).</summary>
    public DaybreakClock(TimeSpan start, Func<TimeSpan>? now = null)
    {
        var origin = Stopwatch.GetTimestamp();
        _now = now ?? (() => Stopwatch.GetElapsedTime(origin));
        _played = start;
    }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsRunning => _runningSince is not null;

    /// <summary>The time played so far.</summary>
    public TimeSpan Played => _runningSince is { } since ? _played + (_now() - since) : _played;

    /// <summary>Runs the clock from where it stands. Nothing happens while it runs already.</summary>
    public void Start() => _runningSince ??= _now();

    /// <summary>Stops the clock where it stands. Nothing happens while it is stopped.</summary>
    public void Stop()
    {
        _played = Played;
        _runningSince = null;
    }
}
