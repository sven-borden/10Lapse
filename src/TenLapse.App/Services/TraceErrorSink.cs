using System.Diagnostics;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// Writes reported failures to the trace listeners (the debugger's Output window) until #26 replaces it with
/// the log file and an InfoBar.
/// </summary>
public sealed class TraceErrorSink : IErrorSink
{
    /// <inheritdoc />
    public void Report(Exception exception, string context) => Trace.TraceError("{0} {1}", context, exception);
}
