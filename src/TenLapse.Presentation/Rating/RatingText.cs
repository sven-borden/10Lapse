using System.Globalization;
using TenLapse.Core.Text;

namespace TenLapse.Presentation.Rating;

/// <summary>
/// Every word of the rating prompt and the Settings feedback card (#31): the card text and InfoBar sentences from
/// <c>Strings/RatingText.resx</c> (#39), and the log lines, which stay here in English.
/// </summary>
public static class RatingText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.RatingText", typeof(RatingText).Assembly);

    // ---- Settings card

    /// <summary>The Settings card's title.</summary>
    public static string CardTitle => _strings.Get();

    /// <summary>The Settings card's caption. {0} the Rate button's label, {1} the Send feedback button's label.</summary>
    public static string CardCaptionFormat => _strings.Get();

    /// <summary><see cref="CardCaptionFormat"/> with the buttons it names.</summary>
    public static string CardCaption => string.Format(CultureInfo.CurrentCulture, CardCaptionFormat, RateButton, FeedbackButton);

    /// <summary>The Rate button, and its accessible name.</summary>
    public static string RateButton => _strings.Get();

    /// <summary>The Send feedback button, and its accessible name.</summary>
    public static string FeedbackButton => _strings.Get();

    /// <summary>The Rate button's tooltip.</summary>
    public static string RateTip => _strings.Get();

    /// <summary>The Send feedback button's tooltip.</summary>
    public static string FeedbackTip => _strings.Get();

    /// <summary>The InfoBar when the Store would not open.</summary>
    public static string RateFailed => _strings.Get();

    /// <summary>The InfoBar when the browser would not open.</summary>
    public static string FeedbackFailed => _strings.Get();

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
