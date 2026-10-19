using System.ComponentModel;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using TenLapse.Presentation;
using TenLapse.Presentation.FirstLaunch;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Themes;
using Windows.Foundation;
using DrawingColor = System.Drawing.Color;

namespace TenLapse.Shell;

/// <summary>
/// The first-launch drop zone (#29); see the comment at the top of FirstLaunchView.xaml. The shell owns the drag
/// handling and tells <see cref="ViewModel"/> about drags; this view shows the drag-over look and runs Daybreak.
/// </summary>
/// <remarks>
/// Daybreak runs at 30 fps on a dispatcher timer while <see cref="DaybreakPlayback"/> says Run: on screen, in the
/// active window, with animations on (polled at every update and every tick), and not in high contrast. Otherwise it
/// stands still or hides; with animations off it shows the golden-hour still frame. The frame shown is computed from
/// the playback time, so a slow tick skips frames instead of slowing the loop. A drawing failure hides Daybreak for
/// good and goes through the error path; the drop zone works on without it. A lost graphics device is not a failure:
/// the CanvasControls make a new one.
/// </remarks>
public sealed partial class FirstLaunchView : UserControl
{
    // 30 fps, the sequence's own rate: one tick per frame.
    private static readonly TimeSpan _tickInterval = TimeSpan.FromTicks(Core.Timing.FrameTiming.FrameDuration(0, Daybreak.Rate));

    private readonly DispatcherQueueTimer _timer;
    private readonly DaybreakPlayback _playback = new();
    private IErrorPresenter? _errors;
    private IErrorSink? _sink;
    private DaybreakPalette? _palette;
    private FlyoutShadow? _shadow;
    private Storyboard? _dragStoryboard;
    private FirstLaunchLayout _layout;
    private readonly long _titleTextToken;
    private double _width;
    private double _titleWidth;
    private bool _regionVisible = true;
    private int _frame = -1;
    private int _thumb = -1;
    private DaybreakMode _loggedMode = DaybreakMode.Hidden;

    /// <summary>A drop zone at rest; the shell calls <see cref="Attach"/> before it loads.</summary>
    public FirstLaunchView()
    {
        InitializeComponent();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = _tickInterval;
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _titleTextToken = TitleText.RegisterPropertyChangedCallback(TextBlock.TextProperty, OnTitleTextChanged);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The drop zone's state, for x:Bind and for the shell's drag handling.</summary>
    public FirstLaunchViewModel ViewModel { get; } = new();

    /// <summary>The import whose commands the buttons run (#12), for x:Bind.</summary>
    public ImportViewModel? Import { get; private set; }

    /// <summary>Hands over the import and the error path. Call once, before the view loads.</summary>
    public void Attach(ImportViewModel import, IErrorPresenter errors, IErrorSink sink)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(sink);
        Import = import;
        _errors = errors;
        _sink = sink;
    }

    /// <summary>The shell's Empty region was shown or hidden (an import arrived, Settings opened or closed).</summary>
    public void SetRegionVisible(bool visible)
    {
        _regionVisible = visible;
        UpdatePlayback();
    }

    /// <summary>
    /// Puts focus on Choose folder, the screen's primary action, without a focus ring until the keyboard is used.
    /// </summary>
    /// <remarks>
    /// FocusState.Pointer, not Programmatic: at launch WinUI draws the ring for programmatic focus as it does for
    /// its own initial focus, which is the ring the review saw on Add photos. With Pointer the ring appears with
    /// the first Tab or arrow key, Enter still runs the button, and Narrator announces it all the same.
    /// </remarks>
    public bool FocusPrimaryAction() => ChooseFolderButton.Focus(FocusState.Pointer);

    /// <summary>
    /// Takes the colors again for a theme change the elements may not see, and shows or hides Daybreak as high
    /// contrast goes on or off.
    /// </summary>
    public void RefreshTheme()
    {
        _shadow?.Recolor();
        StripCanvas.Invalidate();
        UpdatePlayback();
    }

    /// <summary>Stops Daybreak and lets go of the shadow and the Win2D controls (the window is closing).</summary>
    public void Detach()
    {
        _timer.Stop();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        TitleText.UnregisterPropertyChangedCallback(TextBlock.TextProperty, _titleTextToken);
        _shadow?.Dispose();
        _shadow = null;
        FrameCanvas.RemoveFromVisualTree();
        StripCanvas.RemoveFromVisualTree();
    }

    // The one decision (MotionTokens), polled at every update and every tick.
    private static bool AnimationsEnabled => MotionTokens.AnimationsEnabled;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _errors?.Guard(
            () =>
            {
                _palette ??= LoadPalette();
                _shadow ??= FlyoutShadow.Attach(DaybreakShadowCaster);
                ActualThemeChanged += OnActualThemeChanged;
                UpdatePlayback();
#if DEBUG
                if (Debugging.DebugLaunch.DragOver)
                {
                    ViewModel.DragOver(acceptable: true);
                }
#endif
            },
            ProjectText.DaybreakDrawFailed);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ActualThemeChanged -= OnActualThemeChanged;
        UpdatePlayback();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => StripCanvas.Invalidate();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FirstLaunchViewModel.IsDragOver):
                _errors?.Guard(ShowDragLook, ProjectText.DropZoneLookFailed);
                break;
            case nameof(FirstLaunchViewModel.IsWindowActive):
                UpdatePlayback();
                break;
        }
    }

    private void OnDropZoneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _width = e.NewSize.Width;
        _layout = FirstLaunchLayout.Fit(e.NewSize.Width, e.NewSize.Height);
        if (_layout.ShowsIllustration)
        {
            DaybreakPanel.Width = _layout.FrameWidth;
            FrameCanvas.Height = _layout.FrameHeight;
            DaybreakShadowCaster.Height = _layout.FrameHeight;
            StripCanvas.Height = _layout.ThumbHeight + (2 * DaybreakRenderer.RingWidth);
            StripCanvas.Invalidate();
        }

        UpdatePlayback();
    }

    // Every change Daybreak depends on funnels through here: the playback decides Run, Still or Hidden, and the view
    // lays the columns out, starts or stops the timer and shows the frame.
    private void UpdatePlayback()
    {
        var mode = _playback.Update(new DaybreakConditions(
            Loaded: IsLoaded && _palette is not null,
            RegionVisible: _regionVisible,
            FitsLayout: _layout.ShowsIllustration,
            AnimationsEnabled: AnimationsEnabled,
            WindowActive: ViewModel.IsWindowActive,
            HighContrast: ThemeTokens.IsHighContrast));

        Arrange(_playback.TakesPlace ? _layout : FirstLaunchLayout.TextsAlone);

        // The motion log hears when the loop starts, or stands at the still frame because animations are off.
        if (mode != _loggedMode && (mode == DaybreakMode.Run || (mode == DaybreakMode.Still && !AnimationsEnabled)))
        {
            MotionTokens.Plan(MotionCatalogue.Daybreak);
        }

        _loggedMode = mode;
        if (mode == DaybreakMode.Run)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }

        ShowFrame(_playback.Frame);
    }

    // Two columns with Daybreak, or the texts alone and centered.
    private void Arrange(FirstLaunchLayout layout)
    {
        Columns.Padding = new Thickness(layout.SidePadding, 0, layout.SidePadding, 0);
        Columns.ColumnSpacing = layout.ColumnGap;
        DaybreakColumnDefinition.Width = new GridLength(layout.IllustrationShare, GridUnitType.Star);
        TextColumn.HorizontalAlignment = layout.ShowsIllustration ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        DaybreakPanel.Visibility = layout.ShowsIllustration ? Visibility.Visible : Visibility.Collapsed;

        var titleWidth = Math.Min(TextColumn.MaxWidth, layout.TextColumnWidth(_width));
        if (titleWidth != _titleWidth)
        {
            _titleWidth = titleWidth;
            BalanceTitle();
        }
    }

    // The title at rest and the drop title differ in length: each is balanced as it arrives.
    private void OnTitleTextChanged(DependencyObject sender, DependencyProperty property) =>
        _errors?.Guard(BalanceTitle, ProjectText.DropZoneLookFailed);

    // The title breaks into even lines, as the canvas's text-wrap: balance does ("Drop a folder of photos / to make a
    // timelapse"), not greedily with "timelapse" alone on the second line.
    private void BalanceTitle()
    {
        if (_titleWidth <= 0)
        {
            return;
        }

        TitleText.MaxWidth = double.PositiveInfinity;
        TitleText.MaxWidth = TextBalance.Width(_titleWidth, MeasureTitle);
    }

    private double MeasureTitle(double width)
    {
        TitleText.Measure(new Size(width, double.PositiveInfinity));
        return TitleText.DesiredSize.Height;
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        try
        {
            if (!AnimationsEnabled)
            {
                UpdatePlayback();
                return;
            }

            ShowFrame(_playback.Frame);
        }
        catch (Exception exception)
        {
            Break(exception, ProjectText.DaybreakTickFailed);
        }
    }

    private void ShowFrame(int frame)
    {
        if (frame == _frame)
        {
            return;
        }

        _frame = frame;
        FrameCaption.Text = Daybreak.FrameCaption(frame);
        TimeCaption.Text = Daybreak.TimeCaption(frame);
        FrameCanvas.Invalidate();

        var thumb = Daybreak.SelectedThumb(frame);
        if (thumb != _thumb)
        {
            _thumb = thumb;
            StripCanvas.Invalidate();
        }
    }

    private void OnDrawFrame(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_playback.IsBroken || _palette is null || _frame < 0)
        {
            return;
        }

        try
        {
            DaybreakRenderer.DrawFrame(sender, args.DrawingSession, (float)sender.ActualWidth, (float)sender.ActualHeight, _palette.SkyAt(Daybreak.Phase(_frame)), _palette);
        }
        catch (Exception exception) when (!sender.Device.IsDeviceLost(exception.HResult))
        {
            Break(exception, ProjectText.DaybreakDrawFailed);
        }
    }

    private void OnDrawStrip(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_playback.IsBroken || _palette is null || _thumb < 0)
        {
            return;
        }

        try
        {
            var ring = ThemeTokens.Color("DeepAmberIndicatorColor", this);
            DaybreakRenderer.DrawStrip(
                sender,
                args.DrawingSession,
                DaybreakRenderer.RingWidth,
                (float)_layout.ThumbWidth,
                (float)_layout.ThumbHeight,
                (float)FirstLaunchLayout.ThumbGap,
                _thumb,
                ring,
                _palette);
        }
        catch (Exception exception) when (!sender.Device.IsDeviceLost(exception.HResult))
        {
            Break(exception, ProjectText.DaybreakDrawFailed);
        }
    }

    // Daybreak failed: stop it, hide it (after the draw that failed has returned) and say so once. A lost device is
    // not a failure: the draw handlers let it through, so the CanvasControl makes a new device and draws again
    // (a driver update, a GPU reset, sleep and resume, a remote desktop reconnect).
    private void Break(Exception exception, string context)
    {
        if (_playback.IsBroken)
        {
            return;
        }

        _playback.Break();
        _timer.Stop();
        if (_sink is { } sink)
        {
            DispatcherQueue.TryEnqueueGuarded(sink, UpdatePlayback, context);
        }

        _errors?.Show(exception, context);
    }

    // The drag-over look fades in or out over 167 ms (Enter) while Daybreak lifts 6 px over 250 ms; with animations
    // off the look takes the reduced fade and Daybreak stays put.
    private void ShowDragLook()
    {
        var over = ViewModel.IsDragOver;
        var look = MotionTokens.Plan(over ? MotionCatalogue.DragLookIn : MotionCatalogue.DragLookOut);
        var fromOpacity = DragOverLook.Opacity;
        var fromY = DaybreakLift.Y;
        var toOpacity = over ? 1.0 : 0.0;

        // With animations off Daybreak stays put; settling back from a lift always goes to rest.
        var lifts = over && look.AnimationsEnabled;
        var toY = lifts ? MotionSpec.DragLift : 0;

        _dragStoryboard?.Stop();
        _dragStoryboard = null;
        DragOverLook.Opacity = toOpacity;
        DaybreakLift.Y = toY;

        var storyboard = new Storyboard();
        if (look.Animates && fromOpacity != toOpacity)
        {
            storyboard.Children.Add(Ease(DragOverLook, "Opacity", fromOpacity, toOpacity, look));
        }

        if (fromY != toY && MotionTokens.Plan(MotionCatalogue.DragLift) is { Animates: true } lift)
        {
            storyboard.Children.Add(Ease(DaybreakLift, "Y", fromY, toY, lift));
        }

        if (storyboard.Children.Count > 0)
        {
            _dragStoryboard = storyboard;
            storyboard.Begin();
        }
    }

    private static DoubleAnimationUsingKeyFrames Ease(DependencyObject target, string property, double from, double to, MotionStep step)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = from });
        animation.KeyFrames.Add(MotionTokens.KeyFrame(to, step.Duration, step.Easing));
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    // The harbor's colors from the token dictionaries (Themes/Colors.xaml): the brand ramp and the Daybreak* tokens.
    private static DaybreakPalette LoadPalette() =>
        DaybreakPalette.FromTokens(key =>
        {
            var color = ThemeTokens.FixedColor(key);
            return DrawingColor.FromArgb(color.A, color.R, color.G, color.B);
        });
}
