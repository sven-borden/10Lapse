using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;

namespace TenLapse.Tests.Media;

/// <summary>Quality to bitrate, and the size estimate #23 shows from the same numbers.</summary>
public sealed class ExportBitrateTests
{
    private static readonly PixelSize _hd = new(1920, 1080);
    private static readonly PixelSize _uhd = new(3840, 2160);
    private static readonly Fraction _thirty = new(30, 1);

    [Theory]
    [InlineData(OutputQuality.Standard, 4_976_640)]
    [InlineData(OutputQuality.High, 8_709_120)]
    [InlineData(OutputQuality.Maximum, 14_929_920)]
    public void H264At1080p30(OutputQuality quality, uint expected) =>
        Assert.Equal(expected, ExportBitrate.For(_hd, _thirty, VideoFormat.Mp4H264, quality));

    [Fact]
    public void HighAt4K30IsWhatVideoSitesRecommend() =>
        Assert.InRange(ExportBitrate.For(_uhd, _thirty, VideoFormat.Mp4H264, OutputQuality.High), 30_000_000u, 40_000_000u);

    [Fact]
    public void HevcGetsSixtyPercentOfH264()
    {
        foreach (var quality in Enum.GetValues<OutputQuality>())
        {
            var h264 = ExportBitrate.For(_hd, _thirty, VideoFormat.Mp4H264, quality);
            var hevc = ExportBitrate.For(_hd, _thirty, VideoFormat.Mp4Hevc, quality);
            Assert.Equal(h264 * 0.6, hevc, 1.0);
        }
    }

    [Fact]
    public void HigherQualityHigherResolutionAndHigherRateCostMore()
    {
        var sizes = new[] { new PixelSize(1280, 720), _hd, new PixelSize(2560, 1440), _uhd };
        var rates = new[] { new Fraction(1, 10), new Fraction(1, 1), new Fraction(12, 1), new Fraction(24, 1), _thirty, new Fraction(60, 1) };
        foreach (var format in Enum.GetValues<VideoFormat>())
        {
            foreach (var size in sizes)
            {
                foreach (var rate in rates)
                {
                    var bitrates = Enum.GetValues<OutputQuality>().Select(quality => ExportBitrate.For(size, rate, format, quality)).ToArray();
                    Assert.True(bitrates[0] <= bitrates[1] && bitrates[1] <= bitrates[2], $"{format} {size.Width}x{size.Height} {rate}");
                }
            }
        }

        var byRate = rates.Select(rate => ExportBitrate.For(_hd, rate, VideoFormat.Mp4H264, OutputQuality.High)).ToArray();
        Assert.Equal(byRate.Order(), byRate);
        var bySize = sizes.Select(size => ExportBitrate.For(size, _thirty, VideoFormat.Mp4H264, OutputQuality.High)).ToArray();
        Assert.Equal(bySize.Order(), bySize);
    }

    [Fact]
    public void SlowClipsGetMoreBitsPerFrame()
    {
        // Each frame of a 1 fps slideshow is nearly a new picture: more bits per frame than at 30 fps.
        var slow = ExportBitrate.For(_hd, new Fraction(1, 1), VideoFormat.Mp4H264, OutputQuality.High) / 1.0;
        var normal = ExportBitrate.For(_hd, _thirty, VideoFormat.Mp4H264, OutputQuality.High) / 30.0;
        Assert.True(slow > 3 * normal, $"{slow:0} bits a frame at 1 fps, {normal:0} at 30 fps");
    }

    [Fact]
    public void TheBitrateStaysWithinItsBounds()
    {
        Assert.Equal(ExportBitrate.MaxBitrate, ExportBitrate.For(_uhd, new Fraction(120, 1), VideoFormat.Mp4H264, OutputQuality.Maximum));
        Assert.Equal(ExportBitrate.MinBitrate, ExportBitrate.For(new PixelSize(64, 36), new Fraction(1, 10), VideoFormat.Mp4Hevc, OutputQuality.Standard));
    }

    [Fact]
    public void TheEstimateIsTheBitrateOverTheExactDurationPlusTheContainer()
    {
        var bitrate = ExportBitrate.For(_hd, _thirty, VideoFormat.Mp4H264, OutputQuality.High);

        var bytes = ExportBitrate.EstimateBytes(300, _thirty, _hd, VideoFormat.Mp4H264, OutputQuality.High);

        // 10 s exactly.
        Assert.InRange(bytes - (bitrate * 10L / 8), 0, 16 * 1024);
        Assert.Equal(0, ExportBitrate.EstimateBytes(0, _thirty, _hd, VideoFormat.Mp4H264, OutputQuality.High));
        Assert.True(ExportBitrate.EstimateBytes(20_000, _thirty, _uhd, VideoFormat.Mp4H264, OutputQuality.Maximum) > int.MaxValue);
    }

    [Fact]
    public void TheProjectSettingsCarryTheBitrateOfTheirQuality()
    {
        var project = new Project(TimingSettings.PerSecond(new Fraction(24, 1)), new FrameSettings(FrameAspect.Ratio9x16, FitMode.Fit), new OutputSettings(OutputResolution.Qhd1440, VideoFormat.Mp4Hevc, OutputQuality.Maximum), SortMode.Manual);
        project.AddFrames([new Frame(@"C:\photos\a.jpg", 1, DateTime.UtcNow)]);

        var settings = ProjectExporter.SettingsFor(project.Snapshot);

        Assert.Equal((1440, 2560), (settings.Width, settings.Height));
        Assert.Equal(new Fraction(24, 1), settings.FrameRate);
        Assert.Equal(VideoFormat.Mp4Hevc, settings.Format);
        Assert.Equal(FitMode.Fit, settings.Fit);
        Assert.Equal(ExportBitrate.For(new PixelSize(1440, 2560), new Fraction(24, 1), VideoFormat.Mp4Hevc, OutputQuality.Maximum), settings.EffectiveBitrate);
    }

    [Fact]
    public void TenSecondsPerPhotoExportsAtOneTenthOfAFramePerSecond()
    {
        var project = new Project(TimingSettings.PerPhoto(new Fraction(10, 1)), FrameSettings.Default, OutputSettings.Default, SortMode.Manual);
        project.AddFrames(Enumerable.Range(0, 842).Select(i => new Frame($@"C:\photos\{i}.jpg", 1, DateTime.UtcNow)));

        var settings = ProjectExporter.SettingsFor(project.Snapshot);

        Assert.Equal(new Fraction(1, 10), settings.FrameRate);
        Assert.Equal(ExportBitrate.For(project.Snapshot.VideoSize, new Fraction(1, 10), OutputSettings.Default.Format, OutputSettings.Default.Quality), settings.EffectiveBitrate);
        Assert.True(settings.EffectiveBitrate > 0);
    }
}
