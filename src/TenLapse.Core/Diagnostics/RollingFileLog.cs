using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TenLapse.Core.Diagnostics;

/// <summary>Limits of a <see cref="RollingFileLog"/>.</summary>
public sealed record RollingFileLogOptions
{
    /// <summary>How many days of files are kept, today included: at 7, a file from 7 days ago is deleted at startup.</summary>
    public int RetentionDays { get; init; } = 7;

    /// <summary>The size one file grows to before the day goes on in the next part (<c>10Lapse-2026-10-18.1.log</c>).</summary>
    public long MaxFileBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// How many parts one day may have. Past the last one the day's later lines stay out of the files (they are
    /// still kept in memory for <see cref="RollingFileLog.RecentLines"/>), so a loop that logs cannot fill the disk.
    /// </summary>
    public int MaxPartsPerDay { get; init; } = 4;

    /// <summary>How many of this session's lines are kept in memory, for diagnostics when the files cannot be read.</summary>
    public int RecentLineCapacity { get; init; } = 200;
}

/// <summary>
/// The app's log: plain UTF-8 text files in one folder, one per local day (<c>10Lapse-2026-10-18.log</c>), each
/// line stamped with the local time and its offset. Files older than <see cref="RollingFileLogOptions.RetentionDays"/>
/// are deleted when the log opens and when the day turns, and each file is bounded in size. Every write goes to the
/// file at once (flushed), so a line written just before the process dies is on disk.
/// </summary>
/// <remarks>
/// Hand-written rather than a logging package: one sink, one format and a few limits need about two hundred lines,
/// and the app has no other use for a logging framework (#26). Thread-safe. Nothing here ever throws into the
/// caller: a folder that cannot be created or written makes the log keep its lines in memory only, and it tries the
/// file again a little later.
/// </remarks>
public sealed partial class RollingFileLog : ILog, IDisposable
{
    /// <summary>Every log file name starts with this.</summary>
    public const string FilePrefix = "10Lapse-";

    /// <summary>Every log file name ends with this.</summary>
    public const string FileExtension = ".log";

    // After a failed write the file is left alone this long, so a broken folder is not hit on every line.
    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(30);

    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // A write that logs again from inside itself (a first-chance exception handler, a trace listener) is dropped
    // rather than recursed into.
    [ThreadStatic]
    private static bool _writing;

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly RollingFileLogOptions _options;
    private readonly Queue<string> _recent = new();

    private FileStream? _stream;
    private DateOnly _day;
    private int _part;
    private bool _dayFull;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;
    private bool _linesLost;
    private bool _disposed;

    /// <summary>Opens the log in <paramref name="folder"/> (created on the first write) and deletes old files. Never throws for the folder.</summary>
    /// <param name="folder">Where the files go: the package's LocalState\Logs, or %LOCALAPPDATA%\10Lapse\Logs unpackaged.</param>
    /// <param name="clock">The clock for time stamps and days; the system clock by default.</param>
    /// <param name="options">The limits; the defaults by default.</param>
    public RollingFileLog(string folder, TimeProvider? clock = null, RollingFileLogOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
        _clock = clock ?? TimeProvider.System;
        _options = options ?? new RollingFileLogOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.RetentionDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxFileBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxPartsPerDay, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.RecentLineCapacity);

        _day = Today(_clock.GetLocalNow());
        Prune(_day);
    }

    /// <summary>The folder the files are written to.</summary>
    public string Folder { get; }

    /// <summary>True when a line could not be written to the file in this session (it is still in memory).</summary>
    public bool LinesLost
    {
        get
        {
            lock (_gate)
            {
                return _linesLost;
            }
        }
    }

    /// <summary>The file name for a day and part: <c>10Lapse-2026-10-18.log</c>, then <c>10Lapse-2026-10-18.1.log</c>.</summary>
    public static string FileName(DateOnly day, int part = 0) =>
        FilePrefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (part == 0 ? string.Empty : "." + part.ToString(CultureInfo.InvariantCulture)) + FileExtension;

    /// <inheritdoc />
    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        if (_writing)
        {
            return;
        }

        _writing = true;
        try
        {
            var now = _clock.GetLocalNow();
            var lines = Format(now, level, message, exception);
            lock (_gate)
            {
                foreach (var line in lines)
                {
                    Remember(line);
                }

                if (!_disposed)
                {
                    WriteToFile(now, lines);
                }
            }
        }
        catch (Exception)
        {
            // The log is the last place a failure can go; reporting its own failure anywhere could loop. The lines
            // are kept in memory when they got that far.
        }
        finally
        {
            _writing = false;
        }
    }

    /// <summary>
    /// The last <paramref name="count"/> lines, oldest first: from the files, so a session that crashed shows in the
    /// next one's diagnostics; from memory when the files cannot be read or a line of this session did not reach them.
    /// </summary>
    public IReadOnlyList<string> RecentLines(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        try
        {
            bool useMemory;
            lock (_gate)
            {
                _stream?.Flush();
                useMemory = _linesLost;
            }

            if (!useMemory)
            {
                var fromFiles = ReadTail(count);
                if (fromFiles.Count > 0)
                {
                    return fromFiles;
                }
            }
        }
        catch (Exception)
        {
            // Unreadable files: fall back to this session's lines.
        }

        lock (_gate)
        {
            return _recent.Skip(Math.Max(0, _recent.Count - count)).ToArray();
        }
    }

    /// <summary>Closes the file. Later writes are kept in memory only.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CloseFile();
        }
    }

    private static DateOnly Today(DateTimeOffset localNow) => DateOnly.FromDateTime(localNow.DateTime);

    // "2026-10-18T14:03:12.345+02:00 ERROR Message", then the exception, each line indented four spaces.
    private static List<string> Format(DateTimeOffset now, LogLevel level, string message, Exception? exception)
    {
        var stamp = now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        var name = level switch
        {
            LogLevel.Info => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT ",
            _ => "?????",
        };

        var lines = new List<string>();
        var messageLines = SplitLines(string.IsNullOrEmpty(message) ? "(no message)" : message);
        lines.Add($"{stamp} {name} {messageLines[0]}");
        lines.AddRange(messageLines.Skip(1).Select(line => "    " + line));
        if (exception is not null)
        {
            string text;
            try
            {
                text = exception.ToString();
            }
            catch (Exception)
            {
                // A custom exception whose ToString throws still gets its type into the log.
                text = exception.GetType().FullName ?? "Exception";
            }

            lines.AddRange(SplitLines(text).Select(line => "    " + line));
        }

        return lines;
    }

    private static string[] SplitLines(string text) =>
        text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    [GeneratedRegex(@"^10Lapse-(\d{4}-\d{2}-\d{2})(?:\.(\d{1,3}))?\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();

    private static bool TryParse(string path, out DateOnly day, out int part)
    {
        part = 0;
        var match = FileNamePattern().Match(Path.GetFileName(path));
        if (!match.Success || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
        {
            day = default;
            return false;
        }

        return !match.Groups[2].Success || int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out part);
    }

    private void Remember(string line)
    {
        if (_options.RecentLineCapacity == 0)
        {
            return;
        }

        if (_recent.Count == _options.RecentLineCapacity)
        {
            _recent.Dequeue();
        }

        _recent.Enqueue(line);
    }

    // Called under the lock.
    private void WriteToFile(DateTimeOffset now, List<string> lines)
    {
        var day = Today(now);
        if (day != _day)
        {
            CloseFile();
            _day = day;
            _part = 0;
            _dayFull = false;
            Prune(day);
        }

        if (_dayFull)
        {
            _linesLost = true;
            return;
        }

        var bytes = _utf8.GetBytes(string.Join(Environment.NewLine, lines) + Environment.NewLine);
        try
        {
            if (_stream is null && (now < _retryAfter || !OpenFile()))
            {
                _linesLost = true;
                return;
            }

            if (_stream!.Length > 0 && _stream.Length + bytes.Length > _options.MaxFileBytes)
            {
                CloseFile();
                if (_part + 1 >= _options.MaxPartsPerDay)
                {
                    // The day's budget is spent: say so once at the end of the last part, then keep lines in memory.
                    _dayFull = true;
                    _linesLost = true;
                    AppendToLastPart(now);
                    return;
                }

                _part++;
                if (!OpenFile())
                {
                    _linesLost = true;
                    return;
                }
            }

            _stream!.Write(bytes);
            _stream.Flush();
        }
        catch (Exception)
        {
            CloseFile();
            _retryAfter = now + _retryDelay;
            _linesLost = true;
        }
    }

    // Opens today's current part for appending, moving past parts already full from an earlier session.
    private bool OpenFile()
    {
        Directory.CreateDirectory(Folder);
        while (_part < _options.MaxPartsPerDay)
        {
            var stream = new FileStream(
                Path.Combine(Folder, FileName(_day, _part)),
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < _options.MaxFileBytes)
            {
                _stream = stream;
                return true;
            }

            stream.Dispose();
            _part++;
        }

        _dayFull = true;
        return false;
    }

    private void AppendToLastPart(DateTimeOffset now)
    {
        var notice = Format(now, LogLevel.Warning, "The log reached its size limit for today; later lines today are not written to disk.", null);
        File.AppendAllText(Path.Combine(Folder, FileName(_day, _part)), notice[0] + Environment.NewLine, _utf8);
    }

    private void CloseFile()
    {
        try
        {
            _stream?.Dispose();
        }
        catch (Exception)
        {
            // Closing a file that already failed can fail again; it is gone either way.
        }

        _stream = null;
    }

    // Deletes the log files from RetentionDays ago and earlier. Files that are not the log's are left alone.
    private void Prune(DateOnly today)
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            var oldest = today.AddDays(1 - _options.RetentionDays);
            foreach (var file in Directory.EnumerateFiles(Folder, FilePrefix + "*" + FileExtension))
            {
                if (TryParse(file, out var day, out _) && day < oldest)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception)
                    {
                        // Open in an editor, or read-only: it goes on a later start.
                    }
                }
            }
        }
        catch (Exception)
        {
            // An unreadable folder keeps its files; writing will say whether it works at all.
        }
    }

    // The newest files first, until count lines are found. Each file is at most MaxFileBytes.
    private List<string> ReadTail(int count)
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(Folder, FilePrefix + "*" + FileExtension)
            .Select(file => (File: file, Ok: TryParse(file, out var day, out var part), Day: day, Part: part))
            .Where(entry => entry.Ok)
            .OrderByDescending(entry => entry.Day)
            .ThenByDescending(entry => entry.Part)
            .Select(entry => entry.File);

        var tail = new List<string>();
        foreach (var file in files)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, _utf8);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            tail.InsertRange(0, lines.Skip(Math.Max(0, lines.Count - (count - tail.Count))));
            if (tail.Count >= count)
            {
                break;
            }
        }

        return tail;
    }
}
