using TenLapse.Core.Projects;
using TenLapse.Core.Settings;

namespace TenLapse.Tests.Settings;

/// <summary>The settings file on disk (#21): the folder the app uses unpackaged does not exist at first, and a bad file never stops the app.</summary>
public sealed class JsonFileSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "claude", "feat-21-remember-settings", "store-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string SettingsPath => Path.Combine(_folder, "10Lapse", "settings.json");

    [Fact]
    public void NoFileIsTheDefaultsWithoutAProblem()
    {
        var result = new JsonFileSettingsStore(SettingsPath).Load();

        Assert.Equal(UserSettings.Default, result.Settings);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void ASaveCreatesTheFolderAndALoadReadsItBack()
    {
        var settings = new UserSettings { Frame = new FrameSettings(FrameAspect.Ratio4x3, FitMode.Fit), ImportFolder = @"C:\Photos" };
        var store = new JsonFileSettingsStore(SettingsPath);

        store.Save(settings);

        Assert.Equal(settings, new JsonFileSettingsStore(SettingsPath).Load().Settings);
        Assert.False(System.IO.File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void ASecondSaveReplacesTheFirst()
    {
        var store = new JsonFileSettingsStore(SettingsPath);
        store.Save(new UserSettings { ImportFolder = @"C:\One" });

        store.Save(new UserSettings { ImportFolder = @"C:\Two" });

        Assert.Equal(@"C:\Two", store.Load().Settings.ImportFolder);
    }

    [Fact]
    public void ACorruptFileIsTheDefaultsWithAProblemAndStaysUntilTheNextSave()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        System.IO.File.WriteAllText(SettingsPath, "{ \"version\": 1, \"frame\": ");
        var store = new JsonFileSettingsStore(SettingsPath);

        var result = store.Load();

        Assert.Equal(UserSettings.Default, result.Settings);
        Assert.Single(result.Problems);
        Assert.True(System.IO.File.Exists(SettingsPath));

        store.Save(new UserSettings { ImportFolder = @"C:\Photos" });
        Assert.Empty(store.Load().Problems);
    }

    [Fact]
    public void APathThatCannotBeReadIsTheDefaultsWithAProblem()
    {
        // A folder where the file should be: reading it fails like a file that is locked or denied.
        Directory.CreateDirectory(SettingsPath);

        var result = new JsonFileSettingsStore(SettingsPath).Load();

        Assert.Equal(UserSettings.Default, result.Settings);
        Assert.Single(result.Problems);
    }

    [Fact]
    public void ASaveThatCannotBeWrittenThrowsForTheCallerToReport()
    {
        Directory.CreateDirectory(SettingsPath);

        Assert.ThrowsAny<Exception>(() => new JsonFileSettingsStore(SettingsPath).Save(UserSettings.Default));
    }
}
