namespace TenLapse.Presentation.Services;

/// <summary>The kind of an InfoBar, which picks its fill and icon (DESIGN.md, Cards and InfoBars).</summary>
/// <remarks>The order is the order of importance: a full queue drops the least important waiting InfoBar first.</remarks>
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
/// One line of an InfoBar's detail list, such as a file that could not be read and why
/// ("IMG_4402.HEIC", "Damaged file: it ends early").
/// </summary>
public sealed record InfoBarDetail
{
    /// <summary>Describes a line.</summary>
    /// <param name="text">What the line is about, such as a file name.</param>
    /// <param name="note">Why, in a few words, shown beside it in Ink Secondary; null or blank for none.</param>
    public InfoBarDetail(string text, string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
        Note = string.IsNullOrWhiteSpace(note) ? null : note;
    }

    /// <summary>What the line is about.</summary>
    public string Text { get; }

    /// <summary>Why, or null.</summary>
    public string? Note { get; }
}

/// <summary>
/// What one InfoBar says: one sentence of what happened and what to do, and at most one button besides
/// Dismiss: an action, or the toggle of a detail list. Never silent (CLAUDE.md): every failure the user must
/// know about becomes one of these. Per-item failures (photos that could not be read) are gathered by the
/// caller into one InfoBar with one detail line each, never one InfoBar per item.
/// </summary>
public sealed class InfoBarRequest
{
    private static readonly InfoBarDetail[] _noDetails = [];

    /// <summary>Describes an InfoBar.</summary>
    /// <param name="severity">Its kind.</param>
    /// <param name="message">One sentence, sentence case, no exclamation mark.</param>
    /// <param name="actionLabel">The label of its one action ("Try again", "Open folder"), or null for none.</param>
    /// <param name="action">
    /// Runs when the action is pressed, on the UI thread, after the InfoBar has been dismissed (unless
    /// <paramref name="actionKeepsOpen"/>). A failure it throws is reported and shown as a Critical InfoBar.
    /// Given exactly when <paramref name="actionLabel"/> is.
    /// </param>
    /// <param name="actionKeepsOpen">
    /// True when pressing the action leaves the InfoBar open ("Open folder" next to a list the user is still
    /// reading); false (the default) when the action ends the InfoBar's job.
    /// </param>
    /// <param name="details">
    /// Lines the InfoBar can expand to show under its message, collapsed at first, behind a toggle labelled
    /// <see cref="ProjectText.InfoBarShowDetails"/> / <see cref="ProjectText.InfoBarHideDetails"/> that never
    /// dismisses. Null or empty for none. The toggle is the InfoBar's one button, so it cannot have an action too.
    /// </param>
    public InfoBarRequest(
        InfoBarSeverity severity,
        string message,
        string? actionLabel = null,
        Func<Task>? action = null,
        bool actionKeepsOpen = false,
        IEnumerable<InfoBarDetail>? details = null)
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

        if (actionKeepsOpen && action is null)
        {
            throw new ArgumentException("Only an InfoBar with an action can keep itself open when it is pressed.", nameof(actionKeepsOpen));
        }

        var lines = details?.ToArray() ?? _noDetails;
        if (lines.Any(line => line is null))
        {
            throw new ArgumentException("A detail line cannot be null.", nameof(details));
        }

        if (lines.Length > 0 && action is not null)
        {
            throw new ArgumentException("An InfoBar has one button besides Dismiss: an action or the toggle of its detail list, not both.", nameof(details));
        }

        Severity = severity;
        Message = message;
        ActionLabel = actionLabel;
        Action = action;
        ActionKeepsOpen = actionKeepsOpen;
        Details = lines;
    }

    /// <summary>The kind of the InfoBar.</summary>
    public InfoBarSeverity Severity { get; }

    /// <summary>The sentence it shows.</summary>
    public string Message { get; }

    /// <summary>The label of its action, or null when it has none.</summary>
    public string? ActionLabel { get; }

    /// <summary>The action's callback, or null when it has none.</summary>
    public Func<Task>? Action { get; }

    /// <summary>True when pressing the action leaves the InfoBar open.</summary>
    public bool ActionKeepsOpen { get; }

    /// <summary>The lines of its detail list, in order; empty when it has none.</summary>
    public IReadOnlyList<InfoBarDetail> Details { get; }

    /// <summary>True when it has a detail list (and so the toggle that shows it).</summary>
    public bool HasDetails => Details.Count > 0;

    /// <summary>True when both read the same to the user: same severity, message, action label and detail lines.</summary>
    public bool ReadsLike(InfoBarRequest other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Severity == other.Severity
            && string.Equals(Message, other.Message, StringComparison.Ordinal)
            && string.Equals(ActionLabel, other.ActionLabel, StringComparison.Ordinal)
            && Details.SequenceEqual(other.Details);
    }
}

/// <summary>One shown (or waiting) InfoBar, as <see cref="IInfoBarService.Show"/> returns it; dismiss it with this.</summary>
public sealed class InfoBarTicket
{
    internal InfoBarTicket(InfoBarRequest request) => Request = request;

    /// <summary>What it says.</summary>
    public InfoBarRequest Request { get; }

    // True while its action runs, so a second press cannot start it again (guarded by the queue's lock).
    internal bool ActionRunning { get; set; }
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
    /// returned, so a failure that repeats does not stack copies of itself. At most a few wait (see
    /// <see cref="InfoBarQueue.MaxWaiting"/>); past that the least important one is dropped.
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

    /// <summary>
    /// Shows an InfoBar whose message sums up a list the user can expand in it, such as
    /// "3 photos couldn't be read and were skipped." with one line per photo.
    /// </summary>
    public static InfoBarTicket ShowWithDetails(
        this IInfoBarService service, InfoBarSeverity severity, string message, IEnumerable<InfoBarDetail> details)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(details);
        return service.Show(new InfoBarRequest(severity, message, details: details));
    }
}
