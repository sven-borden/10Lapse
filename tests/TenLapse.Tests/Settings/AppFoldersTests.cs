using TenLapse.Core.Settings;

namespace TenLapse.Tests.Settings;

/// <summary>Where the app writes (#110): the defaults do not move, and a Debug run with its folder switches never reaches the real app folder.</summary>
public sealed class AppFoldersTests
{
    private static readonly string _real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "10Lapse");
    private static readonly string _branch = Path.Combine(Path.GetTempPath(), "claude", "fix-110-debug-journal-path", "app-folders-tests");

    private static AppFolders Defaults() => AppFolders.Resolve(Path.Combine(_real, "Logs"), Path.Combine(_real, "Proxies"));

    private static IEnumerable<string> All(AppFolders folders) =>
        [folders.LogFolder, folders.SettingsFile, folders.EncoderCacheFile, folders.UnfinishedExportsFile, folders.ProxyCacheFolder];

    private static bool Under(string path, string folder) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void WithoutSwitchesTheUnpackagedPathsAreTheOnesTheAppAlwaysUsed()
    {
        var folders = Defaults();

        Assert.Equal(Path.Combine(_real, "Logs"), folders.LogFolder);
        Assert.Equal(Path.Combine(_real, "settings.json"), folders.SettingsFile);
        Assert.Equal(Path.Combine(_real, "encoder-capabilities.json"), folders.EncoderCacheFile);
        Assert.Equal(Path.Combine(_real, "unfinished-exports.txt"), folders.UnfinishedExportsFile);
        Assert.Equal(Path.Combine(_real, "Proxies"), folders.ProxyCacheFolder);
        Assert.Equal(folders, AppFolders.Resolve(Path.Combine(_real, "Logs"), Path.Combine(_real, "Proxies"), new AppFolderOverrides(null, null, null, null)));
    }

    [Fact]
    public void PackagedPathsKeepTheirLayout()
    {
        const string state = @"C:\Users\Jane\AppData\Local\Packages\10Lapse_x\LocalState";
        const string cache = @"C:\Users\Jane\AppData\Local\Packages\10Lapse_x\LocalCache\Proxies";

        var folders = AppFolders.Resolve(Path.Combine(state, "Logs"), cache);

        Assert.Equal(Path.Combine(state, "unfinished-exports.txt"), folders.UnfinishedExportsFile);
        Assert.Equal(Path.Combine(state, "settings.json"), folders.SettingsFile);
        Assert.Equal(cache, folders.ProxyCacheFolder);
    }

    [Fact]
    public void TheLogFolderSwitchMovesTheJournalBesideIt()
    {
        var log = Path.Combine(_branch, "run", "logs");

        var folders = AppFolders.Resolve(Path.Combine(_real, "Logs"), Path.Combine(_real, "Proxies"), new AppFolderOverrides(log, null, null, null));

        Assert.Equal(log, folders.LogFolder);
        Assert.Equal(Path.Combine(_branch, "run", "unfinished-exports.txt"), folders.UnfinishedExportsFile);
        Assert.Equal(Path.Combine(_real, "settings.json"), folders.SettingsFile);
    }

    [Fact]
    public void WithEveryFolderSwitchSetNoPathIsUnderTheRealAppFolder()
    {
        var overrides = new AppFolderOverrides(
            Path.Combine(_branch, "logs"),
            Path.Combine(_branch, "settings"),
            Path.Combine(_branch, "encoder", "encoder-capabilities.json"),
            Path.Combine(_branch, "cache"));

        var folders = AppFolders.Resolve(Path.Combine(_real, "Logs"), Path.Combine(_real, "Proxies"), overrides);

        foreach (var path in All(folders))
        {
            Assert.False(Under(path, _real), $"{path} is under {_real}");
            Assert.True(Under(path, _branch), $"{path} is not under {_branch}");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankSwitchIsNotASwitch(string? blank) =>
        Assert.Equal(Defaults(), AppFolders.Resolve(Path.Combine(_real, "Logs"), Path.Combine(_real, "Proxies"), new AppFolderOverrides(blank, blank, blank, blank)));
}
