using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TenLapse.Presentation;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;

namespace TenLapse.Preview;

/// <summary>
/// The scrubber (#19): shows where the playhead is and seeks where the pointer presses and drags. Playback pauses for
/// the drag and goes on after it. The arithmetic (<see cref="PlayerViewModel.FrameAtFraction"/>) is the view model's.
/// </summary>
public sealed partial class Scrubber : UserControl
{
    // The knob's radius: the track runs from its centre at the start to its centre at the end.
    private const double Inset = 8;

    private PlayerViewModel? _player;
    private IErrorPresenter? _errors;
    private bool _dragging;
    private bool _resumeAfterDrag;
    private bool _pointerOver;
    private int _announcedFrame = -1;

    public Scrubber()
    {
        InitializeComponent();
        AutomationProperties.SetName(this, ProjectText.PreviewScrubberName);
        SizeChanged += (_, _) => Place();
        PointerEntered += (_, _) => SetPointerOver(true);
        PointerExited += (_, _) => SetPointerOver(false);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += (_, e) => EndDrag(e);
        PointerCanceled += (_, e) => EndDrag(e);
        PointerCaptureLost += (_, e) => EndDrag(e);
    }

    internal PlayerViewModel? Player => _player;

    /// <summary>Follows <paramref name="player"/>.</summary>
    internal void Attach(PlayerViewModel player, IErrorPresenter errors)
    {
        Detach();
        _player = player;
        _errors = errors;
        _player.PropertyChanged += OnPlayerPropertyChanged;
        Place();
    }

    /// <summary>Stops following the player.</summary>
    internal void Detach()
    {
        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerPropertyChanged;
        }

        _player = null;
    }

    /// <summary>A seek from UI Automation (RangeValue's SetValue): frame number from 1.</summary>
    internal void SeekFromAutomation(int frameNumber) =>
        _errors?.Guard(() => _player?.Seek(frameNumber - 1), ProjectText.PreviewSeekFailed);

    protected override AutomationPeer OnCreateAutomationPeer() => new ScrubberAutomationPeer(this);

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.CurrentFrame) or nameof(PlayerViewModel.FrameCount) or nameof(PlayerViewModel.IsPlaying))
        {
            Place();
        }
    }

    private void Place()
    {
        if (_player is not { } player)
        {
            return;
        }

        var fraction = PlayerViewModel.FractionOfFrame(player.CurrentFrame, player.FrameCount);
        var travel = Math.Max(0, ActualWidth - (2 * Inset));
        KnobOffset.X = fraction * travel;
        Played.Width = fraction * travel;

        // Narrator hears the value change only while paused: playback would read out every frame.
        if (!player.IsPlaying && player.CurrentFrame != _announcedFrame
            && AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged)
            && FrameworkElementAutomationPeer.FromElement(this) is { } peer)
        {
            var before = _announcedFrame + 1.0;
            _announcedFrame = player.CurrentFrame;
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, before, player.CurrentFrame + 1.0);
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_player is not { HasFrames: true } player || _errors is null)
        {
            return;
        }

        _errors.Guard(
            () =>
            {
                Focus(FocusState.Pointer);
                if (CapturePointer(e.Pointer))
                {
                    _dragging = true;
                    _resumeAfterDrag = player.IsPlaying;
                    player.Pause();
                    VisualStateManager.GoToState(this, "Pressed", useTransitions: false);
                }

                SeekTo(e);
            },
            ProjectText.PreviewSeekFailed);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            _errors?.Guard(() => SeekTo(e), ProjectText.PreviewSeekFailed);
            e.Handled = true;
        }
    }

    private void EndDrag(PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleasePointerCapture(e.Pointer);
        VisualStateManager.GoToState(this, _pointerOver ? "PointerOver" : "Normal", useTransitions: false);
        if (_resumeAfterDrag)
        {
            _resumeAfterDrag = false;
            _errors?.Guard(() => _player?.Play(), ProjectText.PreviewPlayFailed);
        }
    }

    private void SeekTo(PointerRoutedEventArgs e)
    {
        if (_player is not { } player)
        {
            return;
        }

        var x = e.GetCurrentPoint(this).Position.X;
        var travel = ActualWidth - (2 * Inset);
        var fraction = travel <= 0 ? 0 : (x - Inset) / travel;
        player.Seek(PlayerViewModel.FrameAtFraction(fraction, player.FrameCount));
    }

    private void SetPointerOver(bool over)
    {
        _pointerOver = over;
        if (!_dragging)
        {
            VisualStateManager.GoToState(this, over ? "PointerOver" : "Normal", useTransitions: false);
        }
    }

    /// <summary>A slider to UI Automation: its value is the frame number, from 1.</summary>
    private sealed partial class ScrubberAutomationPeer(Scrubber owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        public double Value => (owner.Player?.CurrentFrame ?? -1) + 1.0;

        public double Minimum => owner.Player is { HasFrames: true } ? 1 : 0;

        public double Maximum => owner.Player?.FrameCount ?? 0;

        public double SmallChange => 1;

        public double LargeChange => 10;

        public bool IsReadOnly => owner.Player is not { HasFrames: true };

        public void SetValue(double value) => owner.SeekFromAutomation((int)Math.Round(value));

        protected override object? GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPatternCore(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetClassNameCore() => nameof(Scrubber);
    }
}
