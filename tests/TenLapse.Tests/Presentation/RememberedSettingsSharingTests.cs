using System.Reflection;
using TenLapse.Core.Projects;
using TenLapse.Core.Settings;
using TenLapse.Core.Timing;
using TenLapse.Presentation;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// Two 10Lapse windows on one settings file (#52): neither loses the other's recent projects or settings, because each
/// write rereads the file under a lock every process takes and writes only what its own window changed.
/// </summary>
public sealed class RememberedSettingsSharingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "claude", "feat-52-save-projects", "settings-sharing", Guid.NewGuid().ToString("N"));
    private readonly FakeErrorSink _errors = new();
    private readonly FakeLog _log = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    private RememberedSettings Window() => new(new JsonFileSettingsStore(SettingsFile), _errors, _log, new ManualTimeProvider());

    [Fact]
    public void TwoWindowsKeepEachOthersRecentProjectsAndSettings()
    {
        new JsonFileSettingsStore(SettingsFile).Save(new UserSettings { RecentProjects = PathList.Of([@"D:\P.10lapse"]), SuccessfulExports = 1 });
        using var a = Window();
        using var b = Window();

        // B opens Q and changes the frame shape; A then saves P again and changes the timing.
        b.AddRecentProject(@"D:\Q.10lapse");
        b.CountSuccessfulExport();
        b.Flush();
        a.AddRecentProject(@"D:\P.10lapse");
        a.ExportFolder = @"D:\Videos";
        a.CountSuccessfulExport();
        a.Flush();

        var stored = new JsonFileSettingsStore(SettingsFile).Load().Settings;
        Assert.Equal([@"D:\P.10lapse", @"D:\Q.10lapse"], stored.RecentProjects);
        Assert.Equal(@"D:\Videos", stored.ExportFolder);
        Assert.Equal(3, stored.SuccessfulExports);

        // B's later write keeps A's folder and recent order, and adds its own step on top.
        b.LastFolder = @"C:\Photos";
        b.RemoveRecentProject(@"D:\P.10lapse");
        b.Flush();
        stored = new JsonFileSettingsStore(SettingsFile).Load().Settings;
        Assert.Equal([@"D:\Q.10lapse"], stored.RecentProjects);
        Assert.Equal(@"D:\Videos", stored.ExportFolder);
        Assert.Equal(@"C:\Photos", stored.ImportFolder);
        Assert.Empty(_errors.Shown);
        Assert.Empty(_errors.Reported);
    }

    [Fact]
    public async Task AWindowSeesTheOtherWindowsRecentProjectsWhenItLooksAgain()
    {
        using var a = Window();
        using var b = Window();
        b.AddRecentProject(@"D:\Q.10lapse");
        b.Flush();
        var changed = 0;
        a.RecentProjectsChanged += (_, _) => changed++;

        await a.RefreshRecentProjectsAsync();

        Assert.Equal([@"D:\Q.10lapse"], a.RecentProjects);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task WritersInParallelLoseNoUpdate()
    {
        var store = new JsonFileSettingsStore(SettingsFile);
        store.Save(UserSettings.Default);

        // Each its own store object, as each process has: only the named lock keeps them apart.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            var mine = new JsonFileSettingsStore(SettingsFile);
            for (var i = 0; i < 15; i++)
            {
                mine.Update(settings => settings with { SuccessfulExports = settings.SuccessfulExports + 1 });
            }
        })));

        Assert.Equal(120, store.Load().Settings.SuccessfulExports);
    }

    [Fact]
    public void EveryFieldIsMerged()
    {
        // A field this process changed wins; a field it did not change takes the stored one. A field Merge forgets would
        // come back as its default and fail here.
        var stored = new UserSettings
        {
            Timing = TimingSettings.PerSecond(new Fraction(12, 1)),
            Frame = new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit),
            Output = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4Hevc, OutputQuality.Maximum),
            ImportFolder = @"C:\In",
            ExportFolder = @"C:\Out",
            SuccessfulExports = 7,
            RatingAskedVersion = "3.1.0.0",
            RecentProjects = PathList.Of([@"C:\A.10lapse"]),
        };
        foreach (var property in typeof(UserSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.NotEqual(property.GetValue(UserSettings.Default), property.GetValue(stored));
        }

        Assert.Equal(stored, RememberedSettings.Merge(stored, UserSettings.Default, UserSettings.Default, []));
    }
}
