using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// The Windows notification when an export ends while the window is in the background (#24; motion spec, Wrap). On the
/// Windows App SDK's <see cref="AppNotificationManager"/>: registered once at start, as the SDK requires. Packaged (the
/// Store app), the manifest declares the notification activator and closing only unregisters this process. Unpackaged,
/// the SDK would register the exe itself under the user's registry (an AUMID and a COM class per exe path), so the
/// unpackaged Debug app registers only with <c>--notifications</c>, and then removes those entries as it closes
/// (<see cref="AppNotificationManager.UnregisterAll"/>). A click brings the window back (<see cref="Invoked"/>). The text
/// names the file and nothing else: no photo, no path.
/// </summary>
/// <remarks>Never throws: a PC or session where notifications cannot be registered or shown just goes without, and the log says so.</remarks>
internal sealed class ExportNotifications(IErrorSink log) : IDisposable
{
    private bool _registered;

    // Unpackaged (the Debug opt-in): the registry entries Register wrote go again on close.
    private bool _removeRegistration;

    /// <summary>The user clicked a notification while the app runs; raised on a background thread.</summary>
    public event EventHandler? Invoked;

    /// <summary>
    /// True when this process registers: always packaged; unpackaged only in Debug with <c>--notifications</c>. Release is
    /// always packaged (the Store build), so its path is unchanged.
    /// </summary>
    public static bool Wanted =>
        AppEnvironment.HasPackageIdentity
#if DEBUG
        || Debugging.DebugLaunch.Notifications;
#else
        ;
#endif

    /// <summary>Registers with Windows when <see cref="Wanted"/>. Call once, early, on the UI thread.</summary>
    public void Register() => log.RunLogged(
        () =>
        {
            if (!Wanted || !AppNotificationManager.IsSupported())
            {
                return;
            }

            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnInvoked;
            manager.Register();
            _registered = true;
            _removeRegistration = !AppEnvironment.HasPackageIdentity;
        },
        ExportText.NotificationFailed);

    /// <summary>Shows <paramref name="message"/> as a notification (nothing when not registered).</summary>
    public void Show(string message) => log.RunLogged(
        () =>
        {
            if (_registered)
            {
                AppNotificationManager.Default.Show(new AppNotificationBuilder().AddText(message).BuildNotification());
            }
        },
        ExportText.NotificationFailed);

    /// <summary>
    /// Unregisters, so a notification clicked after the app closed does not reach a process that is gone. Packaged that is
    /// <see cref="AppNotificationManager.Unregister"/> (the manifest's registration stays); unpackaged it is
    /// <see cref="AppNotificationManager.UnregisterAll"/>, which removes the registry entries <see cref="Register"/> wrote.
    /// </summary>
    public void Dispose() => log.RunLogged(
        () =>
        {
            if (_registered)
            {
                _registered = false;
                AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
                if (_removeRegistration)
                {
                    AppNotificationManager.Default.UnregisterAll();
                }
                else
                {
                    AppNotificationManager.Default.Unregister();
                }
            }
        },
        ExportText.NotificationFailed);

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        log.RunLogged(() => Invoked?.Invoke(this, EventArgs.Empty), ExportText.NotificationFailed);
}
