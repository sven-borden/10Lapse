using CommunityToolkit.Mvvm.Input;
using TenLapse.Presentation.Rating;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// The Settings page: Copy diagnostics (#26), Reset to defaults (#21), and Rate 10Lapse and Send feedback (#31), each a
/// command here and a card on the page.
/// </summary>
public sealed class SettingsViewModel
{
    /// <summary>The Store's review page for 10Lapse (product 9NBN2XH6S4D8), opened by <see cref="RateCommand"/>.</summary>
    public static readonly Uri StoreReviewUri = new("ms-windows-store://review/?ProductId=9NBN2XH6S4D8");

    /// <summary>Where <see cref="SendFeedbackCommand"/> opens the browser: a new issue in the public repository.</summary>
    public static readonly Uri FeedbackUri = new("https://github.com/sven-borden/10Lapse/issues/new");

    /// <param name="errors">The error path the commands go through.</param>
    /// <param name="diagnostics">Copies the diagnostics.</param>
    /// <param name="remembered">The remembered settings that <c>Reset to defaults</c> clears.</param>
    /// <param name="infoBars">Says that the reset is done, and that a link would not open.</param>
    /// <param name="launcher">Opens the Store and the browser (#31).</param>
    public SettingsViewModel(IErrorPresenter errors, DiagnosticsCopier diagnostics, RememberedSettings remembered, IInfoBarService infoBars, ILauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(remembered);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentNullException.ThrowIfNull(launcher);
        RateCommand = errors.AsyncCommand(() => OpenAsync(launcher, errors, infoBars, StoreReviewUri, RatingText.RateFailed, RatingText.RateCommandFailed), RatingText.RateCommandFailed);
        SendFeedbackCommand = errors.AsyncCommand(() => OpenAsync(launcher, errors, infoBars, FeedbackUri, RatingText.FeedbackFailed, RatingText.FeedbackCommandFailed), RatingText.FeedbackCommandFailed);
        CopyDiagnosticsCommand = errors.AsyncCommand(diagnostics.CopyAsync, ProjectText.CopyDiagnosticsFailed);
        ResetToDefaultsCommand = errors.Command(
            () =>
            {
                // No question first: nothing is lost that one pick does not give back, and DESIGN.md asks for none.
                // A failed write has shown its own InfoBar; "back to defaults" is only said when it is true.
                if (remembered.ResetToDefaults())
                {
                    infoBars.Show(InfoBarSeverity.Success, ProjectText.ResetDone);
                }
            },
            ProjectText.ResetSettingsFailed);
    }

    /// <summary>Copy diagnostics: app, Windows, graphics and the last 200 log lines to the clipboard.</summary>
    public IAsyncRelayCommand CopyDiagnosticsCommand { get; }

    /// <summary>
    /// Reset to defaults: the remembered settings and the open project's timing, frame shape and output go back to
    /// the defaults. The frames are not touched.
    /// </summary>
    public IRelayCommand ResetToDefaultsCommand { get; }

    /// <summary>Rate 10Lapse: opens the Store's review page for the app.</summary>
    public IAsyncRelayCommand RateCommand { get; }

    /// <summary>Send feedback: opens a new issue in the project's repository, in the browser.</summary>
    public IAsyncRelayCommand SendFeedbackCommand { get; }

    // A user-started hand-off to another app. A link that would not open ends in one InfoBar sentence that says what
    // to do instead; the exception behind it goes to the log.
    private static async Task OpenAsync(ILauncher launcher, IErrorPresenter errors, IInfoBarService infoBars, Uri uri, string failed, string context)
    {
        var opened = false;
        try
        {
            opened = await launcher.LaunchUriAsync(uri);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            errors.Report(exception, context);
        }

        if (!opened)
        {
            infoBars.Show(InfoBarSeverity.Caution, failed);
        }
    }
}
