using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Shell;
using TenLapse.Themes;
using Windows.Graphics;

namespace TenLapse;

/// <summary>
/// The single application window: it hosts the <see cref="ShellView"/>, draws the shell's title bar into the
/// window frame on Mica, colors the system caption buttons from the tokens, and keeps the window's size within
/// the spec. The window title (taskbar, Alt+Tab) follows <see cref="ShellViewModel.Title"/>.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ShellView _shell;

    /// <param name="viewModel">The shell view model.</param>
    /// <param name="infoBars">The InfoBars to float over the stage; the composition root hands the same queue to whoever raises them.</param>
    /// <param name="errors">Takes the failures of InfoBar actions.</param>
    public MainWindow(ShellViewModel viewModel, InfoBarQueue infoBars, IErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        _shell = new ShellView(viewModel, infoBars, errors);
        Content = _shell;

        // Window is not a DependencyObject, so its Title cannot take a binding: follow the view model by hand.
        Title = ViewModel.Title;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_shell.TitleBar);
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
        }
        else
        {
            _shell.ShowMicaFallback();
        }

        SizeWindow();
        _shell.Loaded += OnLoaded;
        _shell.ActualThemeChanged += (_, _) => ColorCaptionButtons();
        _shell.SizeChanged += (_, _) => FitTitleBarInsets();
        Closed += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _shell.Detach();
        };
    }

    /// <summary>The window's view model.</summary>
    public ShellViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Title))
        {
            Title = ViewModel.Title;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _shell.Loaded -= OnLoaded;
        ColorCaptionButtons();
        FitTitleBarInsets();

        // Moving to a screen with another scale changes the insets and the minimum size in pixels.
        _shell.XamlRoot.Changed += (_, _) =>
        {
            FitTitleBarInsets();
            ApplyMinimumSize(Scale);
            ColorCaptionButtons();
            _shell.RefreshTheme();
        };
    }

    // Before the content is loaded there is no XamlRoot yet; the window's DPI says the same.
    private double Scale => _shell.XamlRoot?.RasterizationScale ?? GetDpiForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id)) / 96.0;

    // The system caption buttons sit on top of the title bar; keep the content out from under them.
    private void FitTitleBarInsets()
    {
        var scale = Scale;
        _shell.SetTitleBarInsets(AppWindow.TitleBar.LeftInset / scale, AppWindow.TitleBar.RightInset / scale);
    }

    // The caption buttons are drawn by the system; give them the token colors for the theme the shell shows.
    // High contrast keeps the system's own colors.
    private void ColorCaptionButtons()
    {
        var bar = AppWindow.TitleBar;
        if (ThemeTokens.IsHighContrast)
        {
            bar.ButtonBackgroundColor = null;
            bar.ButtonInactiveBackgroundColor = null;
            bar.ButtonForegroundColor = null;
            bar.ButtonHoverForegroundColor = null;
            bar.ButtonPressedForegroundColor = null;
            bar.ButtonInactiveForegroundColor = null;
            bar.ButtonHoverBackgroundColor = null;
            bar.ButtonPressedBackgroundColor = null;
            return;
        }

        var ink = ThemeTokens.Color("InkColor", _shell);
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = ink;
        bar.ButtonHoverForegroundColor = ink;
        bar.ButtonPressedForegroundColor = ThemeTokens.Color("InkSecondaryColor", _shell);
        bar.ButtonInactiveForegroundColor = ThemeTokens.Color("InkSecondaryColor", _shell);
        bar.ButtonHoverBackgroundColor = ThemeTokens.Color("SubtleFillHoverColor", _shell);
        bar.ButtonPressedBackgroundColor = ThemeTokens.Color("SubtleFillPressedColor", _shell);
    }

    private const int DwmwaExtendedFrameBounds = 9;

    // Opens at the prototype's 1280 x 800 (DIPs) centered in the work area when it fits, and never gets smaller
    // than 640 x 480 (DESIGN.md). Both are sizes of the visible window: AppWindow sizes include the invisible
    // resize borders, so those are added.
    private void SizeWindow()
    {
        var scale = Scale;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)Math.Round(ThemeTokens.Number("DefaultWindowWidth") * scale), area.Width);
        var height = Math.Min((int)Math.Round(ThemeTokens.Number("DefaultWindowHeight") * scale), area.Height);
        AppWindow.Resize(new SizeInt32(width, height));

        var borders = InvisibleBorders();
        AppWindow.MoveAndResize(new RectInt32(
            area.X + ((area.Width - width) / 2) - (borders.Width / 2),
            area.Y + ((area.Height - height) / 2),
            width + borders.Width,
            height + borders.Height));
        ApplyMinimumSize(scale);
    }

    // PreferredMinimumWidth and Height are outer sizes in physical pixels, so they follow the scale.
    private void ApplyMinimumSize(double scale)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            var borders = InvisibleBorders();
            presenter.PreferredMinimumWidth = (int)Math.Ceiling(ThemeTokens.Number("MinWindowWidth") * scale) + borders.Width;
            presenter.PreferredMinimumHeight = (int)Math.Ceiling(ThemeTokens.Number("MinWindowHeight") * scale) + borders.Height;
        }
    }

    // How much wider and taller the window is than what shows of it: the resize borders Windows 11 keeps
    // invisible (7 px left, right and bottom at 100%). Zero when DWM cannot say.
    private SizeInt32 InvisibleBorders()
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (!GetWindowRect(hwnd, out var outer)
            || DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var visible, Marshal.SizeOf<NativeRect>()) != 0)
        {
            return default;
        }

        return new SizeInt32(
            Math.Max(0, (outer.Right - outer.Left) - (visible.Right - visible.Left)),
            Math.Max(0, (outer.Bottom - outer.Top) - (visible.Bottom - visible.Top)));
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
