using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TenLapse.Core.Preview;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Services;
using TenLapse.Themes;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Frame = TenLapse.Core.Projects.Frame;

namespace TenLapse.Shell;

/// <summary>
/// The film gate (see the comment at the top of FilmGate.xaml), as the canvas builds it: 30 frames sampled evenly over the
/// whole film, on a strip whose position is linear in the progress, so it crosses the gate once over the whole export
/// (29 strides) whatever the speed. Each progress report gives a new position; the strip glides there linearly over
/// <see cref="ExportViewModel.MinProgressInterval"/>, the time to the next report. Samples the export has not reached show
/// at 30 %, and each change of strength fades over 167 ms linear. With animations off the strip stands still, its middle
/// sample in the gate, and only the strengths change.
/// </summary>
/// <remarks>
/// The 30 pictures are loaded once, as the export starts, through the filmstrip's <see cref="ThumbnailCache{TImage}"/> at low
/// priority (<see cref="ThumbnailCache{TImage}.Keep"/>: after every frame the strip wants, never cancelled by its scrolling,
/// and kept until the export ends), so the gate does not depend on what the strip shows. A sample without its picture yet
/// shows the Track fill.
/// </remarks>
public sealed partial class FilmGate : UserControl
{
    private const double SlotWidth = 48;
    private const double SlotHeight = 32;
    private const double SlotTop = 4;
    private const double Stride = 51;
    private const int Samples = 30;
    private const double PendingOpacity = 0.3;
    private const double FadeShare = 0.16;

    // A sample is reached once the progress is at its place (the canvas's 0.0001 slack).
    private const double Reached = 0.0001;

    private readonly List<Slot> _slots = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private ThumbnailCache<SoftwareBitmap>? _thumbnails;
    private IErrorSink? _log;
    private DispatcherQueueTimer? _timer;
    private ImmutableArray<Frame> _frames = [];
    private bool _ended = true;
    private double _shown;
    private double _from;
    private double _to;
    private TimeSpan _glideStart;
    private SolidColorBrush? _track;

    public FilmGate()
    {
        InitializeComponent();

        // Guarded: a handler that throws would end the process with a stowed exception.
        SizeChanged += (_, _) => Log.RunLogged(Fit, ExportText.ProgressFailed);
        Loaded += (_, _) => Log.RunLogged(Recolor, ExportText.ProgressFailed);
        ActualThemeChanged += (_, _) => Log.RunLogged(Recolor, ExportText.ProgressFailed);
    }

    private IErrorSink Log => _log ?? NoLog.Instance;

    // Whether the strip glides (motion spec, Gate): planned as each export starts, and again when the Windows setting has
    // flipped since (polled at each report, the start of each glide).
    private bool Animate { get; set; } = true;

    /// <summary>Takes the filmstrip's thumbnail cache (null: the samples show the Track fill).</summary>
    internal void Attach(ThumbnailCache<SoftwareBitmap>? thumbnails, IErrorSink log)
    {
        _thumbnails = thumbnails;
        _log = log;
    }

    /// <summary>Stops the glide and lets go of the samples' pictures (the window closes).</summary>
    internal void Detach()
    {
        _timer?.Stop();
        _thumbnails?.Keep([], ThumbnailSize.For(SlotHeight, 1));
        _thumbnails = null;
    }

    /// <summary>
    /// The export of <paramref name="frames"/> is <paramref name="progress"/> (0 to 1) done; empty frames when none runs.
    /// </summary>
    internal void Show(ImmutableArray<Frame> frames, double progress)
    {
        if (frames.IsDefaultOrEmpty)
        {
            // The export ended: the strip keeps its last picture while the panel leaves, and the cache lets the samples go.
            if (!_ended)
            {
                _ended = true;
                _timer?.Stop();
                _thumbnails?.Keep([], ThumbnailSize.For(SlotHeight, PixelScale));
            }

            return;
        }

        progress = Math.Clamp(progress, 0, 1);
        if (_ended || frames != _frames)
        {
            Start(frames, progress);
            Animate = MotionTokens.Plan(MotionCatalogue.GateStrip).Moves;
            MotionTokens.Plan(MotionCatalogue.GateSample);
        }
        else if (MotionTokens.AnimationsEnabled != Animate)
        {
            Animate = MotionTokens.Plan(MotionCatalogue.GateStrip).Moves;
        }

        var now = _clock.Elapsed;
        if (Animate && IsLoaded)
        {
            _from = _shown;
            _to = progress;
            _glideStart = now;
        }
        else
        {
            _shown = _from = _to = progress;
        }

        foreach (var slot in _slots)
        {
            slot.FadeTo(slot.Place <= progress + Reached ? 1 : PendingOpacity, now);
            slot.Bind(_thumbnails);
        }

        Place();
        if (IsLoaded && (_shown != _to || _slots.Any(slot => slot.Fading)))
        {
            Timer().Start();
        }
    }

    // A new export: 30 frames sampled over the film (all of them when it has fewer), at their strength at once, and their
    // pictures asked for at low priority.
    private void Start(ImmutableArray<Frame> frames, double progress)
    {
        _ended = false;
        _frames = frames;
        _shown = _from = _to = progress;
        var count = Math.Min(Samples, frames.Length);
        while (_slots.Count < count)
        {
            var slot = new Slot(_track);
            _slots.Add(slot);
            Strip.Children.Add(slot.Element);
        }

        while (_slots.Count > count)
        {
            Strip.Children.Remove(_slots[^1].Element);
            _slots.RemoveAt(_slots.Count - 1);
        }

        var sampled = new Frame[count];
        for (var i = 0; i < count; i++)
        {
            var place = count == 1 ? 0 : (double)i / (count - 1);
            sampled[i] = frames[(int)Math.Round(place * (frames.Length - 1))];
            _slots[i].Reset(sampled[i].Id, place, place <= progress + Reached ? 1 : PendingOpacity);
        }

        _thumbnails?.Keep(sampled, ThumbnailSize.For(SlotHeight, PixelScale));
    }

    private double PixelScale => XamlRoot?.RasterizationScale ?? 1;

    private DispatcherQueueTimer Timer()
    {
        if (_timer is null)
        {
            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = MotionSpec.HandDrivenTick;
            _timer.IsRepeating = true;
            _timer.Tick += OnTick;
        }

        return _timer;
    }

    private void OnTick(DispatcherQueueTimer sender, object args) => Log.RunLogged(
        () =>
        {
            var now = _clock.Elapsed;
            var share = Math.Min(1, (now - _glideStart) / ExportViewModel.MinProgressInterval);
            _shown = _from + ((_to - _from) * share);
            var fading = false;
            foreach (var slot in _slots)
            {
                fading |= slot.Step(now);
            }

            Place();
            if (share >= 1 && !fading)
            {
                sender.Stop();
            }
        },
        ExportText.ProgressFailed);

    // The strip's left edge: the canvas's gate / 2 - (24 + progress x 29 x 51), so sample i sits in the gate when the
    // progress reaches i / 29. Standing still (animations off), the middle sample sits in the gate.
    private void Place()
    {
        var count = _slots.Count;
        if (count == 0)
        {
            return;
        }

        var reach = (count - 1) * Stride;
        var travelled = Animate ? _shown * reach : Math.Floor((count - 1) / 2.0) * Stride;
        var left = (ActualWidth / 2) - ((SlotWidth / 2) + travelled);
        for (var i = 0; i < count; i++)
        {
            _slots[i].Move(left + (i * Stride));
        }
    }

    // The fades cover the outer 16 % on each side; the strip is clipped to the gate's box.
    private void Fit()
    {
        var fade = Math.Round(ActualWidth * FadeShare);
        LeftFade.Width = RightFade.Width = fade;
        Root.Clip = new RectangleGeometry { Rect = new Rect(0, 0, ActualWidth, ActualHeight) };
        Place();
    }

    // The fades go from the Layer to the Layer made transparent (not to transparent white, which would grey the dark theme).
    private void Recolor()
    {
        var layer = ThemeTokens.Color("LayerColor", this);
        var clear = Windows.UI.Color.FromArgb(0, layer.R, layer.G, layer.B);
        LeftSolid.Color = RightSolid.Color = layer;
        LeftClear.Color = RightClear.Color = clear;

        // The Track fill behind a sample without a picture, in this element's theme.
        _track = new SolidColorBrush(ThemeTokens.Color("TrackColor", this));
        foreach (var slot in _slots)
        {
            slot.Element.Background = _track;
        }
    }

    // One sample: the Track fill, and the frame's picture over it once the cache has it; its strength fades linearly.
    private sealed class Slot
    {
        private static readonly TimeSpan _fade = MotionCatalogue.GateSample.Duration;

        private readonly Border _picture;
        private readonly ImageBrush _brush = new() { Stretch = Stretch.UniformToFill };
        private readonly TranslateTransform _move = new();
        private WriteableBitmap? _bitmap;
        private Guid _id;
        private bool _hasPicture;
        private double _fadeFrom;
        private double _target;
        private TimeSpan _fadeStart;

        public Slot(Brush? track)
        {
            _picture = new Border { Background = _brush, CornerRadius = new CornerRadius(3), Visibility = Visibility.Collapsed };
            Element = new Border
            {
                Width = SlotWidth,
                Height = SlotHeight,
                CornerRadius = new CornerRadius(3),
                Background = track,
                Child = _picture,
                RenderTransform = _move,
            };
            Element.SetValue(Canvas.TopProperty, SlotTop);
        }

        public Border Element { get; }

        /// <summary>Where in the film the sample sits, 0 to 1.</summary>
        public double Place { get; private set; }

        public bool Fading { get; private set; }

        public void Move(double x) => _move.X = x;

        public void Reset(Guid id, double place, double opacity)
        {
            if (_id != id)
            {
                _id = id;
                _hasPicture = false;
                _picture.Visibility = Visibility.Collapsed;
            }

            Place = place;
            _target = opacity;
            Element.Opacity = opacity;
            Fading = false;
        }

        // Starts a 167 ms linear fade to a new strength.
        public void FadeTo(double opacity, TimeSpan now)
        {
            if (opacity == _target)
            {
                return;
            }

            _fadeFrom = Element.Opacity;
            _target = opacity;
            _fadeStart = now;
            Fading = true;
        }

        // One step of the fade; true while it goes on.
        public bool Step(TimeSpan now)
        {
            if (!Fading)
            {
                return false;
            }

            var share = Math.Min(1, (now - _fadeStart) / _fade);
            Element.Opacity = _fadeFrom + ((_target - _fadeFrom) * share);
            Fading = share < 1;
            return Fading;
        }

        // The sample's picture once the cache has it; tried again at each report until it is there.
        public void Bind(ThumbnailCache<SoftwareBitmap>? thumbnails)
        {
            if (_hasPicture)
            {
                return;
            }

            using var lease = thumbnails?.Acquire(_id);
            if (lease is null)
            {
                return;
            }

            var picture = lease.Image;
            if (_bitmap is null || _bitmap.PixelWidth != picture.PixelWidth || _bitmap.PixelHeight != picture.PixelHeight)
            {
                _bitmap = new WriteableBitmap(picture.PixelWidth, picture.PixelHeight);
                _brush.ImageSource = _bitmap;
            }

            picture.CopyToBuffer(_bitmap.PixelBuffer);
            _bitmap.Invalidate();
            _picture.Visibility = Visibility.Visible;
            _hasPicture = true;
        }
    }

    // Before Attach: failures go nowhere rather than throw into the timer.
    private sealed class NoLog : IErrorSink
    {
        public static readonly NoLog Instance = new();

        public void Report(Exception exception, string context)
        {
        }
    }
}
