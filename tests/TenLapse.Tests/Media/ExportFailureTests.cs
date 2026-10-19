using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>
/// How an export fails: the category #22 turns into a sentence, the file it leaves (none), and bugs that must
/// surface as themselves. A fake encode stage fails on cue; the folder cases run the real sink writer.
/// </summary>
public sealed class ExportFailureTests : IDisposable
{
    private static readonly Fraction _rate = new(30, 1);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", Guid.NewGuid().ToString("N"));

    public ExportFailureTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task AnEncoderFailingMidStreamFailsTheExportAndLeavesNoFile()
    {
        SkipWithoutGraphicsDevice();
        var path = Path.Combine(_directory, "midstream.mp4");
        FakeEncoder? encoder = null;
        var failure = new COMException("The fake encoder broke.", unchecked((int)0x80004005)); // E_FAIL

        var exception = await Assert.ThrowsAsync<VideoExportException>(
            () => ExportWithAsync(path, output => encoder = new FakeEncoder(output) { FailAt = 20, Failure = failure }));

        await ExportCleanup.WhenDone(path);
        Assert.Equal(VideoExportFailure.EncoderFailed, exception.Failure);
        Assert.Same(failure, exception.InnerException);
        Assert.Equal(20, encoder!.Written);
        Assert.True(encoder.Disposed);
        Assert.False(File.Exists(path), "The incomplete file is still there.");
    }

    [Theory]
    [InlineData(0x80070070, VideoExportFailure.DiskFull)] // ERROR_DISK_FULL, raised as IOException
    [InlineData(0x887A0005, VideoExportFailure.GraphicsDeviceLost)] // DXGI_ERROR_DEVICE_REMOVED
    [InlineData(0x8007000E, VideoExportFailure.OutOfMemory)] // E_OUTOFMEMORY, raised as OutOfMemoryException
    [InlineData(0x80070005, VideoExportFailure.OutputFolderNotWritable)] // E_ACCESSDENIED, raised as UnauthorizedAccessException
    public async Task AWriteFailureIsCategorisedByItsHResultWhateverItsExceptionType(uint hresult, VideoExportFailure expected)
    {
        SkipWithoutGraphicsDevice();
        var path = Path.Combine(_directory, "write.mp4");
        var failure = Marshal.GetExceptionForHR(unchecked((int)hresult))!;

        var exception = await Assert.ThrowsAsync<VideoExportException>(
            () => ExportWithAsync(path, output => new FakeEncoder(output) { FailAt = 5, Failure = failure }));

        await ExportCleanup.WhenDone(path);
        Assert.Equal(expected, exception.Failure);
        Assert.Same(failure, exception.InnerException);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ABugInTheEncodeStageSurfacesAsItselfNotAsAnEncoderFailure()
    {
        SkipWithoutGraphicsDevice();
        var bug = new InvalidOperationException("Our own mistake.");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ExportWithAsync(Path.Combine(_directory, "bug.mp4"), output => new FakeEncoder(output) { FailAt = 3, Failure = bug }));

        Assert.Same(bug, exception);
    }

    [Fact]
    public async Task AnEncoderThatCannotOpenIsCategorisedAndCreatesNoFile()
    {
        SkipWithoutGraphicsDevice();
        var path = Path.Combine(_directory, "setup.mp4");

        var exception = await Assert.ThrowsAsync<VideoExportException>(
            () => ExportWithAsync(path, _ => throw new ArgumentException("Bad size.") { HResult = unchecked((int)0x80070057) }));

        await ExportCleanup.WhenDone(path);
        Assert.Equal(VideoExportFailure.SettingsRejected, exception.Failure);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AFinishedExportKeepsItsFileAndReportsTheEncoder()
    {
        SkipWithoutGraphicsDevice();
        var path = Path.Combine(_directory, "fake.mp4");
        FakeEncoder? encoder = null;

        var result = await ExportWithAsync(path, output => encoder = new FakeEncoder(output), frames: 12);

        Assert.Equal(12, result.FrameCount);
        Assert.Equal("Fake encoder", result.Encoder?.Name);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => FrameTiming.FrameStart(i, _rate)), encoder!.Times);
        Assert.True(encoder.Finished);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task AnExportIntoAFolderItCannotWriteFailsAsNotWritable()
    {
        SkipWithoutH264Encoder();
        var folder = new DirectoryInfo(Path.Combine(_directory, "locked"));
        folder.Create();
        var deny = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles | FileSystemRights.AppendData | FileSystemRights.CreateDirectories,
            AccessControlType.Deny);
        var security = folder.GetAccessControl();
        security.AddAccessRule(deny);
        folder.SetAccessControl(security);
        try
        {
            var exception = await Assert.ThrowsAsync<VideoExportException>(() => VideoExporter.ExportAsync(
                new NumberedFrames(10, 320, 180),
                Path.Combine(folder.FullName, "out.mp4"),
                new VideoExportSettings(320, 180, _rate),
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(VideoExportFailure.OutputFolderNotWritable, exception.Failure);
            Assert.Empty(folder.GetFiles());
        }
        finally
        {
            security.RemoveAccessRule(deny);
            folder.SetAccessControl(security);
        }
    }

    [Fact]
    public async Task AnExportIntoAMissingFolderFailsAsMissing()
    {
        SkipWithoutH264Encoder();
        var path = Path.Combine(_directory, "not-there", "out.mp4");

        var exception = await Assert.ThrowsAsync<VideoExportException>(() => VideoExporter.ExportAsync(
            new NumberedFrames(10, 320, 180),
            path,
            new VideoExportSettings(320, 180, _rate),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(VideoExportFailure.OutputFolderMissing, exception.Failure);
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheEncoderRunsWithoutBFrames(bool hardware)
    {
        SkipWithoutH264Encoder();
        using var device = VideoDevice.Create();
        using var encoder = new SinkWriterEncoder(
            Path.Combine(_directory, $"bframes-{hardware}.mp4"),
            new VideoExportSettings(160, 90, _rate) { HardwareAcceleration = hardware },
            device);

        TestContext.Current.TestOutputHelper?.WriteLine($"{encoder.Encoder?.Name}: B-frame count {encoder.BFrameCount?.ToString() ?? "not reported"}");
        Assert.True(encoder.BFrameCount is null or 0, $"{encoder.Encoder?.Name} reports {encoder.BFrameCount} B-frames.");
    }

    [Fact]
    public void AVideoWhoseFirstFrameIsNotAtZeroFailsTheFinish()
    {
        // Low latency off: the Microsoft encoder reorders, and the MP4 sink (no edit list) writes a composition
        // offset, so the clip would start one frame late in every player.
        SkipWithoutH264Encoder();
        var path = Path.Combine(_directory, "late.mp4");
        using var device = VideoDevice.Create();
        using var encoder = new SinkWriterEncoder(path, new VideoExportSettings(160, 90, _rate) { HardwareAcceleration = false }, device, lowLatency: false);
        for (var i = 0; i < 10; i++)
        {
            using var target = new CanvasRenderTarget(device.Canvas, 160, 90, 96f);
            using (var session = target.CreateDrawingSession())
            {
                session.Clear(Windows.UI.Color.FromArgb(255, (byte)(i * 20), 80, 120));
            }

            encoder.Write(target, FrameTiming.FrameStart(i, _rate), FrameTiming.FrameDuration(i, _rate));
        }

        var exception = Assert.Throws<VideoExportException>(encoder.Finish);

        Assert.Equal(VideoExportFailure.EncoderFailed, exception.Failure);
        Assert.Contains("instead of 0", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x887A0004, VideoExportFailure.NoGraphicsDevice)] // DXGI_ERROR_UNSUPPORTED
    [InlineData(0x80070003, VideoExportFailure.OutputFolderMissing)] // ERROR_PATH_NOT_FOUND
    [InlineData(0x80070020, VideoExportFailure.OutputFileInUse)] // ERROR_SHARING_VIOLATION
    [InlineData(0x80070027, VideoExportFailure.DiskFull)] // ERROR_HANDLE_DISK_FULL
    [InlineData(0xC00D5212, VideoExportFailure.EncoderUnavailable)] // MF_E_TOPO_CODEC_NOT_FOUND
    public void KnownHResultsHaveTheirCategory(uint hresult, VideoExportFailure expected) =>
        Assert.Equal(expected, ExportFailures.FromHResult(unchecked((int)hresult)));

    [Fact]
    public void AWorkingDeviceHasNoRemovedReasonAndTheReasonsHaveTheirNames()
    {
        // A real driver reset (TDR) cannot be provoked here; the export asks GetDeviceRemovedReason on every
        // frame-level failure and reports GraphicsDeviceLost with the reason when it is not S_OK.
        SkipWithoutGraphicsDevice();
        using var device = VideoDevice.Create();

        Assert.Equal(0, device.RemovedReason());
        Assert.Equal("DXGI_ERROR_DEVICE_HUNG", ExportFailures.DeviceRemovedName(unchecked((int)0x887A0006)));
        Assert.Equal("DXGI_ERROR_DEVICE_RESET", ExportFailures.DeviceRemovedName(unchecked((int)0x887A0007)));
        Assert.Equal("0x80004005", ExportFailures.DeviceRemovedName(unchecked((int)0x80004005)));
    }

    [Fact]
    public void AnInvalidArgumentRejectsTheSettingsOnlyDuringSetup()
    {
        Assert.Equal(VideoExportFailure.SettingsRejected, ExportFailures.FromHResult(unchecked((int)0x80070057), setup: true));
        Assert.Null(ExportFailures.FromHResult(unchecked((int)0x80070057)));
    }

    [Fact]
    public void OurOwnInvalidOperationIsABugButAMappedOneIsNot()
    {
        Assert.True(ExportFailures.IsBug(new InvalidOperationException()));
        Assert.True(ExportFailures.IsBug(new ArgumentNullException("x")));
        Assert.False(ExportFailures.IsBug(Marshal.GetExceptionForHR(unchecked((int)0x8000000E))!)); // E_ILLEGAL_METHOD_CALL
        Assert.False(ExportFailures.IsBug(new UnauthorizedAccessException()));
    }

    [Fact]
    public void TheSummaryNamesTheCodecBeingWritten()
    {
        Assert.Equal("No HEVC encoder could be loaded", ExportFailures.Summary(VideoExportFailure.EncoderUnavailable, VideoFormat.Mp4Hevc));
        Assert.Equal("The HEVC encoder refused these settings", ExportFailures.Summary(VideoExportFailure.SettingsRejected, VideoFormat.Mp4Hevc));
        Assert.Equal("The HEVC encoder stopped taking frames", ExportFailures.Summary(VideoExportFailure.EncoderStalled, VideoFormat.Mp4Hevc));
        Assert.Equal("The HEVC encoder failed", ExportFailures.Summary(VideoExportFailure.EncoderFailed, VideoFormat.Mp4Hevc));
        Assert.Equal("The H.264 encoder failed", ExportFailures.Summary(VideoExportFailure.EncoderFailed, VideoFormat.Mp4H264));
        Assert.Equal("The disk is full", ExportFailures.Summary(VideoExportFailure.DiskFull, VideoFormat.Mp4Hevc));

        var refused = ExportFailures.Classify(
            new COMException("Refused.", unchecked((int)0xC00D36B4)), VideoExportFailure.EncoderFailed, VideoFormat.Mp4Hevc, setup: true);
        Assert.Equal(VideoExportFailure.SettingsRejected, refused.Failure);
        Assert.StartsWith("The HEVC encoder refused these settings: ", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnHevcExportThatFailsNamesHevc()
    {
        SkipWithoutGraphicsDevice();
        var path = Path.Combine(_directory, "hevc.mp4");
        var failure = new COMException("The fake encoder broke.", unchecked((int)0x80004005)); // E_FAIL

        var exception = await Assert.ThrowsAsync<VideoExportException>(
            () => ExportWithAsync(path, output => new FakeEncoder(output) { FailAt = 5, Failure = failure }, format: VideoFormat.Mp4Hevc));

        await ExportCleanup.WhenDone(path);
        Assert.Equal(VideoExportFailure.EncoderFailed, exception.Failure);
        Assert.StartsWith("The HEVC encoder failed: ", exception.Message, StringComparison.Ordinal);
    }

    private Task<VideoExportResult> ExportWithAsync(
        string path,
        Func<string, IVideoEncoder> createEncoder,
        int frames = 60,
        VideoFormat format = VideoFormat.Mp4H264) =>
        VideoExporter.ExportAsync(
            new NumberedFrames(frames, 320, 180),
            path,
            new VideoExportSettings(320, 180, _rate) { Format = format },
            progress: null,
            TestContext.Current.CancellationToken,
            (output, _, _) => createEncoder(output));

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

        /// <summary>Runs at the start of every <see cref="Write"/>.</summary>
        public Action? OnWrite { get; init; }

        /// <summary>Runs in <see cref="Finish"/>.</summary>
        public Action? OnFinish { get; init; }

        public void Write(CanvasRenderTarget target, long time, long duration)
        {
            OnWrite?.Invoke();
            if (Written == FailAt)
            {
                throw Failure!;
            }

            Times.Add(time);
            Written++;
        }

        public void Finish()
        {
            OnFinish?.Invoke();
            Finished = true;
        }

        public void Dispose() => Disposed = true;
    }
}
