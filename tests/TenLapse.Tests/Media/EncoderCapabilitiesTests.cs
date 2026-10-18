using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Media.Export;

namespace TenLapse.Tests.Media;

/// <summary>The #9 answers on hand-built probe sets: no GPU involved.</summary>
public sealed class EncoderCapabilitiesTests
{
    private const VideoFormat H264 = VideoFormat.Mp4H264;
    private const VideoFormat Hevc = VideoFormat.Mp4Hevc;
    private static readonly Fraction _fps30 = new(30, 1);
    private static readonly Fraction _fps60 = new(60, 1);

    internal static ProbedSize Probe(VideoFormat format, int width, int height, int fps, EncoderKind kind, VideoExportFailure? failure = null) =>
        new(format, width, height, fps, kind, kind switch
        {
            EncoderKind.Hardware => "GPU Encoder MFT",
            EncoderKind.Software => "Microsoft software encoder",
            _ => null,
        }, kind == EncoderKind.None ? failure ?? VideoExportFailure.SettingsRejected : null, 50);

    internal static readonly DecoderMft HevcDecoder = new(Hevc, "Microsoft H265 Video Decoder MFT", false, Guid.Empty);

    // With an HEVC decoder listed, as on a PC with the HEVC Video Extensions.
    internal static EncoderCapabilities Set(params ProbedSize[] probes) => new([], probes, TimeSpan.FromMilliseconds(900), decoders: [HevcDecoder]);

    // What this PC (RTX 4060 Ti) answered: four encodes settle the matrix.
    private static EncoderCapabilities AllHardware() => Set(
        Probe(H264, 3840, 2160, 120, EncoderKind.Hardware),
        Probe(H264, 2160, 3840, 120, EncoderKind.Hardware),
        Probe(Hevc, 3840, 2160, 120, EncoderKind.Hardware),
        Probe(Hevc, 2160, 3840, 120, EncoderKind.Hardware));

    // A GPU that stops at 1440p, with the software encoder taking 4K landscape only.
    private static EncoderCapabilities HardwareTo1440() => Set(
        Probe(H264, 3840, 2160, 60, EncoderKind.Software),
        Probe(H264, 2560, 1440, 60, EncoderKind.Hardware),
        Probe(H264, 2160, 3840, 60, EncoderKind.None),
        Probe(H264, 1440, 2560, 60, EncoderKind.Hardware));

    [Fact]
    public void EveryOfferedSizeIsSupportedWhenTheLargestProbesWorked()
    {
        var capabilities = AllHardware();

        foreach (var shape in EncoderProbe.Shapes)
        {
            Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, shape, _fps60));
            Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(Hevc, shape, _fps30, minimum: EncoderKind.Hardware));
        }

        Assert.Equal(EncoderKind.Hardware, capabilities.EncoderFor(H264, 1920, 1080, new Fraction(30000, 1001)));
        Assert.True(capabilities.Supports(Hevc, OutputResolution.Uhd2160, FrameAspect.Ratio9x16, _fps60));
        Assert.True(capabilities.IsAvailable(Hevc));
        Assert.True(capabilities.HasHardware(H264));
        Assert.Equal("GPU Encoder MFT", capabilities.EncoderName(H264));
    }

    [Fact]
    public void NothingFasterOrLargerThanAProbeIsOffered()
    {
        var capabilities = AllHardware();

        // Above the fastest probed rate, and wider than 16:9 at 4K: no probe covers them, so no.
        Assert.True(capabilities.Supports(H264, 1920, 1080, new Fraction(120, 1)));
        Assert.False(capabilities.Supports(H264, 1920, 1080, new Fraction(121, 1)));
        Assert.False(capabilities.Supports(H264, 7680, 4320, _fps30));
        Assert.False(capabilities.Supports(H264, 3840, 3840, _fps30));

        // A 3:1 panorama (Match photos): 4K and 1440p are wider than any probe; 1080p is 3240 wide.
        var panorama = new PixelSize(9000, 3000);
        Assert.Equal(OutputResolution.Hd1080, capabilities.MaxResolution(H264, FrameAspect.MatchPhotos, _fps30, panorama));
        Assert.False(capabilities.Supports(H264, OutputResolution.Qhd1440, FrameAspect.MatchPhotos, _fps30, panorama));
    }

    [Fact]
    public void LargerSizesFallBackToTheSoftwareEncoder()
    {
        var capabilities = HardwareTo1440();

        Assert.Equal(EncoderKind.Software, capabilities.EncoderFor(H264, 3840, 2160, _fps30));
        Assert.Equal(EncoderKind.Hardware, capabilities.EncoderFor(H264, 2560, 1440, _fps60));
        Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, _fps60));
        Assert.Equal(OutputResolution.Qhd1440, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, _fps60, minimum: EncoderKind.Hardware));

        // 4:3 at 4K is 2880 x 2160: under the software 4K probe. 9:16 at 4K failed outright.
        Assert.Equal(EncoderKind.Software, capabilities.EncoderFor(H264, 2880, 2160, _fps30));
        Assert.Equal(OutputResolution.Qhd1440, capabilities.MaxResolution(H264, FrameAspect.Ratio9x16, _fps60));
        Assert.Equal(OutputResolution.Qhd1440, capabilities.MaxResolution(H264, FrameAspect.Ratio4x5, _fps30));
    }

    [Fact]
    public void TheRateCanLowerTheLimit()
    {
        var capabilities = Set(
            Probe(H264, 3840, 2160, 60, EncoderKind.None),
            Probe(H264, 2560, 1440, 60, EncoderKind.Hardware),
            Probe(H264, 3840, 2160, 30, EncoderKind.Hardware));

        Assert.Equal(OutputResolution.Qhd1440, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, _fps60));
        Assert.Equal(OutputResolution.Qhd1440, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, new Fraction(50, 1)));
        Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, _fps30));
        Assert.Equal(OutputResolution.Uhd2160, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, new Fraction(1, 10)));
    }

    [Fact]
    public void ARateBetweenTheProbedRatesTakesTheNextRateUp()
    {
        // A GPU that takes 4K60 but leaves 4K120 to the software encoder.
        var capabilities = Set(
            Probe(H264, 3840, 2160, 120, EncoderKind.Software),
            Probe(H264, 1920, 1080, 120, EncoderKind.Hardware),
            Probe(H264, 3840, 2160, 60, EncoderKind.Hardware));

        Assert.Equal(EncoderKind.Hardware, capabilities.EncoderFor(H264, 3840, 2160, _fps60));
        Assert.Equal(EncoderKind.Software, capabilities.EncoderFor(H264, 3840, 2160, new Fraction(61, 1)));
        Assert.Equal(EncoderKind.Software, capabilities.EncoderFor(H264, 3840, 2160, new Fraction(90, 1)));
        Assert.Equal(EncoderKind.Software, capabilities.EncoderFor(H264, 3840, 2160, new Fraction(120, 1)));
        Assert.Equal(EncoderKind.Hardware, capabilities.EncoderFor(H264, 1920, 1080, new Fraction(100, 1)));
        Assert.Equal(OutputResolution.Hd1080, capabilities.MaxResolution(H264, FrameAspect.Ratio16x9, new Fraction(100, 1), minimum: EncoderKind.Hardware));
        Assert.Equal(EncoderKind.None, capabilities.EncoderFor(H264, 1280, 720, new Fraction(121, 1)));
    }

    [Fact]
    public void ACodecWithoutAWorkingProbeIsNotOffered()
    {
        // HEVC listed but every encode refused, and a set with no HEVC probe at all (no HEVC encoder installed).
        var refused = Set(Probe(H264, 3840, 2160, 60, EncoderKind.Hardware), Probe(Hevc, 1280, 720, 30, EncoderKind.None));
        var absent = Set(Probe(H264, 3840, 2160, 60, EncoderKind.Hardware));

        foreach (var capabilities in new[] { refused, absent })
        {
            Assert.False(capabilities.IsAvailable(Hevc));
            Assert.False(capabilities.Supports(Hevc, 1280, 720, _fps30));
            Assert.Null(capabilities.MaxResolution(Hevc, FrameAspect.Ratio16x9, _fps30));
            Assert.Null(capabilities.EncoderName(Hevc));
        }
    }

    [Fact]
    public void HevcThatWindowsCannotPlayIsNotOffered()
    {
        // A GPU HEVC encoder (it ships with the display driver) that works, but no HEVC Video Extensions: no decoder.
        EncoderMft gpuHevc = new(Hevc, "GPU HEVC Encoder MFT", true, Guid.NewGuid());
        ProbedSize[] probes =
        [
            Probe(H264, 3840, 2160, 120, EncoderKind.Hardware),
            Probe(H264, 2160, 3840, 120, EncoderKind.Hardware),
            Probe(Hevc, 3840, 2160, 120, EncoderKind.Hardware),
            Probe(Hevc, 2160, 3840, 120, EncoderKind.Hardware),
        ];
        var noDecoder = new EncoderCapabilities([gpuHevc], probes, TimeSpan.FromSeconds(1));

        Assert.False(noDecoder.CanPlayBack(Hevc));
        Assert.False(noDecoder.IsAvailable(Hevc));
        Assert.Equal(EncoderKind.None, noDecoder.EncoderFor(Hevc, 1280, 720, _fps30));
        Assert.False(noDecoder.Supports(Hevc, OutputResolution.Hd720, FrameAspect.Ratio16x9, _fps30));
        Assert.Null(noDecoder.MaxResolution(Hevc, FrameAspect.Ratio16x9, _fps30));
        Assert.Equal("This PC cannot play HEVC video, so it does not export it.", noDecoder.Summary(Hevc, FrameAspect.Ratio16x9, _fps30));

        // H.264 needs no listed decoder (it ships with Windows).
        Assert.True(noDecoder.CanPlayBack(H264));
        Assert.True(noDecoder.IsAvailable(H264));

        // An unplayable codec is not probed, so its listed GPU encoder without a hardware result does not make the result provisional.
        Assert.False(new EncoderCapabilities([gpuHevc], probes[..2], TimeSpan.FromSeconds(1)).IsProvisional);

        // The same encodes with a decoder listed: offered.
        var withDecoder = new EncoderCapabilities([gpuHevc], probes, TimeSpan.FromSeconds(1), decoders: [HevcDecoder]);
        Assert.True(withDecoder.IsAvailable(Hevc));
        Assert.Equal(EncoderKind.Hardware, withDecoder.EncoderFor(Hevc, 1280, 720, _fps30));

        // A decoder alone is not enough either: no working HEVC encode.
        Assert.False(new EncoderCapabilities([], probes[..2], TimeSpan.FromSeconds(1), decoders: [HevcDecoder]).IsAvailable(Hevc));
    }

    [Fact]
    public void MaxResolutionAsksForSoftwareOrHardware() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AllHardware().MaxResolution(H264, FrameAspect.Ratio16x9, _fps30, minimum: EncoderKind.None));

    [Fact]
    public void TheSummarySaysWhereTheHardwareStops()
    {
        Assert.Equal("This PC's graphics hardware exports up to 4K.", AllHardware().Summary(H264, FrameAspect.Ratio16x9, _fps30));
        Assert.Equal(
            "This PC's graphics hardware exports up to 1440p; larger sizes use the slower software encoder.",
            HardwareTo1440().Summary(H264, FrameAspect.Ratio16x9, _fps60));
        Assert.Equal("This PC's graphics hardware exports up to 1440p.", HardwareTo1440().Summary(H264, FrameAspect.Ratio9x16, _fps30));

        var softwareOnly = Set(Probe(H264, 3840, 2160, 60, EncoderKind.Software), Probe(H264, 2160, 3840, 60, EncoderKind.Software));
        Assert.Equal(
            "This PC's graphics hardware cannot encode H.264; the slower software encoder exports up to 4K.",
            softwareOnly.Summary(H264, FrameAspect.Ratio1x1, _fps30));

        Assert.Equal("This PC cannot export HEVC video at this shape and frame rate.", softwareOnly.Summary(Hevc, FrameAspect.Ratio16x9, _fps30));
    }

    [Fact]
    public void TheSummaryNamesEachResolution()
    {
        Assert.Equal("This PC's graphics hardware exports up to 720p.", EncoderText.Summary(H264, OutputResolution.Hd720, OutputResolution.Hd720));
        Assert.Equal("This PC's graphics hardware exports up to 1080p.", EncoderText.Summary(H264, OutputResolution.Hd1080, OutputResolution.Hd1080));
        Assert.Equal(
            "This PC's graphics hardware cannot encode HEVC; the slower software encoder exports up to 1080p.",
            EncoderText.Summary(Hevc, null, OutputResolution.Hd1080));
    }

    [Fact]
    public void OnlyTransientFailuresMakeAResultProvisional()
    {
        Assert.False(HardwareTo1440().IsProvisional);
        Assert.True(new EncoderCapabilities([], [], TimeSpan.Zero, VideoExportFailure.NoGraphicsDevice).IsProvisional);
        Assert.True(Set(Probe(H264, 3840, 2160, 60, EncoderKind.None, VideoExportFailure.GraphicsDeviceLost)).IsProvisional);
        Assert.True(Set(Probe(H264, 3840, 2160, 60, EncoderKind.None, VideoExportFailure.DiskFull)).IsProvisional);
        Assert.True(Set(Probe(H264, 3840, 2160, 60, EncoderKind.None, VideoExportFailure.EncoderStalled)).IsProvisional);
        Assert.False(Set(Probe(H264, 3840, 2160, 60, EncoderKind.None, VideoExportFailure.EncoderUnavailable)).IsProvisional);
        Assert.False(Set(Probe(H264, 3840, 2160, 60, EncoderKind.None, VideoExportFailure.SettingsRejected)).IsProvisional);
    }

    [Fact]
    public void AnEncoderFailureWithoutACauseIsNotCached()
    {
        // E_FAIL on the first sample: maybe a driver hiccup, so the next start asks again.
        var failed = Set(Probe(H264, 3840, 2160, 60, EncoderKind.Hardware), Probe(Hevc, 3840, 2160, 60, EncoderKind.None, VideoExportFailure.EncoderFailed));

        Assert.True(failed.IsProvisional);
        Assert.Throws<ArgumentException>(() => EncoderCapabilitiesCache.Write(Path.Combine(Path.GetTempPath(), "TenLapse.Tests", "never.json"), Key, failed));
    }

    [Fact]
    public void AListedGpuEncoderThatNeverRanOnTheHardwareIsNotCached()
    {
        EncoderMft gpu = new(H264, "GPU H.264 Encoder MFT", true, Guid.NewGuid());
        EncoderMft software = new(H264, "H264 Encoder MFT", false, Guid.NewGuid());
        EncoderMft hevcSoftware = new(Hevc, "HEVCVideoExtensionEncoder", false, Guid.Empty);
        ProbedSize[] softwareOnly = [Probe(H264, 3840, 2160, 120, EncoderKind.Software), Probe(H264, 2160, 3840, 120, EncoderKind.Software)];

        // The GPU encoder is listed, yet every size came back from the software encoder: the GPU may be busy or mid-update.
        var withGpu = new EncoderCapabilities([gpu, software, hevcSoftware], softwareOnly, TimeSpan.FromSeconds(1));
        Assert.True(withGpu.IsProvisional);
        Assert.Throws<ArgumentException>(() => EncoderCapabilitiesCache.Write(Path.Combine(Path.GetTempPath(), "TenLapse.Tests", "never.json"), Key, withGpu));

        // No GPU encoder listed: software is the answer, and it is cached. One hardware result is enough too.
        Assert.False(new EncoderCapabilities([software, hevcSoftware], softwareOnly, TimeSpan.FromSeconds(1)).IsProvisional);
        Assert.False(new EncoderCapabilities(
            [gpu, software],
            [Probe(H264, 3840, 2160, 120, EncoderKind.Software), Probe(H264, 1920, 1080, 120, EncoderKind.Hardware)],
            TimeSpan.FromSeconds(1)).IsProvisional);
    }

    private static EncoderCacheKey Key { get; } = new(EncoderCacheKey.CurrentSchema, "adapter", "name", "1.0", "10.0.1.1", "encoders", "decoders", "1.0", "codecs");

    [Fact]
    public void AProbeWithANonsenseSizeIsRefused() =>
        Assert.Throws<ArgumentException>(() => Set(Probe(H264, 0, 2160, 60, EncoderKind.Hardware)));

    [Fact]
    public void TheSearchSkipsWhatTheProbesAlreadySettle()
    {
        List<ProbedSize> probes = [Probe(H264, 3840, 2160, 60, EncoderKind.Hardware)];

        // Covered by a hardware 4K60 probe: smaller and slower is hardware too.
        Assert.True(EncoderProbe.IsSettled(probes, H264, 2880, 2160, 30, EncoderKind.Hardware));

        // Taller than any probe: unknown. A different codec: unknown.
        Assert.False(EncoderProbe.IsSettled(probes, H264, 2160, 3840, 60, EncoderKind.Hardware));
        Assert.False(EncoderProbe.IsSettled(probes, Hevc, 1280, 720, 30, EncoderKind.Hardware));

        // A software answer settles smaller sizes only when nothing can be hardware.
        List<ProbedSize> software = [Probe(H264, 3840, 2160, 60, EncoderKind.Software)];
        Assert.False(EncoderProbe.IsSettled(software, H264, 2560, 1440, 60, EncoderKind.Hardware));
        Assert.True(EncoderProbe.IsSettled(software, H264, 2560, 1440, 60, EncoderKind.Software));

        // A refusal settles everything larger and faster.
        List<ProbedSize> refused = [Probe(H264, 2560, 1440, 30, EncoderKind.None)];
        Assert.True(EncoderProbe.IsSettled(refused, H264, 3840, 2160, 60, EncoderKind.Hardware));
        Assert.False(EncoderProbe.IsSettled(refused, H264, 1920, 1080, 30, EncoderKind.Hardware));
    }
}
