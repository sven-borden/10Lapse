using System.ComponentModel;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.FirstLaunch;
using TenLapse.Presentation.Keyboard;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.ProjectFiles;
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
    private readonly FramePanelViewModel _frame;
    private readonly Presentation.Filmstrip.FilmstripViewModel _filmstrip;
    private readonly PanelSwap _inspectorSwap;
    private readonly ScreenMotion? _screens;
    private ShellScreen _screen;
    private readonly LiveProgress _importProgress = LiveProgress.ForImport();
    private readonly System.Diagnostics.Stopwatch _importClock = System.Diagnostics.Stopwatch.StartNew();
    private bool _importAnnounced;

    /// <param name="viewModel">The shell view model.</param>
    /// <param name="settings">The Settings page's view model.</param>
    /// <param name="import">Add photos, drag and drop and the import progress (#12).</param>
    /// <param name="output">The Output section of the inspector (#23).</param>
    /// <param name="infoBars">The InfoBars to float over the stage.</param>
    /// <param name="errors">Logs failures of the shell's dispatcher callbacks (log only, see <see cref="DispatcherQueueExtensions"/>).</param>
    /// <param name="presenter">The error path for the drop handler.</param>
    /// <param name="preview">The preview's player, images and cache (#19), for the stage's preview and transport.</param>
    /// <param name="exportServices">What the export (#24) works through: the exporter, the Save dialog, the window's taskbar and notification.</param>
    /// <param name="files">The File menu's Open, Save and Save as (#52).</param>
    /// <param name="recent">The first-launch screen's recent projects (#52).</param>
    internal ShellView(ShellViewModel viewModel, SettingsViewModel settings, ImportViewModel import, OutputPanelViewModel output, InfoBarQueue infoBars, IErrorSink errors, IErrorPresenter presenter, Preview.PreviewSession preview, ExportServices exportServices, ProjectFilesViewModel files, RecentProjectsViewModel recent)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(recent);
        ViewModel = viewModel;
        Import = import;
        Files = files;
        _errors = errors;
        Announcer.Errors = errors;
        _presenter = presenter;

        // Before InitializeComponent, so this handler runs before x:Bind's: the screen change's animations are in place
        // when the visual states switch the screens (ScreenMotion).
        ViewModel.PropertyChanged += OnScreenMayChange;
        Layout = new ShellLayout(
            ThemeTokens.Number("InspectorDockMinWindowWidth"),
            ThemeTokens.Number("NarrowPanelHeight"),
            ThemeTokens.Number("NarrowPanelMinHeight"),
            ThemeTokens.Number("NarrowPreviewMinHeight"));
        InitializeComponent();
        _screen = FocusTargets.ScreenOf(viewModel.IsSettingsOpen, viewModel.ShowsEmpty);
        SettingsPage.ViewModel = settings;
        OutputSection.ViewModel = output;
        _timing = new TimingPanelViewModel(viewModel, presenter);
        TimingSection.ViewModel = _timing;
        _frame = new FramePanelViewModel(viewModel, presenter, output);
        FrameSection.ViewModel = _frame;
#if DEBUG
        Debugging.DebugTiming.Apply(_timing);
        Debugging.DebugFrame.Apply(_frame);
#endif
        FirstLaunch.Attach(import, recent, presenter, errors);
        _emptyRegionVisibilityToken = EmptyRegion.RegisterPropertyChangedCallback(VisibilityProperty, OnEmptyRegionVisibilityChanged);

        _infoBarShadow = FlyoutShadow.Attach(InfoBarShadowCaster);
        _infoBars = new InfoBarPresenter(infoBars, InfoBarLayer, ShellInfoBar, InfoBarDetails, InfoBarDetailList, errors);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Import.PropertyChanged += OnImportPropertyChanged;

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

        // The export (#24): the Export button and Ctrl+E, the export panel and finished state that take the inspector's
        // place, the pill and the premiere over the preview. The filmstrip dims what is not written yet, and its
        // thumbnails pass through the film gate.
        ArgumentNullException.ThrowIfNull(exportServices);
        Export = new ExportViewModel(viewModel, output, preview.Player, _filmstrip, () => Import.IsImporting, exportServices, presenter);
        ExportSection.Attach(Export, Filmstrip.Thumbnails, errors);
        ExportOverlay.Attach(Export, preview.Player, presenter, errors, infoBars);
        _inspectorSwap = new PanelSwap([InspectorSections, ExportSection.Running, ExportSection.Finished], DispatcherQueue, errors);
        Export.PropertyChanged += OnExportPropertyChanged;

        // The narrow layout (#36): the window's width picks it, the body's and the stage's heights size the panel.
        ShellRoot.SizeChanged += OnShellSizeChanged;
        Body.SizeChanged += OnPanelHeightInputsChanged;
        FilmstripRegion.SizeChanged += OnPanelHeightInputsChanged;
        Layout.PropertyChanged += OnLayoutPropertyChanged;
#if DEBUG
        Debugging.DebugExport.AutoStart(Export, viewModel, errors, () => Import.IsImporting);
#endif

        // The screen changes and the arrival choreography (#35). Nothing animates on launch: they go live after the first
        // layout.
        _screens = new ScreenMotion(viewModel, EmptyRegion, [EditingSurface, StageRegion, InspectorPane], SettingsPage, DispatcherQueue, errors, Arrive);
#if DEBUG
        var followsWindows = !Gallery.GalleryLaunch.ThemeGiven;
#else
        var followsWindows = true;
#endif
        ThemeFade = new ThemeCrossfade(this, ShellRoot, ThemeSnapshot, DispatcherQueue, errors, followsWindows, PhotosOnScreen);
#if DEBUG
        Debugging.DebugMotion.Apply(this, import, infoBars, errors);
        Debugging.DebugFocusWalk.Apply(this, errors);
#endif
        Loaded += (_, _) => _errors.RunLogged(() => DispatcherQueue.TryEnqueueGuarded(_errors, _screens.GoLive, ProjectText.MotionFailed), ProjectText.MotionFailed);

        // The shell leaves the tree only when the window closes; the shadow goes with it.
        Unloaded += (_, _) => _infoBarShadow.Dispose();
    }

    /// <summary>The shell's view model, for x:Bind.</summary>
    public ShellViewModel ViewModel { get; }

    /// <summary>The import's view model, for x:Bind.</summary>
    public ImportViewModel Import { get; }

    /// <summary>The File menu's view model (#52), for x:Bind.</summary>
    public ProjectFilesViewModel Files { get; }

    /// <summary>The export's view model (#24), for x:Bind and for the window's close check.</summary>
    public ExportViewModel Export { get; }

    /// <summary>The theme crossfade (#35): the shell follows Windows' light or dark mode through it.</summary>
    internal ThemeCrossfade ThemeFade { get; }

    /// <summary>The responsive layout (#36): narrow or docked, the tab and the sections it shows, for x:Bind.</summary>
    public ShellLayout Layout { get; }

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

    /// <summary>
    /// Shows or hides the flat Mica token behind the shell: shown where the system has no Mica, and while the tinted
    /// Mica backdrop cannot connect; hidden again when it connects (#90).
    /// </summary>
    public void SetMicaFallback(bool visible) => MicaFallback.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

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
        ThemeFade.Follow();
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
        ShellRoot.SizeChanged -= OnShellSizeChanged;
        Body.SizeChanged -= OnPanelHeightInputsChanged;
        FilmstripRegion.SizeChanged -= OnPanelHeightInputsChanged;
        Layout.PropertyChanged -= OnLayoutPropertyChanged;
        Export.PropertyChanged -= OnExportPropertyChanged;
        ExportOverlay.Detach();
        ExportSection.Detach();
        Export.Dispose();
        PreviewSurface.Detach();
        Transport.Detach();
        if (Import.CancelCommand.CanExecute(null))
        {
            Import.CancelCommand.Execute(null);
        }

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Import.PropertyChanged -= OnImportPropertyChanged;
        ViewModel.PropertyChanged -= OnScreenMayChange;
        EmptyRegion.UnregisterPropertyChangedCallback(VisibilityProperty, _emptyRegionVisibilityToken);
        FirstLaunch.Detach();
        ShellRoot.RemoveHandler(PointerPressedEvent, new PointerEventHandler(OnShellPointerPressed));
        _infoBars.Detach();
        _infoBarShadow.Dispose();
        _timing.Dispose();
        _frame.Dispose();
        Filmstrip.Detach();
        _filmstrip.Dispose();
    }

    // The photos on screen, which the theme crossfade's picture leaves out: the preview's gate, and the premiere's video.
    private IEnumerable<PhotoArea> PhotosOnScreen()
    {
        if (PreviewSurface.PhotoOnScreen() is { } preview)
        {
            yield return preview;
        }

        if (ExportOverlay.PhotoOnScreen() is { } premiere)
        {
            yield return premiere;
        }
    }

    // Any of the three properties that pick the screen: the screen change's animations are chosen now (ScreenMotion).
    private void OnScreenMayChange(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.ShowsEmpty) or nameof(ShellViewModel.ShowsEditing) or nameof(ShellViewModel.IsSettingsOpen))
        {
            _errors.RunLogged(() => _screens?.Prepare(), ProjectText.MotionFailed);
        }
    }

    // The photos landed on the Empty screen (motion spec, Arrival): the first frame develops, the thumbnails file into the
    // strip, and the inspector's sections slide in from 16 px, 60 ms apart from 160 ms (in the narrow layout, the one its
    // tab shows). Each ends at rest, whatever interrupts it.
    private void Arrive()
    {
        PreviewSurface.Develop();
        Filmstrip.BeginArrival();
        var index = 0;
        foreach (var section in new UIElement[] { TimingSection, FrameSection, OutputSection }.Where(section => section.Visibility == Visibility.Visible))
        {
            Reveal(section, MotionTokens.Plan(MotionCatalogue.ArrivalInspector, index++));
        }
    }

    private static void Reveal(UIElement section, MotionStep step)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(section, true);
        var visual = ElementCompositionPreview.GetElementVisual(section);
        var compositor = visual.Compositor;
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.StopAnimation("Translation");
        visual.Opacity = 1f;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        if (!step.Animates)
        {
            return;
        }

        var easing = MotionTokens.CreateEasing(compositor, step.Easing);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, easing);
        Time(fade, step);
        visual.StartAnimation(nameof(Visual.Opacity), fade);
        if (step.Moves)
        {
            var slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0f, new Vector3(MotionSpec.SectionSlide, 0, 0));
            slide.InsertKeyFrame(1f, Vector3.Zero, easing);
            Time(slide, step);
            visual.StartAnimation("Translation", slide);
        }
    }

    private static void Time(KeyFrameAnimation animation, MotionStep step)
    {
        animation.Duration = step.Duration;
        animation.DelayTime = step.Delay;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
    }

    // Focus after a screen change (FocusTargets, #38), so the keyboard and a screen reader stay where the user goes on:
    // opening Settings puts it on Back and leaving on the Settings button; New project on Choose folder; the first photos
    // landing put it on Play, but only when the change took the focus away (a control still on screen keeps it). After
    // the state change has shown the target.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ShellViewModel.IsSettingsOpen) or nameof(ShellViewModel.ShowsEmpty) or nameof(ShellViewModel.ShowsEditing)))
        {
            return;
        }

        var screen = FocusTargets.ScreenOf(ViewModel.IsSettingsOpen, ViewModel.ShowsEmpty);
        if (screen == _screen)
        {
            return;
        }

        var from = _screen;
        _screen = screen;
        var before = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        DispatcherQueue.TryEnqueueGuarded(
            _errors,
            () =>
            {
                // Laid out first: a region that was collapsed has no templates yet, and a control without one takes no focus.
                UpdateLayout();
                MoveFocus(FocusTargets.AfterScreenChange(from, screen, focusLost: before is null || !IsOnScreen(before)));
            },
            ProjectText.ScreenFocusFailed);
    }

    // The import's progress to Narrator (#38): its first text, then the newest at most every 5 s (LiveProgress), and the
    // photos in the project once the progress goes, instead of every batch. Raised on Add photos, which shows on both
    // screens the import can run on.
    private void OnImportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        string? text = null;
        if (e.PropertyName is nameof(ImportViewModel.ProgressText) or nameof(ImportViewModel.IsImporting) && Import.IsImporting)
        {
            text = _importProgress.Next(Import.ProgressText, _importClock.Elapsed);
            _importAnnounced |= text is not null;
        }
        else if (e.PropertyName == nameof(ImportViewModel.IsImporting) && _importAnnounced)
        {
            _importProgress.Reset();
            _importAnnounced = false;
            var frames = ViewModel.CurrentProject.FrameCount;
            text = frames > 0 ? ProjectText.ImportDoneAnnouncement(frames) : null;
        }

        if (text is not null)
        {
            Announcer.Announce(AddPhotosButton, text, AutomationNotificationProcessing.MostRecent, "Import");
        }
    }

    // Puts the keyboard focus on the target; false when it cannot take it (not on screen, disabled).
    private bool MoveFocus(FocusTarget target) => target switch
    {
        FocusTarget.Back => BackButton.Focus(FocusState.Programmatic),
        FocusTarget.SettingsButton => SettingsButton.Focus(FocusState.Programmatic),
        FocusTarget.ChooseFolder => FirstLaunch.FocusChooseFolder(),
        FocusTarget.Play => Transport.FocusPlay(),
        FocusTarget.ExportTitle => ExportSection.FocusRunning(),
        FocusTarget.PlayVideo => ExportSection.FocusFinished(),
        FocusTarget.ExportButton => ExportButton.Focus(FocusState.Programmatic),
        _ => false,
    };

    // Still shown: neither it nor a parent is collapsed.
    private static bool IsOnScreen(DependencyObject node)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: Visibility.Collapsed })
            {
                return false;
            }
        }

        return true;
    }

    // The inspector column follows the export (#24): the sections, the export panel or the finished state, swapped with
    // the motion spec's Gate timing. Focus goes where the user continues: the export's title (a heading), Play, or
    // back to the Export button.
    private void OnExportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExportViewModel.State))
        {
            return;
        }

        // The canvas's exportOpacity: the Export button stays in the command bar, disabled and at 0.6, outside the
        // inspector; the change fades over 167 ms (a fade, the same with animations off).
        var opacity = Export.IsEditing ? 1 : DimmedExportOpacity;
        if (ExportButton.Opacity != opacity)
        {
            var dim = MotionTokens.Plan(MotionCatalogue.ExportButtonDim);
            ExportButton.OpacityTransition = dim.Animates ? new ScalarTransition { Duration = dim.Duration } : null;
            ExportButton.Opacity = opacity;
        }

        // The inspector's landmark is the export's while the export panel or the finished state shows.
        AutomationProperties.SetName(InspectorLandmark, Export.IsEditing ? ProjectText.InspectorRegionName : ExportText.ExportVideo);
        AutomationProperties.SetLandmarkType(InspectorLandmark, Export.IsEditing ? AutomationLandmarkType.Form : AutomationLandmarkType.Custom);
        AutomationProperties.SetLocalizedLandmarkType(InspectorLandmark, Export.IsEditing ? string.Empty : ProjectText.RegionLandmarkType);

        // Focus moves as the swap starts, not after its animation (FocusTargets.AfterExport).
        void FocusNext() => MoveFocus(FocusTargets.AfterExport(Export.State, ExportButton.Visibility == Visibility.Visible && ExportButton.IsEnabled));
        switch (Export.State)
        {
            case ExportPanelState.Exporting:
                _inspectorSwap.Show(ExportSection.Running, FocusNext);
                break;
            case ExportPanelState.Finished:
                _inspectorSwap.Show(ExportSection.Finished, FocusNext, ExportSection.ArriveFinished);
                break;
            default:
                ExportSection.ResetFinished();
                _inspectorSwap.Show(InspectorSections, FocusNext);
                break;
        }
    }

    // The narrow layout (#36). Below the dock width the inspector is a panel under the stage; the switch is instant (the
    // canvas does not animate it). Focus stays on the control that has it: folding keeps the section holding the focus on
    // its tab (a collapsed section would drop it), and unfolding from the tab row puts focus on the first control of the
    // section that tab showed. The preview, the transport and the filmstrip only get another size, which they follow.
    private void OnShellSizeChanged(object sender, SizeChangedEventArgs e) =>
        _presenter.Guard(() => ApplyLayout(e.NewSize.Width), ProjectText.LayoutSwitchFailed);

    private void ApplyLayout(double width)
    {
        var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var onTabs = focused is not null && IsWithin(focused, InspectorTabs);
        if (!Layout.Resize(width, SectionOf(focused)))
        {
            return;
        }

        var narrow = Layout.IsNarrow;
        VisualStateManager.GoToState(this, narrow ? nameof(Narrow) : nameof(Wide), useTransitions: false);
        OutputSection.SetNarrow(narrow);
        ExportSection.SetNarrow(narrow);
        ApplyPanelHeight();
        if (onTabs && !narrow)
        {
            var section = Layout.Tab switch
            {
                InspectorTab.Timing => (UIElement)TimingSection,
                InspectorTab.Frame => FrameSection,
                _ => OutputSection,
            };
            DispatcherQueue.TryEnqueueGuarded(
                _errors,
                () => (FocusManager.FindFirstFocusableElement(section) as Control)?.Focus(FocusState.Programmatic),
                ProjectText.LayoutFocusFailed);
        }
    }

    // The panel's height: 260 px, less when the window is short (ShellLayout.PanelHeight). Docked, the pane takes the body's height.
    // Skipped until the body has been measured: a height worked out from 0 would clamp to the minimum for one frame.
    private void ApplyPanelHeight()
    {
        if (Layout.IsNarrow && Body.ActualHeight <= 0)
        {
            return;
        }

        InspectorPane.Height = Layout.IsNarrow
            ? Layout.PanelHeight(Body.ActualHeight, StageChromeHeight())
            : double.NaN;
    }

    private void OnPanelHeightInputsChanged(object sender, SizeChangedEventArgs e) =>
        _presenter.Guard(ApplyPanelHeight, ProjectText.LayoutSwitchFailed);

    // What the stage needs besides the preview: its padding and the gaps between rows, the transport and the filmstrip
    // (which grows with the thumbnail size slider).
    private double StageChromeHeight() =>
        StageRegion.Padding.Top + StageRegion.Padding.Bottom + (2 * StageRegion.RowSpacing) + TransportRegion.ActualHeight + FilmstripRegion.ActualHeight;

    // A new tab starts at the top of its section.
    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellLayout.Tab))
        {
            InspectorScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
    }

    // The inspector section a control sits in, or null.
    private InspectorTab? SectionOf(DependencyObject? node)
    {
        if (node is null)
        {
            return null;
        }

        if (IsWithin(node, TimingSection))
        {
            return InspectorTab.Timing;
        }

        if (IsWithin(node, FrameSection))
        {
            return InspectorTab.Frame;
        }

        return IsWithin(node, OutputSection) ? InspectorTab.Output : null;
    }

    private static bool IsWithin(DependencyObject node, DependencyObject ancestor)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    // The Export button's opacity while the export panel or the finished state shows (the canvas's exportOpacity).
    private const double DimmedExportOpacity = 0.6;

    // Ctrl+I, Ctrl+Shift+I, Ctrl+N, Ctrl+E, Ctrl+O, Ctrl+S and Ctrl+Shift+S (ShellView.xaml): handled only where the shortcut works, so elsewhere the key
    // goes on as if there were no shortcut.
    private void OnShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var shift = sender.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift);
        if (ShellShortcuts.Resolve((char)sender.Key, control: true, shift, other: false) is { } shortcut)
        {
            args.Handled = RunShortcut(shortcut);
        }
    }

    /// <summary>
    /// Runs a shell shortcut as its key does (also Debug <c>--shortcut-after=</c>): the command of the control it stands
    /// for, only where <see cref="ShellShortcuts.IsAvailable"/> says it works. True when it applied here.
    /// </summary>
    internal bool RunShortcut(ShellShortcut shortcut)
    {
        var screen = new ShortcutScreen(ViewModel.IsSettingsOpen, IsDialogOpen(), ViewModel.ShowsProjectName, Export.ShowsExportButton, Files.CanSave);
        if (!ShellShortcuts.IsAvailable(shortcut, screen))
        {
            return false;
        }

        // As a click on the button would: an entry typed in the Timing box is taken first (the export uses it, a new
        // project does not inherit it).
        TimingSection.CommitPendingEntry();
        System.Windows.Input.ICommand command = shortcut switch
        {
            ShellShortcut.ChooseFolder => Import.ChooseFolderCommand,
            ShellShortcut.ChoosePhotos => Import.ChoosePhotosCommand,
            ShellShortcut.NewProject => ViewModel.NewProjectCommand,
            ShellShortcut.Open => Files.OpenCommand,
            ShellShortcut.Save => Files.SaveCommand,
            ShellShortcut.SaveAs => Files.SaveAsCommand,
            _ => Export.ExportCommand,
        };
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        return true;
    }

    private bool IsDialogOpen() =>
        XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is ContentDialog);

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
