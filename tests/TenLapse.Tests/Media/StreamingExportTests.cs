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
    [InlineData(60, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 10)]
    public async Task ExportWritesEveryFrameAtItsExactTime(int numerator, int denominator)
    {
        SkipWithoutH264Encoder();
        var rate = new Fraction(numerator, denominator);
        var path = Path.Combine(_directory, $"{numerator}-{denominator}.mp4");

        var result = await ExportOrSkipAsync(new NumberedFrames(Frames, 960, 640), path, new VideoExportSettings(640, 360, rate));

        Assert.Equal(Frames, result.FrameCount);
        Assert.NotNull(result.Encoder);
        var info = VideoFileReader.Read(path);
        Assert.Equal("H264", info.Codec);
        Assert.Equal((640, 360), (info.Width, info.Height));
        Assert.Equal(rate, info.FrameRate);
        Assert.Equal(Frames, info.FrameCount);
        Assert.Equal(Enumerable.Range(0, Frames).Select(i => FrameTiming.FrameStart(i, rate)), info.SampleTimes);
        AssertDurations(info, rate, drift: 0);
    }

    [Fact]
    public async Task AnNtscExportDriftsByAThirdOfATickPerFrameAtMost()
    {
        // The MP4 sink stores 30000/1001 as timescale 29970 with 1000 units a frame (29.97 fps exactly),
        // so frame n lands at n * 1000/29970 s instead of n * 1001/30000 s: a third of a tick later per frame.
        SkipWithoutH264Encoder();
        var rate = FrameRates.Ntsc30;
        var path = Path.Combine(_directory, "ntsc.mp4");

        await ExportOrSkipAsync(new NumberedFrames(Frames, 960, 640), path, new VideoExportSettings(640, 360, rate));

        var info = VideoFileReader.Read(path);
        Assert.Equal(Frames, info.FrameCount);
        for (var i = 0; i < Frames; i++)
        {
            Assert.InRange(info.SampleTimes[i] - FrameTiming.FrameStart(i, rate), 0, 1 + (i / 3));
        }

        AssertDurations(info, rate, drift: 1 + (Frames / 3));
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
    public void ReadingAMissingFileThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => VideoFileReader.Read(Path.Combine(_directory, "missing.mp4")));
    }

    // The MP4 sink keeps 1000 time units per frame, and rounds each sample's duration and the track's down to them.
    private static void AssertDurations(VideoFileInfo info, Fraction rate, long drift)
    {
        var unit = (FrameTiming.FrameDuration(0, rate) / 1000) + 1;
        for (var i = 0; i < info.FrameCount; i++)
        {
            Assert.InRange(info.SampleDurations[i], FrameTiming.FrameDuration(i, rate) - unit, FrameTiming.FrameDuration(i, rate) + 1);
        }

        var expected = FrameTiming.Duration(info.FrameCount, rate);
        Assert.InRange(info.Duration, expected - unit - drift, expected + drift);
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
}
