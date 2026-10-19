using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Media;
using TenLapse.Media.Rendering;
using TenLapse.Presentation;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;
using TenLapse.Services;
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
        }

        var chain = _swapChain;
        var state = _player.State;
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

        if (draw)
        {
            Draw(chain, state.Snapshot, lease?.Image ?? (lease is null && failure is null ? _shown?.Image : null), failure, frame, look);
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
                FrameShown?.Invoke(index, _shownQuality, presented);
            }
            else if (failure is not null)
            {
                _shown?.Dispose();
                _shown = null;
                _shownId = frame.Id;
                _shownQuality = FrameImageQuality.None;
                _shownPlaceholder = true;
            }

            if (reading is { } shownReading)
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
        if (state.IsPlaying)
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

    private void Draw(CanvasSwapChain chain, ProjectSnapshot snapshot, CanvasBitmap? image, FrameLoadFailure? failure, Frame frame, PreviewLook look)
    {
        var video = snapshot.VideoSize;
        var size = chain.Size;
        using var session = chain.CreateDrawingSession(look.Ground);
        var place = PreviewLayout.Gate(size.Width, size.Height, video);
        if (place.IsEmpty)
        {
            return;
        }

        var gate = new Rect(place.X, place.Y, place.Width, place.Height);
        using var clip = CanvasGeometry.CreateRoundedRectangle(session, gate, look.Radius, look.Radius);
        using var layer = session.CreateLayer(1f, clip);
        if (image is null)
        {
            session.FillRectangle(gate, look.Gate);
            if (failure is not null)
            {
                DrawPlaceholder(session, gate, frame, look);
            }

            return;
        }

        // FrameRenderer draws video.Width pixels of the target, which is video.Width * 96 / dpi DIPs: scale that onto the gate.
        var frameDips = session.ConvertPixelsToDips(video.Width);
        var scale = (float)(gate.Width / frameDips);
        session.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation((float)gate.X, (float)gate.Y);
        FrameRenderer.Draw(session, image, snapshot.FrameSettings, video);
    }

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
