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
using TenLapse.Tests.Golden;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>
/// The project export (#22) over the committed fixture photos: the snapshot's rate, size and fit; the partial file
/// that becomes the video only when it is complete; cancel within a second; every failure with its sentence.
/// </summary>
[Collection(nameof(RealEncoderCollection))]
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
    public async Task ATargetHeldForAMomentIsWaitedOutAtTheStartAndAtTheEnd()
    {
        SkipWithoutGraphicsDevice();
        var output = _scratch.PathOf("moment.mp4");
        await File.WriteAllTextAsync(output, "the previous video", TestContext.Current.CancellationToken);

        // Explorer making a thumbnail: held when the export starts, and again when it moves the video in.
        var atStart = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read);
        _ = Task.Delay(300, TestContext.Current.CancellationToken).ContinueWith(_ => atStart.Dispose(), TaskScheduler.Default);
        var result = await ExportAsync(
            Snapshot(GoodPhotos),
            output,
            options: new ProjectExportOptions
            {
                CreateEncoder = (path, _, _) => new ExportFailureTests.FakeEncoder(path)
                {
                    OnFinish = () =>
                    {
                        var atEnd = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        _ = Task.Delay(500).ContinueWith(_ => atEnd.Dispose(), TaskScheduler.Default);
                    },
                },
            });

        Assert.Equal((output, null), (result.Path, result.TargetInUse));
        Assert.Equal(4, new FileInfo(output).Length); // the fake encoder's file
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
    }

    [Fact]
    public async Task ATargetHeldThroughTheFinalMoveGetsTheVideoBesideIt()
    {
        // Renaming over a file another app opened after the early check fails with access denied, however it
        // shares it. The finished video must not be lost to that.
        SkipWithoutGraphicsDevice();
        var output = _scratch.PathOf("held.mp4");
        var taken = _scratch.PathOf("held (2).mp4");
        await File.WriteAllTextAsync(output, "the previous video", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(taken, "another video", TestContext.Current.CancellationToken);
        FileStream? holder = null;
        try
        {
            var result = await ExportAsync(
                Snapshot(GoodPhotos),
                output,
                options: new ProjectExportOptions
                {
                    CreateEncoder = (path, _, _) => new ExportFailureTests.FakeEncoder(path)
                    {
                        OnFinish = () => holder = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
                    },
                    ReplacePatience = TimeSpan.FromMilliseconds(300),
                });

            Assert.Equal((_scratch.PathOf("held (3).mp4"), output), (result.Path, result.TargetInUse));
            Assert.Equal(4, new FileInfo(result.Path).Length);
            Assert.False(File.GetAttributes(result.Path).HasFlag(FileAttributes.Hidden));
            Assert.Equal("another video", await File.ReadAllTextAsync(taken, TestContext.Current.CancellationToken));
            Assert.Empty(ProjectExporter.PartialFilesOf(output));
            Assert.Equal("held.mp4 is open in another app, so the video was saved as held (3).mp4.", ProjectText.ExportSavedBeside(output, result.Path));
        }
        finally
        {
            holder?.Dispose();
        }

        Assert.Equal("the previous video", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
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
        // Well under the 1 s stall timeout, which a timer can end a tick early; well over what a busy machine needs.
        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(15));
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
    public async Task AGifCountsItsProgressInPhotosAndWritesTheFramesItShows()
    {
        // 30 photos at 30 fps as a GIF of up to 10 fps: 10 GIF frames, photos 0, 3, 6 and so on; progress in photos.
        var photos = ManyPhotos(30).ToList();
        var snapshot = GoldenExport.Snapshot(photos.Select(path => GoldenExport.FrameOf(path)), TimingSettings.PerSecond(new Fraction(30, 1)), FrameAspect.Ratio16x9, FitMode.Fill, VideoFormat.Gif);
        var settings = ProjectExporter.SettingsFor(snapshot);
        Assert.Equal((480, 270, new Fraction(10, 1), VideoFormat.Gif, (uint?)null), (settings.Width, settings.Height, settings.FrameRate, settings.Format, settings.Bitrate));
        Assert.Equal(Enumerable.Range(0, 10).Select(i => i * 3), ProjectExporter.FramesFor(snapshot, settings));

        var reports = new List<ExportProgress>();
        var output = _scratch.PathOf("progress.gif");
        var result = await ExportOrSkipAsync(snapshot, output, new Synchronous(reports.Add));

        Assert.Equal((10, new Fraction(10, 1), VideoFormat.Gif, output), (result.FrameCount, result.Rate, result.Format, result.Path));
        Assert.Equal(FrameTiming.Duration(10, new Fraction(10, 1)), result.Duration);
        Assert.Equal(10, GifFileReader.Read(output).FrameCount);
        Assert.Equal((ExportStage.Finishing, 30), (reports[^1].Stage, reports[^1].Frame));
        Assert.All(reports, report => Assert.Equal(30, report.FrameCount));
        Assert.True(reports.Zip(reports.Skip(1)).All(pair => pair.First.Frame <= pair.Second.Frame));
        Assert.All(reports.Where(report => report.Stage == ExportStage.Encoding), report => Assert.Equal(0, report.Frame % 3));
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
    }

    [Fact]
    public void TheExportedFramesFollowTheSequenceForAVideoAndAGif()
    {
        // #49: 10 photos at 30 fps as a boomerang played twice, 0.2 s held first and 0.1 s last (photo 0,
        // home again): 6 + 36 + 3 frames.
        var photos = ManyPhotos(10).Select(path => GoldenExport.FrameOf(path)).ToList();
        var sequence = new SequenceSettings(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.1), 2, boomerang: true);
        var video = GoldenExport.Snapshot(photos, TimingSettings.PerSecond(new Fraction(30, 1)), FrameAspect.Ratio16x9, FitMode.Fill, sequence: sequence);

        var frames = ProjectExporter.FramesFor(video, ProjectExporter.SettingsFor(video));
        Assert.Equal(45, frames.Length);
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 1, 2], frames[..9]);
        Assert.Equal([3, 2, 1, 0, 0, 0], frames[^6..]);
        Assert.Equal(Enumerable.Range(0, 45).Select(video.Playback.PhotoAt), frames);

        // A GIF plays the photos once (it loops by itself) and shows, at each of its 10 fps frames, the photo the video shows.
        var gif = GoldenExport.Snapshot(photos, TimingSettings.PerSecond(new Fraction(30, 1)), FrameAspect.Ratio16x9, FitMode.Fill, VideoFormat.Gif, sequence: sequence);
        var gifSettings = ProjectExporter.SettingsFor(gif);
        Assert.Equal(6 + 18 + 3, gif.Playback.Length);
        Assert.Equal(GifOutput.SourceFrames(27, new Fraction(30, 1), new Fraction(10, 1)).Select(gif.Playback.PhotoAt), ProjectExporter.FramesFor(gif, gifSettings));
        Assert.Equal([0, 0, 0, 3, 6, 9, 6, 3, 0], ProjectExporter.FramesFor(gif, gifSettings));
    }

    [Fact]
    public async Task ASequenceExportCountsItsProgressInVideoFramesAndLastsAsLong()
    {
        // 12 photos at 30 fps as a boomerang with 0.5 s held at both ends: 15 + 22 + 15 = 52 frames, 1.73 s.
        var photos = ManyPhotos(12).Select(path => GoldenExport.FrameOf(path)).ToList();
        var sequence = new SequenceSettings(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5), 1, boomerang: true);
        var snapshot = GoldenExport.Snapshot(photos, TimingSettings.PerSecond(new Fraction(30, 1)), FrameAspect.Ratio16x9, FitMode.Fill, sequence: sequence);
        var reports = new List<ExportProgress>();
        var output = _scratch.PathOf("sequence.mp4");

        var result = await ExportOrSkipAsync(snapshot, output, new Synchronous(reports.Add));

        Assert.Equal(52, result.FrameCount);
        Assert.Equal(FrameTiming.Duration(52, new Fraction(30, 1)), result.Duration);
        Assert.Equal(snapshot.Duration, result.Duration);
        Assert.Equal(52, VideoFileReader.Read(output).FrameCount);
        Assert.All(reports, report => Assert.Equal(52, report.FrameCount));
        Assert.Equal((ExportStage.Finishing, 52), (reports[^1].Stage, reports[^1].Frame));
        Assert.True(reports.Zip(reports.Skip(1)).All(pair => pair.First.Frame <= pair.Second.Frame));
    }

    [Fact]
    public async Task ABrokenPhotoInAGifIsNamedByItsPhotoNumberNotItsGifFrame()
    {
        // At 30 fps as a 10 fps GIF the GIF's frame 2 shows photo 6: the message must name photo 7 (frame 6).
        var photos = ManyPhotos(12).ToList();
        var broken = PhotoFixtureCatalogue.Get("truncated.jpg");
        photos[6] = broken.Path;
        var snapshot = GoldenExport.Snapshot(photos.Select(path => GoldenExport.FrameOf(path)), TimingSettings.PerSecond(new Fraction(30, 1)), FrameAspect.Ratio16x9, FitMode.Fill, VideoFormat.Gif);
        var output = _scratch.PathOf("broken.gif");

        var exception = await Assert.ThrowsAsync<VideoExportException>(() => ExportAsync(snapshot, output));
        await ExportCleanup.WhenDone(output);

        Assert.Equal(VideoExportFailure.FrameUnreadable, exception.Failure);
        Assert.Equal((6, broken.Path), (exception.FrameIndex, exception.SourcePath));
        Assert.StartsWith("Photo 7, truncated.jpg, can't be read", new VideoExportErrorMapper().Map(exception)!.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
    }

    [Fact]
    public async Task AFullDiskDuringAGifWriteShowsTheDiskFullMessage()
    {
        // The disk fills up a few frames in, at the stream the WIC GIF encoder writes to (GifEncoder's open seam): WIC
        // passes ERROR_DISK_FULL through, and the user reads "The disk is full", not "The GIF encoder stopped".
        SkipWithoutGraphicsDevice();
        var photos = ManyPhotos(30).ToList();
        var snapshot = GoldenExport.Snapshot(photos.Select(path => GoldenExport.FrameOf(path)), TimingSettings.PerSecond(new Fraction(30, 1)), FrameAspect.Ratio16x9, FitMode.Fill, VideoFormat.Gif);
        var output = _scratch.PathOf("full.gif");

        var exception = await Assert.ThrowsAsync<VideoExportException>(() => ExportAsync(
            snapshot,
            output,
            options: new ProjectExportOptions { CreateEncoder = (path, settings, _) => new GifEncoder(path, settings, open: file => new FullDiskStream(file, 150_000)) }));
        await ExportCleanup.WhenDone(output);

        Assert.Equal((VideoExportFailure.DiskFull, (VideoFormat?)VideoFormat.Gif), (exception.Failure, exception.Format));
        Assert.Equal(FullDiskStream.DiskFull, exception.InnerException!.HResult);
        Assert.Equal(ProjectText.ExportFailedDiskFull, new VideoExportErrorMapper().Map(exception)!.Text);
        Assert.False(File.Exists(output));
        Assert.Empty(ProjectExporter.PartialFilesOf(output));
    }

    [Fact]
    public void CoveredFramesCountsThePhotosBeforeTheNextExportedOne()
    {
        var counts = new List<int>();
        var covered = new CoveredFrames(new SynchronousCount(counts.Add), [0, 3, 6, 9], 11);
        foreach (var exported in new[] { 1, 2, 3, 4 })
        {
            covered.Report(exported);
        }

        Assert.Equal([3, 6, 9, 11], counts);
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
        TimingSettings? timing = null) =>
        GoldenExport.Snapshot(paths.Select(path => GoldenExport.FrameOf(path)), timing ?? TimingSettings.PerSecond(new Fraction(30, 1)), aspect, fit, quality: OutputQuality.Standard);

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

    private sealed class SynchronousCount(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
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
