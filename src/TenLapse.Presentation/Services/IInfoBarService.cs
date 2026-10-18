namespace TenLapse.Presentation.Services;

/// <summary>The kind of an InfoBar, which picks its fill and icon (DESIGN.md, Cards and InfoBars).</summary>
public enum InfoBarSeverity
{
    /// <summary>Card fill: something worth knowing, nothing went wrong ("Export cancelled").</summary>
    Neutral,

    /// <summary>Success fill.</summary>
    Success,

    /// <summary>Caution fill: something was left out or needs a look ("3 photos couldn't be read").</summary>
    Caution,

    /// <summary>Critical fill: an action failed.</summary>
    Critical,
}

/// <summary>
/// What one InfoBar says: one sentence of what happened and what to do, and at most one action. Never silent
/// (CLAUDE.md): every failure the user must know about becomes one of these.
/// </summary>
public sealed class InfoBarRequest
{
    /// <summary>Describes an InfoBar.</summary>
    /// <param name="severity">Its kind.</param>
    /// <param name="message">One sentence, sentence case, no exclamation mark.</param>
    /// <param name="actionLabel">The label of its one action ("Show list", "Try again"), or null for none.</param>
    /// <param name="action">
    /// Runs when the action is pressed, on the UI thread, after the InfoBar has been dismissed. A failure it
    /// throws goes to the error sink. Given exactly when <paramref name="actionLabel"/> is.
    /// </param>
    public InfoBarRequest(InfoBarSeverity severity, string message, string? actionLabel = null, Func<Task>? action = null)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown InfoBar severity.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (actionLabel is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(actionLabel);
        }

        if ((actionLabel is null) != (action is null))
        {
            throw new ArgumentException("An InfoBar action needs both a label and a callback, or neither.", nameof(action));
        }

        Severity = severity;
        Message = message;
        ActionLabel = actionLabel;
        Action = action;
    }

    /// <summary>The kind of the InfoBar.</summary>
    public InfoBarSeverity Severity { get; }

    /// <summary>The sentence it shows.</summary>
    public string Message { get; }

    /// <summary>The label of its action, or null when it has none.</summary>
    public string? ActionLabel { get; }

    /// <summary>The action's callback, or null when it has none.</summary>
    public Func<Task>? Action { get; }

    /// <summary>True when both read the same to the user: same severity, message and action label.</summary>
    public bool ReadsLike(InfoBarRequest other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Severity == other.Severity
            && string.Equals(Message, other.Message, StringComparison.Ordinal)
            && string.Equals(ActionLabel, other.ActionLabel, StringComparison.Ordinal);
    }
}

/// <summary>One shown (or waiting) InfoBar, as <see cref="IInfoBarService.Show"/> returns it; dismiss it with this.</summary>
public sealed class InfoBarTicket
{
    internal InfoBarTicket(InfoBarRequest request) => Request = request;

    /// <summary>What it says.</summary>
    public InfoBarRequest Request { get; }
}

/// <summary>
/// Floats InfoBars over the top of the stage (#28 draws them, #26 and the import and export issues raise them).
/// One is visible at a time; later ones wait in order and show when the visible one goes. The user dismisses
/// the visible one with its close button or its action. Both methods are safe on any thread.
/// </summary>
public interface IInfoBarService
{
    /// <summary>
    /// Shows <paramref name="request"/>, or queues it behind the visible InfoBar. When an InfoBar that reads the
    /// same (<see cref="InfoBarRequest.ReadsLike"/>) is visible or waiting, nothing is added and its ticket is
    /// returned, so a failure that repeats does not stack copies of itself.
    /// </summary>
    InfoBarTicket Show(InfoBarRequest request);

    /// <summary>
    /// Takes <paramref name="ticket"/> away: the visible one leaves and the next waiting one shows, a waiting
    /// one is dropped. A ticket already dismissed is ignored.
    /// </summary>
    void Dismiss(InfoBarTicket ticket);
}

/// <summary>Shorthands for <see cref="IInfoBarService"/>.</summary>
public static class InfoBarServiceExtensions
{
    /// <summary>Shows an InfoBar built from its parts (see <see cref="InfoBarRequest"/>).</summary>
    public static InfoBarTicket Show(
        this IInfoBarService service, InfoBarSeverity severity, string message, string? actionLabel = null, Func<Task>? action = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.Show(new InfoBarRequest(severity, message, actionLabel, action));
    }
}
