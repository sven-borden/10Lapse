using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Media.Preview;
using TenLapse.Presentation;
using TenLapse.Presentation.Filmstrip;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Services;
using TenLapse.Preview;
using TenLapse.Services;
using TenLapse.Themes;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Frame = TenLapse.Core.Projects.Frame;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace TenLapse.Filmstrip;

/// <summary>
/// The filmstrip (#20): the view of <see cref="FilmstripViewModel"/>. It realizes thumbnails as the repeater asks, gives
/// them pictures from a <see cref="ThumbnailCache{TImage}"/> (asking it for the realized ones, nearest the middle of the
/// view first), turns clicks, drags and keys into the view model's actions, keeps the playhead in sight, and tells
/// Narrator what an edit did.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pointer.</b> The rules are <see cref="FilmstripPointer"/>'s (tested in Presentation); this view feeds it positions
/// and draws. A press on a thumbnail focuses it; a release without moving is a click (with the modifiers held at the
/// press). Moving 6 DIPs with the button down starts a drag, which pauses the video and takes the selection (or the
/// pressed frame, when it is not selected): an Ink bar shows the seam the frames will land in, the strip scrolls when the
/// pointer nears an end, and the release moves them there; Escape or a lost capture cancels, and the pointer is let go.
/// Touch scrolls the strip instead of dragging, and a tap is a click. Thumbnails sit on a fixed stride, so hits are
/// arithmetic. A right-click or the context menu pauses the video first.
/// </para>
/// <para>
/// <b>Keys.</b> Keys pressed on a thumbnail go to <see cref="FilmstripKeys"/> first (arrows select, Alt+arrows move, and
/// so on); what the strip does not take (plain Space) goes on to the transport. Delete and Ctrl+Z also work with nothing
/// focused (after a click on the stage), through a handler on the shell's root, and never behind another focused control.
/// </para>
/// <para>
/// <b>Playhead.</b> When the playhead moves to a thumbnail out of view, the strip scrolls to it, as long as the user had
/// not scrolled the previous playhead out of view: while playing it pages so the playhead runs from a quarter of the way
/// in; paused it centres it, animated only when Windows animations are on.
/// </para>
/// </remarks>
public sealed partial class FilmstripView : UserControl
{
    // DIPs: the repeater's spacing and its inset in the scroller, as FilmstripView.xaml sets them.
    private const double Seam = 4;
    private const double Inset = 6;
    private const double AutoScrollEdge = 40;
    private const double AutoScrollSpeed = 24;

    private readonly Dictionary<Guid, FilmstripThumb> _realized = [];
    private readonly ConcurrentQueue<Guid> _arrived = new();
    private DispatcherQueue? _dispatcher;
    private ThumbnailCache<SoftwareBitmap>? _thumbnails;
    private PreviewSession? _preview;
    private IErrorPresenter? _errors;
    private IErrorSink? _log;
    private UIElement? _keyRoot;
    private Func<bool>? _canEdit;
    private DispatcherQueueTimer? _gapTimer;
    private DispatcherQueueTimer? _recheckTimer;
    private int _flushQueued;
    private bool _wantQueued;
    private int _lastPlayhead = -1;
    private XamlRoot? _scaleRoot;
    private double _scale;

    // The arrival cascade: when it began (Stopwatch ticks, 0 when none runs) and the first index it showed.
    private long _arrivalStart;
    private int _arrivalFirst = int.MaxValue;
    private readonly HashSet<Guid> _cascaded = [];
    private readonly HashSet<FilmstripThumb> _animated = [];

    // A press that may become a click or a drag (the rules are FilmstripPointer's, tested in Presentation).
    private FilmstripPointer? _pointer;
    private bool _autoScrolling;
    private double _dragX;

    // The user scrolling the strip themselves (wheel, touch or touchpad pan, the scroll bar), and until when a scroll
    // still counts as just done: an export's playhead does not pull the strip back from under them meanwhile (#24).
    private bool _manipulating;
    private bool _scrollPressed;
    private long _userScrollUntil;

    public FilmstripView()
    {
        InitializeComponent();
        ListHost.Strip = this;
        Repeater.ElementPrepared += OnElementPrepared;
        Repeater.ElementClearing += OnElementClearing;
        Repeater.KeyDown += OnStripKeyDown;
        Repeater.GettingFocus += OnRepeaterGettingFocus;
        Loaded += (_, _) => Guarded(WatchScale);
        Unloaded += (_, _) => Guarded(StopWatchingScale);
        Scroller.ViewChanged += (_, _) => ScheduleWant();
        Scroller.SizeChanged += (_, _) => ScheduleWant();
        Scroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler((_, _) => UserScrolled()), handledEventsToo: true);
        Scroller.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _scrollPressed = true), handledEventsToo: true);
        Scroller.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => EndScrollPress()), handledEventsToo: true);
        Scroller.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => EndScrollPress()), handledEventsToo: true);
        Scroller.DirectManipulationStarted += (_, _) => _manipulating = true;
        Scroller.DirectManipulationCompleted += (_, _) =>
        {
            _manipulating = false;
            UserScrolled();
        };
        ListHost.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        ListHost.PointerMoved += OnPointerMoved;
        ListHost.PointerReleased += OnPointerReleased;
        ListHost.PointerCanceled += (_, _) => Guarded(CancelPress);
        ListHost.PointerCaptureLost += (_, _) => Guarded(CancelPress);
        StripMenu.Opening += OnStripMenuOpening;
    }

    /// <summary>How a UI Automation client changes the selection.</summary>
    internal enum AutomationSelection
    {
        /// <summary>Select the item alone and show it (SelectionItem.Select, Invoke).</summary>
        Only,

        /// <summary>Add the item (SelectionItem.AddToSelection).</summary>
        Add,

        /// <summary>Take the item out (SelectionItem.RemoveFromSelection).</summary>
        Remove,
    }

    /// <summary>The view model, for x:Bind; null until <see cref="Attach"/>.</summary>
    public FilmstripViewModel? ViewModel { get; private set; }

    /// <summary>The frames in the project (a thumbnail's set size for UI Automation).</summary>
    internal int ItemCount => ViewModel?.Items.Count ?? 0;

    /// <summary>The thumbnails in memory, for the Debug bench.</summary>
    internal ThumbnailCache<SoftwareBitmap>? Thumbnails => _thumbnails;

    /// <summary>The scroller, for the Debug bench.</summary>
    internal ScrollViewer ScrollHost => Scroller;

    /// <summary>
    /// Follows <paramref name="viewModel"/>, takes pictures through the preview's proxy cache and reports unreadable photos
    /// and failures through the preview's problems (one InfoBar for both). Delete and Ctrl+Z are heard on
    /// <paramref name="keyRoot"/> while <paramref name="canEdit"/> says so (not on the Settings page).
    /// </summary>
    internal void Attach(FilmstripViewModel viewModel, PreviewSession preview, UIElement keyRoot, Func<bool> canEdit)
    {
        Detach();
        ViewModel = viewModel;
        _pointer = new FilmstripPointer(viewModel);
        _preview = preview;
        _errors = preview.Errors;
        _log = preview.Log;
        _keyRoot = keyRoot;
        _canEdit = canEdit;
        _dispatcher = DispatcherQueue;
        _thumbnails = new ThumbnailCache<SoftwareBitmap>(new ThumbnailSource(preview.Proxies));
        _thumbnails.Ready += OnThumbnailReady;
        _thumbnails.Failed += OnThumbnailFailed;
        _thumbnails.UnexpectedFailure += OnThumbnailsUnexpected;
        _thumbnails.SetFrames([.. viewModel.Items.Select(item => item.Frame)]);

        _gapTimer = _dispatcher.CreateTimer();
        _gapTimer.Interval = TimeSpan.FromMilliseconds(150);
        _gapTimer.IsRepeating = false;
        _gapTimer.Tick += OnGapTimerTick;

        // Embedded EXIF pictures fill the strip only until #19's proxies exist; every few seconds the ones in view are
        // checked and loaded again from a proxy that has appeared.
        _recheckTimer = _dispatcher.CreateTimer();
        _recheckTimer.Interval = TimeSpan.FromSeconds(2);
        _recheckTimer.IsRepeating = true;
        _recheckTimer.Tick += OnRecheckTimerTick;
        _recheckTimer.Start();

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Announced += OnAnnounced;
        viewModel.PlayheadMoved += OnPlayheadMoved;
        viewModel.GapsInvalidated += OnGapsInvalidated;
        viewModel.FramesChanged += OnFramesChanged;
        keyRoot.KeyDown += OnRootKeyDown;
        Repeater.ItemsSource = viewModel.Items;
        viewModel.RefreshGaps();
        Bindings.Update();
        WatchScale();
    }

    /// <summary>Lets go of the view model and frees the thumbnails (the window closes).</summary>
    internal void Detach()
    {
        CancelPress();
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.Announced -= OnAnnounced;
            viewModel.PlayheadMoved -= OnPlayheadMoved;
            viewModel.GapsInvalidated -= OnGapsInvalidated;
            viewModel.FramesChanged -= OnFramesChanged;
        }

        if (_keyRoot is { } root)
        {
            root.KeyDown -= OnRootKeyDown;
        }

        if (_gapTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnGapTimerTick;
        }

        if (_recheckTimer is { } recheck)
        {
            recheck.Stop();
            recheck.Tick -= OnRecheckTimerTick;
        }

        Repeater.ItemsSource = null;
        if (_thumbnails is { } thumbnails)
        {
            thumbnails.Ready -= OnThumbnailReady;
            thumbnails.Failed -= OnThumbnailFailed;
            thumbnails.UnexpectedFailure -= OnThumbnailsUnexpected;
            thumbnails.Dispose();
        }

        _thumbnails = null;
        _gapTimer = null;
        _recheckTimer = null;
        _keyRoot = null;
        _pointer = null;
        StopWatchingScale();
        ViewModel = null;
    }

    /// <summary>The realized thumbnails that show an item, in playback order.</summary>
    internal IEnumerable<FilmstripThumb> RealizedThumbs() =>
        _realized.Values.Where(thumb => thumb.Item is not null).OrderBy(thumb => thumb.Item!.Number);

    /// <summary>
    /// UI Automation's children (#38): the realized thumbnails inside the scroller's viewport, and the focused one wherever it
    /// is. The repeater keeps a few more realized either side and re-uses them as the strip scrolls (an export scrolls it
    /// all the time); one of those, still at its old place or waiting for its new one, would read as off screen with a
    /// clickable point on screen, which Accessibility Insights fails. The Scroll pattern brings the rest in.
    /// </summary>
    internal IEnumerable<FilmstripThumb> ThumbsOnScreen()
    {
        var focused = XamlRoot is { } root ? FocusManager.GetFocusedElement(root) : null;
        var viewport = new Rect(0, 0, Scroller.ViewportWidth, Scroller.ViewportHeight);
        return RealizedThumbs().Where(thumb => ReferenceEquals(thumb, focused) || IsInViewport(thumb, viewport));
    }

    private bool IsInViewport(FilmstripThumb thumb, Rect viewport)
    {
        if (thumb.ActualWidth <= 0 || thumb.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            var bounds = thumb.TransformToVisual(Scroller).TransformBounds(new Rect(0, 0, thumb.ActualWidth, thumb.ActualHeight));
            return bounds.Right > viewport.Left && bounds.Left < viewport.Right;
        }
        catch (ArgumentException)
        {
            // Not in the scroller's tree (being taken out): not on screen.
            return false;
        }
    }

    /// <summary>The list's automation peer.</summary>
    internal AutomationPeer ListPeer() =>
        FrameworkElementAutomationPeer.FromElement(ListHost) ?? FrameworkElementAutomationPeer.CreatePeerForElement(ListHost);

    /// <summary>The scroller's Scroll pattern, which the list offers as its own.</summary>
    internal object? ScrollPattern() =>
        (FrameworkElementAutomationPeer.FromElement(Scroller) ?? FrameworkElementAutomationPeer.CreatePeerForElement(Scroller))
            .GetPattern(PatternInterface.Scroll);

    /// <summary>Selection from UI Automation (Narrator, the tests' UIA driver).</summary>
    internal void SelectFromAutomation(FilmstripThumb thumb, AutomationSelection how)
    {
        if (ViewModel is not { } viewModel || thumb.Item is not { } item || _errors is not { } errors)
        {
            return;
        }

        errors.Guard(
            () =>
            {
                var index = item.Number - 1;
                switch (how)
                {
                    case AutomationSelection.Add:
                        viewModel.AddToSelection(index);
                        break;
                    case AutomationSelection.Remove:
                        viewModel.RemoveFromSelection(index);
                        break;
                    default:
                        viewModel.Click(index, shift: false, control: false);
                        break;
                }
            },
            ProjectText.FilmstripSelectFailed);
    }

    /// <summary>Scrolls so thumbnail <paramref name="index"/> is in view (the Debug bench and the keys).</summary>
    internal void ScrollToIndex(int index, bool animate)
    {
        if (ViewModel is not { } viewModel || index < 0 || index >= viewModel.Items.Count)
        {
            return;
        }

        var width = viewModel.ThumbnailWidth;
        var x = Inset + (index * (width + Seam));
        var target = x - ((Scroller.ViewportWidth - width) / 2);
        Scroller.ChangeView(Math.Clamp(target, 0, Scroller.ScrollableWidth), null, null, disableAnimation: !animate);
    }

    private double Stride => (ViewModel?.ThumbnailWidth ?? 48) + Seam;

    // ---- Realization and pictures ----

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not FilmstripThumb thumb || ViewModel is not { } viewModel || viewModel.ItemAt(args.Index) is not { } item)
        {
            return;
        }

        Guarded(
            () =>
            {
                thumb.Strip = this;
                thumb.Bind(item, viewModel.ThumbnailWidth, viewModel.ThumbnailHeight);
                _realized[item.Id] = thumb;
                ShowThumbnail(thumb);
                Cascade(thumb, args.Index);
                ScheduleWant();
            });
    }

    // ---- The arrival cascade (motion spec, Arrival) ----

    /// <summary>
    /// The photos landed on the Empty screen: the thumbnails in view rise 8 px into place, 400 ms Enter, 18 ms apart from
    /// 220 ms (a fade with animations off). Thumbnails shown for the first time within the cascade's window join it in
    /// their place; later ones (scrolled to) appear at rest.
    /// </summary>
    internal void BeginArrival()
    {
        _arrivalStart = Stopwatch.GetTimestamp();
        _arrivalFirst = int.MaxValue;
        _cascaded.Clear();
        foreach (var thumb in RealizedThumbs().ToList())
        {
            Cascade(thumb, thumb.Item!.Number - 1);
        }
    }

    private void Cascade(FilmstripThumb thumb, int index)
    {
        if (_arrivalStart == 0)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_arrivalStart);
        if (elapsed > MotionSpec.ThumbnailCascadeWindow)
        {
            _arrivalStart = 0;
            return;
        }

        // Each photo joins once: a thumbnail realized again during the cascade (the strip relaid out) appears at rest.
        if (thumb.Item is not { } item || !_cascaded.Add(item.Id))
        {
            return;
        }

        _arrivalFirst = Math.Min(_arrivalFirst, index);
        var step = MotionTokens.Plan(MotionCatalogue.ArrivalFilmstrip, Math.Max(0, index - _arrivalFirst));
        if (!step.Animates)
        {
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(thumb, true);
        _animated.Add(thumb);
        var visual = ElementCompositionPreview.GetElementVisual(thumb);
        var compositor = visual.Compositor;
        var easing = MotionTokens.CreateEasing(compositor, step.Easing);
        var delay = step.Delay > elapsed ? step.Delay - elapsed : TimeSpan.Zero;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, easing);
        fade.Duration = step.Duration;
        fade.DelayTime = delay;
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation(nameof(Visual.Opacity), fade);
        if (step.Moves)
        {
            var rise = compositor.CreateVector3KeyFrameAnimation();
            rise.InsertKeyFrame(0f, new Vector3(0, MotionSpec.ThumbnailRise, 0));
            rise.InsertKeyFrame(1f, Vector3.Zero, easing);
            rise.Duration = step.Duration;
            rise.DelayTime = delay;
            rise.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Translation", rise);
        }
    }

    // A recycled thumbnail starts at rest, whatever its last cascade left running. Only one the cascade animated has a
    // Translation to stop (asking a visual that never had one throws).
    private void Settle(FilmstripThumb thumb)
    {
        if (!_animated.Remove(thumb))
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(thumb);
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.StopAnimation("Translation");
        visual.Opacity = 1f;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is not FilmstripThumb thumb)
        {
            return;
        }

        Guarded(
            () =>
            {
                if (thumb.Item is { } item && _realized.TryGetValue(item.Id, out var shown) && ReferenceEquals(shown, thumb))
                {
                    _realized.Remove(item.Id);
                }

                thumb.Unbind();
                Settle(thumb);
                ScheduleWant();
            });
    }

    private void ShowThumbnail(FilmstripThumb thumb)
    {
        if (thumb.Item is not { } item || _thumbnails is not { } thumbnails)
        {
            return;
        }

        using var lease = thumbnails.Acquire(item.Id);
        if (lease is not null)
        {
            thumb.ShowPicture(lease.Image);
        }
        else if (thumbnails.FailureOf(item.Id) is not null)
        {
            thumb.ShowUnreadable();
        }
        else if (!thumb.HasPicture)
        {
            thumb.ShowLoading();
        }
    }

    // Worker threads: the arrivals are gathered and shown in one pass on the UI thread.
    private void OnThumbnailReady(object? sender, ThumbnailReadyEventArgs e) => Arrived(e.Id);

    private void OnThumbnailFailed(object? sender, FrameLoadFailedEventArgs e)
    {
        _preview?.ReportFrameFailure(e.Failure);
        Arrived(e.Frame.Id);
    }

    private void OnThumbnailsUnexpected(object? sender, PreviewFailureEventArgs e) => _preview?.ReportUnexpected(e.Exception);

    private void Arrived(Guid id)
    {
        _arrived.Enqueue(id);
        if (Interlocked.Exchange(ref _flushQueued, 1) == 0 && _dispatcher is { } dispatcher && _log is { } log)
        {
            dispatcher.TryEnqueueGuarded(log, FlushArrived, ProjectText.FilmstripThumbnailFailed);
        }
    }

    private void FlushArrived()
    {
        Volatile.Write(ref _flushQueued, 0);
        while (_arrived.TryDequeue(out var id))
        {
            if (_realized.TryGetValue(id, out var thumb))
            {
                Guarded(() => ShowThumbnail(thumb));
            }
        }
    }

    // Once per burst of scrolling and realizing: the realized thumbnails, nearest the middle of the view first.
    private void ScheduleWant()
    {
        if (_wantQueued || _dispatcher is not { } dispatcher || _log is not { } log)
        {
            return;
        }

        _wantQueued = true;
        dispatcher.TryEnqueueGuarded(log, UpdateWant, ProjectText.FilmstripScrollFailed);
    }

    private void UpdateWant()
    {
        _wantQueued = false;
        if (_thumbnails is not { } thumbnails || ViewModel is not { } viewModel)
        {
            return;
        }

        var stride = Stride;
        var middle = Scroller.HorizontalOffset + (Scroller.ViewportWidth / 2);
        var wanted = _realized.Values
            .Select(thumb => thumb.Item)
            .OfType<FilmstripItem>()
            .OrderBy(item => Math.Abs(Inset + ((item.Number - 1) * stride) + (viewModel.ThumbnailWidth / 2) - middle))
            .Select(item => item.Frame)
            .ToList();
        thumbnails.Want(wanted, ThumbnailSize.For(viewModel.ThumbnailHeight, XamlRoot?.RasterizationScale ?? 1));
    }

    // The window moved to a screen of another scale: thumbnails are asked for again at the new pixel size (#82 review,
    // minor 7), not only at the next scroll.
    private void WatchScale()
    {
        if (XamlRoot is not { } root || ReferenceEquals(root, _scaleRoot))
        {
            return;
        }

        StopWatchingScale();
        _scaleRoot = root;
        _scale = root.RasterizationScale;
        root.Changed += OnXamlRootChanged;
    }

    private void StopWatchingScale()
    {
        if (_scaleRoot is { } root)
        {
            root.Changed -= OnXamlRootChanged;
        }

        _scaleRoot = null;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Guarded(
        () =>
        {
            if (sender.RasterizationScale != _scale)
            {
                _scale = sender.RasterizationScale;
                ScheduleWant();
            }
        });

    private void OnFramesChanged(object? sender, ImmutableArray<Frame> frames)
    {
        _thumbnails?.SetFrames(frames);
        ScheduleWant();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FilmstripViewModel.ThumbnailHeight) || ViewModel is not { } viewModel)
        {
            return;
        }

        Guarded(
            () =>
            {
                foreach (var thumb in _realized.Values)
                {
                    thumb.Resize(viewModel.ThumbnailWidth, viewModel.ThumbnailHeight);
                }

                Repeater.InvalidateMeasure();
                ScheduleWant();
                ScrollToIndex(viewModel.PlayheadIndex, animate: false);
            });
    }

    private void OnGapsInvalidated(object? sender, EventArgs e) => _gapTimer?.Start();

    private void OnGapTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (ViewModel is { } viewModel)
        {
            _errors?.Guard(viewModel.RefreshGaps, ProjectText.FilmstripProjectFailed);
        }
    }

    private void OnRecheckTimerTick(DispatcherQueueTimer sender, object args) => Guarded(() => _thumbnails?.Recheck());

    // ---- Narrator ----

    private void OnAnnounced(object? sender, string text) => Guarded(
        () => Shell.Announcer.Announce(
            ListHost,
            text,
            AutomationNotificationProcessing.ImportantMostRecent,
            "TenLapse.Filmstrip.Edit",
            AutomationNotificationKind.ActionCompleted));

    // ---- The playhead ----

    private void OnPlayheadMoved(object? sender, EventArgs e)
    {
        if (ViewModel is not { } viewModel || _preview is not { } preview)
        {
            return;
        }

        var index = viewModel.PlayheadIndex;
        var previous = _lastPlayhead;
        _lastPlayhead = index;
        if (index < 0 || _pointer?.IsDragging == true)
        {
            return;
        }

        // While an export runs the playhead is the frame being written: kept in view wherever it was before (its jumps
        // can outrun the strip), at once, unless the user is scrolling the strip (#24 review, M5).
        if (viewModel.IsExporting)
        {
            if (!UserScrolling && !InView(index))
            {
                Guarded(() =>
                {
                    var x = Inset + (index * Stride);
                    Scroller.ChangeView(Math.Clamp(x - ((Scroller.ViewportWidth - viewModel.ThumbnailWidth) / 2), 0, Scroller.ScrollableWidth), null, null, disableAnimation: true);
                });
            }

            return;
        }

        if ((previous >= 0 && !InView(previous)) || InView(index))
        {
            return;
        }

        Guarded(
            () =>
            {
                var playing = preview.Player.IsPlaying;
                var width = viewModel.ThumbnailWidth;
                var x = Inset + (index * Stride);
                var target = playing ? x - (Scroller.ViewportWidth / 4) : x - ((Scroller.ViewportWidth - width) / 2);
                Scroller.ChangeView(Math.Clamp(target, 0, Scroller.ScrollableWidth), null, null, disableAnimation: playing || !MotionTokens.AnimationsEnabled);
            });
    }

    private bool UserScrolling => _manipulating || _scrollPressed || Environment.TickCount64 < _userScrollUntil;

    // A scroll the user just made counts for two seconds.
    private void UserScrolled() => _userScrollUntil = Environment.TickCount64 + 2000;

    private void EndScrollPress()
    {
        if (_scrollPressed)
        {
            _scrollPressed = false;
            UserScrolled();
        }
    }

    // Whether thumbnail index is wholly in view.
    private bool InView(int index)
    {
        var x = Inset + (index * Stride);
        var left = Scroller.HorizontalOffset;
        return x >= left && x + (ViewModel?.ThumbnailWidth ?? 0) <= left + Scroller.ViewportWidth;
    }

    // ---- Keys ----

    private void OnStripKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || ViewModel is not { } viewModel)
        {
            return;
        }

        // Escape while the button is down: the drag (or the click to come) is cancelled and the pointer let go, so neither
        // a later move nor the release does anything.
        if (e.Key == VirtualKey.Escape && _pointer is { IsPressed: true })
        {
            Guarded(
                () =>
                {
                    CancelPress();
                    ListHost.ReleasePointerCaptures();
                });
            e.Handled = true;
            return;
        }

        if (FocusManager.GetFocusedElement(XamlRoot) is not FilmstripThumb { Item: { } focused })
        {
            return;
        }

        var action = FilmstripKeyboard.Resolve(e, XamlRoot);
        if (action == FilmstripAction.None)
        {
            return;
        }

        _errors?.Guard(
            () =>
            {
                var result = viewModel.Handle(action, focused.Number - 1);
                if (result.Handled)
                {
                    e.Handled = true;
                    FocusIndex(result.Focus);
                }
            },
            ProjectText.FilmstripSelectFailed);
    }

    // Delete and Ctrl+Z with nothing focused (the user clicked the preview's stage). Keys pressed in the strip were the
    // strip's handler's, and any other focused control keeps them (FilmstripKeys). Handled only when something changed.
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || ViewModel is not { } viewModel || _canEdit?.Invoke() != true
            || FocusManager.GetFocusedElement(XamlRoot) is FilmstripThumb)
        {
            return;
        }

        var action = FilmstripKeyboard.Resolve(e, XamlRoot);
        if (action is FilmstripAction.Remove or FilmstripAction.Undo)
        {
            _errors?.Guard(() => e.Handled = viewModel.Handle(action, -1).Handled, action == FilmstripAction.Remove ? ProjectText.FilmstripRemoveFailed : ProjectText.FilmstripUndoFailed);
        }
    }

    private void FocusIndex(int index)
    {
        if (index < 0 || ViewModel is not { } viewModel || index >= viewModel.Items.Count)
        {
            return;
        }

        var element = Repeater.GetOrCreateElement(index);
        element.UpdateLayout();
        if (element is FilmstripThumb thumb)
        {
            thumb.Focus(FocusState.Keyboard);
            thumb.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = MotionTokens.AnimationsEnabled });
        }
    }

    // Tab into the strip lands on the frame the preview shows, not on the last thumbnail that had the focus.
    private void OnRepeaterGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (ViewModel is not { } viewModel || args.FocusState != FocusState.Keyboard || IsInStrip(args.OldFocusedElement)
            || args.NewFocusedElement is not FilmstripThumb || Repeater.TryGetElement(viewModel.PlayheadIndex) is not { } playhead
            || ReferenceEquals(playhead, args.NewFocusedElement))
        {
            return;
        }

        args.TrySetNewFocusedElement(playhead);
    }

    private bool IsInStrip(DependencyObject? element)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, Repeater))
            {
                return true;
            }
        }

        return false;
    }

    // ---- Pointer: click and drag ----

    // The thumbnail under x (repeater coordinates), or -1 in a seam or past the ends.
    private int HitIndex(double x)
    {
        var count = ViewModel?.Items.Count ?? 0;
        if (x < 0 || count == 0)
        {
            return -1;
        }

        var index = (int)Math.Floor(x / Stride);
        return index < count && x - (index * Stride) <= Stride - Seam ? index : -1;
    }

    // The seam nearest x (repeater coordinates): 0 before the first thumbnail, Count after the last.
    private int SeamAt(double x) => Math.Clamp((int)Math.Round((x + (Seam / 2)) / Stride), 0, ViewModel?.Items.Count ?? 0);

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || _pointer is not { } pointer)
        {
            return;
        }

        var point = e.GetCurrentPoint(Repeater);
        var index = HitIndex(point.Position.X);
        if (index < 0 || point.Position.Y < 0 || point.Position.Y > Repeater.ActualHeight)
        {
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            // The context menu acts on the selection, with the video paused so the selection stays where it is.
            _errors?.Guard(() => viewModel.PrepareContextMenu(index), ProjectText.FilmstripSelectFailed);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Guarded(
            () =>
            {
                var at = e.GetCurrentPoint(ListHost).Position;
                var modifiers = e.KeyModifiers;
                var touch = e.Pointer.PointerDeviceType == PointerDeviceType.Touch;
                pointer.Press(index, at.X, at.Y, e.Pointer.PointerId, modifiers.HasFlag(VirtualKeyModifiers.Shift), modifiers.HasFlag(VirtualKeyModifiers.Control), touch);
                Repeater.TryGetElement(index)?.Focus(FocusState.Pointer);
                if (!touch)
                {
                    ListHost.CapturePointer(e.Pointer);
                    e.Handled = true;
                }
            });
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer is not { IsPressed: true } pointer)
        {
            return;
        }

        Guarded(
            () =>
            {
                var position = e.GetCurrentPoint(ListHost).Position;
                var result = FilmstripPointerResult.Ignored;
                _errors?.Guard(() => result = pointer.Move(e.Pointer.PointerId, position.X, position.Y), ProjectText.FilmstripMoveFailed);
                if (result is not (FilmstripPointerResult.DragStarted or FilmstripPointerResult.Dragging))
                {
                    return;
                }

                if (result == FilmstripPointerResult.DragStarted)
                {
                    ShowDrag();
                }

                _dragX = position.X;
                PlaceInsertionMarker();
                e.Handled = true;
            });
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer is not { IsPressed: true } pointer)
        {
            return;
        }

        Guarded(
            () =>
            {
                var result = FilmstripPointerResult.Ignored;
                var context = pointer.IsDragging ? ProjectText.FilmstripMoveFailed : ProjectText.FilmstripSelectFailed;
                _errors?.Guard(() => result = pointer.Release(e.Pointer.PointerId), context);
                if (!pointer.IsDragging)
                {
                    HideDrag();
                }

                if (result != FilmstripPointerResult.Ignored)
                {
                    ListHost.ReleasePointerCapture(e.Pointer);
                    e.Handled = true;
                }
            });
    }

    // The menu opens from a right-click or from the keyboard: the video pauses so the selection stays where it is.
    private void OnStripMenuOpening(object? sender, object e)
    {
        if (ViewModel is { } viewModel)
        {
            _errors?.Guard(() => viewModel.PrepareContextMenu(null), ProjectText.FilmstripSelectFailed);
        }
    }

    // Forgets the press; a drag ends without moving anything.
    private void CancelPress()
    {
        if (_pointer is { } pointer)
        {
            _errors?.Guard(() => pointer.Cancel(), ProjectText.FilmstripMoveFailed);
        }

        HideDrag();
    }

    private void ShowDrag()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        InsertionMarker.Height = viewModel.ThumbnailHeight + (2 * Inset);
        InsertionMarker.Visibility = Visibility.Visible;
        if (!_autoScrolling)
        {
            _autoScrolling = true;
            CompositionTarget.Rendering += OnDragRendering;
        }
    }

    private void HideDrag()
    {
        InsertionMarker.Visibility = Visibility.Collapsed;
        if (_autoScrolling)
        {
            _autoScrolling = false;
            CompositionTarget.Rendering -= OnDragRendering;
        }
    }

    // While dragging near an end, the strip scrolls a little on every frame, faster nearer the edge.
    private void OnDragRendering(object? sender, object e)
    {
        if (_pointer is not { IsDragging: true })
        {
            return;
        }

        Guarded(
            () =>
            {
                var width = ListHost.ActualWidth;
                var step = _dragX < AutoScrollEdge
                    ? -AutoScrollSpeed * (1 - (Math.Max(0, _dragX) / AutoScrollEdge))
                    : _dragX > width - AutoScrollEdge ? AutoScrollSpeed * (1 - (Math.Max(0, width - _dragX) / AutoScrollEdge)) : 0;
                if (step != 0)
                {
                    Scroller.ChangeView(Math.Clamp(Scroller.HorizontalOffset + step, 0, Scroller.ScrollableWidth), null, null, disableAnimation: true);
                    PlaceInsertionMarker();
                }
            });
    }

    private void PlaceInsertionMarker()
    {
        var toRepeater = ListHost.TransformToVisual(Repeater).TransformPoint(new Point(_dragX, 0));
        var seam = SeamAt(toRepeater.X);
        _pointer?.Over(seam);
        var seamX = (seam * Stride) - (Seam / 2);
        var inHost = Repeater.TransformToVisual(ListHost).TransformPoint(new Point(seamX, -Inset));
        Canvas.SetLeft(InsertionMarker, inHost.X - (InsertionMarker.Width / 2));
        Canvas.SetTop(InsertionMarker, inHost.Y);
    }

    // View code that must never throw into WinUI: a failure is shown (once a minute) and logged through the preview's path.
    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            if (_preview is { } preview)
            {
                preview.ReportUnexpected(exception);
            }
            else
            {
                _log?.Report(exception, ProjectText.FilmstripThumbnailFailed);
            }
        }
    }
}
