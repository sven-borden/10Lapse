using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Media.Export;

namespace TenLapse.Tests.Media;

/// <summary>
/// The #9 search loop on simulated PCs: which test encodes it runs, in which order, and what the result answers.
/// The fakes stand in for the sink writer, which falls back to the software encoder by itself when the GPU
/// refuses a size, so a size the GPU cannot take comes back as Software, not as a failure.
/// </summary>
public sealed class EncoderSearchTests
{
    private const VideoFormat H264 = VideoFormat.Mp4H264;
    private const VideoFormat Hevc = VideoFormat.Mp4Hevc;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    private static readonly EncoderMft _gpuH264 = new(H264, "GPU H.264 Encoder MFT", true, Guid.NewGuid());
    private static readonly EncoderMft _softwareH264 = new(H264, "H264 Encoder MFT", false, Guid.NewGuid());
    private static readonly EncoderMft _gpuHevc = new(Hevc, "GPU HEVC Encoder MFT", true, Guid.NewGuid());
    private static readonly EncoderMft _softwareHevc = new(Hevc, "HEVCVideoExtensionEncoder", false, Guid.Empty);

    private readonly List<string> _encodes = [];

    // A fake test encode that answers by the size and rate, and records each call as "H.264 3840x2160@60".
    private Func<VideoFormat, int, int, int, ProbedSize> Fake(Func<VideoFormat, int, int, int, EncoderKind> answer, VideoExportFailure failure = VideoExportFailure.SettingsRejected) =>
        (format, width, height, fps) =>
        {
            lock (_encodes)
            {
                _encodes.Add($"{EncoderText.CodecName(format)} {width}x{height}@{fps}");
            }

            var kind = answer(format, width, height, fps);
            return new ProbedSize(
                format,
                width,
                height,
                fps,
                kind,
                kind == EncoderKind.None ? null : kind == EncoderKind.Hardware ? "GPU" : "software",
                kind == EncoderKind.None ? failure : null,
                10);
        };

    private static EncoderCapabilities Result(IReadOnlyList<EncoderMft> encoders, List<ProbedSize> probes) =>
        new(encoders, probes, TimeSpan.FromSeconds(1));

    [Fact]
    public void AGpuCappedAt4096x2160EncodesPortraitInSoftware()
    {
        EncoderMft[] encoders = [_gpuH264, _softwareH264];

        var probes = EncoderProbe.Search(
            encoders,
            Fake((_, width, height, _) => width <= 4096 && height <= 2160 ? EncoderKind.Hardware : EncoderKind.Software),
            hardwareAcceleration: true,
            _timeout,
            TestContext.Current.CancellationToken,
            out var stalled);

        Assert.False(stalled);
        Assert.Equal(
            [
                // 1080 x 1920 and 1440 x 1800 fit inside 3840 x 2160, so the landscape 4K encode settles them.
                "H.264 3840x2160@120", "H.264 2160x3840@120", "H.264 1440x2560@120",
                "H.264 2160x3840@60", "H.264 1440x2560@60",
                "H.264 2160x3840@30", "H.264 1440x2560@30",
            ],
            _encodes);

        var capabilities = Result(encoders, probes);
        foreach (var rate in new[] { new Fraction(120, 1), new Fraction(60, 1), new Fraction(30, 1) })
        {
            Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, rate, minimum: EncoderKind.Hardware));
            Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, FrameAspect.Ratio9x16, rate));
            Assert.Equal(OutputResolution.Hd1080, capabilities.MaxResolution(H264, FrameAspect.Ratio9x16, rate, minimum: EncoderKind.Hardware));
            Assert.Equal(OutputResolution.Qhd1440, capabilities.MaxResolution(H264, FrameAspect.Ratio4x5, rate, minimum: EncoderKind.Hardware));
            Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, FrameAspect.Ratio1x1, rate, minimum: EncoderKind.Hardware));
        }

        Assert.Equal(
            "This PC's graphics hardware exports up to 1080p; larger sizes use the slower software encoder.",
            capabilities.Summary(H264, FrameAspect.Ratio9x16, new Fraction(30, 1)));
        Assert.False(capabilities.IsAvailable(Hevc));
    }

    [Fact]
    public void AGpuCappedAtLevel51EncodesUhdAt30OnTheHardwareAndAt60InSoftware()
    {
        // H.264 level 5.1: at most 251,658,240 luma samples a second. 4K30 fits (248.8 million), 4K60 does not.
        EncoderMft[] encoders = [_gpuH264, _softwareH264];

        var probes = EncoderProbe.Search(
            encoders,
            Fake((_, width, height, fps) => (long)width * height * fps <= 251_658_240 ? EncoderKind.Hardware : EncoderKind.Software),
            hardwareAcceleration: true,
            _timeout,
            TestContext.Current.CancellationToken,
            out _);

        Assert.Equal(
            [
                "H.264 3840x2160@120", "H.264 2560x1440@120", "H.264 1920x1080@120", "H.264 2160x3840@120", "H.264 1440x2560@120",
                "H.264 1080x1920@120", "H.264 1920x1440@120", "H.264 1440x1440@120", "H.264 1440x1800@120",
                "H.264 3840x2160@60", "H.264 2560x1440@60", "H.264 2160x3840@60", "H.264 1440x2560@60",
                "H.264 2880x2160@60", "H.264 2160x2160@60", // 2160 x 2700 lies between the 1:1 and 9:16 software results
                "H.264 3840x2160@30", "H.264 2160x3840@30",
            ],
            _encodes);

        var capabilities = Result(encoders, probes);
        Assert.Equal(EncoderKind.Hardware, capabilities.EncoderFor(H264, 3840, 2160, new Fraction(30, 1)));
        Assert.Equal(EncoderKind.Software, capabilities.EncoderFor(H264, 3840, 2160, new Fraction(60, 1)));
        Assert.Equal(EncoderKind.Software, capabilities.EncoderFor(H264, 3840, 2160, new Fraction(50, 1)));
        Assert.Equal(EncoderKind.Hardware, capabilities.EncoderFor(H264, 1920, 1080, new Fraction(120, 1)));
        Assert.Equal(EncoderKind.Software, capabilities.EncoderFor(H264, 2560, 1440, new Fraction(120, 1)));
        Assert.Equal(OutputResolution.Hd1080, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, new Fraction(120, 1), minimum: EncoderKind.Hardware));
        Assert.Equal(OutputResolution.Qhd1440, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, new Fraction(60, 1), minimum: EncoderKind.Hardware));
        Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, new Fraction(60, 1)));
        Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, FrameAspect.Ratio4x5, new Fraction(30, 1), minimum: EncoderKind.Hardware));
        Assert.Equal("This PC's graphics hardware exports up to 4K.", capabilities.Summary(H264, FrameAspect.Ratio16x9, new Fraction(30, 1)));
    }

    [Fact]
    public void ASoftwareOnlyPcNeedsTwoEncodesPerCodec()
    {
        EncoderMft[] encoders = [_softwareH264, _softwareHevc];

        var probes = EncoderProbe.Search(
            encoders,
            Fake((_, _, _, _) => EncoderKind.Software),
            hardwareAcceleration: true,
            _timeout,
            TestContext.Current.CancellationToken,
            out _);

        Assert.Equal(["H.264 3840x2160@120", "H.264 2160x3840@120", "HEVC 3840x2160@120", "HEVC 2160x3840@120"], _encodes);
        var capabilities = Result(encoders, probes);
        foreach (var shape in EncoderProbe.Shapes)
        {
            Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, shape, new Fraction(30, 1)));
            Assert.Null(capabilities.MaxResolution(H264, shape, new Fraction(30, 1), minimum: EncoderKind.Hardware));
        }

        Assert.Equal(
            "This PC's graphics hardware cannot encode H.264; the slower software encoder exports up to 4K.",
            capabilities.Summary(H264, FrameAspect.Ratio4x5, new Fraction(25, 1)));
    }

    [Fact]
    public void AnEncoderThatFailsOnTheFirstSampleIsGivenUpAfterTheSmallestSizeFailsToo()
    {
        // H.264 on the GPU works; the HEVC encoder takes the settings and then fails every sample (E_FAIL).
        EncoderMft[] encoders = [_gpuH264, _softwareH264, _softwareHevc];

        var probes = EncoderProbe.Search(
            encoders,
            Fake((format, _, _, _) => format == H264 ? EncoderKind.Hardware : EncoderKind.None, VideoExportFailure.EncoderFailed),
            hardwareAcceleration: true,
            _timeout,
            TestContext.Current.CancellationToken,
            out var stalled);

        Assert.False(stalled);
        Assert.Equal(["H.264 3840x2160@120", "H.264 2160x3840@120", "HEVC 3840x2160@120", "HEVC 1280x720@30"], _encodes);
        var capabilities = Result(encoders, probes);
        Assert.True(capabilities.IsAvailable(H264));
        Assert.False(capabilities.IsAvailable(Hevc));
        Assert.Null(capabilities.MaxResolution(Hevc, FrameAspect.Ratio9x16, new Fraction(30, 1)));
        Assert.True(capabilities.IsProvisional, "A failure without a cause must not be cached.");
    }

    [Fact]
    public void AnEncoderThatFailsOnlyAtLargeSizesIsStillProbedBelowThem()
    {
        // EncoderFailed at 4K landscape only: the smallest size works, so the search carries on.
        EncoderMft[] encoders = [_softwareH264];

        var probes = EncoderProbe.Search(
            encoders,
            Fake((_, width, _, _) => width > 3000 ? EncoderKind.None : EncoderKind.Software, VideoExportFailure.EncoderFailed),
            hardwareAcceleration: true,
            _timeout,
            TestContext.Current.CancellationToken,
            out _);

        Assert.Equal(
            [
                "H.264 3840x2160@120", "H.264 1280x720@30", "H.264 2560x1440@120", "H.264 2160x3840@120", "H.264 2880x2160@120",
                "H.264 3840x2160@60", "H.264 3840x2160@30",
            ],
            _encodes);
        var capabilities = Result(encoders, probes);
        Assert.Equal(OutputResolution.Qhd1440, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, new Fraction(60, 1)));
        Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, FrameAspect.Ratio9x16, new Fraction(60, 1)));
    }

    [Fact]
    public void ATestEncodeThatHangsIsRecordedAsStalledAndEndsTheSearch()
    {
        EncoderMft[] encoders = [_gpuH264, _softwareH264, _gpuHevc];
        using var release = new ManualResetEventSlim();
        var fake = Fake((_, _, _, _) => EncoderKind.Hardware);
        try
        {
            var probes = EncoderProbe.Search(
                encoders,
                (format, width, height, fps) =>
                {
                    if (format == Hevc)
                    {
                        release.Wait(TimeSpan.FromSeconds(30)); // A driver that never answers.
                    }

                    return fake(format, width, height, fps);
                },
                hardwareAcceleration: true,
                TimeSpan.FromMilliseconds(200),
                TestContext.Current.CancellationToken,
                out var stalled);

            Assert.True(stalled);
            var last = probes[^1];
            Assert.Equal((Hevc, 3840, 2160, VideoExportFailure.EncoderStalled), (last.Format, last.Width, last.Height, last.Failure));
            Assert.Equal(3, probes.Count);
            Assert.Equal(EncoderKind.None, last.Encoder);
            Assert.InRange(last.Milliseconds, 150, 10_000);
            Assert.True(Result(encoders, probes).IsProvisional, "A stalled probe must not be cached.");
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void CancellingStopsBetweenTestEncodes()
    {
        using var cancel = new CancellationTokenSource();
        var fake = Fake((_, _, _, _) => EncoderKind.Software);

        Assert.Throws<OperationCanceledException>(() => EncoderProbe.Search(
            [_softwareH264],
            (format, width, height, fps) =>
            {
                cancel.Cancel();
                return fake(format, width, height, fps);
            },
            hardwareAcceleration: true,
            _timeout,
            cancel.Token,
            out _));
        Assert.Single(_encodes);
    }
}
