using System.Diagnostics;
using TenLapse.Core.Import;
using TenLapse.Core.Projects;
using TenLapse.Media.Import;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Import;

/// <summary>
/// Cancel and New project are prompt (#12 review): a reader stuck in a slow open (a sleeping network drive) never holds
/// them up, and an import queued behind a cancelled one starts at once.
/// </summary>
public sealed class ImportCancelTests : IDisposable
{
    private static readonly TimeSpan _prompt = TimeSpan.FromSeconds(1);

    private readonly string _root = Directory.CreateTempSubdirectory("10lapse-cancel-").FullName;

    // Released when the test ends, so the stuck readers finish instead of waiting for the process to exit.
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _readerStuck = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        _release.TrySetResult();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ReadingEndsWithinASecondOfCancelEvenWithAReaderStuckInASlowOpen()
    {
        var folder = Folder("slow", 3);
        var importer = Importer(stuckUnder: folder);
        var frames = Directory.GetFiles(folder).Select(path => new Frame(path, 1, DateTime.UtcNow)).ToList();
        using var cancel = new CancellationTokenSource();

        var reading = Task.Run(async () =>
        {
            await foreach (var batch in importer.ReadAsync(frames, cancel.Token))
            {
                Assert.Fail("A stuck reader hands nothing out.");
            }
        }, TestContext.Current.CancellationToken);
        await _readerStuck.Task.WaitAsync(TestContext.Current.CancellationToken);
        var clock = Stopwatch.StartNew();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(clock.Elapsed < _prompt, $"Cancel took {clock.Elapsed.TotalMilliseconds:N0} ms.");
    }

    [Fact]
    public async Task CancelEndsTheImportWithinASecondClearsTheProgressAndTakesTheUnreadPhotosOut()
    {
        var folder = Folder("slow", 3);
        using var harness = ImportHarness.With(Importer(stuckUnder: folder));

        var import = harness.ImportAsync(folder);
        await _readerStuck.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, await harness.Ui.InvokeAsync(() => harness.Project.Frames.Length));
        var clock = Stopwatch.StartNew();
        await harness.Ui.InvokeAsync(() =>
        {
            harness.Import.CancelCommand.Execute(null);
            return true;
        });

        await import.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(clock.Elapsed < _prompt, $"Cancel took {clock.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.Empty(harness.Project.Frames);
        Assert.False(harness.Import.IsImporting);
        Assert.Equal(string.Empty, harness.Import.ProgressText);
        Assert.Equal(ProjectText.ImportStoppedNone, Assert.Single(harness.InfoBars.Shown).Message);
        Assert.Empty(harness.RealFailures());
    }

    [Fact]
    public async Task AnImportStartedAfterNewProjectDoesNotWaitForTheCancelledOne()
    {
        var slow = Folder("slow", 3);
        var quick = Folder("quick", 2);
        using var harness = ImportHarness.With(Importer(stuckUnder: slow));

        var first = harness.ImportAsync(slow);
        await _readerStuck.Task.WaitAsync(TestContext.Current.CancellationToken);
        await harness.Ui.RunAsync(() => harness.Shell.NewProjectCommand.ExecuteAsync(null));
        var clock = Stopwatch.StartNew();
        await harness.ImportAsync(quick).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await first.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(clock.Elapsed < _prompt, $"The second import took {clock.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.Equal(2, harness.Project.Frames.Length);
        Assert.Empty(harness.RealFailures());
        Assert.False(harness.Import.IsImporting);
    }

    // The real importer, whose reader never returns (and ignores the token) for the files under one folder.
    private PhotoImporter Importer(string stuckUnder) =>
        new(path => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            ReadOne = async (frame, token) =>
            {
                if (frame.SourcePath.StartsWith(stuckUnder, StringComparison.OrdinalIgnoreCase))
                {
                    _readerStuck.TrySetResult();
                    await _release.Task;
                }

                return FrameReading.Read(frame with { PixelSize = new PixelSize(4, 3) });
            },
        };

    private string Folder(string name, int photos)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        for (var i = 1; i <= photos; i++)
        {
            File.WriteAllBytes(Path.Combine(folder, $"IMG_{i}.jpg"), [1]);
        }

        return folder;
    }
}
