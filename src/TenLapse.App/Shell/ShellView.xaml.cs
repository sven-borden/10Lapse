using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Themes;
using Windows.Foundation;

namespace TenLapse.Shell;

/// <summary>
/// The app shell: title bar, command bar, the Empty and Editing body with its named regions, and the floating
/// InfoBar layer (see the comment at the top of ShellView.xaml). <see cref="MainWindow"/> hosts it and draws the
/// title bar into the window frame.
/// </summary>
public sealed partial class ShellView : UserControl
{
    private readonly InfoBarPresenter _infoBars;
    private readonly FlyoutShadow _infoBarShadow;
    private readonly IErrorSink _errors;

    /// <param name="viewModel">The shell view model.</param>
    /// <param name="settings">The Settings page's view model.</param>
    /// <param name="infoBars">The InfoBars to float over the stage.</param>
    /// <param name="errors">Logs failures of the shell's dispatcher callbacks (log only, see <see cref="DispatcherQueueExtensions"/>).</param>
    public ShellView(ShellViewModel viewModel, SettingsViewModel settings, InfoBarQueue infoBars, IErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentNullException.ThrowIfNull(errors);
        ViewModel = viewModel;
        _errors = errors;
        InitializeComponent();
        SettingsPage.ViewModel = settings;

        _infoBarShadow = FlyoutShadow.Attach(InfoBarShadowCaster);
        _infoBars = new InfoBarPresenter(infoBars, InfoBarLayer, ShellInfoBar, InfoBarDetails, InfoBarDetailList, errors);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Handled events too: a button or the page's ScrollViewer under the pointer may have handled the press.
        ShellRoot.AddHandler(PointerPressedEvent, new PointerEventHandler(OnShellPointerPressed), handledEventsToo: true);

        // The shell leaves the tree only when the window closes; the shadow goes with it.
        Unloaded += (_, _) => _infoBarShadow.Dispose();
    }

    /// <summary>The shell's view model, for x:Bind.</summary>
    public ShellViewModel ViewModel { get; }

    /// <summary>The title bar and command bar band, which the window makes its drag region.</summary>
    public UIElement TitleBar => DragRegion;

    /// <summary>The command bar, whose layout passes move its controls' passthrough rectangles.</summary>
    public FrameworkElement CommandBar => CommandBarRegion;

    /// <summary>Keeps the title bar content clear of the system caption buttons (DIPs on each side).</summary>
    public void SetTitleBarInsets(double left, double right)
    {
        TitleBarLeftInset.Width = new GridLength(Math.Max(0, left));
        TitleBarRightInset.Width = new GridLength(Math.Max(0, right));
    }

    /// <summary>Paints the Mica token behind the shell where the system has no Mica backdrop.</summary>
    public void ShowMicaFallback() => MicaFallback.Visibility = Visibility.Visible;

    /// <summary>Dims the title texts while the window is inactive.</summary>
    public void SetWindowActive(bool active) =>
        VisualStateManager.GoToState(this, active ? nameof(WindowActive) : nameof(WindowInactive), useTransitions: false);

    /// <summary>
    /// Takes the theme again for what follows it by hand: the InfoBar shadow's colors. MainWindow calls it when
    /// Windows switches high contrast or its colors, which the elements' theme does not always report.
    /// </summary>
    public void RefreshTheme() => _infoBarShadow.Recolor();

    /// <summary>
    /// The bounds of every visible control in the command bar (DIPs, relative to the window's content), which
    /// take their own input inside the drag region.
    /// </summary>
    public IReadOnlyList<Rect> CommandBarControlBounds()
    {
        var bounds = new List<Rect>();
        Collect(CommandBarRegion, bounds);
        return bounds;
    }

    /// <summary>Stops following the InfoBar queue and takes the InfoBar shadow down (the window closed).</summary>
    public void Detach()
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ShellRoot.RemoveHandler(PointerPressedEvent, new PointerEventHandler(OnShellPointerPressed));
        _infoBars.Detach();
        _infoBarShadow.Dispose();
    }

    // Opening Settings puts focus on Back, leaving it puts focus back on the Settings button, so the keyboard and a
    // screen reader stay where the user is. After the state change has shown the button.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsSettingsOpen))
        {
            var target = ViewModel.IsSettingsOpen ? BackButton : SettingsButton;
            DispatcherQueue.TryEnqueueGuarded(_errors, () => target.Focus(FocusState.Programmatic), ProjectText.SettingsFocusFailed);
        }
    }

    // The mouse's back button (XButton1) leaves Settings, as Back, Escape and Alt+Left do.
    private void OnShellPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.IsSettingsOpen
            && e.GetCurrentPoint(ShellRoot).Properties.IsXButton1Pressed
            && ViewModel.CloseSettingsCommand.CanExecute(null))
        {
            ViewModel.CloseSettingsCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Walks the panels down to the controls, skipping what is collapsed; a control's own parts are its business.
    private static void Collect(DependencyObject node, List<Rect> bounds)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is not FrameworkElement { Visibility: Visibility.Visible } child)
            {
                continue;
            }

            if (child is not Control control)
            {
                Collect(child, bounds);
            }
            else if (control.ActualWidth > 0 && control.ActualHeight > 0)
            {
                bounds.Add(control.TransformToVisual(null).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight)));
            }
        }
    }
}
