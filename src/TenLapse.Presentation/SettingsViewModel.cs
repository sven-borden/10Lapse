using CommunityToolkit.Mvvm.Input;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// The Settings page: Copy diagnostics (#26) and Reset to defaults (#21); #31 adds rate and feedback, and the about
/// card with the version, each as a property or command here and a card on the page.
/// </summary>
public sealed class SettingsViewModel
{
    /// <param name="errors">The error path the commands go through.</param>
    /// <param name="diagnostics">Copies the diagnostics.</param>
    /// <param name="remembered">The remembered settings that <c>Reset to defaults</c> clears.</param>
    /// <param name="infoBars">Says that the reset is done.</param>
    public SettingsViewModel(IErrorPresenter errors, DiagnosticsCopier diagnostics, RememberedSettings remembered, IInfoBarService infoBars)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(remembered);
        ArgumentNullException.ThrowIfNull(infoBars);
        CopyDiagnosticsCommand = errors.AsyncCommand(diagnostics.CopyAsync, ProjectText.CopyDiagnosticsFailed);
        ResetToDefaultsCommand = errors.Command(
            () =>
            {
                // No question first: nothing is lost that one pick does not give back, and DESIGN.md asks for none.
                remembered.ResetToDefaults();
                infoBars.Show(InfoBarSeverity.Success, ProjectText.ResetDone);
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
}
