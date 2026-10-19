using System.Diagnostics;
using System.Runtime.InteropServices;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using TenLapse.Tests.Fixtures;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>The journal of unfinished export files: what a killed or hurried process leaves, the next launch deletes.</summary>
public sealed class PartialFileJournalTests : IDisposable
{
    private readonly PhotoFixtures _scratch = new();
    private readonly ManualResetEventSlim _release = new();

    public void Dispose()
    {
        _release.Set();
        _scratch.Dispose();
    }

    private static PartialFileJournal.Owner ThisProcess
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return new PartialFileJournal.Owner(process.Id, process.StartTime.ToUniversalTime().Ticks);
        }
    }

    [Fact]
    public void LeftoversOfAProcessThatIsGoneAreDeletedAndThoseOfARunningOneAreKept()
    {
        var path = _scratch.PathOf("exports.journal");
        var gone = _scratch.PathOf("killed.mp4.0000aaaa.partial");
        var running = _scratch.PathOf("running.mp4.0000bbbb.partial");
        var missing = _scratch.PathOf("missing.mp4.0000cccc.partial");
        File.WriteAllText(gone, "left by a crash");
        File.WriteAllText(running, "being written");

        // The same process id with another start time is an earlier process that had the id.
        new PartialFileJournal(path, ThisProcess with { StartedTicks = 1 }).Add(gone);
        new PartialFileJournal(path, ThisProcess with { StartedTicks = 1 }).Add(missing);
        new PartialFileJournal(path).Add(running);

        var deleted = new PartialFileJournal(path).DeleteLeftovers();

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(gone));
        Assert.True(File.Exists(running));
        Assert.Equal([running], new PartialFileJournal(path).Files);

        new PartialFileJournal(path).Remove(running);
        Assert.False(File.Exists(path), "an empty journal stays on disk");
    }

    [Fact]
    public void OnlyPartialFilesWithAFullPathAreEverDeleted()
    {
        var path = _scratch.PathOf("exports.journal");
        var video = _scratch.PathOf("finished.mp4");
        File.WriteAllText(video, "a video");
        File.WriteAllText(path, $"1\t1\t{video}{Environment.NewLine}1\t1\trelative.mp4.partial{Environment.NewLine}garbage{Environment.NewLine}");

        Assert.Equal(0, new PartialFileJournal(path).DeleteLeftovers());

        Assert.True(File.Exists(video));
        Assert.Empty(new PartialFileJournal(path).Files);
    }

    [Fact]
    public void AnUnreadableJournalIsReportedNotThrown()
    {
        var path = _scratch.PathOf("locked.journal");
        File.WriteAllText(path, "1\t1\tC:\\x.mp4.partial");
        var reported = new List<ExportCleanupFailure>();
        void OnFailed(object? sender, ExportCleanupFailure failure)
        {
            if (failure.Path == path)
            {
                lock (reported)
                {
                    reported.Add(failure);
                }
            }
        }

        ExportCleanup.Failed += OnFailed;
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(0, new PartialFileJournal(path).DeleteLeftovers());
            }
        }
        finally
        {
            ExportCleanup.Failed -= OnFailed;
        }

        Assert.NotEmpty(reported);
    }

    [Fact]
    public async Task AProjectExportListsItsPartialFileOnlyWhileItExists()
    {
        SkipWithoutGraphicsDevice();
        var journal = new PartialFileJournal(_scratch.PathOf("exports.journal"));
        var output = _scratch.PathOf("listed.mp4");
        IReadOnlyList<string>? listedWhileWriting = null;

        // Success: listed while it is written, forgotten once it is the video.
        await Export(output, new ProjectExportOptions
        {
            Journal = journal,
            CreateEncoder = (path, _, _) => new ExportFailureTests.FakeEncoder(path) { OnWrite = () => listedWhileWriting ??= journal.Files },
        });
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
        Assert.Single(listedWhileWriting!);
        Assert.Empty(journal.Files);

        // A failure: forgotten once the clean-up has deleted it.
        await Assert.ThrowsAsync<VideoExportException>(() => Export(output, new ProjectExportOptions
        {
            Journal = journal,
            CreateEncoder = (path, _, _) => new ExportFailureTests.FakeEncoder(path) { FailAt = 2, Failure = new COMException("Broken.", unchecked((int)0x80004005)) },
        }));
        await ExportCleanup.WhenDone(output);
        Assert.Empty(journal.Files);

        // A stall: the encoder still holds the file, so it stays listed until the clean-up deletes it.
        await Assert.ThrowsAsync<VideoExportException>(() => Export(output, new ProjectExportOptions
        {
            Journal = journal,
            CreateEncoder = (path, _, _) => new ExportFailureTests.FakeEncoder(path) { OnWrite = () => _release.Wait() },
            Tune = settings => settings with { StallTimeout = TimeSpan.FromMilliseconds(500) },
        }));
        Assert.Equal(ProjectExporter.PartialFilesOf(output), journal.Files);
        Assert.False(ExportCleanup.WhenAllDone().IsCompleted, "WhenAllDone does not wait for the stalled export's clean-up");

        _release.Set();
        await ExportCleanup.WhenAllDone().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Empty(journal.Files);
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
    }

    private Task<ProjectExportResult> Export(string output, ProjectExportOptions options)
    {
        var project = new Project(
            TimingSettings.PerSecond(new Fraction(30, 1)),
            new FrameSettings(FrameAspect.Ratio16x9, FitMode.Fill),
            new OutputSettings(OutputResolution.Hd720, VideoFormat.Mp4H264, OutputQuality.Standard),
            SortMode.Manual);
        var photo = PhotoFixtureCatalogue.Get("orientation-1.jpg").Path;
        project.AddFrames(Enumerable.Range(0, 4).Select(i =>
        {
            var copy = _scratch.PathOf($"frame-{i}.jpg");
            if (!File.Exists(copy))
            {
                File.Copy(photo, copy);
            }

            return new Frame(copy, new FileInfo(copy).Length, DateTime.UtcNow);
        }));
        return ProjectExporter.ExportAsync(project.Snapshot, output, null, TestContext.Current.CancellationToken, options);
    }
}
