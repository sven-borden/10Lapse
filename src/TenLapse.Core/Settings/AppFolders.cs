namespace TenLapse.Core.Settings;

/// <summary>
/// The Debug folder switches that move the app's own files (<c>--log-folder=</c>, <c>--settings-folder=</c>,
/// <c>--encoder-cache=</c>, <c>--cache-folder=</c>), each null when not given (#110).
/// </summary>
public sealed record AppFolderOverrides(string? LogFolder, string? SettingsFolder, string? EncoderCacheFile, string? CacheFolder);

/// <summary>
/// Every file and folder the app itself writes to or deletes in (#110), resolved from the defaults (packaged
/// LocalState/LocalCache, or %LOCALAPPDATA%\10Lapse unpackaged) and the Debug overrides. Pure, so a test covers that a
/// Debug run with its switches set never reaches the real app folder. Without overrides the result is the defaults, so
/// the packaged and the unpackaged paths do not change.
/// </summary>
/// <param name="LogFolder">The log files.</param>
/// <param name="SettingsFile">settings.json (#21).</param>
/// <param name="EncoderCacheFile">The encoder probe's cache (#9).</param>
/// <param name="UnfinishedExportsFile">The journal of export files still being written (#22).</param>
/// <param name="ProxyCacheFolder">The preview's proxies (#19).</param>
/// <param name="RecoveryFolder">The open projects' recovery copies (#52), beside the settings file, so <c>--settings-folder=</c> moves it too.</param>
public sealed record AppFolders(string LogFolder, string SettingsFile, string EncoderCacheFile, string UnfinishedExportsFile, string ProxyCacheFolder, string RecoveryFolder)
{
    /// <summary>
    /// Resolves the paths. <paramref name="defaultLogFolder"/> and <paramref name="defaultProxyCacheFolder"/> are the
    /// app's own (<c>AppEnvironment</c>); the small files sit beside the log folder, as they always have.
    /// An override replaces only its own path, except the journal of unfinished export files, which sits beside the log
    /// folder in use, so <c>--log-folder=</c> moves it too, and the recovery copies, which sit beside the settings file in use.
    /// </summary>
    public static AppFolders Resolve(string defaultLogFolder, string defaultProxyCacheFolder, AppFolderOverrides? overrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLogFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultProxyCacheFolder);

        var log = Given(overrides?.LogFolder) ?? defaultLogFolder;
        var settings = Given(overrides?.SettingsFolder) is { } settingsFolder
            ? Path.Combine(settingsFolder, "settings.json")
            : SettingsLocation.BesideLogFolder(defaultLogFolder, "settings.json");
        var encoderCache = Given(overrides?.EncoderCacheFile) ?? SettingsLocation.BesideLogFolder(defaultLogFolder, "encoder-capabilities.json");
        var proxies = Given(overrides?.CacheFolder) ?? defaultProxyCacheFolder;

        var recovery = Path.Combine(Path.GetDirectoryName(settings) ?? log, "Recovery");
        return new AppFolders(log, settings, encoderCache, SettingsLocation.BesideLogFolder(log, "unfinished-exports.txt"), proxies, recovery);
    }

    private static string? Given(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
