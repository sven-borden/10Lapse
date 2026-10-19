using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TenLapse.Core.Preview;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Services;
using TenLapse.Themes;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Frame = TenLapse.Core.Projects.Frame;

namespace TenLapse.Shell;

/// <summary>
/// The film gate (see the comment at the top of FilmGate.xaml): nine slots that show the frames around the one being
/// encoded. Each progress report gives a new position; the strip glides there linearly over
/// <see cref="ExportViewModel.MinProgressInterval"/>, the time to the next report, so it moves steadily with the real
/// progress. With animations off it jumps, standing still: only the pictures in the slots change.
/// </summary>
/// <remarks>
/// The pictures are the filmstrip's thumbnails (<see cref="ThumbnailCache{TImage}"/>), taken only if they are in memory:
/// the gate never asks for loads, so it cannot steal the strip's. The strip follows the playhead, which the export moves
/// to the frame being written, so the thumbnails around it are usually there; a slot without one shows the Track fill.
/// </remarks>
public sealed partial class FilmGate : UserControl
{
    private const double SlotWidth = 48;
    private const double SlotHeight = 32;
    private const double SlotTop = 4;
    private const double Stride = 51;
    private const int Reach = 4;
    private const double PendingOpacity = 0.3;
    private const double FadeShare = 0.16;

    private readonly Slot[] _slots = new Slot[(2 * Reach) + 1];
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

    public FilmGate()
    {
        InitializeComponent();
        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new Slot();
            Strip.Children.Add(_slots[i].Element);
        }

        SizeChanged += (_, _) => Fit();
        Loaded += (_, _) => Recolor();
        ActualThemeChanged += (_, _) => Recolor();
    }

    /// <summary>Takes the filmstrip's thumbnails (null: the slots show the Track fill).</summary>
    internal void Attach(ThumbnailCache<SoftwareBitmap>? thumbnails, IErrorSink log)
    {
        _thumbnails = thumbnails;
        _log = log;
    }

    /// <summary>Stops the glide (the window closes).</summary>
    internal void Detach()
    {
        _timer?.Stop();
        _thumbnails = null;
    }

    /// <summary>The export writes frame <paramref name="writing"/> (0-based) of <paramref name="frames"/>; empty when none runs.</summary>
    internal void Show(ImmutableArray<Frame> frames, int writing)
    {
        if (frames.IsDefaultOrEmpty)
        {
            // The export ended: the strip keeps its last picture while the panel leaves.
            _timer?.Stop();
            _ended = true;
            return;
        }

        if (_ended || frames != _frames)
        {
            _ended = false;
            // A new export: the strip starts where it is, without a glide from the last one.
            _frames = frames;
            _shown = _from = _to = writing;
        }

        if (MotionTokens.AnimationsEnabled && !ExportPanel.ReduceMotionSwitch && IsLoaded)
        {
            _from = _shown;
            _to = writing;
            _glideStart = _clock.Elapsed;
            Timer().Start();
        }
        else
        {
            _timer?.Stop();
            _shown = _to = writing;
        }

        Place();
    }

    private DispatcherQueueTimer Timer()
    {
        if (_timer is null)
        {
            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.IsRepeating = true;
            _timer.Tick += OnTick;
        }

        return _timer;
    }

    private void OnTick(DispatcherQueueTimer sender, object args) => (_log ?? NoLog.Instance).RunLogged(
        () =>
        {
            var share = Math.Min(1, (_clock.Elapsed - _glideStart) / ExportViewModel.MinProgressInterval);
            _shown = _from + ((_to - _from) * share);
            Place();
            if (share >= 1)
            {
                sender.Stop();
            }
        },
        ExportText.ProgressFailed);

    // Puts each slot at its place around the gate, with its frame's picture and strength.
    private void Place()
    {
        var count = _frames.IsDefault ? 0 : _frames.Length;
        var first = (int)Math.Floor(_shown);
        var offset = _shown - first;
        var left = (ActualWidth / 2) - (SlotWidth / 2);

        // A frame is at full strength from the moment it reaches the gate, so the dimming moves with the strip.
        var gate = (int)Math.Round(_shown);
        for (var k = -Reach; k <= Reach; k++)
        {
            var slot = _slots[k + Reach];
            var index = first + k;
            if (index < 0 || index >= count)
            {
                slot.Element.Visibility = Visibility.Collapsed;
                slot.Clear();
                continue;
            }

            slot.Element.Visibility = Visibility.Visible;
            slot.Move(left + ((k - offset) * Stride));
            slot.Element.Opacity = index <= gate ? 1 : PendingOpacity;
            slot.Bind(_frames[index].Id, _thumbnails);
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

        // The Track fill behind a slot without a picture, in this element's theme.
        var track = new SolidColorBrush(ThemeTokens.Color("TrackColor", this));
        foreach (var slot in _slots)
        {
            slot.Element.Background = track;
        }
    }

    // One thumbnail place: the Track fill, and the frame's picture over it once the strip has loaded it.
    private sealed class Slot
    {
        private readonly Border _picture;
        private readonly ImageBrush _brush = new() { Stretch = Stretch.UniformToFill };
        private readonly TranslateTransform _move = new();
        private WriteableBitmap? _bitmap;
        private Guid? _id;
        private bool _hasPicture;

        public Slot()
        {
            _picture = new Border { Background = _brush, CornerRadius = new CornerRadius(3), Visibility = Visibility.Collapsed };
            Element = new Border
            {
                Width = SlotWidth,
                Height = SlotHeight,
                CornerRadius = new CornerRadius(3),
                Child = _picture,
                RenderTransform = _move,
            };
            Element.SetValue(Canvas.TopProperty, SlotTop);
        }

        public Border Element { get; }

        public void Move(double x) => _move.X = x;

        public void Clear()
        {
            _id = null;
            _hasPicture = false;
            _picture.Visibility = Visibility.Collapsed;
        }

        // The picture of frame `id`, if the strip has it in memory; tried again on the next placement until it is there.
        public void Bind(Guid id, ThumbnailCache<SoftwareBitmap>? thumbnails)
        {
            if (_id == id && _hasPicture)
            {
                return;
            }

            if (_id != id)
            {
                _id = id;
                _hasPicture = false;
                _picture.Visibility = Visibility.Collapsed;
            }

            using var lease = thumbnails?.Acquire(id);
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
