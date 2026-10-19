using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace TenLapse.Media.Export;

/// <summary>
/// The unfinished export files (<see cref="ProjectExporter.PartialSuffix"/>) that exist right now, each with the
/// process that made it, kept in a small text file in the app's LocalState. A process killed in the middle of an
/// export, or closed before its background clean-up finished (<see cref="ExportCleanup"/>), leaves its partial file
/// behind; the next launch calls <see cref="DeleteLeftovers"/>, which deletes the ones whose process is gone.
/// </summary>
/// <remarks>
/// <see cref="ProjectExporter"/> records a partial file before it creates it and forgets it once it has become the
/// video or been deleted; a file whose deletion failed stays listed for the next launch. The journal never throws:
/// a failure to read or write it is reported through <see cref="ExportCleanup.Failed"/>. Two app instances writing
/// it at the same moment can lose an entry (that file then stays on disk); a running export's file is never
/// deleted, because its process is still running.
/// </remarks>
public sealed class PartialFileJournal
{
    // One lock for every journal in the process: two instances may name the same file.
    private static readonly Lock _gate = new();
    private static readonly Lazy<Owner> _thisProcess = new(() =>
    {
        using var process = Process.GetCurrentProcess();
        return new Owner(process.Id, process.StartTime.ToUniversalTime().Ticks);
    });

    private readonly Owner _owner;

    /// <summary>A journal kept in <paramref name="path"/> (created when the first partial file is recorded).</summary>
    public PartialFileJournal(string path)
        : this(path, null)
    {
    }

    /// <summary>A journal that records entries as <paramref name="owner"/>'s, for tests; null for this process.</summary>
    internal PartialFileJournal(string path, Owner? owner)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = System.IO.Path.GetFullPath(path);
        _owner = owner ?? _thisProcess.Value;
    }

    /// <summary>The journal file.</summary>
    public string Path { get; }

    /// <summary>The partial files listed now, for tests and diagnostics.</summary>
    public IReadOnlyList<string> Files
    {
        get
        {
            lock (_gate)
            {
                return [.. (Read() ?? []).Select(entry => entry.File)];
            }
        }
    }

    /// <summary>
    /// Deletes every listed partial file whose process is no longer running and forgets it; a file that is already
    /// gone is forgotten too. One that cannot be deleted stays listed and is reported. Returns how many it deleted.
    /// </summary>
    public int DeleteLeftovers()
    {
        lock (_gate)
        {
            var deleted = 0;
            var keep = new List<Entry>();
            if (Read() is not { } entries)
            {
                return 0;
            }

            foreach (var entry in entries)
            {
                if (IsRunning(entry.Owner))
                {
                    keep.Add(entry);
                    continue;
                }

                // Only ever a partial file with a full path: the journal names nothing else.
                if (!System.IO.Path.IsPathFullyQualified(entry.File) || !entry.File.EndsWith(ProjectExporter.PartialSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var existed = File.Exists(entry.File);
                if (ExportCleanup.Try(entry.File, "delete an unfinished file left by an earlier run", () => ExportCleanup.DeleteIfThere(entry.File)))
                {
                    deleted += existed ? 1 : 0;
                }
                else
                {
                    keep.Add(entry);
                }
            }

            Write(keep);
            return deleted;
        }
    }

    /// <summary>Lists <paramref name="partial"/> as this process's.</summary>
    internal void Add(string partial)
    {
        lock (_gate)
        {
            if (Read() is { } entries)
            {
                entries.Add(new Entry(_owner, partial));
                Write(entries);
            }
        }
    }

    /// <summary>Forgets <paramref name="partial"/>.</summary>
    internal void Remove(string partial)
    {
        lock (_gate)
        {
            if (Read() is { } entries && entries.RemoveAll(entry => string.Equals(entry.File, partial, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                Write(entries);
            }
        }
    }

    /// <summary>True while the process that made an entry runs: the same id and the same start time.</summary>
    internal static bool IsRunning(Owner owner)
    {
        try
        {
            using var process = Process.GetProcessById(owner.ProcessId);
            return process.StartTime.ToUniversalTime().Ticks == owner.StartedTicks;
        }
        catch (ArgumentException)
        {
            // No process with that id.
            return false;
        }
        catch (InvalidOperationException)
        {
            // It exited while we looked.
            return false;
        }
        catch (Exception exception) when (exception is Win32Exception or NotSupportedException)
        {
            // Cannot tell (another user's process): leave its file alone.
            return true;
        }
    }

    // One line per file: process id, its start time in UTC ticks, the full path, separated by tabs. Null when the
    // file cannot be read (reported): nothing is written then, so a passing failure never empties the journal.
    private List<Entry>? Read()
    {
        var entries = new List<Entry>();
        try
        {
            if (!File.Exists(Path))
            {
                return entries;
            }

            foreach (var line in File.ReadAllLines(Path))
            {
                var parts = line.Split('\t', 3);
                if (parts.Length == 3
                    && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var started)
                    && parts[2].Length > 0)
                {
                    entries.Add(new Entry(new Owner(id, started), parts[2]));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ExportCleanup.Report(new ExportCleanupFailure(Path, "read the list of unfinished export files", exception));
            return null;
        }

        return entries;
    }

    private void Write(List<Entry> entries)
    {
        try
        {
            if (entries.Count == 0)
            {
                ExportCleanup.DeleteIfThere(Path);
                return;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllLines(Path, entries.Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Owner.ProcessId}\t{entry.Owner.StartedTicks}\t{entry.File}")));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ExportCleanup.Report(new ExportCleanupFailure(Path, "update the list of unfinished export files", exception));
        }
    }

    /// <summary>A process: its id and its start time in UTC ticks (an id alone is reused).</summary>
    internal readonly record struct Owner(int ProcessId, long StartedTicks);

    private readonly record struct Entry(Owner Owner, string File);
}
