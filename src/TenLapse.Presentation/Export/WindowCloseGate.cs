using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Export;

/// <summary>
/// The main window's answer to a close (#24). Closing during an export asks first ("Stop the export and close?"): on yes
/// the export stops and its unfinished file goes (waited for at most <see cref="ExportViewModel.CloseLimit"/>), then the
/// window closes. A cancelled export still removing its file is waited for the same way. Otherwise the window closes at
/// once.
/// </summary>
/// <remarks>
/// A failure of the check itself never traps the user in a window that cannot close (#90): it is logged, a running
/// export is cancelled as a best effort, and the close goes ahead. UI thread only.
/// </remarks>
public sealed class WindowCloseGate
{
    private readonly Func<bool> _mayCloseNow;
    private readonly Func<Task<bool>> _prepareToClose;
    private readonly Action _cancelExport;
    private readonly Action _close;
    private readonly IErrorPresenter _errors;
    private bool _asked;
    private bool _approved;

    /// <param name="mayCloseNow">True when nothing runs that a close would cut off (<see cref="ExportViewModel.MayCloseNow"/>).</param>
    /// <param name="prepareToClose">Asks and stops the export; true when the window may close (<see cref="ExportViewModel.PrepareToCloseAsync"/>).</param>
    /// <param name="cancelExport">Stops a running export without asking (<see cref="ExportViewModel.CancelForClose"/>).</param>
    /// <param name="close">Closes the window again, once the export allows it.</param>
    /// <param name="errors">Logs a failed check, and shows a failed ask.</param>
    public WindowCloseGate(Func<bool> mayCloseNow, Func<Task<bool>> prepareToClose, Action cancelExport, Action close, IErrorPresenter errors)
    {
        ArgumentNullException.ThrowIfNull(mayCloseNow);
        ArgumentNullException.ThrowIfNull(prepareToClose);
        ArgumentNullException.ThrowIfNull(cancelExport);
        ArgumentNullException.ThrowIfNull(close);
        ArgumentNullException.ThrowIfNull(errors);
        _mayCloseNow = mayCloseNow;
        _prepareToClose = prepareToClose;
        _cancelExport = cancelExport;
        _close = close;
        _errors = errors;
    }

    /// <summary>Answers a close the window was asked for. Never throws.</summary>
    /// <returns>True to let it go ahead; false to hold it while the export is asked about and stopped.</returns>
    public bool Closing()
    {
        try
        {
            if (_approved || _mayCloseNow())
            {
                return true;
            }

            if (!_asked)
            {
                _asked = true;
                _ = _errors.GuardAsync(CloseAfterExportAsync, ExportText.CloseFailed);
            }

            return false;
        }
        catch (Exception exception)
        {
            _errors.Report(exception, ExportText.CloseCheckFailed);
            _errors.RunLogged(_cancelExport, ExportText.CloseStopFailed);
            return true;
        }
    }

    private async Task CloseAfterExportAsync()
    {
        try
        {
            if (await _prepareToClose())
            {
                _approved = true;
                _close();
            }
        }
        finally
        {
            _asked = false;
        }
    }
}
