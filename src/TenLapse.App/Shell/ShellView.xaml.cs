using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation;
using TenLapse.Presentation.FirstLaunch;
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
    private readonly Presentation.Preview.PlayerViewModel _player;

    // What the drag over the window holds, counted once per drag: the photo files, null while not known (still
    // counting, or folders among them), 0 when nothing in it can be imported; _countDone once the count has finished
    // (or failed). DragEnter bubbles up from every child the pointer crosses, so the count is kept for the drag's
    // DataView (by reference) until the drop or until the pointer leaves the window.
    private DataPackageView? _countedView;
    private int? _draggedPhotos;
    private bool _countDone;
    private int _dragGeneration;
    private bool _activatedOnce;
    private readonly long _emptyRegionVisibilityToken;
    private readonly TimingPanelViewModel _timing;
    private readonly Presentation.Filmstrip.FilmstripViewModel _filmstrip;

    /// <param name="viewModel">The shell view model.</param>
    /// <param name="settings">The Settings page's view model.</param>
    /// <param name="import">Add photos, drag and drop and the import progress (#12).</param>
    /// <param name="output">The Output section of the inspector (#23).</param>
    /// <param name="infoBars">The InfoBars to float over the stage.</param>
    /// <param name="errors">Logs failures of the shell's dispatcher callbacks (log only, see <see cref="DispatcherQueueExtensions"/>).</param>
    /// <param name="presenter">The error path for the drop handler.</param>
    /// <param name="preview">The preview's player, images and cache (#19), for the stage's preview and transport.</param>
    internal ShellView(ShellViewModel viewModel, SettingsViewModel settings, ImportViewModel import, OutputPanelViewModel output, InfoBarQueue infoBars, IErrorSink errors, IErrorPresenter presenter, Preview.PreviewSession preview)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(presenter);
        ViewModel = viewModel;
        Import = import;
        _errors = errors;
        _presenter = presenter;
        InitializeComponent();
        SettingsPage.ViewModel = settings;
        OutputSection.ViewModel = output;
        _timing = new TimingPanelViewModel(viewModel, presenter);
        TimingSection.ViewModel = _timing;
#if DEBUG
        Debugging.DebugTiming.Apply(_timing);
#endif
        FirstLaunch.Attach(import, presenter, errors);
        _emptyRegionVisibilityToken = EmptyRegion.RegisterPropertyChangedCallback(VisibilityProperty, OnEmptyRegionVisibilityChanged);

        _infoBarShadow = FlyoutShadow.Attach(InfoBarShadowCaster);
        _infoBars = new InfoBarPresenter(infoBars, InfoBarLayer, ShellInfoBar, InfoBarDetails, InfoBarDetailList, errors);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Handled events too: a button or the page's ScrollViewer under the pointer may have handled the press.
        ShellRoot.AddHandler(PointerPressedEvent, new PointerEventHandler(OnShellPointerPressed), handledEventsToo: true);

        // The preview (#19): the picture, the transport row, and the transport keys for keys no control handled.
        ArgumentNullException.ThrowIfNull(preview);
        _player = preview.Player;
        PreviewSurface.Attach(preview);
        Transport.Attach(preview.Player, presenter);
        ShellRoot.KeyDown += OnShellKeyDown;

        // The filmstrip (#20): thumbnails, selection, removal and undo, reorder and gap markers. Its Delete and Ctrl+Z
        // also listen on ShellRoot, after the transport keys, and not on the Settings page.
        _filmstrip = new Presentation.Filmstrip.FilmstripViewModel(viewModel, preview.Player, presenter);
        Filmstrip.Attach(_filmstrip, preview, ShellRoot, () => !ViewModel.IsSettingsOpen);
#if DEBUG
        Debugging.DebugLaunch.ApplyFilmstrip(Filmstrip, preview);
#endif

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

    /// <summary>
    /// Dims the title texts while the window is inactive, and stops the first-launch timelapse (motion spec,
    /// Daybreak). At the first activation on the Empty screen, focus starts on the drop zone's Choose folder.
    /// </summary>
    public void SetWindowActive(bool active)
    {
        VisualStateManager.GoToState(this, active ? nameof(WindowActive) : nameof(WindowInactive), useTransitions: false);
        FirstLaunch.ViewModel.IsWindowActive = active;
        if (active && !_activatedOnce)
        {
            _activatedOnce = true;
            if (IsLoaded)
            {
                FocusPrimaryActionAtLaunch();
            }
            else
            {
                // The window activates before its content loads, and places its own initial focus as it loads.
                Loaded += OnFirstLoaded;
            }
        }
    }

    /// <summary>The window was minimized or hidden (false), or shows again: the preview pauses, and plays on (#19).</summary>
    public void SetWindowShown(bool shown) =>
        _presenter.Guard(() => _player.SetWindowShown(shown), ProjectText.PreviewPlayFailed);

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        FocusPrimaryActionAtLaunch();
    }

    // The Empty screen's primary action rather than Add photos, the first control in the Tab order, which the window
    // focuses. No focus ring until the keyboard is used (FirstLaunchView.FocusPrimaryAction), Enter runs it, and the
    // Tab order is unchanged. Queued, so it comes after the window's own initial focus.
    private void FocusPrimaryActionAtLaunch() =>
        DispatcherQueue.TryEnqueueGuarded(
            _errors,
            () =>
            {
                if (EmptyRegion.Visibility == Visibility.Visible)
                {
                    FirstLaunch.FocusPrimaryAction();
                }
            },
            ProjectText.FirstLaunchFocusFailed);

    /// <summary>
    /// Takes the theme again for what follows it by hand: the InfoBar shadow's colors. MainWindow calls it when
    /// Windows switches high contrast or its colors, which the elements' theme does not always report.
    /// </summary>
    public void RefreshTheme()
    {
        _infoBarShadow.Recolor();
        FirstLaunch.RefreshTheme();
    }

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

    /// <summary>
    /// Stops following the InfoBar queue, takes the InfoBar shadow down and stops a running import, whose batches
    /// could no longer reach the UI thread (the window closed).
    /// </summary>
    public void Detach()
    {
        ShellRoot.KeyDown -= OnShellKeyDown;
        PreviewSurface.Detach();
        Transport.Detach();
        if (Import.CancelCommand.CanExecute(null))
        {
            Import.CancelCommand.Execute(null);
        }

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        EmptyRegion.UnregisterPropertyChangedCallback(VisibilityProperty, _emptyRegionVisibilityToken);
        FirstLaunch.Detach();
        ShellRoot.RemoveHandler(PointerPressedEvent, new PointerEventHandler(OnShellPointerPressed));
        _infoBars.Detach();
        _infoBarShadow.Dispose();
        _timing.Dispose();
        Filmstrip.Detach();
        _filmstrip.Dispose();
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

    // The Empty state shows or hides the first-launch drop zone (state setters on EmptyRegion), which runs its
    // timelapse only while it is on screen.
    private void OnEmptyRegionVisibilityChanged(DependencyObject sender, DependencyProperty property) =>
        FirstLaunch.SetRegionVisible(EmptyRegion.Visibility == Visibility.Visible);

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
    // nothing to import (no folder, no photo file) is refused, so the cursor says so before the drop. While Settings is
    // open every drop is refused, with a caption that says how to add the photos.
    // Entering an element also voids a leave waiting for its grace (the pointer crossed from one element to another).
    private async void OnDragEnter(object sender, DragEventArgs e) => await ErrorPath.GuardAsync(
        _presenter,
        () =>
        {
            FirstLaunch.ViewModel.DragEntered();
            return CountDraggedAsync(e);
        },
        ProjectText.DragCountFailed);

    // DragLeave bubbles from the children too: only a pointer that has left the window ends this drag's count. A
    // leave inside the window is either the pointer crossing into another element (DragEnter and DragOver follow at
    // once) or the drag being cancelled with Escape (nothing follows), so the drop zone's drag-over look waits a
    // moment before it goes (FirstLaunchViewModel, remarks).
    private async void OnDragLeave(object sender, DragEventArgs e) => await ErrorPath.GuardAsync(_presenter, () => LeaveAsync(e), ProjectText.DragLeaveFailed);

    // The drop zone's drag-over look follows the cursor once the drag is counted: shown while the window would take
    // the drop and knows it holds something to import (DragVerdict).
    private void OnDragOver(object sender, DragEventArgs e) => FirstLaunch.ViewModel.DragOver(Accept(e).ShowsLook);

    // Sets what a drop would do (the cursor's caption) and returns the verdict on the drag.
    private DragVerdict Accept(DragEventArgs e)
    {
        var verdict = DragVerdict.Of(ViewModel.IsSettingsOpen, e.DataView.Contains(StandardDataFormats.StorageItems), _countDone, _draggedPhotos);
        if (verdict.SettingsOpen)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.DragUIOverride.Caption = ProjectText.DragCaptionSettingsOpen;
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = false;
            e.Handled = true;
        }
        else if (!verdict.Accepts)
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = ProjectText.DragCaption(_draggedPhotos);
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
            e.Handled = true;
        }

        return verdict;
    }

    private async void OnDrop(object sender, DragEventArgs e) => await ErrorPath.GuardAsync(_presenter, () => DropAsync(e), ProjectText.ImportDropFailed);

    // Never throws: a drag that cannot be counted keeps the generic caption, and the drop still works.
    private async Task CountDraggedAsync(DragEventArgs e)
    {
        var view = e.DataView;
        if (ReferenceEquals(view, _countedView))
        {
            // The same drag entering a child element: counted already (or being counted).
            return;
        }

        ForgetDrag();
        _countedView = view;
        var generation = _dragGeneration;
        if (!view.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            var items = await view.GetStorageItemsAsync();
            int? count = items.Count(item => item is IStorageFile && Import.IsPhotoFile(item.Path));
            if (items.Any(item => item is IStorageFolder))
            {
                count = null;
            }

            if (generation == _dragGeneration)
            {
                _draggedPhotos = count;
                _countDone = true;
            }
        }
        catch (Exception exception)
        {
            // Not known, then: the cursor keeps "Add photos", the look shows, and the drop finds out.
            if (generation == _dragGeneration)
            {
                _countDone = true;
            }

            _errors.Report(exception, ProjectText.DragCountFailed);
        }
    }

    private async Task LeaveAsync(DragEventArgs e)
    {
        var point = e.GetPosition(ShellRoot);
        if (point.X <= 0 || point.Y <= 0 || point.X >= ShellRoot.ActualWidth || point.Y >= ShellRoot.ActualHeight)
        {
            ForgetDrag();
            FirstLaunch.ViewModel.DragEnded();
            return;
        }

        var ticket = FirstLaunch.ViewModel.DragLeft();
        await Task.Delay(FirstLaunchViewModel.DragLeaveGrace);
        FirstLaunch.ViewModel.ConfirmLeave(ticket);
    }

    private async Task DropAsync(DragEventArgs e)
    {
        ForgetDrag();
        FirstLaunch.ViewModel.DragEnded();
        if (ViewModel.IsSettingsOpen || !e.DataView.Contains(StandardDataFormats.StorageItems))
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

    // The drag is over (dropped, or gone from the window): the next one is counted afresh, and a count still running
    // for this one is dropped.
    private void ForgetDrag()
    {
        _dragGeneration++;
        _countedView = null;
        _draggedPhotos = null;
        _countDone = false;
    }

    // The transport keys (#19) while editing, for keys no control used: Space, the arrows, Home and End. A key typed in a
    // text box stays there (TransportKeyboard).
    private void OnShellKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.ShowsEditing)
        {
            _presenter.Guard(() => Preview.TransportKeyboard.Handle(e, _player, XamlRoot), ProjectText.PreviewSeekFailed);
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
