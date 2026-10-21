using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Presentation;
using TenLapse.Services;
using TenLapse.Tests.Media;

namespace TenLapse.Tests.Presentation;

/// <summary>The Output panel on #9's real answers (hand-built probe sets), through the App's adapter.</summary>
public sealed class EncoderOutputCapabilitiesTests
{
    private static readonly Fraction _fps30 = new(30, 1);

    // A GPU that stops at 1440p, with the software encoder taking 4K landscape only (no HEVC probes, no HEVC decoder use).
    private static EncoderOutputCapabilities HardwareTo1440() => new(EncoderCapabilitiesTests.Set(
        EncoderCapabilitiesTests.Probe(VideoFormat.Mp4H264, 3840, 2160, 60, EncoderKind.Software),
        EncoderCapabilitiesTests.Probe(VideoFormat.Mp4H264, 2560, 1440, 60, EncoderKind.Hardware),
        EncoderCapabilitiesTests.Probe(VideoFormat.Mp4H264, 2160, 3840, 60, EncoderKind.None),
        EncoderCapabilitiesTests.Probe(VideoFormat.Mp4H264, 1440, 2560, 60, EncoderKind.Hardware)));

    [Fact]
    public void TheAdapterAnswersFromTheProbe()
    {
        var pc = HardwareTo1440();

        Assert.True(pc.IsAvailable(VideoFormat.Mp4H264));
        Assert.False(pc.IsAvailable(VideoFormat.Mp4Hevc));
        Assert.True(pc.Supports(VideoFormat.Mp4H264, OutputResolution.Uhd2160, FrameAspect.Ratio16x9, _fps30, null));
        Assert.False(pc.UsesHardware(VideoFormat.Mp4H264, OutputResolution.Uhd2160, FrameAspect.Ratio16x9, _fps30, null));
        Assert.True(pc.UsesHardware(VideoFormat.Mp4H264, OutputResolution.Qhd1440, FrameAspect.Ratio16x9, _fps30, null));
        Assert.False(pc.Supports(VideoFormat.Mp4H264, OutputResolution.Uhd2160, FrameAspect.Ratio9x16, _fps30, null));
        Assert.False(pc.Supports(VideoFormat.Mp4Hevc, OutputResolution.Hd720, FrameAspect.Ratio16x9, _fps30, null));
        Assert.Contains("1440p", pc.Summary(VideoFormat.Mp4H264, FrameAspect.Ratio16x9, _fps30, null), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePanelHidesTheSizeThePortraitShapeCannotDoAndKeepsItForLandscape()
    {
        var shell = new ShellViewModel(new FakeDialogService(), new FakeErrorSink());
        var panel = new OutputPanelViewModel(shell, new FakeErrorSink());
        panel.SetCapabilities(HardwareTo1440());

        Assert.Equal(4, panel.Resolutions.Count);
        Assert.Contains("slower software encoder", panel.Note, StringComparison.Ordinal);

        shell.CurrentProject.Model.FrameSettings = new FrameSettings(FrameAspect.Ratio9x16, FitMode.Fill);

        Assert.Equal(
            [OutputResolution.Hd720, OutputResolution.Hd1080, OutputResolution.Qhd1440],
            panel.Resolutions.Select(option => option.Value));
        Assert.Equal(["MP4 · H.264", "Animated GIF"], panel.Formats.Select(option => option.Label));
        Assert.Equal("This PC's graphics hardware exports up to 1440p.", panel.Note);
        Assert.Equal("720p · 720 × 1280", panel.Resolutions[0].Label);
    }
}
