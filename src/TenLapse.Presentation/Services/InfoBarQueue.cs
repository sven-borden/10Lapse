namespace TenLapse.Presentation.Services;

/// <summary>
/// The <see cref="IInfoBarService"/> the App uses: the visible InfoBar and the ones waiting behind it, in the
/// order they were shown. The App's InfoBar view follows <see cref="Current"/> and runs actions through
/// <see cref="RunActionAsync"/>. Thread-safe.
/// </summary>
/// <remarks>
/// The queue stays short on purpose: at most <see cref="MaxWaiting"/> InfoBars wait, and one that reads the
/// same as one visible or waiting is folded into it. Callers gather per-item failures into one InfoBar with a
/// detail list (<see cref="InfoBarServiceExtensions.ShowWithDetails"/>) instead of raising one per item. An InfoBar
/// that continues an action (<see cref="InfoBarRequest.ShowsNext"/>) shows at once, ahead of the others.
/// </remarks>
public sealed class InfoBarQueue : IInfoBarService
{
    /// <summary>
    /// How many InfoBars can wait behind the visible one. When one more arrives, the least important waiting one
    /// (lowest severity, oldest first) is dropped for it; when every waiting one matters more, the new one is
    /// dropped instead.
    /// </summary>
    public const int MaxWaiting = 5;

    private readonly Lock _gate = new();
    private readonly IErrorSink _errors;

    // The first is the visible one; the rest wait in order.
    private readonly List<InfoBarTicket> _tickets = [];

    /// <param name="errors">Takes the failures of InfoBar actions (they are also shown as a Critical InfoBar).</param>
    public InfoBarQueue(IErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        _errors = errors;
    }

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
            if (_tickets.Count - 1 >= MaxWaiting && !MakeRoomFor(request))
            {
                // Every waiting InfoBar matters more: this one is dropped, and its ticket acts as dismissed.
                return ticket;
            }

            var position = request.ShowsNext ? 0 : _tickets.Count;
            _tickets.Insert(position, ticket);
            becameCurrent = position == 0;
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

    /// <summary>
    /// Runs the action of <paramref name="ticket"/> when it is the visible InfoBar and its action is not already
    /// running; otherwise does nothing, so a second press (or a press during the exit) never runs it twice. The
    /// InfoBar is dismissed first unless <see cref="InfoBarRequest.ActionKeepsOpen"/>. The action runs on the
    /// calling thread (the UI thread). Never throws: a failure is reported to the error sink and shown as a
    /// Critical InfoBar saying <see cref="ProjectText.InfoBarActionFailed"/>.
    /// </summary>
    public async Task RunActionAsync(InfoBarTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (ticket.Request.Action is not { } action)
        {
            return;
        }

        lock (_gate)
        {
            if (_tickets.Count == 0 || !ReferenceEquals(_tickets[0], ticket) || ticket.ActionRunning)
            {
                return;
            }

            ticket.ActionRunning = true;
        }

        try
        {
            if (!ticket.Request.ActionKeepsOpen)
            {
                Dismiss(ticket);
            }

            await action();
        }
        catch (Exception exception)
        {
            _errors.Report(exception, ProjectText.InfoBarActionFailed);
            Show(new InfoBarRequest(InfoBarSeverity.Critical, ProjectText.InfoBarActionFailed));
        }
        finally
        {
            lock (_gate)
            {
                ticket.ActionRunning = false;
            }
        }
    }

    // The queue is full: drop the least important waiting InfoBar (lowest severity, oldest first) when it does
    // not matter more than the new one. Called under the lock.
    private bool MakeRoomFor(InfoBarRequest request)
    {
        var drop = 1;
        for (var i = 2; i < _tickets.Count; i++)
        {
            if (_tickets[i].Request.Severity < _tickets[drop].Request.Severity)
            {
                drop = i;
            }
        }

        if (_tickets[drop].Request.Severity > request.Severity)
        {
            return false;
        }

        _tickets.RemoveAt(drop);
        return true;
    }
}
