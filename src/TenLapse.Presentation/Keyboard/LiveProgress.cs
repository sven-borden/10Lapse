namespace TenLapse.Presentation.Keyboard;

/// <summary>
/// What a screen reader hears of a progress text that changes many times a second (#38: the import's "Adding 1,240 of
/// 5,000 photos…"): the first text as the work starts, then the newest text at most once per <see cref="Interval"/>, never
/// the same text twice in a row, and nothing between. The view raises each answer as a UIA notification.
/// </summary>
/// <param name="interval">The least time between two announcements.</param>
public sealed class LiveProgress(TimeSpan interval)
{
    private TimeSpan? _saidAt;
    private string? _said;

    /// <summary>The least time between two announcements.</summary>
    public TimeSpan Interval { get; } = interval;

    /// <summary>For the import's progress: at most every 5 s (a few thousand photos take about two).</summary>
    public static LiveProgress ForImport() => new(TimeSpan.FromSeconds(5));

    /// <summary>The text to announce now, or null to stay quiet.</summary>
    /// <param name="text">The progress text now.</param>
    /// <param name="now">A monotonic time.</param>
    public string? Next(string text, TimeSpan now)
    {
        if (string.IsNullOrEmpty(text) || string.Equals(text, _said, StringComparison.Ordinal)
            || (_saidAt is { } at && now - at < Interval))
        {
            return null;
        }

        _saidAt = now;
        _said = text;
        return text;
    }

    /// <summary>The work ended: the next text is the first of a new run, said at once.</summary>
    public void Reset()
    {
        _saidAt = null;
        _said = null;
    }
}
