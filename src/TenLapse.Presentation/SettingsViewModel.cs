using CommunityToolkit.Mvvm.Input;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// The Settings page. For now one card, Copy diagnostics (#26); #21 adds the default settings and reset, #31 rate
/// and feedback, and the about card with the version, each as a property or command here and a card on the page.
/// </summary>
public sealed class SettingsViewModel
{
    /// <param name="errors">The error path the commands go through.</param>
    /// <param name="diagnostics">Copies the diagnostics.</param>
    public SettingsViewModel(IErrorPresenter errors, DiagnosticsCopier diagnostics)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(diagnostics);
        CopyDiagnosticsCommand = errors.AsyncCommand(diagnostics.CopyAsync, ProjectText.CopyDiagnosticsFailed);
    }

    /// <summary>Copy diagnostics: app, Windows, graphics and the last 200 log lines to the clipboard.</summary>
    public IAsyncRelayCommand CopyDiagnosticsCommand { get; }
}
