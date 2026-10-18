using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Themes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

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
    private readonly IErrorPresenter _presenter;

    // What the drag over the window holds, counted once when it enters: the photo files, null while not known (still
    // counting, or folders among them), 0 when nothing in it can be imported.
    private int? _draggedPhotos;
    private int _dragGeneration;

    /// <param name="viewModel">The shell view model.</param>
    /// <param name="settings">The Settings page's view model.</param>
    /// <param name="import">Add photos, drag and drop and the import progress (#12).</param>
    /// <param name="infoBars">The InfoBars to float over the stage.</param>
    /// <param name="errors">Logs failures of the shell's dispatcher callbacks (log only, see <see cref="DispatcherQueueExtensions"/>).</param>
    /// <param name="presenter">The error path for the drop handler.</param>
    public ShellView(ShellViewModel viewModel, SettingsViewModel settings, ImportViewModel import, InfoBarQueue infoBars, IErrorSink errors, IErrorPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(presenter);
        ViewModel = viewModel;
        Import = import;
        _errors = errors;
        _presenter = presenter;
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

    /// <summary>The import's view model, for x:Bind.</summary>
    public ImportViewModel Import { get; }

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

    // Drag and drop (#12). A drag that enters is counted once in the background (the photo files it holds), so the
    // caption can say "Add 120 photos"; until then, and when it holds folders, it says "Add photos". A drag with
    // nothing to import (no folder, no photo file) is refused, so the cursor says so before the drop.
    private async void OnDragEnter(object sender, DragEventArgs e) => await ErrorPath.GuardAsync(_presenter, () => CountDraggedAsync(e), ProjectText.DragCountFailed);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) || _draggedPhotos == 0)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = ProjectText.DragCaption(_draggedPhotos);
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e) => await ErrorPath.GuardAsync(_presenter, () => DropAsync(e), ProjectText.ImportDropFailed);

    // Never throws: a drag that cannot be counted keeps the generic caption, and the drop still works.
    private async Task CountDraggedAsync(DragEventArgs e)
    {
        var generation = ++_dragGeneration;
        _draggedPhotos = null;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            int? count = items.Count(item => item is IStorageFile && Import.IsPhotoFile(item.Path));
            if (items.Any(item => item is IStorageFolder))
            {
                count = null;
            }

            if (generation == _dragGeneration)
            {
                _draggedPhotos = count;
            }
        }
        catch (Exception exception)
        {
            _errors.Report(exception, ProjectText.DragCountFailed);
        }
    }

    private async Task DropAsync(DragEventArgs e)
    {
        _dragGeneration++;
        _draggedPhotos = null;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        // The source keeps its data available until the deferral completes.
        IReadOnlyList<IStorageItem> items;
        var deferral = e.GetDeferral();
        try
        {
            items = await e.DataView.GetStorageItemsAsync();
        }
        finally
        {
            deferral.Complete();
        }

        // Items without a path (a phone's photos over MTP, files inside a zip) cannot be read from disk: left out,
        // and DropAsync says so when nothing is left.
        await Import.DropAsync([.. items.Select(item => item.Path).Where(path => !string.IsNullOrEmpty(path))]);
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
