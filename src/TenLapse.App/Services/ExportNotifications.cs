using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// The Windows notification when an export ends while the window is in the background (#24; motion spec, Wrap). On the
/// Windows App SDK's <see cref="AppNotificationManager"/>: registered once at start, as the SDK requires. Unpackaged (Debug)
/// the SDK registers the app itself under the user's registry with the exe's name and icon; packaged, the manifest
/// declares the notification activator. A click brings the window back (<see cref="Invoked"/>). The text names the file
/// and nothing else: no photo, no path.
/// </summary>
/// <remarks>Never throws: a PC or session where notifications cannot be registered or shown just goes without, and the log says so.</remarks>
internal sealed class ExportNotifications(IErrorSink log) : IDisposable
{
    private bool _registered;

    /// <summary>The user clicked a notification while the app runs; raised on a background thread.</summary>
    public event EventHandler? Invoked;

    /// <summary>Registers with Windows. Call once, early, on the UI thread.</summary>
    public void Register() => log.RunLogged(
        () =>
        {
            if (!AppNotificationManager.IsSupported())
            {
                return;
            }

            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnInvoked;
            manager.Register();
            _registered = true;
        },
        ExportText.NotificationFailed);

    /// <summary>Shows <paramref name="message"/> as a notification.</summary>
    public void Show(string message) => log.RunLogged(
        () =>
        {
            if (_registered)
            {
                AppNotificationManager.Default.Show(new AppNotificationBuilder().AddText(message).BuildNotification());
            }
        },
        ExportText.NotificationFailed);

    /// <summary>Unregisters, so a notification clicked after the app closed does not reach a process that is gone.</summary>
    public void Dispose() => log.RunLogged(
        () =>
        {
            if (_registered)
            {
                _registered = false;
                AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
                AppNotificationManager.Default.Unregister();
            }
        },
        ExportText.NotificationFailed);

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        log.RunLogged(() => Invoked?.Invoke(this, EventArgs.Empty), ExportText.NotificationFailed);
}
