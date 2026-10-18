using Microsoft.UI.Xaml;

namespace TenLapse;

/// <summary>The application entry point: creates and shows the main window.</summary>
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
            : new MainWindow();
#else
        _window = new MainWindow();
#endif
        _window.Activate();
    }
}
