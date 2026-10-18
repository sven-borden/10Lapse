using TenLapse.Core.Diagnostics;

namespace TenLapse.Presentation.Services;

/// <summary>
/// The App's <see cref="IErrorPresenter"/>: every failure goes to the log; the ones the user should hear about also
/// become an InfoBar that says what happened and what to do (<see cref="ErrorMessages.For"/>). Thread-safe.
/// </summary>
public sealed class ErrorPresenter : IErrorPresenter
{
    private readonly ILog _log;
    private readonly IInfoBarService _infoBars;
    private readonly IReadOnlyList<IErrorMessageMapper> _mappers;
    private readonly Func<Task>? _copyDiagnostics;

    /// <param name="log">Where every failure is written.</param>
    /// <param name="infoBars">Where the user is told.</param>
    /// <param name="mappers">Sentences for exception kinds Presentation cannot see (Media's export failures), asked first.</param>
    /// <param name="copyDiagnostics">
    /// The <c>Copy diagnostics</c> action of the generic InfoBar (<see cref="DiagnosticsCopier.CopyAsync"/>); without
    /// it the generic InfoBar has no action.
    /// </param>
    public ErrorPresenter(ILog log, IInfoBarService infoBars, IEnumerable<IErrorMessageMapper>? mappers = null, Func<Task>? copyDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(infoBars);
        _log = log;
        _infoBars = infoBars;
        _mappers = mappers?.ToArray() ?? [];
        _copyDiagnostics = copyDiagnostics;
    }

    /// <summary>Writes the failure to the log only: background clean-up the user need not act on.</summary>
    public void Report(Exception exception, string context) => _log.Error(context, exception);

    /// <inheritdoc />
    public void Show(Exception exception, string context) => Present(exception, context, unhandled: false);

    /// <summary>
    /// Like <see cref="Show"/> for a failure nothing caught (the App's global handlers): logged as
    /// <see cref="LogLevel.Critical"/>, and when its kind is not one the user can act on, the InfoBar says
    /// <see cref="ProjectText.ErrorUnhandled"/> (the app kept running) instead of the generic sentence.
    /// </summary>
    public void ShowUnhandled(Exception exception, string context) => Present(exception, context, unhandled: true);

    private void Present(Exception exception, string context, bool unhandled)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (ErrorMessages.IsCancellation(exception))
            {
                _log.Info(ProjectText.Format(ProjectText.LogCancelled, context));
                return;
            }

            _log.Write(unhandled ? LogLevel.Critical : LogLevel.Error, context, exception);
            var message = ErrorMessages.For(exception, _mappers);
            if (unhandled && ReferenceEquals(message, ErrorMessages.Unexpected))
            {
                message = message with { Text = ProjectText.ErrorUnhandled };
            }

            _infoBars.Show(message.OffersDiagnostics && _copyDiagnostics is { } copy
                ? new InfoBarRequest(message.Severity, message.Text, ProjectText.CopyDiagnostics, copy)
                : new InfoBarRequest(message.Severity, message.Text));
        }
        catch (Exception failure)
        {
            // Never throws (the caller is already handling a failure); the log never throws either.
            _log.Critical(ProjectText.ShowingErrorFailed, failure);
        }
    }
}

/// <summary>
/// An <see cref="IErrorSink"/> that only writes to the log. The InfoBar queue takes this one: it shows its own
/// Critical InfoBar for an action that failed, and the <see cref="ErrorPresenter"/> it would otherwise use needs the
/// queue first.
/// </summary>
public sealed class LogErrorSink(ILog log) : IErrorSink
{
    private readonly ILog _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <inheritdoc />
    public void Report(Exception exception, string context) => _log.Error(context, exception);
}
