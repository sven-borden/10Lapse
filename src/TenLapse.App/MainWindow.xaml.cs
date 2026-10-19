using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Shell;
using TenLapse.Themes;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace TenLapse;

/// <summary>
/// The single application window: it hosts the <see cref="ShellView"/>, draws the shell's title bar and command
/// bar into the window frame on Mica (both drag the window, the command bar's buttons excepted), colors the
/// system caption buttons from the tokens, and keeps the window's size within the spec. The window title
/// (taskbar, Alt+Tab) follows <see cref="ShellViewModel.Title"/>.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ShellView _shell;
    private readonly TintedMicaBackdrop? _backdrop;
    private readonly IErrorSink _errors;
    private readonly StartupTrace _startup;
    private readonly TaskbarProgress _taskbar;
    private readonly WindowExportFeedback _exportFeedback;
    private readonly WindowCloseGate _closeGate;

    // Taken on the UI thread: background-thread callbacks must not read Window.DispatcherQueue.
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _queue;

    // The window is the active one (for the export's notification, #24).
    private bool _active = true;

    // Kept as a field: its events stop when nothing holds it.
    private readonly UISettings _uiSettings = new();

    // The scale the minimum size was last computed for, and whether it waits for the window to be restored
    // (the invisible borders cannot be measured while maximized).
    private double _minimumScale;
    private bool _minimumPending;

    // The command bar's controls, carved out of the drag region (physical pixels), as last set.
    private RectInt32[] _passthrough = [];

    // Set when the window closes: the layout passes that still come must not touch the AppWindow.
    private bool _closed;

    /// <param name="viewModel">The shell view model.</param>
    /// <param name="settings">The Settings page's view model.</param>
    /// <param name="import">Add photos, drag and drop and the import progress (#12).</param>
    /// <param name="output">The Output section of the inspector (#23).</param>
    /// <param name="infoBars">The InfoBars to float over the stage; the composition root hands the same queue to whoever raises them.</param>
    /// <param name="errors">Logs failures of the window's dispatcher callbacks (log only, see <see cref="DispatcherQueueExtensions"/>).</param>
    /// <param name="presenter">The error path, for the shell's drop handler.</param>
    /// <param name="preview">The preview's player, images and cache (#19).</param>
    /// <param name="notifications">The export's Windows notifications, registered at start (#24).</param>
    /// <param name="exportServices">Builds the export's services (#24) around this window's taskbar button and notifications.</param>
    /// <param name="startup">Logs the start-up phases (#90).</param>
    internal MainWindow(ShellViewModel viewModel, SettingsViewModel settings, ImportViewModel import, OutputPanelViewModel output, InfoBarQueue infoBars, IErrorSink errors, IErrorPresenter presenter, Preview.PreviewSession preview, ExportNotifications notifications, Func<IExportWindow, ExportServices> exportServices, StartupTrace startup)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(exportServices);
        ArgumentNullException.ThrowIfNull(startup);
        ViewModel = viewModel;
        _errors = errors;
        _startup = startup;
        InitializeComponent();

        // The export's taskbar progress and its notification when the window is in the background (#24). A click on the
        // notification brings the window back.
        _taskbar = new TaskbarProgress(() => Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        _exportFeedback = new WindowExportFeedback(_taskbar, notifications, () => !_active || IsMinimized, errors);

        // Invoked comes on a background thread, where Window.DispatcherQueue must not be read: the queue is taken here.
        _queue = DispatcherQueue;
        notifications.Invoked += (_, _) => _queue.TryEnqueueGuarded(errors, Activate, ExportText.NotificationFailed);

        _startup.Phase("building the shell");
        _shell = new ShellView(viewModel, settings, import, output, infoBars, errors, presenter, preview, exportServices(_exportFeedback));
        Content = _shell;
        _closeGate = new WindowCloseGate(() => _shell.Export.MayCloseNow, _shell.Export.PrepareToCloseAsync, _shell.Export.CancelForClose, Close, presenter);
        _startup.Phase("setting the title bar and the backdrop");

        // Window is not a DependencyObject, so its Title cannot take a binding: follow the view model by hand.
        Title = ViewModel.Title;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        SetWindowIcon();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_shell.TitleBar);
        if (MicaController.IsSupported())
        {
            // XAML connects the backdrop inside this setter and again whenever the window's target comes back; a Mica
            // failure falls back to the flat color the shell paints where Mica is not supported (#90).
            _backdrop = new TintedMicaBackdrop(errors, _shell.SetMicaFallback);
            SystemBackdrop = _backdrop;
        }
        else
        {
            _shell.SetMicaFallback(true);
        }

        _startup.Phase("sizing the window");
        SizeWindow();
        _shell.Loaded += OnLoaded;
        _shell.ActualThemeChanged += (_, _) => ColorCaptionButtons();
        _shell.SizeChanged += (_, _) => FitTitleBarInsets();
        _shell.CommandBar.LayoutUpdated += (_, _) => UpdatePassthrough();
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnClosing;
        Activated += OnActivated;

        // High contrast on or off, and the system's light / dark switch, change the system colors, and may come
        // without a theme change the shell sees. (AccessibilitySettings.HighContrastChanged would say it more
        // directly, but subscribing to it throws CLASS_E_CLASSNOTAVAILABLE in a desktop app.)
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;

        Closed += (_, _) =>
        {
            _closed = true;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            AppWindow.Changed -= OnAppWindowChanged;
            AppWindow.Closing -= OnClosing;
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            _shell.Detach();
            _taskbar.Dispose();
        };
    }

    /// <summary>The window's view model.</summary>
    public ShellViewModel ViewModel { get; }

    /// <summary>Raised once, as the first frame is drawn: the encoder probe begins then (#90).</summary>
    internal event EventHandler? FirstFrameRendered;

    /// <summary>The export panel's view model (the rating prompt, #31, follows its starts and ends).</summary>
    internal ExportViewModel Export => _shell.Export;

    /// <summary>True while this is the active window and not minimized: the rating prompt only asks then (#31).</summary>
    internal bool IsInForeground => _active && !IsMinimized;

    /// <summary>The window's handle, for the Store's rating dialog (#31).</summary>
    internal nint Handle => Win32Interop.GetWindowFromWindowId(AppWindow.Id);

    // The taskbar and Alt+Tab icon, packaged or not (#37). Decorative: a missing or unreadable .ico is logged and the window
    // keeps the default icon; it must never stop the app from starting.
    private void SetWindowIcon()
    {
        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "TenLapse.ico"));
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException or COMException)
        {
            _errors.Report(exception, ProjectText.WindowIconFailed);
        }
    }

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
        _startup.Phase("shell loaded; fitting the title bar");
        ColorCaptionButtons();
        FitTitleBarInsets();
        UpdatePassthrough();

        // XamlRoot.Changed fires on every resize too; only a new scale changes the minimum size in pixels.
        _shell.XamlRoot.Changed += (_, _) =>
        {
            FitTitleBarInsets();
            if (Scale != _minimumScale)
            {
                ApplyMinimumSize(Scale);
            }
        };

        _startup.Phase("waiting for the first frame");
        CompositionTarget.Rendering += OnFirstRendering;
    }

    // The first frame is being drawn: one end of start-up (the encoder probe's answer is the other).
    private void OnFirstRendering(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnFirstRendering;
        _startup.FirstFrame("first frame rendering");
        FirstFrameRendered?.Invoke(this, EventArgs.Empty);
    }

    // Inactive windows dim their title (Windows 11): the title texts go to Ink Secondary. Back in the window, a failed
    // export's red taskbar button goes (the InfoBar says it now).
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        _active = args.WindowActivationState != WindowActivationState.Deactivated;
        _shell.SetWindowActive(_active);
        if (_active)
        {
            _exportFeedback.WindowActivated();
        }
    }

    private bool IsMinimized => AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    // Closing during an export asks first (#24, WindowCloseGate). AppWindow.Closing is raised by the window's message
    // handling, not by XAML, so the answer runs inside RunLogged (#90); Cancel is set only once the gate has answered,
    // so a failure lets the window close (the gate itself never throws, and stops a running export when its check fails).
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args) =>
        _errors.RunLogged(() => args.Cancel = !_closeGate.Closing(), ExportText.CloseCheckFailed);

    // A minimum size measured while maximized waits for the window to be restored. Minimized or hidden, the preview
    // pauses (and plays on when the window shows again): nothing should decode and draw unseen.
    // AppWindow.Changed is raised by the window's message handling, not by XAML: an exception from its handler reaches
    // no handler and leaves no trace (checked in #90), so the work runs inside RunLogged.
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) =>
        _errors.RunLogged(() => FollowAppWindow(sender, args.DidPresenterChange || args.DidSizeChange), ProjectText.WindowChangeFailed);

    private void FollowAppWindow(AppWindow window, bool presenterOrSizeChanged)
    {
        if (_minimumPending && presenterOrSizeChanged && IsRestored)
        {
            ApplyMinimumSize(Scale);
        }

        if (!_closed)
        {
            _shell.SetWindowShown(window.IsVisible && window.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized });
        }
    }

    // Raised on a background thread.
    private void OnColorValuesChanged(UISettings sender, object args) =>
        _queue.TryEnqueueGuarded(_errors, RefreshSystemColors, ProjectText.SystemColorsFailed);

    // What follows the theme by hand: the caption buttons, the shell's shadows and the Mica tint.
    private void RefreshSystemColors()
    {
        ColorCaptionButtons();
        _shell.RefreshTheme();
        _backdrop?.Refresh();
    }

    // Before the content is loaded there is no XamlRoot yet; the window's DPI says the same.
    private double Scale => _shell.XamlRoot?.RasterizationScale ?? GetDpiForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id)) / 96.0;

    private bool IsRestored => AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored };

    // The system caption buttons sit on top of the title bar; keep the content out from under them.
    private void FitTitleBarInsets()
    {
        var scale = Scale;
        _shell.SetTitleBarInsets(AppWindow.TitleBar.LeftInset / scale, AppWindow.TitleBar.RightInset / scale);
    }

    // The title bar and the command bar are one drag region (SetTitleBar); the command bar's controls take
    // their own input. Their rectangles follow every layout pass, and are only sent when they moved.
    private void UpdatePassthrough()
    {
        // A layout pass can still run while the window closes, when its AppWindow is already gone.
        if (_closed || _shell.XamlRoot is null || AppWindow is null)
        {
            return;
        }

        var scale = Scale;
        var rects = _shell.CommandBarControlBounds()
            .Select(bounds => new RectInt32(
                (int)Math.Round(bounds.X * scale),
                (int)Math.Round(bounds.Y * scale),
                (int)Math.Round(bounds.Width * scale),
                (int)Math.Round(bounds.Height * scale)))
            .ToArray();
        if (rects.SequenceEqual(_passthrough))
        {
            return;
        }

        _passthrough = rects;
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, rects);
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

    // PreferredMinimumWidth and Height are outer sizes in physical pixels, so they follow the scale. The
    // invisible borders are only measurable while the window is restored (maximized, the frame bounds say
    // 16 x 16), so otherwise this waits for OnAppWindowChanged.
    private void ApplyMinimumSize(double scale)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (presenter.State != OverlappedPresenterState.Restored)
        {
            _minimumPending = true;
            return;
        }

        var borders = InvisibleBorders();
        presenter.PreferredMinimumWidth = (int)Math.Ceiling(ThemeTokens.Number("MinWindowWidth") * scale) + borders.Width;
        presenter.PreferredMinimumHeight = (int)Math.Ceiling(ThemeTokens.Number("MinWindowHeight") * scale) + borders.Height;
        _minimumScale = scale;
        _minimumPending = false;
    }

    // How much wider and taller the window is than what shows of it: the resize borders Windows 11 keeps
    // invisible (7 px left, right and bottom at 100%). Zero when DWM cannot say. Restored windows only.
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
