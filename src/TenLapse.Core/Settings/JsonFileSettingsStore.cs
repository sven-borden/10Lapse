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

    /// <summary>The file the settings are in.</summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public SettingsLoadResult Load()
    {
        string text;
        try
        {
            text = File.ReadAllText(FilePath);
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
        var folder = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, UserSettingsJson.Write(settings));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
