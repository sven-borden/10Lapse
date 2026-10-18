namespace TenLapse.Presentation.Services;

/// <summary>
/// Takes failures that must not stop the action that met them, such as a resource of the old project that
/// throws while <c>New project</c> closes it: the action finishes and the failure is reported here. The App
/// writes them to the trace for now; #26 replaces that with the log and an InfoBar.
/// </summary>
public interface IErrorSink
{
    /// <summary>Records a failure. Must not throw.</summary>
    /// <param name="exception">What went wrong.</param>
    /// <param name="context">Where, in a few words (see <see cref="ProjectText"/>).</param>
    void Report(Exception exception, string context);
}
