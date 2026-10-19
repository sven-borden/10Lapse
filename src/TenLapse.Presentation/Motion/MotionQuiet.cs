namespace TenLapse.Presentation.Motion;

/// <summary>
/// When the running transitions end, for what must not start during one (motion spec, InfoBar: "Never appears during a
/// transition"). A transition that <see cref="MotionTransition.HoldsInfoBars"/> pushes the end forward as it starts (its
/// delay and duration, so with animations off only its short fade); <see cref="Remaining"/> says how long is left. Any
/// thread.
/// </summary>
public sealed class MotionQuiet
{
    private readonly TimeProvider _time;
    private long _quietAt;

    /// <param name="time">The clock (its timestamps).</param>
    public MotionQuiet(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _quietAt = time.GetTimestamp();
    }

    /// <summary>How long until every running transition has ended; zero when none runs.</summary>
    public TimeSpan Remaining
    {
        get
        {
            var now = _time.GetTimestamp();
            var quietAt = Interlocked.Read(ref _quietAt);
            return quietAt > now ? _time.GetElapsedTime(now, quietAt) : TimeSpan.Zero;
        }
    }

    /// <summary>A transition starts now: when it holds InfoBars and animates, nothing waits for less than its end.</summary>
    public void Hold(MotionStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Transition.HoldsInfoBars && step.Animates)
        {
            Hold(step.Delay + step.Duration);
        }
    }

    /// <summary>Something runs for <paramref name="length"/> from now (a swap that runs one transition after another).</summary>
    public void Hold(TimeSpan length)
    {
        if (length <= TimeSpan.Zero)
        {
            return;
        }

        var end = _time.GetTimestamp() + (long)((Int128)length.Ticks * _time.TimestampFrequency / TimeSpan.TicksPerSecond);
        var seen = Interlocked.Read(ref _quietAt);
        while (end > seen)
        {
            var before = Interlocked.CompareExchange(ref _quietAt, end, seen);
            if (before == seen)
            {
                return;
            }

            seen = before;
        }
    }
}
