using System.Globalization;
using TenLapse.Core.Text;

namespace TenLapse.Presentation.Rating;

/// <summary>
/// Every word of the rating question (#139) and the Settings feedback card (#31): the card text and InfoBar sentences from
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

    // ---- The question in the finished state (#139)

    /// <summary>The card's heading, announced as it shows: "Do you like 10Lapse?".</summary>
    public static string Question => _strings.Get();

    /// <summary>The Yes button, and its accessible name.</summary>
    public static string Yes => _strings.Get();

    /// <summary>The No button, and its accessible name.</summary>
    public static string No => _strings.Get();

    /// <summary>Error context: the Yes button failed.</summary>
    public static string YesFailed => "Answering Yes to the rating question failed.";

    /// <summary>Error context: the No button failed.</summary>
    public static string NoFailed => "Answering No to the rating question failed.";

    /// <summary>Error context (log only): the rating question could not count an export or show its card.</summary>
    public static string PromptFailed => "The rating question failed.";

    /// <summary>Error context (log only): the Store's rating dialog failed; the Store's review page opens instead.</summary>
    public static string RequestFailed => "Showing the Store's rating dialog failed; opening its review page instead.";

    /// <summary>Log line: an export succeeded and the question is not due yet.</summary>
    public static string NotYet(int exports) =>
        string.Create(CultureInfo.InvariantCulture, $"Rating question: {exports} successful export(s) so far; it shows after {RatingPrompt.AskAfterExports}.");

    /// <summary>Log line: the question was already shown in this version.</summary>
    public static string AlreadyAsked(string version) => $"Rating question: already shown in version {version}.";

    /// <summary>Log line: the question was due but the finished state was not up; the next successful export shows it.</summary>
    public static string NotFinished => "Rating question: due, but the finished state is not showing; the next successful export shows it.";

    /// <summary>Log line: the card shows, and the version is recorded.</summary>
    public static string Shown(string version) => $"Rating question: shown in the finished state (version {version}).";

    /// <summary>Log line: the finished state left with the card unanswered; it does not come back in this version.</summary>
    public static string LeftUnanswered => "Rating question: left unanswered.";

    /// <summary>Log line: Yes.</summary>
    public static string AnsweredYes => "Rating question: Yes.";

    /// <summary>Log line: No.</summary>
    public static string AnsweredNo => "Rating question: No.";

    /// <summary>Log line: the Store showed its rating dialog.</summary>
    public static string Asked(string version) => $"Rating question: the Store showed its rating dialog (version {version}).";

    /// <summary>Log line: the Store cannot show its dialog here (no package identity, or it answered with an error).</summary>
    public static string Unavailable => "Rating question: the Store cannot show its rating dialog here; opening its review page instead.";

    /// <summary>Log line, once per run: this unpackaged run has no Store identity, so the Store's dialog cannot show.</summary>
    public static string UnpackagedNoOp => "Rating question: this run has no package identity (unpackaged), so the Store's rating dialog cannot show.";

    /// <summary>Log line (Debug switch): the faked Store dialog.</summary>
    public static string FakeAsked => "Rating question (Debug --fake-rating): would show the Store's rating dialog now.";
}
