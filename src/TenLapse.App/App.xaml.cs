using Microsoft.UI.Xaml;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Services;

namespace TenLapse;

/// <summary>
/// The application entry point and composition root: builds the services and the shell view model by hand
/// and gives them to the main window. One window and a handful of services do not need a DI container.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
#if DEBUG
        Gallery.GalleryLaunch.ApplyTheme(this);
#endif
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
#if DEBUG
        // Debug only: --gallery opens the token gallery instead of the main window.
        _window = Gallery.GalleryLaunch.Requested
            ? new Gallery.GalleryWindow(Gallery.GalleryLaunch.Scroll)
            : CreateMainWindow();
#else
        _window = CreateMainWindow();
#endif
        _window.Activate();
    }

    private static MainWindow CreateMainWindow()
    {
        MainWindow? window = null;
        var errors = new TraceErrorSink();
        var dialogs = new ContentDialogService(() => window?.Content?.XamlRoot);

        // The one InfoBar queue: #26's error path and the import and export issues raise InfoBars through it
        // (as IInfoBarService); the window shows them.
        var infoBars = new InfoBarQueue();
        var shell = new ShellViewModel(dialogs, errors);
        window = new MainWindow(shell, infoBars, errors);
#if DEBUG
        Debugging.DebugLaunch.Apply(shell, infoBars);
#endif
        return window;
    }
}
