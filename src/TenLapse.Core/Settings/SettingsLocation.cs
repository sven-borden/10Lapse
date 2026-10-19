namespace TenLapse.Core.Settings;

/// <summary>Where the app's small files live relative to its log folder (#21), kept pure so a test covers the packaged layout.</summary>
public static class SettingsLocation
{
    /// <summary>
    /// The file <paramref name="name"/> beside the log folder: the parent of <c>LocalState\Logs</c> packaged, or of
    /// <c>%LOCALAPPDATA%\10Lapse\Logs</c> unpackaged.
    /// </summary>
    public static string BesideLogFolder(string logFolder, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(logFolder));
        return Path.Combine(string.IsNullOrEmpty(parent) ? logFolder : parent, name);
    }
}
