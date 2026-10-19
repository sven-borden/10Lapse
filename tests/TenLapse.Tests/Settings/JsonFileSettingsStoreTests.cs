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
    public void AFileOverTheSizeCapIsTheDefaultsWithAProblemAndIsNotRead()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        // Valid JSON padded past the cap: if it were read, the folder would come back.
        var padding = new string(' ', (int)JsonFileSettingsStore.MaxFileBytes + 1);
        System.IO.File.WriteAllText(SettingsPath, "{ \"version\": 1, \"importFolder\": \"C:\\\\Photos\" }" + padding);

        var result = new JsonFileSettingsStore(SettingsPath).Load();

        Assert.Equal(UserSettings.Default, result.Settings);
        Assert.Single(result.Problems);
        Assert.Contains("not read", result.Problems[0]);
    }

    [Fact]
    public void AFileJustUnderTheCapStillReads()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var text = "{ \"version\": 1, \"importFolder\": \"C:\\\\Photos\" }";
        System.IO.File.WriteAllText(SettingsPath, text + new string(' ', (int)JsonFileSettingsStore.MaxFileBytes - text.Length));

        var result = new JsonFileSettingsStore(SettingsPath).Load();

        Assert.Equal(@"C:\Photos", result.Settings.ImportFolder);
    }

    [Fact]
    public void TwoStoresSavingToOnePathNeverCollideAndLeaveNoTemporaryFile()
    {
        var first = new JsonFileSettingsStore(SettingsPath);
        var second = new JsonFileSettingsStore(SettingsPath);
        first.Save(UserSettings.Default);
        var collisions = 0;

        Parallel.For(0, 200, i =>
        {
            try
            {
                (i % 2 == 0 ? first : second).Save(new UserSettings { ImportFolder = @"C:\Folder" + i });
            }
            catch (IOException exception) when (exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                // The temporary file's own name was taken: what a fixed ".tmp" name would do.
                Interlocked.Increment(ref collisions);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Two writers replacing the file in the same instant can make one fail on Windows; the app shows that
                // once and the next change writes again. The file itself must stay whole.
            }
        });

        Assert.Equal(0, collisions);
        var loaded = first.Load();
        Assert.Empty(loaded.Problems);
        Assert.StartsWith(@"C:\Folder", loaded.Settings.ImportFolder);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.tmp"));
    }

    [Fact]
    public void AFailedSaveLeavesNoTemporaryFile()
    {
        // A folder where the file should be: the replace fails after the temporary file was written.
        Directory.CreateDirectory(SettingsPath);

        Assert.ThrowsAny<Exception>(() => new JsonFileSettingsStore(SettingsPath).Save(UserSettings.Default));

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.tmp"));
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Packages\Id\LocalState\Logs", @"C:\Users\me\AppData\Local\Packages\Id\LocalState\settings.json")]
    [InlineData(@"C:\Users\me\AppData\Local\10Lapse\Logs\", @"C:\Users\me\AppData\Local\10Lapse\settings.json")]
    public void TheSettingsFileSitsBesideTheLogFolder(string logFolder, string expected)
    {
        Assert.Equal(expected, SettingsLocation.BesideLogFolder(logFolder, "settings.json"));
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
