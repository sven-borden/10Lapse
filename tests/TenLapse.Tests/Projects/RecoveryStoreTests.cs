using TenLapse.Core.Projects;

namespace TenLapse.Tests.Projects;

/// <summary>
/// The recovery folder (#52), on the real disk under this branch's temp folder: each running app has its own slot, a slot
/// whose owner is gone is offered once to the next launch, and a clean close leaves nothing behind.
/// </summary>
public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "claude", "feat-52-save-projects", "recovery-tests", Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void ACleanCloseLeavesNothingToOffer()
    {
        using (var first = new RecoveryStore(_folder))
        {
            first.Save("one");
        }

        using var next = new RecoveryStore(_folder);
        Assert.Null(next.TakeOrphan());
        Assert.Empty(Directory.GetFiles(_folder, "*.10lapse"));
    }

    [Fact]
    public void ACopyOfAnAppThatIsStillRunningIsNotTaken()
    {
        using var running = new RecoveryStore(_folder);
        running.Save("running");

        using var second = new RecoveryStore(_folder);
        Assert.Null(second.TakeOrphan());
    }

    [Fact]
    public void ACopyLeftByAnAppThatEndedIsOfferedOnceAndKeptUntilTheNextCleanClose()
    {
        // An app that ended without closing: its copy stays and its lock file is gone (Windows deletes it with the process).
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "deadbeef.10lapse"), "left behind");

        using (var next = new RecoveryStore(_folder))
        {
            Assert.Equal("left behind", next.TakeOrphan()?.Text);

            // A third launch while this one runs does not get it again.
            using var third = new RecoveryStore(_folder);
            Assert.Null(third.TakeOrphan());
        }

        using var after = new RecoveryStore(_folder);
        Assert.Null(after.TakeOrphan());
    }

    [Fact]
    public void AnOfferNotTakenUpIsOfferedAgainIfThisRunEndsWithoutClosingToo()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "deadbeef.10lapse"), "left behind");
        var crashed = new RecoveryStore(_folder);
        Assert.NotNull(crashed.TakeOrphan());

        // This run crashes too: its lock goes, the copy it was offering stays.
        ReleaseLockWithoutCleaning(crashed);

        using var next = new RecoveryStore(_folder);
        Assert.Equal("left behind", next.TakeOrphan()?.Text);
    }

    [Fact]
    public void ARestoredOfferGoesOnceTheRestoredProjectIsWritten()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "deadbeef.10lapse"), "left behind");
        using var store = new RecoveryStore(_folder);
        Assert.NotNull(store.TakeOrphan());

        store.MarkOfferRestored();
        store.Save("restored and edited");

        Assert.Equal(["restored and edited"], Directory.GetFiles(_folder, "*.10lapse").Select(File.ReadAllText));
    }

    [Fact]
    public void ClearRemovesOnlyThisAppsCopy()
    {
        using var running = new RecoveryStore(_folder);
        running.Save("other");
        using var store = new RecoveryStore(_folder);
        store.Save("mine");

        store.Clear();

        Assert.Equal(["other"], Directory.GetFiles(_folder, "*.10lapse").Select(File.ReadAllText));
    }

    // The process "dies": its lock handle closes (deleting the lock file) but Dispose's clean-up never runs.
    private static void ReleaseLockWithoutCleaning(RecoveryStore store)
    {
        var field = typeof(RecoveryStore).GetField("_lock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((FileStream?)field.GetValue(store))?.Dispose();
        field.SetValue(store, null);
        GC.SuppressFinalize(store);
    }
}
