namespace TenLapse.Core.Projects;

/// <summary>A recovery copy left by a 10Lapse that ended without closing (#52), now this process's to offer.</summary>
/// <param name="Text">The copy's text (a <c>.10lapse</c> file with its <c>recovered</c> group).</param>
/// <param name="Saved">When the copy was written, UTC.</param>
public sealed record RecoveryCopy(string Text, DateTime Saved);

/// <summary>
/// The recovery copies of open projects (#52): one slot per running 10Lapse, in the app's own folder, never the user's
/// file. A fake in tests.
/// </summary>
public interface IRecoveryStore : IDisposable
{
    /// <summary>Writes this process's copy, replacing the last one. Throws when the write fails.</summary>
    void Save(string text);

    /// <summary>Removes this process's copy (the project was saved, exported, emptied or replaced). Never throws.</summary>
    void Clear();

    /// <summary>
    /// Takes the newest copy a 10Lapse left behind when it ended without closing (a crash, a kill, a power cut), or null
    /// when there is none. Taken means it is this process's now: offered once per launch, kept until this process closes
    /// (or until <see cref="MarkOfferRestored"/> and the next <see cref="Save"/>), and offered again if this process ends
    /// without closing too. Throws when the copy cannot be read; it is then deleted.
    /// </summary>
    RecoveryCopy? TakeOrphan();

    /// <summary>The offered copy was restored: it goes once this process's own copy of the restored project is written.</summary>
    void MarkOfferRestored();
}

/// <summary>
/// The recovery folder on disk (#52). Each running 10Lapse holds a lock file of its own (<c>id.lock</c>, open with no
/// sharing and deleted when it closes, so Windows releases it even when the process is killed) and writes its copy as
/// <c>id.10lapse</c>. A copy whose lock is free belongs to a 10Lapse that is gone: an orphan. Two copies of the app running
/// at once each keep their own slot.
/// </summary>
public sealed class RecoveryStore : IRecoveryStore
{
    private const string CopyExtension = ".10lapse";
    private const string OfferSuffix = ".offer" + CopyExtension;

    private readonly string _folder;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly Lock _gate = new();
    private FileStream? _lock;
    private bool _offerRestored;
    private bool _disposed;

    /// <summary>Keeps the copies in <paramref name="folder"/> (created on first use).</summary>
    public RecoveryStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = folder;
    }

    /// <summary>The folder the copies are in.</summary>
    public string Folder => _folder;

    private string CopyPath => Path.Combine(_folder, _id + CopyExtension);

    private string OfferPath => Path.Combine(_folder, _id + OfferSuffix);

    /// <inheritdoc />
    public void Save(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Claim();
            IO.AtomicFile.WriteAllText(CopyPath, text);
            if (_offerRestored)
            {
                TryDelete(OfferPath);
            }
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
        {
            TryDelete(CopyPath);
        }
    }

    /// <inheritdoc />
    public RecoveryCopy? TakeOrphan()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Claim();
            foreach (var orphan in Orphans())
            {
                try
                {
                    // Two launches at once: only one of them gets it; the other finds it gone and looks on.
                    File.Move(orphan, OfferPath, overwrite: false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                try
                {
                    var saved = File.GetLastWriteTimeUtc(OfferPath);
                    return new RecoveryCopy(ProjectFileSystem.Instance.ReadText(OfferPath), saved);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    TryDelete(OfferPath);
                    throw;
                }
            }

            return null;
        }
    }

    /// <inheritdoc />
    public void MarkOfferRestored()
    {
        lock (_gate)
        {
            _offerRestored = true;
        }
    }

    /// <summary>Closing: this process's copy and its offered one go, and the lock is released (and deleted).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            TryDelete(CopyPath);
            TryDelete(OfferPath);
            _lock?.Dispose();
            _lock = null;
        }
    }

    // The lock that says this process is alive, taken before the first copy is written or looked for.
    private void Claim()
    {
        if (_lock is not null)
        {
            return;
        }

        Directory.CreateDirectory(_folder);
        _lock = new FileStream(Path.Combine(_folder, _id + ".lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
    }

    // The copies of 10Lapses that are gone, the newest first.
    private IEnumerable<string> Orphans()
    {
        IEnumerable<FileInfo> copies;
        try
        {
            copies = new DirectoryInfo(_folder).EnumerateFiles("*" + CopyExtension).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return copies
            .Where(copy => Owner(copy.Name) is { } owner && owner != _id && !IsAlive(owner))
            .OrderByDescending(copy => copy.LastWriteTimeUtc)
            .Select(copy => copy.FullName)
            .ToList();
    }

    private static string? Owner(string fileName)
    {
        var dot = fileName.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? fileName[..dot] : null;
    }

    // Alive while its lock file is held open. A lock file left without a holder (it should delete itself) is stale.
    private bool IsAlive(string owner)
    {
        var path = Path.Combine(_folder, owner + ".lock");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }

            TryDelete(path);
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A copy that cannot go now is an orphan for a later launch to offer, never lost silently.
        }
    }
}
