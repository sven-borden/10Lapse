using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
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

/// <summary>The colors the render thread draws with, read from the tokens on the UI thread.</summary>
/// <param name="Gate">The gate behind a frame that has no picture yet, or cannot be read (Letterbox).</param>
/// <param name="Text">The placeholder's text on the gate.</param>
internal readonly record struct PreviewColors(Color Gate, Color Text);

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
/// seek, an image arriving, a resize, the theme.
/// </para>
/// <para>
/// The swap chain is made and given to the panel on the UI thread; everything else happens here. The image drawn
/// last stays leased, so the cache cannot free it while it may be drawn again (a resize). A lost graphics device frees
/// the images and makes a new swap chain on the new device; any other drawing failure is told once.
/// </para>
/// </remarks>
internal sealed class PreviewRenderLoop : IDisposable
{
    private const float GateRadius = 6;
    private const int MinLookahead = 6;
    private const int MaxLookahead = 30;

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
    private PreviewColors _colors;
    private CanvasSwapChain? _offered;
    private bool _recreating;
    private bool _chainMade;
    private long _changes;
    private volatile bool _stop;

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

    /// <summary>New colors (theme or high contrast changed; UI thread).</summary>
    public void SetColors(PreviewColors colors)
    {
        lock (_gate)
        {
            _colors = colors;
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
        PreviewColors colors;
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
            colors = _colors;
            changes = _changes;
        }

        var chain = _swapChain;
        var state = _player.State;
        if (chain is null || size.Width < 1 || size.Height < 1)
        {
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
                using (chain.CreateDrawingSession(Colors.Transparent))
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
        var failure = lease is null ? _images.FailureOf(frame.Id) : null;
        var settingsChanged = state.Version != _shownVersion || changes != _drawnChanges || resized;
        bool draw;
        if (lease is not null)
        {
            draw = settingsChanged || frame.Id != _shownId || _shownPlaceholder || lease.Quality > _shownQuality;
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
            Draw(chain, state.Snapshot, lease?.Image ?? (lease is null && failure is null ? _shown?.Image : null), failure, frame, colors);
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
            var before = Stopwatch.GetTimestamp();
            if (!draw)
            {
                chain.WaitForVerticalBlank();
            }

            // A minimized window has no vertical blank to wait for: do not spin.
            if (Stopwatch.GetTimestamp() - before < Stopwatch.Frequency / 1000 && !draw)
            {
                Thread.Sleep(8);
            }
        }
        else
        {
            _wake.WaitOne(500);
        }
    }

    private void Draw(CanvasSwapChain chain, ProjectSnapshot snapshot, CanvasBitmap? image, FrameLoadFailure? failure, Frame frame, PreviewColors colors)
    {
        var video = snapshot.VideoSize;
        var size = chain.Size;
        using var session = chain.CreateDrawingSession(Colors.Transparent);
        var gate = GateRect(size, video);
        if (gate.Width < 1 || gate.Height < 1)
        {
            return;
        }

        using var clip = CanvasGeometry.CreateRoundedRectangle(session, gate, GateRadius, GateRadius);
        using var layer = session.CreateLayer(1f, clip);
        if (image is null)
        {
            session.FillRectangle(gate, colors.Gate);
            if (failure is not null)
            {
                DrawPlaceholder(session, gate, frame, colors);
            }

            return;
        }

        // FrameRenderer draws video.Width pixels of the target, which is video.Width * 96 / dpi DIPs: scale that onto the gate.
        var frameDips = session.ConvertPixelsToDips(video.Width);
        var scale = (float)(gate.Width / frameDips);
        session.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation((float)gate.X, (float)gate.Y);
        FrameRenderer.Draw(session, image, snapshot.FrameSettings, video);
    }

    private static void DrawPlaceholder(CanvasDrawingSession session, Rect gate, Frame frame, PreviewColors colors)
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
        session.DrawText("", new Rect(gate.X, gate.Y, gate.Width, middle - gate.Y - 4), colors.Text, glyph);
        var text = ProjectText.PreviewPlaceholder(frame.FileName);
        session.DrawText(text, new Rect(gate.X + 16, middle + 4, Math.Max(1, gate.Width - 32), 20), colors.Text, caption);
    }

    /// <summary>The frame's place in a <paramref name="area"/> (DIPs): as large as fits at the video's shape, centred.</summary>
    internal static Rect GateRect(Size area, PixelSize video)
    {
        if (area.Width <= 0 || area.Height <= 0 || video.Width <= 0 || video.Height <= 0)
        {
            return default;
        }

        var scale = Math.Min(area.Width / video.Width, area.Height / video.Height);
        var width = Math.Round(video.Width * scale);
        var height = Math.Round(video.Height * scale);
        return new Rect(Math.Round((area.Width - width) / 2), Math.Round((area.Height - height) / 2), width, height);
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
        _images.Clear();
        try
        {
            chain.Device.RaiseDeviceLost();
        }
        catch (Exception exception)
        {
            _log.Report(exception, ProjectText.PreviewDrawFailed);
        }

        lock (_gate)
        {
            if (_recreating)
            {
                return;
            }

            _recreating = true;
        }

        _dispatcher.TryEnqueueGuarded(_log, Recreate, ProjectText.PreviewDrawFailed);
    }

    // UI thread: a new swap chain on the new shared device.
    private void Recreate()
    {
        Size size;
        float dpi;
        lock (_gate)
        {
            _recreating = false;
            size = _size;
            dpi = _dpi;
        }

        if (_stop || size.Width < 1 || size.Height < 1)
        {
            return;
        }

        var created = new CanvasSwapChain(GraphicsDevice.Shared, (float)size.Width, (float)size.Height, dpi);
        lock (_gate)
        {
            _offered = created;
            _changes++;
        }

        _panel.SwapChain = created;
        _wake.Set();
    }
}
