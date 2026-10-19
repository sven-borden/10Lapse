using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Media;
using TenLapse.Media.Rendering;
using TenLapse.Presentation;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Themes;
using Windows.Foundation;
using Windows.UI;

namespace TenLapse.Preview;

/// <summary>What the render thread draws with, read from the tokens on the UI thread.</summary>
/// <param name="Ground">Around the gate: the Layer the stage sits on (drawn, not left transparent, so the corners blend the same everywhere).</param>
/// <param name="Gate">The gate behind a frame that has no picture yet, or cannot be read (Letterbox).</param>
/// <param name="Text">The placeholder's text on the gate.</param>
/// <param name="Radius">The gate's corners (RadiusCard), in DIPs.</param>
internal readonly record struct PreviewLook(Color Ground, Color Gate, Color Text, float Radius);

/// <summary>
/// The preview's render thread (#19): draws the frame the player says belongs on screen into a swap chain, through
/// <see cref="FrameRenderer.Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize)"/> with the snapshot's
/// frame settings and video size, the exact call the export makes; the gate only places and rounds it.
/// </summary>
/// <remarks>
/// <para>
/// While playing it wakes on every vertical blank, asks the <see cref="PlaybackClock"/> which frame belongs on screen
/// now and draws it when it changed, so a slow moment drops frames instead of drifting. A frame whose image is not
/// in memory yet keeps the previous picture (counted as held). While paused it sleeps until something changes: a
/// seek, an image arriving, a resize, the theme. A minimized or hidden window pauses the player
/// (<see cref="PlayerViewModel.SetWindowShown"/>), so nothing is drawn unseen.
/// </para>
/// <para>
/// The swap chain is made and given to the panel on the UI thread; everything else happens here. The image drawn
/// last stays leased, so the cache cannot free it while it may be drawn again (a resize). A lost graphics device frees
/// the images, starts a new generation in the cache (loads in flight on the old device are dropped) and makes a new
/// swap chain on the new device, trying again with a growing pause when that fails; any other drawing failure is told
/// once.
/// </para>
/// </remarks>
internal sealed class PreviewRenderLoop : IDisposable
{
    private const int MinLookahead = 6;
    private const int MaxLookahead = 30;

    // A vertical blank that returns at once again and again means there is none to wait for (a display that is off):
    // after this many in a row the loop sleeps a moment instead of spinning. One quick return is normal (a blank had
    // just passed), so a single one never sleeps and no blank is skipped on a fast display.
    private const int QuickBlanksBeforeSleep = 3;

    private static readonly TimeSpan _longestRetry = TimeSpan.FromSeconds(30);

    private readonly CanvasSwapChainPanel _panel;
    private readonly PlayerViewModel _player;
    private readonly FrameImageCache<CanvasBitmap> _images;
    private readonly IErrorPresenter _errors;
    private readonly IErrorSink _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Lock _gate = new();

    // Shared with the UI thread, under _gate.
    private Size _size;
    private float _dpi = 96;
    private PreviewLook _look;
    private CanvasSwapChain? _offered;
    private bool _chainMade;
    private long _changes;
    private volatile bool _stop;

    // A new swap chain is needed (the device was lost); one is being made on the UI thread; when the next try may
    // start (Stopwatch ticks); how many tries in a row failed.
    private bool _needsChain;
    private bool _recreating;
    private long _recreateDue;
    private int _recreateFailures;

    // The render thread's own.
    private CanvasSwapChain? _swapChain;
    private Size _drawnSize;
    private float _drawnDpi;
    private long _drawnChanges = -1;
    private FrameImageLease<CanvasBitmap>? _shown;
    private Guid _shownId;
    private FrameImageQuality _shownQuality;
    private bool _shownPlaceholder;
    private bool _drewAnything;
    private long _shownVersion = -1;
    private ImmutableArray<Frame> _frames = [];
    private bool _drawFailureTold;
    private int _quickBlanks;

    // The motion (#35). Shared under _gate: a develop the UI thread asked for, and when. The render thread's own: the
    // develop and the reframe that run, and what the last draw put on screen (the gate and the whole photo's rectangle,
    // shown and aimed at, the shape and fit they were for, the panel size).
    private MotionStep? _developAsked;
    private long _developAskedAt;
    private MotionStep? _develop;
    private long _developStart;
    private long _developDeadline;
    private bool _developWaited;
    private Reframe? _reframe;
    private Rect _shownGate;
    private Rect _shownPhoto;
    private Rect _aimedGate;
    private Rect _aimedPhoto;
    private PixelSize _shownVideo;
    private FitMode _shownFit;
    private Size _shownPanel;
    private bool _shownPicture;

    // The snapshot the last pass drew for: a project with no photos or another project forgets what was shown.
    private ProjectSnapshot? _drawnSnapshot;

    public PreviewRenderLoop(CanvasSwapChainPanel panel, PlayerViewModel player, FrameImageCache<CanvasBitmap> images, IErrorPresenter errors, IErrorSink log)
    {
        _panel = panel;
        _player = player;
        _images = images;
        _errors = errors;
        _log = log;
        _dispatcher = panel.DispatcherQueue;
        _player.StateChanged += OnChanged;
        _images.ImageReady += OnImageReady;
        _thread = new Thread(Run) { IsBackground = true, Name = "Preview render", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>
    /// Every picture presented while playing: the moment after the present, the clock's reading and what was shown.
    /// Raised on the render thread; the Debug harness records the pacing with it.
    /// </summary>
    public event Action<long, PlaybackReading, PresentedImage>? Presented;

    /// <summary>A frame was presented with its own image (any quality), while paused or playing: the frame and the moment. Render thread.</summary>
    public event Action<int, FrameImageQuality, long>? FrameShown;

    /// <summary>The panel's size in DIPs and its scale changed (UI thread). Makes the swap chain when there is none yet.</summary>
    public void Resize(Size size, float compositionScale)
    {
        var dpi = 96 * Math.Max(0.25f, compositionScale);
        CanvasSwapChain? created = null;
        lock (_gate)
        {
            _size = size;
            _dpi = dpi;
            _changes++;
            if (!_chainMade && size.Width >= 1 && size.Height >= 1)
            {
                created = new CanvasSwapChain(GraphicsDevice.Shared, (float)size.Width, (float)size.Height, dpi);
                _offered = created;
                _chainMade = true;
            }
        }

        if (created is not null)
        {
            _panel.SwapChain = created;
        }

        _wake.Set();
    }

    /// <summary>New colors and corners (theme or high contrast changed; UI thread).</summary>
    public void SetLook(PreviewLook look)
    {
        lock (_gate)
        {
            _look = look;
            _changes++;
        }

        _wake.Set();
    }

    /// <summary>
    /// The photos landed (UI thread; motion spec, Arrival): the first frame develops like a print, blur 14 to 0 px, scale
    /// 1.03 to 1 and opacity in over 700 ms Enter, after the drop zone's exit; with animations off, a short fade. Its clock
    /// starts when the first picture is there to draw (within <see cref="DevelopPatience"/>; after that it shows at rest).
    /// </summary>
    public void Develop()
    {
        var step = MotionTokens.Plan(MotionCatalogue.ArrivalDevelop);
        lock (_gate)
        {
            _developAsked = step.Animates ? step : null;
            _developAskedAt = Stopwatch.GetTimestamp();
            _changes++;
        }

        _wake.Set();
    }

    private static TimeSpan DevelopPatience => MotionSpec.DevelopPatience;

    public void Dispose()
    {
        _stop = true;
        _player.StateChanged -= OnChanged;
        _images.ImageReady -= OnImageReady;
        _wake.Set();
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private static int Lookahead(Core.Timing.Fraction rate) =>
        Math.Clamp((int)Math.Ceiling(rate.ToDouble() / 2), MinLookahead, MaxLookahead);

    // 1, 2, 4, 8 and 16 s, then every 30 s.
    private static long RetryDelay(int failures) =>
        (long)(Math.Min(_longestRetry.TotalSeconds, Math.Pow(2, Math.Min(failures - 1, 5))) * Stopwatch.Frequency);

    private void OnChanged(object? sender, EventArgs e) => _wake.Set();

    private void OnImageReady(object? sender, FrameImageReadyEventArgs e) => _wake.Set();

    private void Run()
    {
        while (!_stop)
        {
            try
            {
                Step();
            }
            catch (Exception exception) when (_swapChain is { } chain && chain.Device.IsDeviceLost(exception.HResult))
            {
                RecoverFromDeviceLoss(chain);
            }
            catch (Exception exception)
            {
                if (!_drawFailureTold)
                {
                    _drawFailureTold = true;
                    _errors.Show(exception, ProjectText.PreviewDrawFailed);
                }
                else
                {
                    _log.Report(exception, ProjectText.PreviewDrawFailed);
                }

                // Do not spin on a failure that repeats.
                _wake.WaitOne(250);
            }
        }

        // The wake event stays undisposed: the UI thread may still set it after the loop has gone.
        _shown?.Dispose();
        _shown = null;
        _swapChain = null;
    }

    private void Step()
    {
        Size size;
        float dpi;
        PreviewLook look;
        long changes;
        var developTaken = false;
        lock (_gate)
        {
            if (_offered is not null)
            {
                _swapChain = _offered;
                _offered = null;
                _drawnSize = default;
            }

            size = _size;
            dpi = _dpi;
            look = _look;
            changes = _changes;
            if (_developAsked is { } asked)
            {
                _develop = asked;
                _developStart = _developAskedAt;
                _developWaited = false;
                _developDeadline = _developAskedAt + (long)(DevelopPatience.TotalSeconds * Stopwatch.Frequency);
                _developAsked = null;
                developTaken = true;
            }
        }

        var chain = _swapChain;
        var state = _player.State;

        // The photos landed, or the project has none or is another one: nothing shown before is held or reframed from.
        if (developTaken || PreviewContinuity.StartsAfresh(_drawnSnapshot, state.Snapshot))
        {
            ForgetShown();
        }

        _drawnSnapshot = state.Snapshot;
        if (chain is null || size.Width < 1 || size.Height < 1)
        {
            RequestChain();
            _wake.WaitOne(250);
            return;
        }

        var resized = size != _drawnSize || dpi != _drawnDpi;
        if (resized)
        {
            chain.ResizeBuffers((float)size.Width, (float)size.Height, dpi);
            _drawnSize = size;
            _drawnDpi = dpi;
        }

        var frames = state.Snapshot.Frames;
        if (frames != _frames)
        {
            _frames = frames;
            _images.SetFrames(frames);
        }

        var now = Stopwatch.GetTimestamp();
        PlaybackReading? reading = state.Clock?.Read(now);
        if (frames.IsEmpty)
        {
            if (resized || changes != _drawnChanges || _shownVersion != state.Version)
            {
                using (chain.CreateDrawingSession(look.Ground))
                {
                }

                chain.Present();
                _shownVersion = state.Version;
                _drawnChanges = changes;
            }

            _wake.WaitOne(500);
            return;
        }

        var index = Math.Clamp(reading?.Frame ?? state.Frame, 0, frames.Length - 1);
        var frame = frames[index];
        _images.SetFocus(index, state.IsPlaying, Lookahead(state.Clock?.Rate ?? state.Snapshot.EffectiveRate.Rate));

        var lease = _images.Acquire(frame.Id);

        // Only a photo that cannot be read has a failure here; a load that threw (a lost device, a bug) keeps the
        // previous picture, as a frame still loading does.
        var failure = lease is null ? _images.FailureOf(frame.Id) : null;
        var settingsChanged = state.Version != _shownVersion || changes != _drawnChanges || resized;
        bool draw;
        if (lease is not null)
        {
            // A new image for the same frame (better, or the photo changed on disk) is drawn too.
            draw = settingsChanged || frame.Id != _shownId || _shownPlaceholder || !ReferenceEquals(lease.Image, _shown?.Image);
        }
        else if (failure is not null)
        {
            draw = settingsChanged || frame.Id != _shownId || !_shownPlaceholder;
        }
        else
        {
            // Not loaded yet: the previous picture stays, unless the window changed under it.
            draw = resized || changes != _drawnChanges || !_drewAnything;
        }

        // A develop or a reframe draws every pass (each present waits for the vertical blank) until it ends; only a draw the
        // picture itself needed counts for the events below.
        var animating = _develop is not null || _reframe is not null;
        var needed = draw;
        draw |= animating;
        if (draw)
        {
            Draw(chain, state.Snapshot, lease?.Image ?? (lease is null && failure is null ? _shown?.Image : null), lease is not null, failure, frame, look, now);
            chain.Present();
            var presented = Stopwatch.GetTimestamp();
            _drewAnything = true;
            _drawnChanges = changes;
            _shownVersion = state.Version;
            if (lease is not null)
            {
                _shown?.Dispose();
                _shown = lease;
                lease = null;
                _shownId = frame.Id;
                _shownQuality = _shown.Quality;
                _shownPlaceholder = false;
                if (needed)
                {
                    FrameShown?.Invoke(index, _shownQuality, presented);
                }
            }
            else if (failure is not null)
            {
                _shown?.Dispose();
                _shown = null;
                _shownId = frame.Id;
                _shownQuality = FrameImageQuality.None;
                _shownPlaceholder = true;
            }

            if (needed && reading is { } shownReading)
            {
                Presented?.Invoke(presented, shownReading, _shownId == frame.Id && !_shownPlaceholder
                    ? (_shownQuality == FrameImageQuality.Proxy ? PresentedImage.Proxy : PresentedImage.Quick)
                    : PresentedImage.Held);
            }
        }
        else if (reading is { } heldReading && lease is null && failure is null && frame.Id != _shownId)
        {
            Presented?.Invoke(now, heldReading, PresentedImage.Held);
        }

        lease?.Dispose();
        if (animating)
        {
            // The present waited for the blank; the next pass draws the next step.
            _quickBlanks = 0;
        }
        else if (state.IsPlaying)
        {
            if (!draw)
            {
                WaitForBlank(chain);
            }
        }
        else
        {
            _quickBlanks = 0;
            _wake.WaitOne(500);
        }
    }

    // Playing and nothing new to present: wait for the next vertical blank (a present waits by itself).
    private void WaitForBlank(CanvasSwapChain chain)
    {
        var before = Stopwatch.GetTimestamp();
        chain.WaitForVerticalBlank();
        if (Stopwatch.GetTimestamp() - before >= Stopwatch.Frequency / 1000)
        {
            _quickBlanks = 0;
        }
        else if (++_quickBlanks >= QuickBlanksBeforeSleep)
        {
            _wake.WaitOne(8);
        }
    }

    // image: the frame's own picture (current), or the previous frame's, held while this one loads.
    private void Draw(CanvasSwapChain chain, ProjectSnapshot snapshot, CanvasBitmap? image, bool current, FrameLoadFailure? failure, Frame frame, PreviewLook look, long now)
    {
        var video = snapshot.VideoSize;
        var fit = snapshot.FrameSettings.Fit;
        var size = chain.Size;
        using var session = chain.CreateDrawingSession(look.Ground);
        var place = PreviewLayout.Gate(size.Width, size.Height, video);
        if (place.IsEmpty)
        {
            _reframe = null;
            return;
        }

        var gate = new Rect(place.X, place.Y, place.Width, place.Height);
        var photo = image is null ? gate : PhotoRect(gate, video, fit, image);
        var (shownGate, shownPhoto) = Reframing(gate, photo, image is not null, video, fit, size, now);
        var (opacity, blur, grow) = Developing(current, now);
        _shownGate = shownGate;
        _shownPhoto = shownPhoto;
        _aimedGate = gate;
        _aimedPhoto = photo;
        _shownVideo = video;
        _shownFit = fit;
        _shownPanel = size;
        _shownPicture = image is not null;
        if (opacity <= 0)
        {
            // The develop's delay: the stage only, until the drop zone has gone.
            return;
        }

        // The develop's scale grows the whole gate around its middle; the clip follows the reframe.
        var center = new Vector2((float)(gate.X + (gate.Width / 2)), (float)(gate.Y + (gate.Height / 2)));
        var develop = grow == 1f ? Matrix3x2.Identity : Matrix3x2.CreateScale(grow, center);
        session.Transform = develop;
        using var clip = CanvasGeometry.CreateRoundedRectangle(session, shownGate, look.Radius, look.Radius);
        using var layer = session.CreateLayer(opacity, clip);
        if (image is null)
        {
            session.FillRectangle(shownGate, look.Gate);
            if (failure is not null)
            {
                DrawPlaceholder(session, shownGate, frame, look);
            }

            return;
        }

        if (blur <= 0)
        {
            DrawPicture(session, snapshot, image, gate, shownGate, shownPhoto, develop);
            return;
        }

        // Developing: the same picture through a blur that clears.
        using var picture = new CanvasCommandList(session);
        using (var inner = picture.CreateDrawingSession())
        {
            DrawPicture(inner, snapshot, image, gate, shownGate, shownPhoto, Matrix3x2.Identity);
        }

        using var effect = new GaussianBlurEffect { Source = picture, BlurAmount = blur, BorderMode = EffectBorderMode.Hard };
        session.DrawImage(effect);
    }

    // At rest the frame is FrameRenderer's, the export's own drawing. Only while the gate reshapes is the photo placed by
    // hand: the whole photo at its moving rectangle on the gate's black, inside the moving clip; the reframe's end is
    // FrameRenderer's picture again.
    private void DrawPicture(CanvasDrawingSession session, ProjectSnapshot snapshot, CanvasBitmap image, Rect gate, Rect shownGate, Rect shownPhoto, Matrix3x2 develop)
    {
        var video = snapshot.VideoSize;
        if (_reframe is not null)
        {
            session.Transform = develop;
            session.FillRectangle(shownGate, FrameRenderer.Background);
            session.DrawImage(image, shownPhoto, new Rect(0, 0, image.Size.Width, image.Size.Height), 1f, CanvasImageInterpolation.HighQualityCubic);
            return;
        }

        // FrameRenderer draws video.Width pixels of the target, which is video.Width * 96 / dpi DIPs: scale that onto the gate.
        var frameDips = session.ConvertPixelsToDips(video.Width);
        var scale = (float)(gate.Width / frameDips);
        session.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation((float)gate.X, (float)gate.Y) * develop;
        FrameRenderer.Draw(session, image, snapshot.FrameSettings, video);
    }

    // The reframe (motion spec, Reframe): a new shape or fit at the same panel size moves the gate's clip and the photo's
    // rectangle from where they are on screen to their new places, 400 ms Move; with animations off, at once. A resize ends
    // it (the old places no longer mean anything). Returns the gate and the photo to draw now.
    private (Rect Gate, Rect Photo) Reframing(Rect gate, Rect photo, bool hasPicture, PixelSize video, FitMode fit, Size panel, long now)
    {
        if (panel != _shownPanel || _develop is not null)
        {
            // A resize, or the arrival: while the photos develop, a shape that settles as their sizes become known (Match
            // photos) is not a reframe; the gate takes it at once.
            _reframe = null;
        }
        else if (hasPicture && _shownPicture && (video != _shownVideo || fit != _shownFit) && (gate != _aimedGate || photo != _aimedPhoto))
        {
            var step = MotionTokens.Plan(MotionCatalogue.ReframeGate);
            MotionTokens.Plan(MotionCatalogue.ReframePhoto);
            _reframe = step.Animates ? new Reframe(_shownGate, _shownPhoto, now, step) : null;
        }

        if (_reframe is not { } reframe)
        {
            return (gate, photo);
        }

        var share = Stopwatch.GetElapsedTime(reframe.Start, now) / reframe.Step.Duration;
        if (share >= 1)
        {
            _reframe = null;
            return (gate, photo);
        }

        var eased = MotionSpec.Ease(reframe.Step.Easing, share);
        return (Lerp(reframe.FromGate, gate, eased), Lerp(reframe.FromPhoto, photo, eased));
    }

    // The develop now: the gate's opacity, the blur in DIPs and the scale. Its clock waits for the first picture of the
    // frame on screen (a picture held from another frame does not count).
    private (float Opacity, float Blur, float Scale) Developing(bool hasPicture, long now)
    {
        if (_develop is not { } develop)
        {
            return (1f, 0f, 1f);
        }

        if (!hasPicture)
        {
            if (now > _developDeadline)
            {
                _develop = null;
                return (1f, 0f, 1f);
            }

            _developStart = now;
            _developWaited = true;
            return (0f, 0f, 1f);
        }

        if (_developWaited)
        {
            // The clock starts now, on the first picture: an InfoBar waits for the develop's real end.
            _developWaited = false;
            MotionTokens.Quiet.Hold(develop);
        }

        var since = Stopwatch.GetElapsedTime(_developStart, now) - develop.Delay;
        if (since < TimeSpan.Zero)
        {
            return (0f, 0f, 1f);
        }

        var share = since / develop.Duration;
        if (share >= 1)
        {
            _develop = null;
            return (1f, 0f, 1f);
        }

        var eased = (float)MotionSpec.Ease(develop.Easing, share);
        return develop.Moves
            ? (eased, MotionSpec.DevelopBlur * (1 - eased), MotionSpec.DevelopScale - ((MotionSpec.DevelopScale - 1) * eased))
            : (eased, 0f, 1f);
    }

    // Forgets the picture on screen and where the gate and the photo were: the next draw starts afresh, with no picture
    // held from the old photos and no reframe from their shape.
    private void ForgetShown()
    {
        _shown?.Dispose();
        _shown = null;
        _shownId = Guid.Empty;
        _shownQuality = FrameImageQuality.None;
        _shownPlaceholder = false;
        _drewAnything = false;
        _shownPicture = false;
        _shownVideo = default;
        _shownFit = default;
        _shownPanel = default;
        _shownGate = default;
        _shownPhoto = default;
        _aimedGate = default;
        _aimedPhoto = default;
        _reframe = null;
    }

    // Where the whole photo lies on screen when FrameRenderer draws it into the gate (Fill overflows the gate, Fit fits in it).
    private static Rect PhotoRect(Rect gate, PixelSize video, FitMode fit, CanvasBitmap image)
    {
        var pixels = image.SizeInPixels;
        var photo = new PixelSize((int)pixels.Width, (int)pixels.Height);
        if (photo.Width <= 0 || photo.Height <= 0 || video.Width <= 0)
        {
            return gate;
        }

        var (source, destination) = FrameRenderer.Layout(photo, video, fit);
        var perPhotoPixel = destination.Width / source.Width;
        var toScreen = gate.Width / video.Width;
        return new Rect(
            gate.X + ((destination.X - (source.X * perPhotoPixel)) * toScreen),
            gate.Y + ((destination.Y - (source.Y * perPhotoPixel)) * toScreen),
            photo.Width * perPhotoPixel * toScreen,
            photo.Height * perPhotoPixel * toScreen);
    }

    private static Rect Lerp(Rect from, Rect to, double share) => new(
        from.X + ((to.X - from.X) * share),
        from.Y + ((to.Y - from.Y) * share),
        Math.Max(0, from.Width + ((to.Width - from.Width) * share)),
        Math.Max(0, from.Height + ((to.Height - from.Height) * share)));

    // A reframe running: where the gate and the photo were on screen when it began, when, and its step.
    private sealed record Reframe(Rect FromGate, Rect FromPhoto, long Start, MotionStep Step);

    private static void DrawPlaceholder(CanvasDrawingSession session, Rect gate, Frame frame, PreviewLook look)
    {
        using var glyph = new CanvasTextFormat
        {
            FontFamily = "Segoe Fluent Icons",
            FontSize = 24,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Bottom,
        };
        using var caption = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text",
            FontSize = 12,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
        };
        var middle = gate.Y + (gate.Height / 2);
        session.DrawText("", new Rect(gate.X, gate.Y, gate.Width, middle - gate.Y - 4), look.Text, glyph);
        var text = ProjectText.PreviewPlaceholder(frame.FileName);
        session.DrawText(text, new Rect(gate.X + 16, middle + 4, Math.Max(1, gate.Width - 32), 20), look.Text, caption);
    }

    private void RecoverFromDeviceLoss(CanvasSwapChain chain)
    {
        _log.Report(new InvalidOperationException("The graphics device was lost; the preview starts again on a new one."), ProjectText.PreviewDrawFailed);
        _shown?.Dispose();
        _shown = null;
        _shownId = Guid.Empty;
        _shownPlaceholder = false;
        _drewAnything = false;
        _swapChain = null;
        try
        {
            // First, so the shared device is a new one from here on.
            chain.Device.RaiseDeviceLost();
        }
        catch (Exception exception)
        {
            _log.Report(exception, ProjectText.PreviewDrawFailed);
        }

        // Then a new generation: loads still in flight made their images on the old device, and are freed when they
        // come back (review M3).
        _images.Clear();
        lock (_gate)
        {
            _needsChain = true;
            _recreateDue = 0;
        }

        RequestChain();
    }

    // Render thread: asks the UI thread for a new swap chain when one is needed and its retry time has come.
    private void RequestChain()
    {
        lock (_gate)
        {
            if (!_needsChain || _recreating || _stop || Stopwatch.GetTimestamp() < _recreateDue)
            {
                return;
            }

            _recreating = true;
        }

        if (!_dispatcher.TryEnqueueGuarded(_log, Recreate, ProjectText.PreviewRestartFailed))
        {
            // The window is closing.
            lock (_gate)
            {
                _recreating = false;
            }
        }
    }

    // UI thread: a new swap chain on the new shared device. A failure is told once (and logged after that) and tried
    // again later, 1 s at first, then less often, up to every 30 s; the render thread asks for it.
    private void Recreate()
    {
        Size size;
        float dpi;
        lock (_gate)
        {
            size = _size;
            dpi = _dpi;
        }

        if (_stop || size.Width < 1 || size.Height < 1)
        {
            // Nothing to make yet; the render thread asks again.
            lock (_gate)
            {
                _recreating = false;
            }

            return;
        }

        CanvasSwapChain? created = null;
        try
        {
            created = new CanvasSwapChain(GraphicsDevice.Shared, (float)size.Width, (float)size.Height, dpi);
            _panel.SwapChain = created;
        }
        catch (Exception exception)
        {
            created?.Dispose();
            int failures;
            lock (_gate)
            {
                _recreating = false;
                failures = ++_recreateFailures;
                _recreateDue = Stopwatch.GetTimestamp() + RetryDelay(failures);
            }

            if (failures == 1)
            {
                _errors.Show(exception, ProjectText.PreviewRestartFailed);
            }
            else
            {
                _log.Report(exception, ProjectText.PreviewRestartFailed);
            }

            return;
        }

        lock (_gate)
        {
            _recreating = false;
            _needsChain = false;
            _recreateFailures = 0;
            _offered = created;
            _changes++;
        }

        _wake.Set();
    }
}
