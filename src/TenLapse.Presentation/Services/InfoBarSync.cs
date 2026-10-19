namespace TenLapse.Presentation.Services;

/// <summary>What the InfoBar presenter does next to bring the screen in line with the <see cref="InfoBarQueue"/>.</summary>
public enum InfoBarMove
{
    /// <summary>Nothing: the screen shows the queue's current InfoBar, or one is leaving (its end syncs again).</summary>
    Stay,

    /// <summary>The InfoBar on screen is no longer the current one: it leaves first.</summary>
    Leave,

    /// <summary>The current InfoBar comes in.</summary>
    Enter,

    /// <summary>The current InfoBar waits for the running transition to end, then the presenter syncs again.</summary>
    Wait,
}

/// <summary>
/// The InfoBar presenter's next step (#35): one step at a time, each animation finishing before the next, and never an
/// entrance during a transition (motion spec, InfoBar). Only the entrance waits: a dismissal leaves at once, and the queue
/// keeps every InfoBar and its order meanwhile.
/// </summary>
public static class InfoBarSync
{
    /// <summary>The next step, from the queue's current ticket, the one on screen, whether it is leaving, and how long the running transitions have left.</summary>
    public static InfoBarMove Next(InfoBarTicket? current, InfoBarTicket? shown, bool leaving, TimeSpan untilQuiet)
    {
        if (leaving || ReferenceEquals(current, shown))
        {
            return InfoBarMove.Stay;
        }

        if (shown is not null)
        {
            return InfoBarMove.Leave;
        }

        if (current is null)
        {
            return InfoBarMove.Stay;
        }

        return untilQuiet > TimeSpan.Zero ? InfoBarMove.Wait : InfoBarMove.Enter;
    }
}
