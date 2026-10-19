using TenLapse.Core.Diagnostics;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Rating;

/// <summary>Asks the Microsoft Store to show its rating dialog. The App implements it; tests use a fake.</summary>
public interface IRatingService
{
    /// <summary>
    /// Asks the Store. Returns true when the Store was asked (the user may have rated or dismissed it), false when it cannot
    /// be asked here (no package identity, or the Store did not answer), so the prompt tries again later. Throws when the
    /// request fails; the prompt logs that.
    /// </summary>
    Task<bool> RequestAsync();
}

/// <summary>Where the rating prompt keeps its count and the version it last asked in (#31). <see cref="RememberedSettings"/> implements it.</summary>
public interface IRatingMemory
{
    /// <summary>The app version the prompt was last shown in, or null.</summary>
    string? RatingAskedVersion { get; set; }

    /// <summary>Counts one successful export, remembers the count, and returns it.</summary>
    int CountSuccessfulExport();
}

/// <summary>
/// The rating prompt (#31): after the second successful export, once per app version, it asks the Store to show its
/// rating dialog. It hooks <see cref="ExportViewModel.Started"/> and <see cref="ExportViewModel.Ended"/> and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="ExportOutcome.Succeeded"/> counts: a failed or cancelled export never counts and never asks. The ask
/// waits <see cref="DefaultDelay"/> after the export, so the finished state and its premiere have been seen, then asks only
/// if <c>canAsk</c> still holds (the window in front, the finished state still showing, no dialog open). If it does not
/// hold, or a new export starts first, this attempt is dropped without recording anything and the next successful export
/// tries again. Once the Store has been asked in a version, it is not asked again in that version.
/// </para>
/// <para>
/// A failure to ask is logged only: it is an unrequested courtesy the user could do nothing about, so an InfoBar would
/// only nag. Everything runs on the UI thread (the delay's continuation returns to it), where the window can be read.
/// </para>
/// </remarks>
public sealed class RatingPrompt : IDisposable
{
    /// <summary>Successful exports before the first ask.</summary>
    public const int AskAfterExports = 2;

    /// <summary>The pause between the finished state appearing and the ask: after its check, title and premiere have started.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(4);

    private readonly IRatingMemory _memory;
    private readonly IRatingService _service;
    private readonly Func<bool> _canAsk;
    private readonly string _version;
    private readonly ILog _log;
    private readonly IErrorSink _errors;
    private readonly TimeProvider _time;
    private readonly TimeSpan _delay;
    private ExportViewModel? _export;
    private CancellationTokenSource? _pending;
    private Task _settled = Task.CompletedTask;
    private bool _disposed;

    /// <param name="memory">The count and the version asked in.</param>
    /// <param name="service">Asks the Store.</param>
    /// <param name="canAsk">True while asking would interrupt nothing: the window is in front, the finished state still shows, no dialog is open. Read on the UI thread.</param>
    /// <param name="appVersion">The running app version.</param>
    /// <param name="log">Takes what the prompt decided.</param>
    /// <param name="errors">Takes failures (log only).</param>
    /// <param name="time">The clock the delay runs on; tests pass a fake.</param>
    /// <param name="delay">The pause before the ask; <see cref="DefaultDelay"/> when null.</param>
    public RatingPrompt(IRatingMemory memory, IRatingService service, Func<bool> canAsk, string appVersion, ILog log, IErrorSink errors, TimeProvider? time = null, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(canAsk);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(errors);
        _memory = memory;
        _service = service;
        _canAsk = canAsk;
        _version = appVersion;
        _log = log;
        _errors = errors;
        _time = time ?? TimeProvider.System;
        _delay = delay ?? DefaultDelay;
    }

    /// <summary>Completes when the ask in progress (its wait and its request) has ended; already complete when none is. For tests.</summary>
    public Task WhenSettled => _settled;

    /// <summary>Follows <paramref name="export"/>'s starts and ends.</summary>
    public void Attach(ExportViewModel export)
    {
        ArgumentNullException.ThrowIfNull(export);
        _export = export;
        export.Started += OnStarted;
        export.Ended += OnEnded;
    }

    /// <summary>A new export started: a waiting ask is dropped, so the dialog never lands on a running export.</summary>
    public void ExportStarted()
    {
        if (_pending is { } pending)
        {
            _log.Info(RatingText.Superseded);
            Cancel(pending);
        }
    }

    /// <summary>An export ended: counts a success, and when due, waits and asks. Never throws.</summary>
    public void ExportEnded(ExportOutcome outcome)
    {
        if (outcome != ExportOutcome.Succeeded || _disposed)
        {
            return;
        }

        try
        {
            var count = _memory.CountSuccessfulExport();
            if (count < AskAfterExports)
            {
                _log.Info(RatingText.NotYet(count));
                return;
            }

            if (string.Equals(_memory.RatingAskedVersion, _version, StringComparison.Ordinal))
            {
                _log.Info(RatingText.AlreadyAsked(_version));
                return;
            }

            if (_pending is { } older)
            {
                Cancel(older);
            }

            var source = _pending = new CancellationTokenSource();
            _settled = AskLaterAsync(source);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _errors.Report(exception, RatingText.PromptFailed);
        }
    }

    /// <summary>Stops following the export and drops a waiting ask.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_export is { } export)
        {
            export.Started -= OnStarted;
            export.Ended -= OnEnded;
        }

        if (_pending is { } pending)
        {
            Cancel(pending);
        }
    }

    private void OnStarted(object? sender, EventArgs e) => ExportStarted();

    private void OnEnded(object? sender, ExportEndedEventArgs e) => ExportEnded(e.Outcome);

    private void Cancel(CancellationTokenSource source)
    {
        if (ReferenceEquals(_pending, source))
        {
            _pending = null;
        }

        source.Cancel();
    }

    private async Task AskLaterAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(_delay, _time, source.Token);
            if (source.IsCancellationRequested || _disposed)
            {
                return;
            }

            if (!_canAsk())
            {
                _log.Info(RatingText.Postponed);
                return;
            }

            // Remembered before the Store is asked: a crash or a close during its dialog must not bring it back.
            var before = _memory.RatingAskedVersion;
            _memory.RatingAskedVersion = _version;
            bool asked;
            try
            {
                asked = await _service.RequestAsync();
            }
            catch
            {
                _memory.RatingAskedVersion = before;
                throw;
            }

            if (asked)
            {
                _log.Info(RatingText.Asked(_version));
            }
            else
            {
                _memory.RatingAskedVersion = before;
                _log.Info(RatingText.Unavailable);
            }
        }
        catch (OperationCanceledException)
        {
            // A new export started, or the window closed: the ask is dropped.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _errors.Report(exception, RatingText.RequestFailed);
        }
        finally
        {
            if (ReferenceEquals(_pending, source))
            {
                _pending = null;
            }

            source.Dispose();
        }
    }
}
