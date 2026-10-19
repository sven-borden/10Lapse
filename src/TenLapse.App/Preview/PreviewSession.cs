using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
using TenLapse.Core.Diagnostics;
using TenLapse.Core.Preview;
using TenLapse.Media;
using TenLapse.Media.Preview;
using TenLapse.Presentation;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;
using TenLapse.Services;

namespace TenLapse.Preview;

/// <summary>
/// The preview's parts for the window (#19), built once by the composition root: the proxy cache on disk, the frame
/// images in memory, the player and the problem reporter. The views (<see cref="PreviewView"/>,
/// <see cref="TransportBar"/>) attach to it.
/// </summary>
internal sealed class PreviewSession : IDisposable
{
    private static readonly TimeSpan _problemDelay = TimeSpan.FromMilliseconds(600);

    // An unexpected failure is shown at most once in this long; the others in between go to the log only, so a
    // failure that repeats on every retry (a lost device, a bug) does not raise the InfoBar again and again.
    private static readonly long _unexpectedQuiet = (long)TimeSpan.FromMinutes(1).TotalMilliseconds;

    private readonly ShellViewModel _shell;
    private readonly IErrorPresenter _errors;
    private readonly IErrorSink _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _problemTimer;
    private long _unexpectedShownAt = long.MinValue;

    /// <summary>Builds the parts on the UI thread and starts opening the proxy cache in the background.</summary>
    public PreviewSession(ShellViewModel shell, IErrorPresenter errors, IInfoBarService infoBars, ILog fileLog, string cacheFolder)
    {
        _shell = shell;
        _errors = errors;
        var log = new LogErrorSink(fileLog);
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        Proxies = new ProxyCache(cacheFolder, new ProxyCacheOptions { MaxBytes = ProxyCache.DefaultMaxBytes(cacheFolder) })
        {
            CleanupFailed = exception => log.Report(exception, ProjectText.PreviewCacheCleanupFailed),
        };
        _ = Proxies.OpenAsync();

        Images = new FrameImageCache<CanvasBitmap>(new PreviewImageSource(Proxies, () => GraphicsDevice.Shared) { Log = fileLog });
        Player = new PlayerViewModel(shell, errors);
        Problems = new PreviewProblems(infoBars, log);

        _problemTimer = _dispatcher.CreateTimer();
        _problemTimer.Interval = _problemDelay;
        _problemTimer.IsRepeating = false;
        _problemTimer.Tick += OnProblemTimerTick;

        Images.FrameFailed += OnFrameFailed;
        Images.CacheFailed += OnCacheFailed;
        Images.UnexpectedFailure += OnUnexpectedFailure;
        shell.ProjectReplaced += OnProjectReplaced;
    }

    /// <summary>The proxies on disk.</summary>
    public ProxyCache Proxies { get; }

    /// <summary>The frame images in memory, and the background proxy pass.</summary>
    public FrameImageCache<CanvasBitmap> Images { get; }

    /// <summary>The transport.</summary>
    public PlayerViewModel Player { get; }

    /// <summary>What the user is told.</summary>
    public PreviewProblems Problems { get; }

    /// <summary>The error path, for the views' handlers.</summary>
    public IErrorPresenter Errors => _errors;

    /// <summary>The log-only sink, for the views' dispatcher callbacks.</summary>
    public IErrorSink Log => _log;

    /// <summary>The render thread while the preview is in the window (the Debug harness listens to it); set by <see cref="PreviewView"/>.</summary>
    public PreviewRenderLoop? Loop { get; set; }

    public void Dispose()
    {
        _shell.ProjectReplaced -= OnProjectReplaced;
        Images.FrameFailed -= OnFrameFailed;
        Images.CacheFailed -= OnCacheFailed;
        Images.UnexpectedFailure -= OnUnexpectedFailure;
        _problemTimer.Stop();
        _problemTimer.Tick -= OnProblemTimerTick;
        Player.Dispose();
        Images.Dispose();
    }

    /// <summary>
    /// A photo that cannot be read, from any thread: the preview's frames and the filmstrip's thumbnails (#20) gather
    /// into the same InfoBar a moment later, once per photo.
    /// </summary>
    internal void ReportFrameFailure(FrameLoadFailure failure)
    {
        if (Problems.FrameFailed(failure))
        {
            _dispatcher.TryEnqueueGuarded(_log, _problemTimer.Start, ProjectText.PreviewProblemsFailed);
        }
    }

    /// <summary>An unexpected failure of the preview's or the filmstrip's image code, from any thread: shown at most once a minute, logged always.</summary>
    internal void ReportUnexpected(Exception exception) => OnUnexpectedFailure(this, new PreviewFailureEventArgs(exception));

    // Worker thread: gathered, and shown as one bar a moment later on the UI thread.
    private void OnFrameFailed(object? sender, FrameLoadFailedEventArgs e) => ReportFrameFailure(e.Failure);

    private void OnCacheFailed(object? sender, PreviewFailureEventArgs e) => Problems.CacheFailed(e.Exception);

    // Worker threads.
    private void OnUnexpectedFailure(object? sender, PreviewFailureEventArgs e)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _unexpectedShownAt);
        if ((last == long.MinValue || now - last >= _unexpectedQuiet) && Interlocked.CompareExchange(ref _unexpectedShownAt, now, last) == last)
        {
            _errors.Show(e.Exception, ProjectText.PreviewCacheUnexpected);
        }
        else
        {
            _log.Report(e.Exception, ProjectText.PreviewCacheUnexpected);
        }
    }

    private void OnProblemTimerTick(DispatcherQueueTimer sender, object args) => _errors.Guard(Problems.Flush, ProjectText.PreviewProblemsFailed);

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e) => Problems.Reset();
}
