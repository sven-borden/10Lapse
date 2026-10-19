using TenLapse.Media.Export;
using TenLapse.Presentation.Export;
using TenLapse.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// The App's adapter over Media's exporter (#24, compiled in from the App): Media's progress stages in Presentation's
/// words, and whether a cancelled export's unfinished file is gone, against real files.
/// </summary>
public sealed class ProjectVideoExporterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", "VideoExporter", Guid.NewGuid().ToString("N"));

    public ProjectVideoExporterTests() => Directory.CreateDirectory(_folder);

    [Theory]
    [InlineData(ExportStage.Preparing, ExportPhase.Preparing)]
    [InlineData(ExportStage.Encoding, ExportPhase.Encoding)]
    [InlineData(ExportStage.Finishing, ExportPhase.Finishing)]
    public void ProgressKeepsItsStageAndNumbers(ExportStage stage, ExportPhase phase)
    {
        var seen = new List<ExportStatus>();
        var relay = new ProjectVideoExporter.Relay(new Recorder(seen));

        relay.Report(new ExportProgress(stage, 12, 120, 4.3, TimeSpan.FromSeconds(25)));

        Assert.Equal(new ExportStatus(phase, 12, 120, 4.3, TimeSpan.FromSeconds(25)), Assert.Single(seen));
    }

    [Fact]
    public async Task ACleanUpWithNoUnfinishedFileLeftSaysItIsGone()
    {
        var target = Path.Combine(_folder, "Harbor sunset.mp4");

        Assert.True(await new ProjectVideoExporter().CleanUpAsync(target));
    }

    [Fact]
    public async Task AnUnfinishedFileStillThereIsReported()
    {
        var target = Path.Combine(_folder, "Harbor sunset.mp4");
        await File.WriteAllBytesAsync(target + ".0a1b2c3d" + ProjectExporter.PartialSuffix, [1, 2, 3], TestContext.Current.CancellationToken);

        Assert.False(await new ProjectVideoExporter().CleanUpAsync(target));
    }

    [Fact]
    public async Task ANewExportsUnfinishedFileDoesNotCountAsTheCancelledOnes()
    {
        var target = Path.Combine(_folder, "Harbor sunset.mp4");
        var mine = target + ".0a1b2c3d" + ProjectExporter.PartialSuffix;
        await File.WriteAllBytesAsync(mine, [1, 2, 3], TestContext.Current.CancellationToken);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ExportCleanup.Track(target, cleanup.Task);

        var asked = new ProjectVideoExporter().CleanUpAsync(target);

        // The cancelled export's file goes; a re-export of the same file has meanwhile started its own.
        File.Delete(mine);
        await File.WriteAllBytesAsync(target + ".99887766" + ProjectExporter.PartialSuffix, [4], TestContext.Current.CancellationToken);
        cleanup.SetResult();

        Assert.True(await asked.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own clean-up.
        }
    }

    private sealed class Recorder(List<ExportStatus> seen) : IProgress<ExportStatus>
    {
        public void Report(ExportStatus value) => seen.Add(value);
    }
}
