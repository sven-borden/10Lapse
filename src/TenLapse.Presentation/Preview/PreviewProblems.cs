using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Preview;

/// <summary>
/// Tells the user what went wrong in the preview (#19, never silent), without a bar per frame: photos that cannot be
/// read are gathered and shown as one Caution InfoBar with a detail list, which a later flush replaces with the
/// longer list; a proxy cache that cannot be written is told once. Everything is logged as it happens.
/// </summary>
/// <remarks>
/// <see cref="FrameFailed"/> and <see cref="CacheFailed"/> are safe on any thread (the cache's workers raise them).
/// The App flushes on the UI thread a moment after the first new failure, so a burst becomes one bar.
/// </remarks>
public sealed class PreviewProblems
{
    private readonly Lock _gate = new();
    private readonly IInfoBarService _infoBars;
    private readonly IErrorSink _log;
    private readonly List<SkippedFile> _unreadable = [];
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private InfoBarTicket? _shown;
    private bool _pending;
    private bool _cacheTold;

    /// <summary>Shows through <paramref name="infoBars"/> and logs through <paramref name="log"/>.</summary>
    public PreviewProblems(IInfoBarService infoBars, IErrorSink log)
    {
        _infoBars = infoBars ?? throw new ArgumentNullException(nameof(infoBars));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>A frame cannot be shown. True when it is the first failure since the last <see cref="Flush"/>: schedule one.</summary>
    public bool FrameFailed(FrameLoadFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _log.Report(failure.Exception ?? new IOException(failure.File.Message), ProjectText.Format(ProjectText.PreviewFrameFailedLog, failure.File.Path));
        lock (_gate)
        {
            if (!_known.Add(failure.File.Path))
            {
                return false;
            }

            _unreadable.Add(failure.File);
            var first = !_pending;
            _pending = true;
            return first;
        }
    }

    /// <summary>The proxy cache failed: logged each time, told once per session (the preview still works, slower).</summary>
    public void CacheFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _log.Report(exception, ProjectText.PreviewCacheFailedLog);
        lock (_gate)
        {
            if (_cacheTold)
            {
                return;
            }

            _cacheTold = true;
        }

        _infoBars.Show(InfoBarSeverity.Caution, ProjectText.PreviewCacheFailed);
    }

    /// <summary>
    /// Shows the photos gathered so far as one bar, in place of the bar an earlier flush showed. Nothing when there is
    /// nothing new.
    /// </summary>
    public void Flush()
    {
        InfoBarTicket? previous;
        List<SkippedFile> files;
        lock (_gate)
        {
            if (!_pending)
            {
                return;
            }

            _pending = false;
            previous = _shown;
            files = [.. _unreadable];
        }

        if (previous is not null)
        {
            _infoBars.Dismiss(previous);
        }

        var ticket = _infoBars.ShowWithDetails(
            InfoBarSeverity.Caution,
            ProjectText.PreviewFrameUnreadable(files.Count),
            files.Select(file => new InfoBarDetail(Path.GetFileName(file.Path), SkippedFileText.Reason(file.Reason))));
        lock (_gate)
        {
            _shown = ticket;
        }
    }

    /// <summary>A new project: its photos start a new list (the bar already shown stays until dismissed).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _unreadable.Clear();
            _known.Clear();
            _pending = false;
            _shown = null;
        }
    }
}
