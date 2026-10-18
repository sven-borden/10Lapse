using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TenLapse.Core.Diagnostics;

/// <summary>Limits of a <see cref="RollingFileLog"/>.</summary>
public sealed record RollingFileLogOptions
{
    /// <summary>How many days of files are kept, today included: at 7, a file from 7 days ago is deleted at startup.</summary>
    public int RetentionDays { get; init; } = 7;

    /// <summary>The size one file grows to before the day goes on in the next part (<c>10Lapse-2026-10-18.p12345.1.log</c>).</summary>
    public long MaxFileBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// How many parts one day may have in one process. Past the last one the day's later lines stay out of the files
    /// (they are still kept in memory for <see cref="RollingFileLog.RecentLines"/>), so a loop that logs cannot fill
    /// the disk.
    /// </summary>
    public int MaxPartsPerDay { get; init; } = 4;

    /// <summary>How many of this session's lines are kept in memory, for diagnostics when the files cannot be read.</summary>
    public int RecentLineCapacity { get; init; } = 200;

    /// <summary>
    /// The id in this process's file names (<c>.p12345</c>); this process's id by default. Each process writes files
    /// of its own, so two instances of the app never write over each other's lines.
    /// </summary>
    public int ProcessId { get; init; } = Environment.ProcessId;

    /// <summary>
    /// How long after an entry is written the same entry (level, message and exception type) is only counted; at the
    /// first write after that, one line says how many times it repeated. An error storm then costs a few lines, not
    /// the day's budget. <see cref="TimeSpan.Zero"/> writes every entry.
    /// </summary>
    public TimeSpan RepeatWindow { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many bytes <see cref="LogLevel.Critical"/> entries may still add to the last part each day after the
    /// day's parts are full, so the line that explains a crash reaches the disk even after an error storm.
    /// </summary>
    public long CriticalReserveBytes { get; init; } = 256 * 1024;
}

/// <summary>
/// The app's log: plain UTF-8 text files in one folder, one per local day and process
/// (<c>10Lapse-2026-10-18.p12345.log</c>), each line stamped with the local time and its offset. Files older than
/// <see cref="RollingFileLogOptions.RetentionDays"/> are deleted when the log opens and when the day turns, whichever
/// process wrote them, and each file is bounded in size. Every write goes to the file at once (flushed), so a line
/// written just before the process dies is on disk.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than a logging package: one sink, one format and a few limits need a few hundred lines, and
/// the app has no other use for a logging framework (#26). Thread-safe. Nothing here ever throws into the caller: a
/// folder that cannot be created or written makes the log keep its lines in memory only, and it tries the file again
/// a little later.
/// </para>
/// <para>
/// One file per process: two instances of the app each append through a <see cref="FileStream"/> of their own, and
/// each stream keeps its own position, so with one shared file they wrote over each other's lines.
/// <see cref="RecentLines"/> reads the files of today and yesterday, every process's, and merges them by time.
/// </para>
/// <para>
/// An entry repeated within <see cref="RollingFileLogOptions.RepeatWindow"/> is counted rather than written, and
/// <see cref="LogLevel.Critical"/> entries keep a reserve past the day's size limit
/// (<see cref="RollingFileLogOptions.CriticalReserveBytes"/>).
/// </para>
/// </remarks>
public sealed partial class RollingFileLog : ILog, IDisposable
{
    /// <summary>Every log file name starts with this.</summary>
    public const string FilePrefix = "10Lapse-";

    /// <summary>Every log file name ends with this.</summary>
    public const string FileExtension = ".log";

    // Every entry's first line starts with its local time and offset: 2026-10-18T14:03:12.345+02:00.
    private const string StampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffzzz";
    private const int StampLength = 29;

    // After a failed write the file is left alone this long, so a broken folder is not hit on every line.
    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(30);

    // How many different entries are counted at once; more than that are written as they come.
    private const int MaxTrackedRepeats = 64;

    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // A write that logs again from inside itself (a first-chance exception handler, a trace listener) is dropped
    // rather than recursed into.
    [ThreadStatic]
    private static bool _writing;

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly RollingFileLogOptions _options;
    private readonly Queue<string> _recent = new();

    // The entries written within the repeat window, each with how many times it came again since.
    private readonly Dictionary<(LogLevel Level, string Message, string? Exception), Repeat> _repeats = [];

    private FileStream? _stream;
    private DateOnly _day;
    private int _part;
    private bool _dayFull;
    private long _reserveUsed;
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
        ArgumentOutOfRangeException.ThrowIfNegative(_options.ProcessId);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.RepeatWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.CriticalReserveBytes);

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

    /// <summary>
    /// The file name for a day, process and part: <c>10Lapse-2026-10-18.p12345.log</c>, then
    /// <c>10Lapse-2026-10-18.p12345.1.log</c>.
    /// </summary>
    public static string FileName(DateOnly day, int processId, int part = 0) =>
        FilePrefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        + ".p" + processId.ToString(CultureInfo.InvariantCulture)
        + (part == 0 ? string.Empty : "." + part.ToString(CultureInfo.InvariantCulture))
        + FileExtension;

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
            lock (_gate)
            {
                WriteRepeatCounts(now, all: false);
                if (!IsRepeat(now, level, message, exception))
                {
                    Emit(now, level, Format(now, level, message, exception));
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
    /// The last <paramref name="count"/> lines, oldest first: from the files of today and yesterday, every process's,
    /// merged by time (an entry's continuation lines stay with it), so a session that crashed shows in the next one's
    /// diagnostics; from memory when the files cannot be read or a line of this session did not reach them.
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
                // A storm still being counted says so now, so the diagnostics show it.
                WriteRepeatCounts(_clock.GetLocalNow(), all: true);
                _stream?.Flush();
                useMemory = _linesLost;
            }

            if (!useMemory)
            {
                var fromFiles = ReadTail(count, Today(_clock.GetLocalNow()));
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
            if (_disposed)
            {
                return;
            }

            try
            {
                WriteRepeatCounts(_clock.GetLocalNow(), all: true);
            }
            catch (Exception)
            {
                // Closing goes on; the counts are lost with the process.
            }

            _disposed = true;
            CloseFile();
        }
    }

    private static DateOnly Today(DateTimeOffset localNow) => DateOnly.FromDateTime(localNow.DateTime);

    // "2026-10-18T14:03:12.345+02:00 ERROR Message", then the exception, each line indented four spaces.
    private static List<string> Format(DateTimeOffset now, LogLevel level, string message, Exception? exception)
    {
        var stamp = now.ToString(StampFormat, CultureInfo.InvariantCulture);
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

    // 10Lapse-2026-10-18.p12345.1.log; a file from before the process id was in the name has none (process 0).
    [GeneratedRegex(@"^10Lapse-(\d{4}-\d{2}-\d{2})(?:\.p(\d{1,10}))?(?:\.(\d{1,3}))?\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();

    private static bool TryParse(string path, out DateOnly day, out long processId, out int part)
    {
        processId = 0;
        part = 0;
        var match = FileNamePattern().Match(Path.GetFileName(path));
        if (!match.Success || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
        {
            day = default;
            return false;
        }

        return (!match.Groups[2].Success || long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out processId))
            && (!match.Groups[3].Success || int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out part));
    }

    // The time stamp an entry's first line starts with; null for a continuation line (indented) or anything else.
    private static DateTimeOffset? StampOf(string line) =>
        line.Length >= StampLength
        && char.IsAsciiDigit(line[0])
        && DateTimeOffset.TryParseExact(line.AsSpan(0, StampLength), StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp)
            ? stamp
            : null;

    // The last count lines of these files, newest file first. Each file is at most MaxFileBytes.
    private static List<string> Tail(IEnumerable<string> newestFirst, int count)
    {
        var tail = new List<string>();
        foreach (var file in newestFirst)
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

    // Called under the lock: keeps the entry in memory and writes it to the file.
    private void Emit(DateTimeOffset now, LogLevel level, List<string> lines)
    {
        foreach (var line in lines)
        {
            Remember(line);
        }

        if (!_disposed)
        {
            WriteToFile(now, level, lines);
        }
    }

    // Called under the lock. True when the same entry was written within the repeat window: it is only counted.
    private bool IsRepeat(DateTimeOffset now, LogLevel level, string message, Exception? exception)
    {
        if (_options.RepeatWindow <= TimeSpan.Zero)
        {
            return false;
        }

        var key = (level, message ?? string.Empty, exception?.GetType().FullName);
        if (_repeats.TryGetValue(key, out var repeat))
        {
            repeat.Count++;
            return true;
        }

        if (_repeats.Count < MaxTrackedRepeats)
        {
            _repeats[key] = new Repeat(now);
        }

        return false;
    }

    // Called under the lock. Ends the windows that are over (all of them when closing or reading): an entry that came
    // again gets one line, "Previous message repeated 41 more times since 14:03:12.345: Message (IOException)".
    private void WriteRepeatCounts(DateTimeOffset now, bool all)
    {
        if (_repeats.Count == 0)
        {
            return;
        }

        var ended = _repeats.Where(entry => all || now - entry.Value.First >= _options.RepeatWindow).ToList();
        foreach (var (key, repeat) in ended)
        {
            _repeats.Remove(key);
            if (repeat.Count == 0)
            {
                continue;
            }

            var exception = key.Exception is { } type ? $" ({type[(type.LastIndexOf('.') + 1)..]})" : string.Empty;
            var message = SplitLines(string.IsNullOrEmpty(key.Message) ? "(no message)" : key.Message)[0];
            var since = repeat.First.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            Emit(now, key.Level, Format(now, key.Level, $"Previous message repeated {repeat.Count} more times since {since}: {message}{exception}", null));
        }
    }

    // Called under the lock.
    private void WriteToFile(DateTimeOffset now, LogLevel level, List<string> lines)
    {
        var day = Today(now);
        if (day != _day)
        {
            CloseFile();
            _day = day;
            _part = 0;
            _dayFull = false;
            _reserveUsed = 0;
            Prune(day);
        }

        var bytes = _utf8.GetBytes(string.Join(Environment.NewLine, lines) + Environment.NewLine);
        try
        {
            if (!_dayFull && WriteToPart(now, bytes))
            {
                return;
            }

            // Past the day's limit only Critical entries are written, into a reserve of their own, so the line that
            // explains a crash is on disk even after an error storm spent the budget.
            if (_dayFull && level == LogLevel.Critical && _reserveUsed + bytes.Length <= _options.CriticalReserveBytes)
            {
                using var stream = new FileStream(PartPath(_part), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write(bytes);
                _reserveUsed += bytes.Length;
                return;
            }

            _linesLost = true;
        }
        catch (Exception)
        {
            CloseFile();
            _retryAfter = now + _retryDelay;
            _linesLost = true;
        }
    }

    // Writes to the current part, going on in the next one when it is full. False when the line was not written:
    // the folder waits for a retry or cannot be opened, or the day's parts are full (then _dayFull is set).
    private bool WriteToPart(DateTimeOffset now, byte[] bytes)
    {
        if (_stream is null && (now < _retryAfter || !OpenFile()))
        {
            return false;
        }

        if (_stream!.Length > 0 && _stream.Length + bytes.Length > _options.MaxFileBytes)
        {
            CloseFile();
            if (_part + 1 >= _options.MaxPartsPerDay)
            {
                // The day's budget is spent: say so once at the end of the last part.
                _dayFull = true;
                AppendToLastPart(now);
                return false;
            }

            _part++;
            if (!OpenFile())
            {
                return false;
            }
        }

        _stream!.Write(bytes);
        _stream.Flush();
        return true;
    }

    private string PartPath(int part) => Path.Combine(Folder, FileName(_day, _options.ProcessId, part));

    // Opens this process's current part of today for appending, moving past parts already full from an earlier
    // session with the same process id.
    private bool OpenFile()
    {
        Directory.CreateDirectory(Folder);
        while (_part < _options.MaxPartsPerDay)
        {
            var stream = new FileStream(PartPath(_part), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < _options.MaxFileBytes)
            {
                _stream = stream;
                return true;
            }

            stream.Dispose();
            _part++;
        }

        _part = _options.MaxPartsPerDay - 1;
        _dayFull = true;
        return false;
    }

    private void AppendToLastPart(DateTimeOffset now)
    {
        var notice = Format(now, LogLevel.Warning, "The log reached its size limit for today; later lines today are not written to disk, except critical ones.", null);
        File.AppendAllText(PartPath(_part), notice[0] + Environment.NewLine, _utf8);
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

    // Deletes the log files from RetentionDays ago and earlier, whichever process wrote them. Files that are not
    // the log's are left alone.
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
                if (TryParse(file, out var day, out _, out _) && day < oldest)
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

    // The last count lines of today's and yesterday's files, every process's: each process's newest files first
    // until it has count lines, then all of them merged by time. A line takes the time of the entry it belongs to,
    // so an exception's lines stay under their entry; equal times keep each process's own order.
    private List<string> ReadTail(int count, DateOnly today)
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var yesterday = today.AddDays(-1);
        var writers = Directory.EnumerateFiles(Folder, FilePrefix + "*" + FileExtension)
            .Select(file => (File: file, Ok: TryParse(file, out var day, out var processId, out var part), Day: day, ProcessId: processId, Part: part))
            .Where(entry => entry.Ok && (entry.Day == today || entry.Day == yesterday))
            .GroupBy(entry => entry.ProcessId)
            .OrderBy(group => group.Key);

        var merged = new List<(long Ticks, int Writer, int Index, string Line)>();
        var writer = 0;
        foreach (var files in writers)
        {
            var tail = Tail(files.OrderByDescending(entry => entry.Day).ThenByDescending(entry => entry.Part).Select(entry => entry.File), count);
            var stamp = tail.Select(StampOf).FirstOrDefault(found => found is not null) ?? DateTimeOffset.MinValue;
            for (var index = 0; index < tail.Count; index++)
            {
                stamp = StampOf(tail[index]) ?? stamp;
                merged.Add((stamp.UtcTicks, writer, index, tail[index]));
            }

            writer++;
        }

        return merged
            .OrderBy(line => line.Ticks)
            .ThenBy(line => line.Writer)
            .ThenBy(line => line.Index)
            .Select(line => line.Line)
            .TakeLast(count)
            .ToList();
    }

    // An entry written within the repeat window: when, and how many times it came again since.
    private sealed class Repeat(DateTimeOffset first)
    {
        public DateTimeOffset First { get; } = first;

        public int Count { get; set; }
    }
}
