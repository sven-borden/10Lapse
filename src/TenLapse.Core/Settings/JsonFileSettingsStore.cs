using System.Security.Cryptography;
using System.Text;

namespace TenLapse.Core.Settings;

/// <summary>
/// The settings in one JSON file (#21). Packaged, the app puts it in LocalState (removed with the app); unpackaged
/// (Debug) in %LOCALAPPDATA%\10Lapse, the folder the log and the encoder cache already use. It needs no package
/// identity, so one implementation serves both. A write goes to a temporary file first and replaces the old one, so a
/// crash or a full disk never leaves half a file.
/// </summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    /// <summary>Stores the settings in <paramref name="filePath"/> (a full path; its folder is created on the first save).</summary>
    public JsonFileSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    /// <summary>The largest settings file that is read. The real one is under 1 kB; anything bigger is not ours.</summary>
    public const long MaxFileBytes = 256 * 1024;

    /// <summary>The file the settings are in.</summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public SettingsLoadResult Load()
    {
        string text;
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxFileBytes)
            {
                return new SettingsLoadResult(UserSettings.Default, [$"The settings file is {stream.Length / 1024} kB, more than the {MaxFileBytes / 1024} kB a settings file can be, so it was not read."]);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            text = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return SettingsLoadResult.Fresh;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(UserSettings.Default, [$"The settings file could not be read ({exception.Message})."]);
        }

        return UserSettingsJson.Read(text);
    }

    /// <inheritdoc />
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Through a temporary file and one rename (#52 shares it with the project files).
        IO.AtomicFile.WriteAllText(FilePath, UserSettingsJson.Write(settings));
    }

    /// <summary>
    /// Reads, changes and writes the file under a lock every 10Lapse process takes for this file (a named mutex), so two
    /// windows (#52) never write over each other's changes. A lock its holder never released (the process died) is taken
    /// over: the file itself is always whole (<see cref="IO.AtomicFile"/>). Waits at most <see cref="LockWait"/>, then
    /// throws <see cref="IOException"/>; the caller tries again at the next change.
    /// </summary>
    public UserSettings Update(Func<UserSettings, UserSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(LockWait);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            if (!owned)
            {
                throw new IOException("Another 10Lapse is writing the settings file; the change is written with the next one.");
            }

            var next = change(Load().Settings);
            Save(next);
            return next;
        }
        finally
        {
            if (owned)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    /// <summary>The longest <see cref="Update"/> waits for another 10Lapse's write.</summary>
    public static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    // One name per settings file (a Debug --settings-folder gets its own), in this session's namespace.
    private string MutexName =>
        @"Local\TenLapse.Settings." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(FilePath).ToUpperInvariant())))[..32];
}
