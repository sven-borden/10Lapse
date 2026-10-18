using TenLapse.Core.Diagnostics;

namespace TenLapse.Tests.Diagnostics;

/// <summary>The app's log: one file per local day, kept 7 days, bounded, never throwing into the caller.</summary>
public sealed class RollingFileLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "10Lapse tests", "log " + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.FromHours(2)));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void ALineHasTheLocalTimeWithItsOffsetTheLevelAndTheException()
    {
        using var log = new RollingFileLog(_folder, _clock);

        log.Error("Export failed.", new InvalidOperationException("encoder\nstopped"));

        var lines = ReadLines(Path.Combine(_folder, Today()));
        Assert.Equal("2026-10-18T09:30:00.000+02:00 ERROR Export failed.", lines[0]);
        Assert.Equal("    System.InvalidOperationException: encoder", lines[1]);
        Assert.Equal("    stopped", lines[2]);
    }

    [Fact]
    public void ANewDayStartsANewFileAndOldFilesArePrunedAtStartup()
    {
        Directory.CreateDirectory(_folder);
        foreach (var day in new[] { "2026-10-10", "2026-10-11", "2026-10-12", "2026-10-17" })
        {
            File.WriteAllText(Path.Combine(_folder, $"10Lapse-{day}.log"), "old\n");
        }

        File.WriteAllText(Path.Combine(_folder, "10Lapse-2026-10-11.1.log"), "old part\n");
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not the log's");

        using (var log = new RollingFileLog(_folder, _clock))
        {
            // Seven days are kept, today included: the 12th to the 18th.
            Assert.Equal(["10Lapse-2026-10-12.log", "10Lapse-2026-10-17.log", "notes.txt"], Files());

            log.Info("Saturday");
            _clock.Now = _clock.Now.AddDays(1);
            log.Info("Sunday");
            Assert.Contains(Name(19), Files());
            Assert.Contains("Saturday", ReadText(Path.Combine(_folder, Name(18))), StringComparison.Ordinal);
            Assert.Contains("Sunday", ReadText(Path.Combine(_folder, Name(19))), StringComparison.Ordinal);

            // The day turning prunes too: the 12th is now 7 days old.
            Assert.DoesNotContain("10Lapse-2026-10-12.log", Files());
        }

        _clock.Now = _clock.Now.AddDays(10);
        using (new RollingFileLog(_folder, _clock))
        {
            Assert.Equal(["notes.txt"], Files());
        }
    }

    [Fact]
    public void AFullFileGoesOnInTheNextPartAndTheDayStopsAtItsLimit()
    {
        var options = new RollingFileLogOptions { MaxFileBytes = 2048, MaxPartsPerDay = 2 };
        using var log = new RollingFileLog(_folder, _clock, options);
        var line = new string('x', 200);

        for (var i = 0; i < 40; i++)
        {
            log.Info($"{i:00} {line}");
        }

        Assert.Equal([Name(18, 1), Name(18)], Files());
        Assert.All(Files(), file => Assert.True(new FileInfo(Path.Combine(_folder, file)).Length < 2048 + 512));
        Assert.Contains("size limit", ReadText(Path.Combine(_folder, Name(18, 1))), StringComparison.Ordinal);
        Assert.True(log.LinesLost);

        // What the files could not take is still in memory for the diagnostics.
        Assert.EndsWith(line, log.RecentLines(1)[0], StringComparison.Ordinal);
        Assert.Contains(" 39 ", log.RecentLines(1)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnwritableFolderNeverThrowsAndKeepsTheLinesInMemory()
    {
        // A file where the folder should be: the folder can be neither created nor written.
        Directory.CreateDirectory(Path.GetDirectoryName(_folder)!);
        File.WriteAllText(_folder, "in the way");
        try
        {
            using var log = new RollingFileLog(_folder, _clock);

            var failure = Record.Exception(() =>
            {
                log.Info("first");
                log.Error("second", new IOException("disk"));
            });

            Assert.Null(failure);
            Assert.True(log.LinesLost);
            var lines = log.RecentLines(200);
            Assert.EndsWith("INFO  first", lines[0], StringComparison.Ordinal);
            Assert.EndsWith("ERROR second", lines[1], StringComparison.Ordinal);
            Assert.Equal("    System.IO.IOException: disk", lines[2]);
        }
        finally
        {
            File.Delete(_folder);
        }
    }

    [Fact]
    public void AnInvalidFolderPathNeverThrows()
    {
        using var log = new RollingFileLog(@"C:\bad<>|folder", _clock);

        Assert.Null(Record.Exception(() => log.Info("still fine")));
        Assert.Single(log.RecentLines(10));
    }

    [Fact]
    public void TheDiagnosticsGetTheLast200LinesAcrossFilesAndSessions()
    {
        using (var yesterday = new RollingFileLog(_folder, new FakeClock(_clock.Now.AddDays(-1))))
        {
            for (var i = 1; i <= 120; i++)
            {
                yesterday.Info($"yesterday {i}");
            }
        }

        using var log = new RollingFileLog(_folder, _clock);
        for (var i = 1; i <= 130; i++)
        {
            log.Info($"today {i}");
        }

        var lines = log.RecentLines(200);

        Assert.Equal(200, lines.Count);
        Assert.EndsWith("yesterday 51", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("yesterday 120", lines[69], StringComparison.Ordinal);
        Assert.EndsWith("today 1", lines[70], StringComparison.Ordinal);
        Assert.EndsWith("today 130", lines[199], StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryKeepsTheLast200LinesOfTheSession()
    {
        using var log = new RollingFileLog(@"C:\bad<>|folder", _clock);
        for (var i = 1; i <= 250; i++)
        {
            log.Info($"line {i}");
        }

        var lines = log.RecentLines(500);

        Assert.Equal(200, lines.Count);
        Assert.EndsWith("line 51", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("line 250", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritesFromManyThreadsAllArrive()
    {
        using var log = new RollingFileLog(_folder, _clock);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                log.Info($"thread {thread} line {i}");
            }
        })));

        Assert.Equal(400, ReadLines(Path.Combine(_folder, Today())).Length);
    }

    [Fact]
    public void AnotherProcessCanReadTheFileWhileTheLogHasItOpen()
    {
        using var log = new RollingFileLog(_folder, _clock);
        log.Info("open");

        using var reader = new FileStream(Path.Combine(_folder, Today()), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert.True(reader.Length > 0);
    }

    [Fact]
    public void TwoProcessesWriteFilesOfTheirOwnAndTheDiagnosticsMergeThemByTime()
    {
        // Two instances of the app on one folder: with one shared file each stream's own position made them write
        // over each other's lines.
        var clock = new FakeClock(_clock.Now);
        using var first = new RollingFileLog(_folder, clock, new RollingFileLogOptions { ProcessId = 101 });
        using var second = new RollingFileLog(_folder, clock, new RollingFileLogOptions { ProcessId = 202 });
        for (var i = 1; i <= 60; i++)
        {
            clock.Now = clock.Now.AddMilliseconds(10);
            (i % 2 == 1 ? first : second).Info($"line {i}");
            if (i == 31)
            {
                second.Error("with an exception", new InvalidOperationException("two\nlines"));
            }
        }

        var day = new DateOnly(2026, 10, 18);
        Assert.Equal([RollingFileLog.FileName(day, 101), RollingFileLog.FileName(day, 202)], Files());
        Assert.Equal(30, ReadLines(Path.Combine(_folder, RollingFileLog.FileName(day, 101))).Length);
        Assert.Equal(30 + 3, ReadLines(Path.Combine(_folder, RollingFileLog.FileName(day, 202))).Length);

        // Either process's diagnostics read both, in time order, the exception's lines under their entry.
        var lines = first.RecentLines(200);
        Assert.Equal(63, lines.Count);
        Assert.Equal(
            [.. Enumerable.Range(1, 31).Select(i => $"line {i}"), "with an exception", "    System.InvalidOperationException: two", "    lines", .. Enumerable.Range(32, 29).Select(i => $"line {i}")],
            lines.Select(line => line.StartsWith(' ') ? line : line[36..]));
        Assert.Equal(lines, second.RecentLines(200));
        Assert.EndsWith("line 60", first.RecentLines(1)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TheDiagnosticsReadOnlyTodayAndYesterday()
    {
        using (var old = new RollingFileLog(_folder, new FakeClock(_clock.Now.AddDays(-2)), new RollingFileLogOptions { ProcessId = 7 }))
        {
            old.Info("two days ago");
        }

        using var log = new RollingFileLog(_folder, _clock, new RollingFileLogOptions { ProcessId = 8 });
        log.Info("today");

        Assert.EndsWith("today", Assert.Single(log.RecentLines(200)), StringComparison.Ordinal);
    }

    private static string Name(int day, int part = 0) => RollingFileLog.FileName(new DateOnly(2026, 10, day), Environment.ProcessId, part);

    private static string Today() => Name(18);

    // The log keeps its file open for writing; readers share it, as Notepad and the diagnostics do.
    private static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string[] ReadLines(string path) => ReadText(path).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    private List<string> Files() =>
        Directory.EnumerateFiles(_folder).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal).ToList();

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private static readonly TimeZoneInfo _zone = TimeZoneInfo.CreateCustomTimeZone("Test +2", TimeSpan.FromHours(2), "Test +2", "Test +2");

        public DateTimeOffset Now { get; set; } = now;

        public override TimeZoneInfo LocalTimeZone => _zone;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }
}
