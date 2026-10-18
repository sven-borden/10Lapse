using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;

namespace TenLapse.Tests.Media;

/// <summary>The pipeline's failure paths, driven by a fake encode stage that fails on cue.</summary>
public sealed class EncoderFailureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", Guid.NewGuid().ToString("N"));

    public EncoderFailureTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task AnEncoderFailingMidStreamFailsTheExportAndLeavesNoFile()
    {
        var path = Path.Combine(_directory, "midstream.mp4");
        FakeEncoder? encoder = null;
        var failure = new COMException("The fake encoder broke.", unchecked((int)0x80004005)); // E_FAIL

        var exception = await Assert.ThrowsAsync<VideoExportException>(() => VideoExporter.ExportAsync(
            new NumberedFrames(60, 320, 180),
            path,
            new VideoExportSettings(320, 180, new Fraction(30, 1)),
            progress: null,
            TestContext.Current.CancellationToken,
            (output, _, _) => encoder = new FakeEncoder(output) { FailAt = 20, Failure = failure }));

        Assert.Equal(VideoExportFailure.EncoderFailed, exception.Failure);
        Assert.Same(failure, exception.InnerException);
        Assert.Equal(20, encoder!.Written);
        Assert.True(encoder.Disposed);
        Assert.False(File.Exists(path), "The incomplete file is still there.");
    }

    [Fact]
    public async Task AFinishedExportKeepsItsFileAndReportsTheEncoder()
    {
        var path = Path.Combine(_directory, "fake.mp4");
        FakeEncoder? encoder = null;

        var result = await VideoExporter.ExportAsync(
            new NumberedFrames(12, 320, 180),
            path,
            new VideoExportSettings(320, 180, new Fraction(30, 1)),
            progress: null,
            TestContext.Current.CancellationToken,
            (output, _, _) => encoder = new FakeEncoder(output));

        Assert.Equal(12, result.FrameCount);
        Assert.Equal("Fake encoder", result.Encoder?.Name);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => FrameTiming.FrameStart(i, new Fraction(30, 1))), encoder!.Times);
        Assert.True(encoder.Finished);
        Assert.True(File.Exists(path));
    }

    /// <summary>Writes a placeholder file when created, as the sink writer does, and counts frames.</summary>
    internal sealed class FakeEncoder : IVideoEncoder
    {
        public FakeEncoder(string path) => File.WriteAllBytes(path, [0, 0, 0, 0]);

        public int FailAt { get; init; } = -1;

        public Exception? Failure { get; init; }

        public int Written { get; private set; }

        public List<long> Times { get; } = [];

        public bool Finished { get; private set; }

        public bool Disposed { get; private set; }

        public VideoEncoderInfo? Encoder { get; } = new("Fake encoder", IsHardware: false);

        public void Write(CanvasRenderTarget target, long time, long duration)
        {
            if (Written == FailAt)
            {
                throw Failure!;
            }

            Times.Add(time);
            Written++;
        }

        public void Finish() => Finished = true;

        public void Dispose() => Disposed = true;
    }
}
