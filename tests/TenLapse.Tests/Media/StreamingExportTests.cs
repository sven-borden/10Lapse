using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>
/// The #8 pipeline end to end at a small size: draw, fill, encode to H.264 MP4
/// through IMFSinkWriter, read back with the Media Foundation source reader.
/// </summary>
public sealed class StreamingExportTests : IDisposable
{
    private const int Frames = 60;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", Guid.NewGuid().ToString("N"));

    public StreamingExportTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A reader still closing the file; the temp folder is cleaned up by Windows eventually.
        }
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(24, 1)]
    [InlineData(25, 1)]
    [InlineData(50, 1)]
    [InlineData(60, 1)]
    [InlineData(12, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 10)]
    public async Task ExportWritesEveryFrameAtItsExactTime(int numerator, int denominator)
    {
        // Rates whose thousandfold is whole: the MP4 sink's timescale (1000 units a frame) holds every FrameStart exactly.
        SkipWithoutH264Encoder();
        var rate = new Fraction(numerator, denominator);
        var path = Path.Combine(_directory, $"{numerator}-{denominator}.mp4");

        var result = await ExportOrSkipAsync(new NumberedFrames(Frames, 960, 640), path, new VideoExportSettings(640, 360, rate));

        Assert.Equal(Frames, result.FrameCount);
        Assert.NotNull(result.Encoder);
        var info = VideoFileReader.Read(path);
        Assert.Equal("H264", info.Codec);
        Assert.Equal((640, 360), (info.Width, info.Height));
        Assert.Equal(Frames, info.FrameCount);
        Assert.Equal(Enumerable.Range(0, Frames).Select(i => FrameTiming.FrameStart(i, rate)), info.SampleTimes);
        AssertFrameRate(info, rate);
        AssertDurations(info, rate);
    }

    [Theory]
    [InlineData(30000, 1001, 2001)] // late by up to 333 ticks (frame 997), on time again at frame 2000
    [InlineData(24000, 1001, 300)]
    [InlineData(60000, 1001, 300)]
    [InlineData(7, 3, 60)] // length mode: timescale 2333, up to 4,286 ticks late (frame 7)
    [InlineData(200, 9, 120)] // length mode: timescale 22222, up to 450 ticks late (frame 100)
    [InlineData(3, 7, 12)] // length mode: timescale 428, up to 23,364 ticks late (frame 3)
    public async Task EveryFrameLandsAtMostOneTimescaleUnitAfterItsStart(int numerator, int denominator, int frames)
    {
        // The MP4 sink's timescale is floor(rate x 1000). When that is not exact, each sample lands on that
        // grid at or after FrameStart and less than one unit later; the error does not accumulate.
        SkipWithoutH264Encoder();
        var rate = new Fraction(numerator, denominator);
        var path = Path.Combine(_directory, $"{numerator}-{denominator}.mp4");

        await ExportOrSkipAsync(new NumberedFrames(frames, 320, 180), path, new VideoExportSettings(160, 90, rate));

        var info = VideoFileReader.Read(path);
        Assert.Equal(frames, info.FrameCount);
        var unit = TimescaleUnit(rate);
        for (var i = 0; i < frames; i++)
        {
            var late = info.SampleTimes[i] - FrameTiming.FrameStart(i, rate);
            Assert.True(late >= 0 && late <= unit, $"Frame {i} is {late} ticks late; one timescale unit is {unit} ticks.");
        }

        AssertFrameRate(info, rate);
        AssertDurations(info, rate);
    }

    [Fact]
    public async Task EveryDecodedFrameShowsItsOwnNumber()
    {
        SkipWithoutH264Encoder();
        var path = Path.Combine(_directory, "numbers.mp4");
        // Two render targets for 60 frames: each is reused about 30 times, so a target drawn again while the encoder still read it would show.
        var settings = new VideoExportSettings(640, 360, new Fraction(30, 1)) { RenderTargets = 2 };

        await ExportOrSkipAsync(new NumberedFrames(Frames, 960, 640), path, settings);

        var numbers = new List<int>();
        var times = new List<long>();
        var decoded = VideoFileReader.ReadFrames(path, frame =>
        {
            numbers.Add(FrameCode.Read(frame.Pixels, frame.Width, frame.Height, frame.Stride));
            times.Add(frame.Time);
            Assert.Equal((640, 360), (frame.Width, frame.Height));
        });

        Assert.Equal(Frames, decoded);
        Assert.Equal(Enumerable.Range(0, Frames), numbers);
        Assert.Equal(Enumerable.Range(0, Frames).Select(i => FrameTiming.FrameStart(i, settings.FrameRate)), times);
    }

    [Fact]
    public async Task AFailingFrameFailsTheExportWithItsOwnException()
    {
        SkipWithoutH264Encoder();
        var source = new NumberedFrames(Frames, 320, 180)
        {
            BeforeLoad = index =>
            {
                if (index == 10)
                {
                    throw new InvalidDataException("Photo 10 is damaged.");
                }
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => VideoExporter.ExportAsync(
            source,
            Path.Combine(_directory, "broken.mp4"),
            new VideoExportSettings(320, 180, new Fraction(30, 1)),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("Photo 10 is damaged.", exception.Message);
    }

    [Fact]
    public async Task CancellingStopsTheExport()
    {
        SkipWithoutH264Encoder();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var source = new NumberedFrames(6000, 320, 180)
        {
            BeforeLoad = index =>
            {
                if (index == 20)
                {
                    cancellation.Cancel();
                }
            },
        };

        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoExporter.ExportAsync(
            source,
            Path.Combine(_directory, "cancelled.mp4"),
            new VideoExportSettings(320, 180, new Fraction(30, 1)),
            cancellationToken: cancellation.Token));
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ACancelledExportWaitsForTheDecodesInFlightBeforeItReturns()
    {
        // A WIC decode cannot be interrupted: the export must not dispose the device under it and return.
        SkipWithoutH264Encoder();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var source = new SlowFrames(200, TimeSpan.FromMilliseconds(150)) { OnStart = index => { if (index == 6) cancellation.Cancel(); } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoExporter.ExportAsync(
            source,
            Path.Combine(_directory, "inflight.mp4"),
            new VideoExportSettings(320, 180, new Fraction(30, 1)) { DecodeAhead = 4 },
            cancellationToken: cancellation.Token));

        Assert.True(source.Started > 6, $"Only {source.Started} loads started.");
        Assert.Equal(source.Started, source.Finished);
        Assert.Empty(source.Errors);
    }

    [Fact]
    public async Task CancellingJustAsTheExportCompletesNeverThrowsInTheCaller()
    {
        // Best effort: the caller's Cancel() lands at a different moment of the finish and clean-up in each round.
        // The window the fix closed (between disposing _stop and the registration) is a few instructions wide,
        // so this cannot reliably catch that regression; it checks a late cancel is harmless wherever it lands.
        SkipWithoutH264Encoder();
        const int Count = 4;
        for (var round = 0; round < 6; round++)
        {
            using var cancellation = new CancellationTokenSource();
            Exception? cancelFailure = null;
            Thread? canceller = null;
            var delay = TimeSpan.FromMilliseconds(round * 2);
            var progress = new SynchronousProgress(taken =>
            {
                if (taken != Count)
                {
                    return;
                }

                canceller = new Thread(() =>
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (clock.Elapsed < delay)
                    {
                        Thread.SpinWait(20);
                    }

                    try
                    {
                        cancellation.Cancel();
                    }
                    catch (Exception exception)
                    {
                        cancelFailure = exception;
                    }
                });
                canceller.Start();
            });

            try
            {
                await VideoExporter.ExportAsync(
                    new NumberedFrames(Count, 160, 90),
                    Path.Combine(_directory, $"race{round}.mp4"),
                    new VideoExportSettings(160, 90, new Fraction(30, 1)),
                    progress,
                    cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // The cancel won the race: fine.
            }

            canceller?.Join();
            Assert.Null(cancelFailure);
        }
    }

    [Fact]
    public void ReadingAMissingFileThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => VideoFileReader.Read(Path.Combine(_directory, "missing.mp4")));
    }

    /// <summary>
    /// One unit of the MP4 sink's timescale, floor(rate x 1000) units a second, in whole ticks rounded up:
    /// 334 at 30 and 29.97 fps, 4,287 at 7/3, 23,365 at 3/7. The bound for #32's timing checks.
    /// </summary>
    private static long TimescaleUnit(Fraction rate)
    {
        var timescale = (long)rate.Numerator * 1000 / rate.Denominator;
        return (FrameTiming.TicksPerSecond + timescale - 1) / timescale;
    }

    // Each sample's duration and the track's are whole timescale units: within one unit of ours.
    private static void AssertDurations(VideoFileInfo info, Fraction rate)
    {
        var unit = TimescaleUnit(rate);
        for (var i = 0; i < info.FrameCount; i++)
        {
            Assert.InRange(info.SampleDurations[i], FrameTiming.FrameDuration(i, rate) - unit, FrameTiming.FrameDuration(i, rate) + unit);
        }

        var expected = FrameTiming.Duration(info.FrameCount, rate);
        Assert.InRange(info.Duration, expected - unit, expected + unit);
    }

    // The source reader derives the rate from the 100 ns frame duration and gives the exact fraction back only for
    // the broadcast rates (12 fps reads back as 10,000,000/833,333, 24000/1001 as 10,000,000/417,083, 7/3 as 10,000,000/4,285,719). Elsewhere the
    // frame duration it implies is within one timescale unit of ours.
    private static void AssertFrameRate(VideoFileInfo info, Fraction rate)
    {
        Fraction[] broadcast = [new(24, 1), new(25, 1), new(30, 1), new(50, 1), new(60, 1), FrameRates.Ntsc30, FrameRates.Ntsc60];
        Assert.NotNull(info.FrameRate);
        if (broadcast.Contains(rate))
        {
            Assert.Equal(rate, info.FrameRate);
            return;
        }

        var read = info.FrameRate.Value;
        var readDuration = (double)FrameTiming.TicksPerSecond * read.Denominator / read.Numerator;
        var ourDuration = (double)FrameTiming.TicksPerSecond * rate.Denominator / rate.Numerator;
        Assert.InRange(readDuration - ourDuration, -TimescaleUnit(rate), TimescaleUnit(rate));
    }

    [Fact]
    public void AnExportStartedOnAnStaThreadWorks()
    {
        // The WinUI UI thread is an STA. A COM object created there belongs to that apartment, and Media
        // Foundation's own threads (and the thread pool) then cannot use it.
        SkipWithoutH264Encoder();
        var path = Path.Combine(_directory, "sta.mp4");
        Exception? failure = null;
        VideoExportResult? result = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = VideoExporter.ExportAsync(
                        new NumberedFrames(10, 320, 180),
                        path,
                        new VideoExportSettings(320, 180, new Fraction(30, 1)),
                        cancellationToken: TestContext.Current.CancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The export did not finish within 60 s.");

        Assert.Null(failure);
        Assert.Equal(10, result!.FrameCount);
        Assert.Equal(10, VideoFileReader.Read(path).FrameCount);
    }

    /// <summary>Reports on the thread that reports, unlike <see cref="Progress{T}"/>, which posts.</summary>
    private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
