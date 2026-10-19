using TenLapse.Presentation.Export;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// <see cref="IExportWindow"/> for the main window (#24): the taskbar button's progress and the notification when an
/// export ends while the window is in the background (not the active window, or minimized). A failure shows red on the
/// taskbar button until the user is back in the window (<see cref="WindowActivated"/>). Never throws: what the taskbar
/// or the notification cannot do is logged, and the panel and the InfoBars still say everything.
/// </summary>
/// <param name="taskbar">The window's taskbar button.</param>
/// <param name="notifications">The notifications, registered at start.</param>
/// <param name="inBackground">True while the window is not the one the user works in.</param>
/// <param name="log">Where a failure goes (log only).</param>
internal sealed class WindowExportFeedback(TaskbarProgress taskbar, ExportNotifications notifications, Func<bool> inBackground, IErrorSink log) : IExportWindow
{
    private bool _failedShown;

    /// <inheritdoc />
    public void ShowProgress(double? fraction) => log.RunLogged(
        () =>
        {
            _failedShown = false;
            if (fraction is { } value)
            {
                taskbar.SetState(TaskbarProgress.State.Normal);
                taskbar.SetValue(value);
            }
            else
            {
                taskbar.SetState(TaskbarProgress.State.Indeterminate);
            }
        },
        ExportText.TaskbarFailed);

    /// <inheritdoc />
    public void ClearProgress() => log.RunLogged(
        () =>
        {
            _failedShown = false;
            taskbar.SetState(TaskbarProgress.State.None);
        },
        ExportText.TaskbarFailed);

    /// <inheritdoc />
    public void ShowFailed() => log.RunLogged(
        () =>
        {
            taskbar.SetState(TaskbarProgress.State.Error);
            taskbar.SetValue(1);
            _failedShown = true;
            if (!inBackground())
            {
                // The user is looking: the InfoBar says it; the button needs no red.
                ClearProgress();
            }
        },
        ExportText.TaskbarFailed);

    /// <inheritdoc />
    public void NotifyIfInBackground(string message)
    {
        if (inBackground())
        {
            notifications.Show(message);
        }
    }

    /// <summary>The window is active again: a failure's red goes.</summary>
    public void WindowActivated()
    {
        if (_failedShown)
        {
            ClearProgress();
        }
    }
}
