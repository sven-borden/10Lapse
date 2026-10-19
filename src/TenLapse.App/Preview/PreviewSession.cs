using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
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

    private readonly ShellViewModel _shell;
    private readonly IErrorPresenter _errors;
    private readonly IErrorSink _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _problemTimer;

    /// <summary>Builds the parts on the UI thread and starts opening the proxy cache in the background.</summary>
    public PreviewSession(ShellViewModel shell, IErrorPresenter errors, IInfoBarService infoBars, IErrorSink log, string cacheFolder)
    {
        _shell = shell;
        _errors = errors;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        Proxies = new ProxyCache(cacheFolder, new ProxyCacheOptions { MaxBytes = ProxyCache.DefaultMaxBytes(cacheFolder) })
        {
            CleanupFailed = exception => log.Report(exception, ProjectText.PreviewCacheCleanupFailed),
        };
        _ = Proxies.OpenAsync();

        Images = new FrameImageCache<CanvasBitmap>(new PreviewImageSource(Proxies, () => GraphicsDevice.Shared));
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

    // Worker thread: gathered, and shown as one bar a moment later on the UI thread.
    private void OnFrameFailed(object? sender, FrameLoadFailedEventArgs e)
    {
        if (Problems.FrameFailed(e.Failure))
        {
            _dispatcher.TryEnqueueGuarded(_log, _problemTimer.Start, ProjectText.PreviewProblemsFailed);
        }
    }

    private void OnCacheFailed(object? sender, PreviewFailureEventArgs e) => Problems.CacheFailed(e.Exception);

    private void OnUnexpectedFailure(object? sender, PreviewFailureEventArgs e) => _errors.Show(e.Exception, ProjectText.PreviewCacheUnexpected);

    private void OnProblemTimerTick(DispatcherQueueTimer sender, object args) => _errors.Guard(Problems.Flush, ProjectText.PreviewProblemsFailed);

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e) => Problems.Reset();
}
