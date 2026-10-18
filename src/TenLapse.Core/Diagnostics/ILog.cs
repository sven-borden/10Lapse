namespace TenLapse.Core.Diagnostics;

/// <summary>How much a log line matters.</summary>
public enum LogLevel
{
    /// <summary>What the app did: started, exported, a command was cancelled.</summary>
    Info,

    /// <summary>Something odd that did not stop anything.</summary>
    Warning,

    /// <summary>An action failed; the user was told, or it was background clean-up.</summary>
    Error,

    /// <summary>A failure nothing caught: an unhandled exception, a crash at startup.</summary>
    Critical,
}

/// <summary>
/// The app's local log (#26). What it may hold is what PRIVACY.md promises: what the app did and why something
/// failed, with the names and paths of files the user picked and exception details, never image data. It stays on
/// the PC; the user copies it from Settings (Copy diagnostics) if they want to share it.
/// </summary>
public interface ILog
{
    /// <summary>
    /// Writes one entry. Safe on any thread and never throws: a failing write is dropped from the file (the line
    /// is still kept for <see cref="RecentLines"/>), so logging a failure can never cause another.
    /// </summary>
    /// <param name="level">How much it matters.</param>
    /// <param name="message">What happened, in a sentence.</param>
    /// <param name="exception">The exception behind it, written with its type, message and stack trace.</param>
    void Write(LogLevel level, string message, Exception? exception = null);

    /// <summary>The last <paramref name="count"/> lines, oldest first, for Copy diagnostics. Never throws.</summary>
    IReadOnlyList<string> RecentLines(int count);
}

/// <summary>Shorthands for <see cref="ILog.Write"/>.</summary>
public static class LogExtensions
{
    /// <summary>Writes an <see cref="LogLevel.Info"/> entry.</summary>
    public static void Info(this ILog log, string message) => log.Write(LogLevel.Info, message);

    /// <summary>Writes a <see cref="LogLevel.Warning"/> entry.</summary>
    public static void Warning(this ILog log, string message, Exception? exception = null) => log.Write(LogLevel.Warning, message, exception);

    /// <summary>Writes an <see cref="LogLevel.Error"/> entry.</summary>
    public static void Error(this ILog log, string message, Exception? exception = null) => log.Write(LogLevel.Error, message, exception);

    /// <summary>Writes a <see cref="LogLevel.Critical"/> entry.</summary>
    public static void Critical(this ILog log, string message, Exception? exception = null) => log.Write(LogLevel.Critical, message, exception);
}
