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
}
