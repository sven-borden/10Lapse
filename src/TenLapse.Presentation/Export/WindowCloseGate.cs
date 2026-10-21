using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Export;

/// <summary>
/// The main window's answer to a close (#24, #52). With unsaved changes it first asks "Save changes to …?" (#52): Save
/// saves and goes on, Don't save goes on, Cancel keeps the window. Then, during an export, it asks "Stop the export and
/// close?": on yes the export stops and its unfinished file goes (waited for at most <see cref="ExportViewModel.CloseLimit"/>),
/// then the window closes. A cancelled export still removing its file is waited for the same way. Otherwise the window
/// closes at once. The save question comes first because it loses nothing: an answer of No to the export question after
/// it leaves a saved project, not a stopped export.
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
    private readonly Func<bool> _hasUnsavedWork;
    private readonly Func<Task<bool>> _confirmUnsaved;
    private readonly Action _keepRecovery;
    private bool _asked;
    private bool _approved;

    /// <param name="mayCloseNow">True when nothing runs that a close would cut off (<see cref="ExportViewModel.MayCloseNow"/>).</param>
    /// <param name="prepareToClose">Asks and stops the export; true when the window may close (<see cref="ExportViewModel.PrepareToCloseAsync"/>).</param>
    /// <param name="cancelExport">Stops a running export without asking (<see cref="ExportViewModel.CancelForClose"/>).</param>
    /// <param name="close">Closes the window again, once the export allows it.</param>
    /// <param name="errors">Logs a failed check, and shows a failed ask.</param>
    /// <param name="hasUnsavedWork">True when the project has unsaved changes (#52, <see cref="ProjectFiles.ProjectFilesViewModel.HasUnsavedWork"/>); none when null.</param>
    /// <param name="confirmUnsaved">Asks to save them; true when the window may close (<see cref="ProjectFiles.ProjectFilesViewModel.ConfirmCloseAsync"/>).</param>
    /// <param name="keepRecovery">
    /// The save question itself failed (a dialog that cannot open): the window closes all the same, so the user is never
    /// trapped, and this keeps the unsaved work's recovery copy for the next launch to offer (<see cref="ProjectFiles.ProjectAutosave.KeepForNextLaunch"/>).
    /// </param>
    public WindowCloseGate(
        Func<bool> mayCloseNow,
        Func<Task<bool>> prepareToClose,
        Action cancelExport,
        Action close,
        IErrorPresenter errors,
        Func<bool>? hasUnsavedWork = null,
        Func<Task<bool>>? confirmUnsaved = null,
        Action? keepRecovery = null)
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
        _hasUnsavedWork = hasUnsavedWork ?? (() => false);
        _confirmUnsaved = confirmUnsaved ?? (() => Task.FromResult(true));
        _keepRecovery = keepRecovery ?? (() => { });
    }

    /// <summary>Answers a close the window was asked for. Never throws.</summary>
    /// <returns>True to let it go ahead; false to hold it while the user is asked and the export is stopped.</returns>
    public bool Closing()
    {
        try
        {
            if (_approved || (_mayCloseNow() && !_hasUnsavedWork()))
            {
                return true;
            }

            if (!_asked)
            {
                _asked = true;
                _ = _errors.GuardAsync(CloseAfterAskingAsync, ExportText.CloseFailed);
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

    private async Task CloseAfterAskingAsync()
    {
        try
        {
            if (!await SaveQuestionAllowsCloseAsync())
            {
                return;
            }

            if (_mayCloseNow() || await _prepareToClose())
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

    // The save question (#52), under its own log context. When the question itself fails, the close goes on (a broken
    // dialog must not trap the window, #90) and the recovery copy stays for the next launch: nothing is lost.
    private async Task<bool> SaveQuestionAllowsCloseAsync()
    {
        try
        {
            return !_hasUnsavedWork() || await _confirmUnsaved();
        }
        catch (Exception exception)
        {
            _errors.Report(exception, ProjectFiles.ProjectFileText.CloseQuestionFailedContext);
            _errors.RunLogged(_keepRecovery, ProjectFiles.ProjectFileText.CloseQuestionFailedContext);
            return true;
        }
    }
}
