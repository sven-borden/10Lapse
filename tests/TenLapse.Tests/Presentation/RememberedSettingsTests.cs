using TenLapse.Core.Diagnostics;
using TenLapse.Core.Projects;
using TenLapse.Core.Settings;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>A clock the test moves: timers fire when <see cref="Advance"/> passes their due time, on the calling thread.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private TimeSpan _now;

    // The wall clock moves with Advance too, from a fixed start.
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero) + _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (var timer in _timers.ToArray())
        {
            timer.FireIfDue(_now);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan? _due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void FireIfDue(TimeSpan now)
        {
            if (_due is { } due && due <= now)
            {
                _due = null;
                callback(state);
            }
        }

        public void Dispose() => _due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>The remembered settings (#21): a new project starts from them, changes are saved after a pause and at close, reset.</summary>
public sealed class RememberedSettingsTests
{
    private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(600);

    private readonly MemorySettingsStore _store = new();
    private readonly FakeErrorSink _errors = new();
    private readonly FakeLog _log = new();
    private readonly ManualTimeProvider _time = new();

    private static readonly UserSettings _saved = new()
    {
        Timing = new TimingSettings(SpeedMode.Length, new Fraction(24, 1), new Fraction(45, 1), new Fraction(2, 1)),
        Frame = new FrameSettings(FrameAspect.Ratio9x16, FitMode.Fit),
        Output = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4Hevc, OutputQuality.Maximum),
        ImportFolder = @"C:\Photos\Trip",
        ExportFolder = @"D:\Videos",
    };

    private RememberedSettings Remembered(ISettingsStore? store = null) => new(store ?? _store, _errors, _log, _time, _debounce);

    private ShellViewModel Attached(RememberedSettings remembered)
    {
        var shell = new ShellViewModel(new FakeDialogService(), _errors, remembered.CreateProject);
        remembered.Attach(shell);
        return shell;
    }

    [Fact]
    public void AnEmptyStoreGivesTheDefaultsAndANewProjectStartsFromThem()
    {
        var remembered = Remembered();

        var project = remembered.CreateProject();

        Assert.Equal(UserSettings.Default, remembered.Current);
        Assert.Equal(TimingSettings.Default, project.Timing);
        Assert.Equal(FrameSettings.Default, project.FrameSettings);
        Assert.Equal(OutputSettings.Default, project.Output);
        Assert.False(project.IsDirty);
        Assert.Null(remembered.LastFolder);
        Assert.Null(remembered.ExportFolder);
    }

    [Fact]
    public void ANewProjectStartsFromTheSavedValuesAndIsClean()
    {
        var store = new MemorySettingsStore(new SettingsLoadResult(_saved, []));
        var remembered = Remembered(store);

        var shell = new ShellViewModel(new FakeDialogService(), _errors, remembered.CreateProject);

        var project = shell.CurrentProject.Model;
        Assert.Equal(_saved.Timing, project.Timing);
        Assert.Equal(_saved.Frame, project.FrameSettings);
        Assert.Equal(_saved.Output, project.Output);
        Assert.False(project.IsDirty);
        Assert.False(shell.HasUnsavedChanges);
        Assert.Equal(@"C:\Photos\Trip", remembered.LastFolder);
        Assert.Equal(@"D:\Videos", remembered.ExportFolder);
    }

    [Fact]
    public void ANewProjectAfterAChangeStartsFromTheChange()
    {
        var remembered = Remembered();
        var shell = Attached(remembered);
        shell.CurrentProject.Model.FrameSettings = new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit);
        shell.CurrentProject.Model.Output = new OutputSettings(OutputResolution.Hd720, VideoFormat.Mp4H264, OutputQuality.Standard);

        Assert.True(shell.ReplaceWithoutAsking());

        var next = shell.CurrentProject.Model;
        Assert.Equal(new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit), next.FrameSettings);
        Assert.Equal(OutputResolution.Hd720, next.Output.Resolution);
        Assert.False(next.IsDirty);
    }

    [Fact]
    public void ChangesInTheProjectAfterItIsReplacedAreFollowedToo()
    {
        var remembered = Remembered();
        var shell = Attached(remembered);
        var first = shell.CurrentProject.Model;
        shell.ReplaceWithoutAsking();

        shell.CurrentProject.Model.Timing = TimingSettings.PerPhoto(new Fraction(3, 2));
        _time.Advance(_debounce);

        Assert.Equal(TimingSettings.PerPhoto(new Fraction(3, 2)), _store.Stored.Timing);
        Assert.True(first.IsClosed);
    }

    [Fact]
    public void EveryRememberedFieldIsSavedAfterThePause()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;

        project.Timing = _saved.Timing;
        project.FrameSettings = _saved.Frame;
        project.Output = _saved.Output;
        remembered.LastFolder = _saved.ImportFolder;
        remembered.ExportFolder = _saved.ExportFolder;
        _time.Advance(_debounce);

        Assert.Equal(_saved, _store.Stored);
        Assert.Equal(1, _store.Saves);
    }

    [Fact]
    public void TheOutputSavedIsThePreferenceNotWhatThePcCanDo()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;

        // 4K and HEVC on a PC that cannot do them: the project holds the pick, and so does the file.
        project.Output = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4Hevc, OutputQuality.High);
        _time.Advance(_debounce);

        Assert.Equal(OutputResolution.Uhd2160, _store.Stored.Output.Resolution);
        Assert.Equal(VideoFormat.Mp4Hevc, _store.Stored.Output.Format);
    }

    [Fact]
    public void NothingIsWrittenBeforeThePauseEndsAndABurstIsOneWrite()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;

        project.Timing = TimingSettings.PerSecond(new Fraction(10, 1));
        _time.Advance(TimeSpan.FromMilliseconds(599));
        Assert.Equal(0, _store.Saves);

        // Each change restarts the wait.
        project.Timing = TimingSettings.PerSecond(new Fraction(11, 1));
        _time.Advance(TimeSpan.FromMilliseconds(400));
        project.Timing = TimingSettings.PerSecond(new Fraction(12, 1));
        _time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(0, _store.Saves);

        _time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1, _store.Saves);
        Assert.Equal(new Fraction(12, 1), _store.Stored.Timing.FramesPerSecond);
    }

    [Fact]
    public void ClosingWritesTheLastChangeAtOnce()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;
        project.FrameSettings = new FrameSettings(FrameAspect.Ratio4x3, FitMode.Fit);
        Assert.Equal(0, _store.Saves);

        remembered.Dispose();

        Assert.Equal(1, _store.Saves);
        Assert.Equal(FrameAspect.Ratio4x3, _store.Stored.Frame.Aspect);
    }

    [Fact]
    public void FlushWritesOnlyWhatIsPending()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;
        remembered.Flush();
        Assert.Equal(0, _store.Saves);

        project.Output = project.Output with { Quality = OutputQuality.Maximum };
        remembered.Flush();
        remembered.Flush();
        _time.Advance(_debounce);

        Assert.Equal(1, _store.Saves);
    }

    [Fact]
    public void ChangesAfterClosingAreIgnoredAndClosingTwiceIsFine()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;
        remembered.Dispose();

        project.FrameSettings = new FrameSettings(FrameAspect.Ratio4x5, FitMode.Fit);
        remembered.LastFolder = @"C:\Late";
        remembered.Dispose();
        _time.Advance(_debounce);

        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public void AValueSetToWhatItAlreadyIsWritesNothing()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;

        project.Timing = TimingSettings.Default;
        remembered.LastFolder = null;
        _time.Advance(_debounce);

        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public void ChangesMadeBeforeAttachingAreNotRemembered()
    {
        // The Debug switches (--output=2160) set the project before the window is built; they are not the user's choice.
        var remembered = Remembered();
        var shell = new ShellViewModel(new FakeDialogService(), _errors, remembered.CreateProject);
        shell.CurrentProject.Model.Output = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4Hevc, OutputQuality.Maximum);

        remembered.Attach(shell);
        _time.Advance(_debounce);

        Assert.Equal(0, _store.Saves);
        Assert.Equal(OutputSettings.Default, remembered.Current.Output);
    }

    [Fact]
    public void AnImportOrAnEditOfFramesWritesNothing()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;

        project.AddFrames(TestFrames.Folder("a", 5));
        project.SortMode = SortMode.Name;
        _time.Advance(_debounce);

        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public void TheFolderPickersReadAndWriteTheRememberedFolder()
    {
        var remembered = Remembered();
        IImportFolderMemory memory = remembered;

        memory.LastFolder = @"C:\Photos\Hike";
        _time.Advance(_debounce);

        Assert.Equal(@"C:\Photos\Hike", _store.Stored.ImportFolder);
        Assert.Equal(@"C:\Photos\Hike", new RememberedSettings(_store, _errors, _log, _time, _debounce).LastFolder);
    }

    [Fact]
    public void ResetSavesTheDefaultsAtOnceAndPutsTheOpenProjectBackWithoutTouchingItsFrames()
    {
        var store = new MemorySettingsStore(new SettingsLoadResult(_saved, []));
        var remembered = Remembered(store);
        var shell = Attached(remembered);
        var project = shell.CurrentProject.Model;
        project.AddFrames(TestFrames.Folder("a", 7));
        project.SortMode = SortMode.Name;
        var before = project.Frames;

        remembered.ResetToDefaults();

        Assert.Equal(UserSettings.Default, store.Stored);
        Assert.Equal(UserSettings.Default, remembered.Current);
        Assert.Equal(TimingSettings.Default, project.Timing);
        Assert.Equal(FrameSettings.Default, project.FrameSettings);
        Assert.Equal(OutputSettings.Default, project.Output);
        Assert.Equal(before, project.Frames);
        Assert.Equal(SortMode.Name, project.SortMode);
        Assert.Null(remembered.LastFolder);

        // Nothing is left to write, and a later project starts from the defaults.
        var saves = store.Saves;
        _time.Advance(_debounce);
        Assert.Equal(saves, store.Saves);
        shell.ReplaceWithoutAsking();
        Assert.Equal(OutputSettings.Default, shell.CurrentProject.Model.Output);
    }

    [Fact]
    public void SavingAndResettingNeverChangeTheProjectBackSoNothingLoops()
    {
        // A write must not raise a project change (which would schedule another write), and a reset is one change per
        // setting at most: the number of changes and of writes stay small however often the clock runs. There is no
        // memory assertion: GC.GetTotalMemory covers the whole process, which other test classes disturb in parallel,
        // and ResetToDefaults writes on a Task.Run thread, so GetAllocatedBytesForCurrentThread would miss the write.
        // A loop would show in the two counts first.
        var store = new MemorySettingsStore(new SettingsLoadResult(_saved, []));
        var remembered = Remembered(store);
        var project = Attached(remembered).CurrentProject.Model;
        var changes = 0;
        project.Changed += (_, _) => changes++;

        project.Timing = TimingSettings.PerSecond(new Fraction(20, 1));
        _time.Advance(_debounce);
        var versionAfterSave = project.Version;
        for (var i = 0; i < 50; i++)
        {
            _time.Advance(_debounce);
            remembered.Flush();
        }

        Assert.Equal(versionAfterSave, project.Version);
        Assert.Equal(1, changes);
        Assert.Equal(1, store.Saves);

        remembered.ResetToDefaults();
        for (var i = 0; i < 50; i++)
        {
            _time.Advance(_debounce);
        }

        Assert.True(changes <= 4, $"A reset raised {changes - 1} project changes.");
        Assert.Equal(2, store.Saves);
    }

    [Fact]
    public void ResetOnADefaultProjectLeavesItClean()
    {
        var remembered = Remembered();
        var shell = Attached(remembered);

        remembered.ResetToDefaults();

        Assert.False(shell.CurrentProject.Model.IsDirty);
    }

    [Fact]
    public void ResetStillWorksBeforeTheShellIsAttached()
    {
        var store = new MemorySettingsStore(new SettingsLoadResult(_saved, []));
        var remembered = Remembered(store);

        remembered.ResetToDefaults();

        Assert.Equal(UserSettings.Default, store.Stored);
    }

    [Fact]
    public void TheSettingsViewModelsResetCommandResetsAndSaysSo()
    {
        var store = new MemorySettingsStore(new SettingsLoadResult(_saved, []));
        var remembered = Remembered(store);
        var shell = Attached(remembered);
        var infoBars = new FakeInfoBarService();
        var collector = new DiagnosticsCollector([], _log);
        var settings = new SettingsViewModel(_errors, new DiagnosticsCopier(collector, new FakeClipboard(), infoBars), remembered, infoBars, new FakeLauncher());

        settings.ResetToDefaultsCommand.Execute(null);

        Assert.Equal(UserSettings.Default, store.Stored);
        Assert.Equal(OutputSettings.Default, shell.CurrentProject.Model.Output);
        var bar = Assert.Single(infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Success, bar.Severity);
        Assert.Empty(_errors.Shown);
    }

    [Fact]
    public void WhatCouldNotBeReadAtStartIsLoggedOnceAndTheRestIsKept()
    {
        var problems = new[] { "Setting timing.mode is not usable.", "Setting output.quality is not usable." };
        var store = new MemorySettingsStore(new SettingsLoadResult(_saved with { Frame = FrameSettings.Default }, problems));

        var remembered = Remembered(store);

        var warning = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("timing.mode", warning.Message, StringComparison.Ordinal);
        Assert.Contains("output.quality", warning.Message, StringComparison.Ordinal);
        Assert.Equal(_saved.Output, remembered.Current.Output);
        Assert.Empty(_errors.Shown);
    }

    [Fact]
    public void ACleanLoadLogsNoWarning()
    {
        Remembered();

        Assert.DoesNotContain(_log.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public void AFailedWriteIsShownOnceAndTheRepeatsGoToTheLog()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;
        _store.FailWith = new IOException("The disk is full.");

        project.FrameSettings = new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit);
        _time.Advance(_debounce);
        project.FrameSettings = new FrameSettings(FrameAspect.Ratio4x3, FitMode.Fit);
        _time.Advance(_debounce);
        project.FrameSettings = new FrameSettings(FrameAspect.Ratio4x5, FitMode.Fit);
        _time.Advance(_debounce);

        Assert.Single(_errors.Shown);
        Assert.Equal(2, _errors.Reported.Count);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public void AfterAFailureTheNextChangeWritesEverythingStillPending()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;
        _store.FailWith = new IOException("Locked.");
        project.Timing = TimingSettings.PerSecond(new Fraction(8, 1));
        _time.Advance(_debounce);

        _store.FailWith = null;
        project.FrameSettings = new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit);
        _time.Advance(_debounce);

        Assert.Equal(new Fraction(8, 1), _store.Stored.Timing.FramesPerSecond);
        Assert.Equal(FrameAspect.Ratio1x1, _store.Stored.Frame.Aspect);

        // A failure after a success is news again.
        _store.FailWith = new IOException("Locked again.");
        project.FrameSettings = new FrameSettings(FrameAspect.Ratio4x3, FitMode.Fit);
        _time.Advance(_debounce);
        Assert.Equal(2, _errors.Shown.Count);
    }

    [Fact]
    public void AWriteThatFailsAtCloseGoesToTheLogOnly()
    {
        var remembered = Remembered();
        var project = Attached(remembered).CurrentProject.Model;
        _store.FailWith = new IOException("Locked.");
        project.Timing = TimingSettings.PerSecond(new Fraction(9, 1));

        remembered.Dispose();

        Assert.Empty(_errors.Shown);
        Assert.Single(_errors.Reported);
    }

    [Fact]
    public void AStoreThatThrowsOnLoadGivesTheDefaultsAndOneLogLine()
    {
        var remembered = Remembered(new ThrowingStore());

        Assert.Equal(UserSettings.Default, remembered.Current);
        Assert.Single(_log.Entries, entry => entry.Level == TenLapse.Core.Diagnostics.LogLevel.Error);
        Assert.Empty(_errors.Shown);
        Assert.Equal(OutputSettings.Default, remembered.CreateProject().Output);
    }

    [Fact]
    public void AHostileFileOnDiskStartsTheAppWithDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "claude", "feat-21-remember-settings", "hostile-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{ "version": 1, "timing": { "framesPerSecond": [null, 1] } }""");

            var remembered = Remembered(new JsonFileSettingsStore(path));

            Assert.Equal(UserSettings.Default, remembered.Current);
            Assert.Single(_log.Entries, entry => entry.Level == TenLapse.Core.Diagnostics.LogLevel.Warning);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ResetReportsWhetherTheFileWasWritten()
    {
        var remembered = Remembered(new MemorySettingsStore(new SettingsLoadResult(_saved, [])));
        Assert.True(remembered.ResetToDefaults());

        _store.FailWith = new IOException("Locked.");
        var failing = Remembered();
        failing.LastFolder = @"C:\Photos";
        Assert.False(failing.ResetToDefaults());
        Assert.Single(_errors.Shown);
    }

    [Fact]
    public void TheResetCommandSaysNothingWhenTheWriteFailed()
    {
        _store.FailWith = new IOException("Locked.");
        var remembered = Remembered();
        var infoBars = new FakeInfoBarService();
        var collector = new DiagnosticsCollector([], _log);
        var settings = new SettingsViewModel(_errors, new DiagnosticsCopier(collector, new FakeClipboard(), infoBars), remembered, infoBars, new FakeLauncher());

        settings.ResetToDefaultsCommand.Execute(null);

        Assert.Empty(infoBars.Shown);
        Assert.Single(_errors.Shown);
    }

    [Fact]
    public void AWriteThatHangsDoesNotHangClosingOrReset()
    {
        using var release = new ManualResetEventSlim();
        var store = new BlockingStore(release);
        var remembered = new RememberedSettings(store, _errors, _log, _time, _debounce, TimeSpan.FromMilliseconds(100));
        remembered.LastFolder = @"C:\Photos";

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var written = remembered.ResetToDefaults();
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

        Assert.False(written);
        // No lower bound: the warning below says the timeout ran out, and a wait can return up to a timer tick (about 15.6 ms) early.
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"The reset took {elapsed.TotalMilliseconds:0} ms.");
        Assert.Contains(_log.Entries, entry => entry.Level == TenLapse.Core.Diagnostics.LogLevel.Warning && entry.Message.Contains("longer than", StringComparison.Ordinal));
        release.Set();
    }

    [Fact]
    public async Task RealTimersWithParallelChangesAndADisposeRacingThemKeepTheLastWrittenStateConsistent()
    {
        for (var round = 0; round < 5; round++)
        {
            var store = new MemorySettingsStore();
            var errors = new FakeErrorSink();
            var remembered = new RememberedSettings(store, errors, new FakeLog(), TimeProvider.System, TimeSpan.FromMilliseconds(1));
            using var go = new ManualResetEventSlim();
            var workers = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
            {
                go.Wait();
                for (var i = 0; i < 300; i++)
                {
                    remembered.LastFolder = @"C:\Folder" + worker + @"\" + i;
                    remembered.ExportFolder = @"D:\Out" + worker + @"\" + i;
                    if (i % 50 == 0)
                    {
                        remembered.Flush();
                    }
                }
            })).ToArray();

            go.Set();
            await Task.Delay(2, TestContext.Current.CancellationToken);
            remembered.Dispose();
            await Task.WhenAll(workers);

            // Changes after the dispose are ignored; everything before it is on disk, in order.
            Assert.Equal(remembered.Current, store.Stored);
            Assert.Empty(errors.Shown);
            Assert.Empty(errors.Reported);
        }
    }

    private sealed class ThrowingStore : ISettingsStore
    {
        public SettingsLoadResult Load() => throw new NullReferenceException("a store that broke its promise");

        public void Save(UserSettings settings)
        {
        }
    }

    private sealed class BlockingStore(ManualResetEventSlim release) : ISettingsStore
    {
        public SettingsLoadResult Load() => SettingsLoadResult.Fresh;

        public void Save(UserSettings settings) => release.Wait(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ALoadTakesNoNoticeableTime()
    {
        var path = Path.Combine(Path.GetTempPath(), "claude", "feat-21-remember-settings", "load-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            new JsonFileSettingsStore(path).Save(_saved);
            _ = UserSettingsJson.Read(File.ReadAllText(path));

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var loaded = new JsonFileSettingsStore(path).Load();
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

            Assert.Equal(_saved, loaded.Settings);
            Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Loading took {elapsed.TotalMilliseconds:0.0} ms.");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
