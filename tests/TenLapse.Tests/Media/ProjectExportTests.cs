using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Decoding;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using TenLapse.Presentation;
using TenLapse.Services;
using TenLapse.Tests.Fixtures;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>
/// The project export (#22) over the committed fixture photos: the snapshot's rate, size and fit; the partial file
/// that becomes the video only when it is complete; cancel within a second; every failure with its sentence.
/// </summary>
public sealed class ProjectExportTests : IDisposable
{
    private readonly PhotoFixtures _scratch = new();
    private readonly ManualResetEventSlim _release = new();

    public void Dispose()
    {
        // Lets an abandoned encoder call return, so its clean-up finishes.
        _release.Set();
        _scratch.Dispose();
    }

    private static IReadOnlyList<string> GoodPhotos { get; } =
        [.. PhotoFixtureCatalogue.All.Where(fixture => fixture.Decodes).Select(fixture => fixture.Path)];

    [Theory]
    [InlineData(FrameAspect.Ratio16x9, FitMode.Fit)]
    [InlineData(FrameAspect.Ratio16x9, FitMode.Fill)]
    [InlineData(FrameAspect.Ratio9x16, FitMode.Fit)]
    [InlineData(FrameAspect.Ratio9x16, FitMode.Fill)]
    public async Task AProjectExportsAtItsRateSizeAndFit(FrameAspect aspect, FitMode fit)
    {
        SkipWithoutH264Encoder();
        // Length mode: 16 photos in 2 s is 8 fps, a rate the snapshot computes, not one typed in.
        var snapshot = Snapshot(GoodPhotos, aspect, fit, TimingSettings.Length(new Fraction(2, 1)));
        var rate = snapshot.EffectiveRate.Rate;
        var size = OutputSize.For(OutputResolution.Hd720, aspect);
        var output = _scratch.PathOf($"{aspect}-{fit}.mp4");

        var result = await ExportOrSkipAsync(snapshot, output);

        Assert.Equal(new Fraction(GoodPhotos.Count, 2), rate);
        Assert.Equal((output, GoodPhotos.Count, size, rate), (result.Path, result.FrameCount, result.Size, result.Rate));
        Assert.Equal(FrameTiming.Duration(GoodPhotos.Count, rate), result.Duration);
        Assert.Equal(new FileInfo(output).Length, result.FileBytes);
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
        var info = VideoFileReader.Read(output);
        Assert.Equal((GoodPhotos.Count, size.Width, size.Height), (info.FrameCount, info.Width, info.Height));
        Assert.Equal(Enumerable.Range(0, GoodPhotos.Count).Select(i => FrameTiming.FrameStart(i, rate)), info.SampleTimes);
        Assert.Equal(FrameTiming.Duration(GoodPhotos.Count, rate), info.Duration);

        // Frame 0 is png-named.jpg, 96x64: red top left, blue top right.
        var first = FirstFrame(output);
        (int X, int Y)[] none = [];
        var (bars, red, blue) = (aspect, fit) switch
        {
            (FrameAspect.Ratio16x9, FitMode.Fit) => (Points((40, 360), (1240, 360)), Points((400, 180)), Points((880, 180))), // 1080 wide, 100 px bars
            (FrameAspect.Ratio16x9, FitMode.Fill) => (none, Points((40, 180)), Points((1240, 180))),
            (FrameAspect.Ratio9x16, FitMode.Fit) => (Points((360, 200), (360, 1080)), Points((180, 500)), Points((540, 500))), // 480 tall, 400 px bars
            _ => (none, Points((100, 300)), Points((620, 300))),
        };
        Assert.All(bars, point => Assert.True(first.Is(point, 0, 0, 0), $"{point} is {first.At(point)}, not a black bar"));
        Assert.All(red, point => Assert.True(first.Is(point, 240, 20, 20), $"{point} is {first.At(point)}, not the red quarter"));
        Assert.All(blue, point => Assert.True(first.Is(point, 30, 60, 220), $"{point} is {first.At(point)}, not the blue quarter"));
    }

    [Fact]
    public async Task ProgressGoesFromPreparingToFinishingAndNeverBack()
    {
        SkipWithoutH264Encoder();
        var reports = new List<ExportProgress>();
        var progress = new Synchronous(reports.Add);

        await ExportOrSkipAsync(Snapshot(GoodPhotos), _scratch.PathOf("progress.mp4"), progress);

        Assert.Equal(ExportStage.Preparing, reports[0].Stage);
        Assert.Equal(0, reports[0].Frame);
        Assert.Equal((ExportStage.Finishing, GoodPhotos.Count), (reports[^1].Stage, reports[^1].Frame));
        Assert.True(reports.Zip(reports.Skip(1)).All(pair => pair.First.Frame <= pair.Second.Frame && pair.First.Stage <= pair.Second.Stage));
        Assert.InRange(reports.Count, 3, GoodPhotos.Count + 3);
        Assert.All(reports, report => Assert.Equal(GoodPhotos.Count, report.FrameCount));
    }

    [Fact]
    public async Task AnExistingFileIsReplacedOnlyByAFinishedVideoAndNoPartialFileIsLeft()
    {
        SkipWithoutH264Encoder();
        var output = _scratch.PathOf("replace.mp4");
        await File.WriteAllTextAsync(output, "the previous video", TestContext.Current.CancellationToken);
        var snapshot = Snapshot(GoodPhotos);

        // A failure: the old file stays, the partial goes.
        await Assert.ThrowsAsync<VideoExportException>(() => ExportAsync(
            snapshot,
            output,
            options: new ProjectExportOptions { CreateEncoder = (path, _, _) => new ExportFailureTests.FakeEncoder(path) { FailAt = 3, Failure = new COMException("Broken.", unchecked((int)0x80004005)) } }));
        await ExportCleanup.WhenDone(output);
        Assert.Equal("the previous video", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
        Assert.True(ProjectExporter.PartialFilesOf(output).Count == 0, "the partial file is left after a failure");

        // A cancel: the same.
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            var cancelAt = new Synchronous(report => { if (report.Frame >= 3) cancellation.Cancel(); });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectExporter.ExportAsync(Snapshot(ManyPhotos(60)), output, cancelAt, cancellation.Token));
        }

        await ExportCleanup.WhenDone(output);
        Assert.Equal("the previous video", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
        Assert.True(ProjectExporter.PartialFilesOf(output).Count == 0, "the partial file is left after a cancel");

        // Success: replaced.
        var result = await ExportOrSkipAsync(snapshot, output);
        Assert.Equal(GoodPhotos.Count, VideoFileReader.Read(output).FrameCount);
        Assert.Equal(result.FileBytes, new FileInfo(output).Length);
        Assert.True(ProjectExporter.PartialFilesOf(output).Count == 0, "the partial file is left after success");
    }

    [Fact]
    public async Task APartialFileLeftByACrashDoesNotStopTheNextExport()
    {
        SkipWithoutH264Encoder();
        var output = _scratch.PathOf("stale.mp4");
        var stale = output + ".0badf00d" + ProjectExporter.PartialSuffix;
        await File.WriteAllTextAsync(stale, "left by a crash", TestContext.Current.CancellationToken);

        await ExportOrSkipAsync(Snapshot(GoodPhotos), output);

        // Every export has its own partial file; the one a crash left is the journal's to delete (PartialFileJournalTests).
        Assert.Equal(GoodPhotos.Count, VideoFileReader.Read(output).FrameCount);
        Assert.Equal([stale], ProjectExporter.PartialFilesOf(output));
    }

    [Fact]
    public async Task ThePartialFileIsHiddenWhileItIsWrittenAndTheVideoIsNot()
    {
        SkipWithoutGraphicsDevice();
        var output = _scratch.PathOf("hidden.mp4");
        bool? hiddenWhileWriting = null;

        await ExportAsync(
            Snapshot(GoodPhotos),
            output,
            options: new ProjectExportOptions
            {
                CreateEncoder = (path, _, _) => new ExportFailureTests.FakeEncoder(path)
                {
                    OnWrite = () => hiddenWhileWriting ??= File.GetAttributes(path).HasFlag(FileAttributes.Hidden),
                },
            });

        Assert.True(hiddenWhileWriting);
        Assert.False(File.GetAttributes(output).HasFlag(FileAttributes.Hidden));
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
    }

    [Fact]
    public async Task AnExportRightAfterAStalledOneOfTheSameFileGoesAhead()
    {
        // The stalled export's encoder still holds its partial file; the next one writes its own and does not wait.
        SkipWithoutGraphicsDevice();
        var output = _scratch.PathOf("again.mp4");
        await Assert.ThrowsAsync<VideoExportException>(() => ExportAsync(
            Snapshot(GoodPhotos),
            output,
            options: new ProjectExportOptions
            {
                CreateEncoder = (path, _, _) => new StallingEncoder(path, 2, _release),
                Tune = settings => settings with { StallTimeout = TimeSpan.FromSeconds(1) },
            }));
        Assert.Single(ProjectExporter.PartialFilesOf(output));

        var clock = Stopwatch.StartNew();
        var result = await ExportAsync(
            Snapshot(GoodPhotos),
            output,
            options: new ProjectExportOptions { CreateEncoder = (path, _, _) => new ExportFailureTests.FakeEncoder(path) });

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"The second export took {clock.Elapsed.TotalSeconds:0.0} s.");
        Assert.Equal(output, result.Path);
        Assert.True(File.Exists(output));
        Assert.Single(ProjectExporter.PartialFilesOf(output));

        _release.Set();
        await ExportCleanup.WhenDone(output).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
        Assert.True(File.Exists(output), "the stalled export's clean-up deleted the newer video");
    }

    [Fact]
    public async Task ATargetOpenInAnotherAppFailsBeforeAnythingIsWritten()
    {
        SkipWithoutGraphicsDevice();
        var output = _scratch.PathOf("open.mp4");
        await File.WriteAllTextAsync(output, "playing", TestContext.Current.CancellationToken);
        var clock = Stopwatch.StartNew();
        VideoExportException exception;
        using (new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            exception = await Assert.ThrowsAsync<VideoExportException>(() => ExportAsync(Snapshot(GoodPhotos), output));
        }

        Assert.Equal(VideoExportFailure.OutputFileInUse, exception.Failure);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"it took {clock.Elapsed.TotalSeconds:0.0} s to say so");
        Assert.Equal("playing", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
    }

    [Fact]
    public async Task AMissingFolderFailsAtOnce()
    {
        var output = Path.Combine(_scratch.Directory, "gone", "out.mp4");

        var exception = await Assert.ThrowsAsync<VideoExportException>(() => ExportAsync(Snapshot(GoodPhotos), output));

        Assert.Equal(VideoExportFailure.OutputFolderMissing, exception.Failure);
        Assert.Equal(ProjectText.ExportFailedFolderMissing, new VideoExportErrorMapper().Map(exception)!.Text);
    }

    [Theory]
    [InlineData(0x80004005u, VideoExportFailure.EncoderFailed)] // E_FAIL
    [InlineData(0x80070070u, VideoExportFailure.DiskFull)] // ERROR_DISK_FULL
    [InlineData(0x887A0005u, VideoExportFailure.GraphicsDeviceLost)] // DXGI_ERROR_DEVICE_REMOVED
    public async Task AForcedEncoderFailureShowsItsSentenceAndItsFrame(uint hresult, VideoExportFailure expected)
    {
        SkipWithoutGraphicsDevice();
        var output = _scratch.PathOf($"forced-{hresult:X8}.mp4");
        var failure = Marshal.GetExceptionForHR(unchecked((int)hresult))!;

        var exception = await Assert.ThrowsAsync<VideoExportException>(() => ExportAsync(
            Snapshot(GoodPhotos),
            output,
            options: new ProjectExportOptions { CreateEncoder = (path, _, _) => new ExportFailureTests.FakeEncoder(path) { FailAt = 3, Failure = failure } }));
        await ExportCleanup.WhenDone(output);

        Assert.Equal(expected, exception.Failure);
        Assert.Equal((3, GoodPhotos[3]), (exception.FrameIndex, exception.SourcePath));
        Assert.Same(failure, exception.InnerException);
        var message = new VideoExportErrorMapper().Map(exception)!;
        Assert.Equal(VideoExportErrorMapper.For(expected), message);
        Assert.False(File.Exists(output));
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
    }

    [Fact]
    public async Task ABrokenPhotoFailsWithItsFrameNumberFileNameAndReason()
    {
        SkipWithoutH264Encoder();
        var broken = PhotoFixtureCatalogue.Get("truncated.jpg");
        var output = _scratch.PathOf("broken.mp4");

        var exception = await Assert.ThrowsAsync<VideoExportException>(() => ExportAsync(Snapshot([GoodPhotos[0], GoodPhotos[1], broken.Path, GoodPhotos[2]]), output));
        await ExportCleanup.WhenDone(output);

        Assert.Equal(VideoExportFailure.FrameUnreadable, exception.Failure);
        Assert.Equal((2, broken.Path), (exception.FrameIndex, exception.SourcePath));
        var text = new VideoExportErrorMapper().Map(exception)!.Text;
        Assert.Equal(ProjectText.ExportFailedPhoto(2, broken.Path, SkipReason.Truncated), text);
        Assert.StartsWith("Photo 3, truncated.jpg, can't be read (Damaged file: it ends early).", text, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-1)] // in Finish
    public async Task AStalledEncoderFailsTheExportInsteadOfHangingIt(int stallAt)
    {
        SkipWithoutGraphicsDevice();
        var output = _scratch.PathOf($"stalled{stallAt}.mp4");
        var clock = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<VideoExportException>(() => ExportAsync(
            Snapshot(GoodPhotos),
            output,
            options: new ProjectExportOptions
            {
                CreateEncoder = (path, _, _) => new StallingEncoder(path, stallAt, _release),
                Tune = settings => settings with { StallTimeout = TimeSpan.FromSeconds(1) },
            }));

        Assert.Equal(VideoExportFailure.EncoderStalled, exception.Failure);
        Assert.Equal(stallAt >= 0 ? stallAt : null, exception.FrameIndex);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        Assert.Equal(ProjectText.ExportFailedEncoderStalled, new VideoExportErrorMapper().Map(exception)!.Text);
        Assert.False(File.Exists(output));

        // The stuck call returns at last: the clean-up then releases everything and removes the partial file.
        _release.Set();
        await ExportCleanup.WhenDone(output).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
    }

    [Fact]
    public async Task CancellingAProjectExportReturnsWithinASecondEveryTime()
    {
        SkipWithoutH264Encoder();
        var snapshot = Snapshot(ManyPhotos(60));
        var slowest = TimeSpan.Zero;
        for (var round = 0; round < 10; round++)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var cancelledAt = 0L;
            var at = 1 + (round % 5 * 4);
            var progress = new Synchronous(report =>
            {
                if (report.Frame >= at && Volatile.Read(ref cancelledAt) == 0)
                {
                    Volatile.Write(ref cancelledAt, Stopwatch.GetTimestamp());
                    cancellation.Cancel();
                }
            });
            var output = _scratch.PathOf($"cancel{round}.mp4");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectExporter.ExportAsync(snapshot, output, progress, cancellation.Token));
            var took = Stopwatch.GetElapsedTime(Volatile.Read(ref cancelledAt));
            slowest = took > slowest ? took : slowest;

            Assert.True(took < TimeSpan.FromSeconds(1), $"Round {round}: the cancel took {took.TotalMilliseconds:0} ms.");
            await ExportCleanup.WhenDone(output).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.False(File.Exists(output));
            Assert.Empty(ProjectExporter.PartialFilesOf(output));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"slowest cancel of 10: {slowest.TotalMilliseconds:0} ms");
    }

    [Fact]
    public void AProjectWithoutFramesIsRefused() =>
        Assert.Throws<ArgumentException>(() => { _ = ProjectExporter.ExportAsync(new Project().Snapshot, _scratch.PathOf("empty.mp4"), cancellationToken: TestContext.Current.CancellationToken); });

    [Fact]
    public void MatchPhotosTakesTheMostCommonPhotoSize()
    {
        var project = new Project(TimingSettings.Default, new FrameSettings(FrameAspect.MatchPhotos, FitMode.Fill), OutputSettings.Default, SortMode.Manual);
        project.AddFrames(
        [
            new Frame(@"C:\p\a.jpg", 1, DateTime.UtcNow) { PixelSize = new PixelSize(4000, 3000) },
            new Frame(@"C:\p\b.jpg", 1, DateTime.UtcNow) { PixelSize = new PixelSize(3000, 4000) },
            new Frame(@"C:\p\c.jpg", 1, DateTime.UtcNow) { PixelSize = new PixelSize(3000, 4000) },
            new Frame(@"C:\p\d.jpg", 1, DateTime.UtcNow),
        ]);

        Assert.Equal(new PixelSize(1080, 1440), project.Snapshot.VideoSize);
        Assert.Equal(new PixelSize(1920, 1080), new Project(TimingSettings.Default, new FrameSettings(FrameAspect.MatchPhotos, FitMode.Fill), OutputSettings.Default, SortMode.Manual).Snapshot.VideoSize);
    }

    private static ProjectSnapshot Snapshot(
        IEnumerable<string> paths,
        FrameAspect aspect = FrameAspect.Ratio16x9,
        FitMode fit = FitMode.Fill,
        TimingSettings? timing = null)
    {
        var project = new Project(
            timing ?? TimingSettings.PerSecond(new Fraction(30, 1)),
            new FrameSettings(aspect, fit),
            new OutputSettings(OutputResolution.Hd720, VideoFormat.Mp4H264, OutputQuality.Standard),
            SortMode.Manual);
        project.AddFrames(paths.Select(path => new Frame(path, new FileInfo(path).Exists ? new FileInfo(path).Length : 0, DateTime.UtcNow)));
        return project.Snapshot;
    }

    // Distinct copies of one fixture: a project holds each file once.
    private IEnumerable<string> ManyPhotos(int count)
    {
        var source = PhotoFixtureCatalogue.Get("orientation-1.jpg").Path;
        for (var i = 0; i < count; i++)
        {
            var path = _scratch.PathOf($"many-{i:D4}.jpg");
            if (!File.Exists(path))
            {
                File.Copy(source, path);
            }

            yield return path;
        }
    }

    private static Task<ProjectExportResult> ExportAsync(ProjectSnapshot snapshot, string output, IProgress<ExportProgress>? progress = null, ProjectExportOptions? options = null) =>
        ProjectExporter.ExportAsync(snapshot, output, progress, TestContext.Current.CancellationToken, options ?? new ProjectExportOptions());

    private static async Task<ProjectExportResult> ExportOrSkipAsync(ProjectSnapshot snapshot, string output, IProgress<ExportProgress>? progress = null)
    {
        try
        {
            return await ExportAsync(snapshot, output, progress);
        }
        catch (VideoExportException exception) when (exception.Failure is VideoExportFailure.EncoderUnavailable or VideoExportFailure.NoGraphicsDevice)
        {
            Assert.Skip($"The export cannot run here ({exception.Message}).");
            throw;
        }
    }

    private static (int X, int Y)[] Points(params (int X, int Y)[] points) => points;

    private static DecodedFrame FirstFrame(string path)
    {
        DecodedFrame? first = null;
        VideoFileReader.ReadFrames(path, frame =>
        {
            if (frame.Index == 0)
            {
                first = new DecodedFrame(frame.Pixels.ToArray(), frame.Width, frame.Stride);
            }
        });
        return first!;
    }

    private sealed record DecodedFrame(byte[] Pixels, int Width, int Stride)
    {
        public (byte R, byte G, byte B) At((int X, int Y) point)
        {
            var i = (point.Y * Stride) + (point.X * 4);
            return (Pixels[i + 2], Pixels[i + 1], Pixels[i]);
        }

        // Flat colours through H.264 at a low bitrate and studio range: within 40 levels.
        public bool Is((int X, int Y) point, byte r, byte g, byte b)
        {
            var (pr, pg, pb) = At(point);
            return Math.Abs(pr - r) <= 40 && Math.Abs(pg - g) <= 40 && Math.Abs(pb - b) <= 40;
        }
    }

    private sealed class Synchronous(Action<ExportProgress> report) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => report(value);
    }

    /// <summary>Blocks in <see cref="Write"/> at frame <c>stallAt</c> (or in <see cref="Finish"/> for -1) until released, as a hung driver does.</summary>
    private sealed class StallingEncoder : IVideoEncoder
    {
        private readonly int _stallAt;
        private readonly ManualResetEventSlim _release;
        private int _written;

        public StallingEncoder(string path, int stallAt, ManualResetEventSlim release)
        {
            File.WriteAllBytes(path, [0, 0, 0, 0]);
            (_stallAt, _release) = (stallAt, release);
        }

        public VideoEncoderInfo? Encoder => new("Stalling encoder", IsHardware: false);

        public void Write(CanvasRenderTarget target, long time, long duration)
        {
            if (_written++ == _stallAt)
            {
                _release.Wait();
            }
        }

        public void Finish()
        {
            if (_stallAt < 0)
            {
                _release.Wait();
            }
        }

        public void Dispose()
        {
        }
    }
}
