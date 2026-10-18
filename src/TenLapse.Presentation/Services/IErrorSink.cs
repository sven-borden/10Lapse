namespace TenLapse.Presentation.Services;

/// <summary>
/// Takes failures that must not stop the action that met them and that the user need not act on, such as a
/// resource of the old project that throws while <c>New project</c> closes it: the action finishes and the failure
/// is written to the log. Failures the user should hear about go to <see cref="IErrorPresenter.Show"/> instead.
/// </summary>
public interface IErrorSink
{
    /// <summary>Records a failure in the log. Must not throw. Safe on any thread.</summary>
    /// <param name="exception">What went wrong.</param>
    /// <param name="context">Where, in a few words (see <see cref="ProjectText"/>).</param>
    void Report(Exception exception, string context);
}

/// <summary>
/// The one error path (#26): logs a failure and tells the user what happened and what to do with an InfoBar.
/// Commands reach it through <see cref="ErrorPath"/> (<c>errors.AsyncCommand(...)</c>), event handlers through
/// <see cref="ErrorPath.GuardAsync"/>; code that catches a failure itself calls <see cref="Show"/>.
/// </summary>
public interface IErrorPresenter : IErrorSink
{
    /// <summary>
    /// Logs <paramref name="exception"/> and shows an InfoBar for it: a sentence for the kinds it knows (a file
    /// that is gone, access denied, a full disk, too little memory, an export failure) and otherwise a generic one
    /// with a <c>Copy diagnostics</c> action. A cancellation is not an error and shows nothing. Never throws. Safe
    /// on any thread.
    /// </summary>
    /// <param name="exception">What went wrong.</param>
    /// <param name="context">What was being done, for the log (see <see cref="ProjectText"/>).</param>
    void Show(Exception exception, string context);
}

/// <summary>What the user is told about one failure.</summary>
/// <param name="Severity">The InfoBar's kind; <see cref="InfoBarSeverity.Critical"/> for an action that failed.</param>
/// <param name="Text">One sentence of what happened and what to do, from <see cref="ProjectText"/>.</param>
/// <param name="OffersDiagnostics">True when the InfoBar should offer <c>Copy diagnostics</c> (something the user cannot fix alone).</param>
public sealed record ErrorMessage(InfoBarSeverity Severity, string Text, bool OffersDiagnostics = false);

/// <summary>
/// Turns exceptions of one kind into what the user is told. The App registers one per library whose exceptions
/// Presentation cannot see (Media's <c>VideoExportException</c>); they are asked before the built-in kinds.
/// </summary>
public interface IErrorMessageMapper
{
    /// <summary>The message for <paramref name="exception"/>, or null when it is not this mapper's kind.</summary>
    ErrorMessage? Map(Exception exception);
}
