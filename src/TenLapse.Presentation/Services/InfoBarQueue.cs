namespace TenLapse.Presentation.Services;

/// <summary>
/// The <see cref="IInfoBarService"/> the App uses: the visible InfoBar and the ones waiting behind it, in the
/// order they were shown. The App's InfoBar view follows <see cref="Current"/>. Thread-safe.
/// </summary>
public sealed class InfoBarQueue : IInfoBarService
{
    private readonly Lock _gate = new();

    // The first is the visible one; the rest wait in order.
    private readonly List<InfoBarTicket> _tickets = [];

    /// <summary>
    /// Raised after <see cref="Current"/> may have changed, on the thread that changed it and outside the lock.
    /// Handlers read <see cref="Current"/> again (on their own thread) rather than trust the order of events
    /// raised on different threads.
    /// </summary>
    public event EventHandler? CurrentChanged;

    /// <summary>The InfoBar to show now, or null when there is none.</summary>
    public InfoBarTicket? Current
    {
        get
        {
            lock (_gate)
            {
                return _tickets.Count > 0 ? _tickets[0] : null;
            }
        }
    }

    /// <summary>How many InfoBars wait behind the visible one.</summary>
    public int WaitingCount
    {
        get
        {
            lock (_gate)
            {
                return Math.Max(0, _tickets.Count - 1);
            }
        }
    }

    /// <inheritdoc />
    public InfoBarTicket Show(InfoBarRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        InfoBarTicket ticket;
        bool becameCurrent;
        lock (_gate)
        {
            if (_tickets.FirstOrDefault(existing => existing.Request.ReadsLike(request)) is { } same)
            {
                return same;
            }

            ticket = new InfoBarTicket(request);
            _tickets.Add(ticket);
            becameCurrent = _tickets.Count == 1;
        }

        if (becameCurrent)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }

        return ticket;
    }

    /// <inheritdoc />
    public void Dismiss(InfoBarTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        bool wasCurrent;
        lock (_gate)
        {
            var index = _tickets.IndexOf(ticket);
            if (index < 0)
            {
                return;
            }

            _tickets.RemoveAt(index);
            wasCurrent = index == 0;
        }

        if (wasCurrent)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
