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
        var folder = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // A name of its own, so two running copies of the app never write the same temporary file.
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(UserSettingsJson.Write(settings));
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            // The data is on disk already (Flush above); the rename swaps it in whole or not at all.
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            // After a good write the temporary file is gone already; after a failed one it must not stay behind.
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do for a leftover temporary file; the save's own error is what matters.
            }
        }
    }
}
