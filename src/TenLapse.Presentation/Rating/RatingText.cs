using System.Globalization;

namespace TenLapse.Presentation.Rating;

/// <summary>Every word of the rating prompt and the Settings feedback card (#31): card text, InfoBar sentences, log lines.</summary>
public static class RatingText
{
    // ---- Settings card

    /// <summary>The Settings card's title.</summary>
    public static string CardTitle => "Feedback";

    /// <summary>The Settings card's caption.</summary>
    public static string CardCaption => "Rate 10Lapse opens its page in the Microsoft Store. Send feedback opens GitHub in your browser. 10Lapse sends nothing itself.";

    /// <summary>The Rate button, and its accessible name.</summary>
    public static string RateButton => "Rate 10Lapse";

    /// <summary>The Send feedback button, and its accessible name.</summary>
    public static string FeedbackButton => "Send feedback";

    /// <summary>The Rate button's tooltip.</summary>
    public static string RateTip => "Opens the Microsoft Store";

    /// <summary>The Send feedback button's tooltip.</summary>
    public static string FeedbackTip => "Opens GitHub in your browser";

    /// <summary>The InfoBar when the Store would not open.</summary>
    public static string RateFailed => "The Microsoft Store didn't open. Open the Store yourself and search for 10Lapse to rate it.";

    /// <summary>The InfoBar when the browser would not open.</summary>
    public static string FeedbackFailed => "Your browser didn't open. Go to github.com/sven-borden/10Lapse/issues to send your feedback.";

    /// <summary>Error context: the Rate button failed.</summary>
    public static string RateCommandFailed => "Opening the Store's review page failed.";

    /// <summary>Error context: the Send feedback button failed.</summary>
    public static string FeedbackCommandFailed => "Opening the feedback page failed.";

    // ---- The prompt

    /// <summary>Error context (log only): the rating prompt could not count an export or schedule its question.</summary>
    public static string PromptFailed => "The rating prompt failed.";

    /// <summary>Error context (log only): the Store's rating request failed; the next successful export tries again.</summary>
    public static string RequestFailed => "Asking the Store to show its rating dialog failed.";

    /// <summary>Log line: an export succeeded and the prompt is not due yet.</summary>
    public static string NotYet(int exports) =>
        string.Create(CultureInfo.InvariantCulture, $"Rating prompt: {exports} successful export(s) so far; it asks after {RatingPrompt.AskAfterExports}.");

    /// <summary>Log line: the prompt was already shown in this version.</summary>
    public static string AlreadyAsked(string version) => $"Rating prompt: already shown in version {version}.";

    /// <summary>Log line: the prompt was due but the window was busy; it waits for the next successful export.</summary>
    public static string Postponed => "Rating prompt: due, but the window is in the background or busy; it waits for the next successful export.";

    /// <summary>Log line: the prompt was due, but a new export started first.</summary>
    public static string Superseded => "Rating prompt: due, but another export started; it waits for the next successful export.";

    /// <summary>Log line: the Store was asked.</summary>
    public static string Asked(string version) => $"Rating prompt: asked the Store to show its rating dialog (version {version}).";

    /// <summary>Log line: the Store could not be asked (no package identity, or it did not answer), so nothing was recorded.</summary>
    public static string Unavailable => "Rating prompt: the Store cannot be asked here, so nothing was recorded.";

    /// <summary>Log line, once per run: this unpackaged run has no Store identity, so the prompt does nothing.</summary>
    public static string UnpackagedNoOp => "Rating prompt: this run has no package identity (unpackaged), so it never asks the Store.";

    /// <summary>Log line (Debug switch): the faked prompt.</summary>
    public static string FakeAsked => "Rating prompt (Debug --fake-rating): would show the Store's rating dialog now.";
}
