using System.Diagnostics;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>The #9 probe on this PC's real encoders: one probe (about a second), then the cache.</summary>
public sealed class EncoderProbeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", Guid.NewGuid().ToString("N"));

    public EncoderProbeTests() => Directory.CreateDirectory(_directory);

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

    [Fact]
    public async Task ThisPcExports1080pH264AndTheSecondLoadComesFromTheCache()
    {
        SkipWithoutH264Encoder();
        var cache = Path.Combine(_directory, "encoders.json");
        var token = TestContext.Current.CancellationToken;

        var probed = await EncoderProbe.LoadOrProbeAsync(cache, token);
        Assert.False(probed.IsProvisional, $"The probe could not finish: {string.Join(", ", probed.Probes.Select(p => p.Failure))}");
        Assert.True(probed.Supports(VideoFormat.Mp4H264, 1920, 1080, new Fraction(30, 1)));
        Assert.True(probed.MaxResolution(VideoFormat.Mp4H264, FrameAspect.Ratio16x9, new Fraction(30, 1)) >= OutputResolution.Hd1080);
        Assert.True(File.Exists(cache));
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "10lapse-probe-*"));

        var clock = Stopwatch.StartNew();
        var cached = await EncoderProbe.LoadOrProbeAsync(cache, token);
        clock.Stop();

        Assert.Equal(probed.Probes, cached.Probes);
        Assert.Equal(probed.ProbeDuration, cached.ProbeDuration);

        // Listing the encoders dominates a cached load (about 90 ms here); a probe takes about a second.
        Assert.True(clock.Elapsed < probed.ProbeDuration, $"The cached load took {clock.Elapsed.TotalMilliseconds:0} ms.");
    }

    [Fact]
    public async Task AnHevcExportWritesHevcWhereTheEncoderIsListed()
    {
        SkipWithoutGraphicsDevice();
        Assert.SkipWhen(EncoderCatalog.Find(VideoFormat.Mp4Hevc).Count == 0, "No HEVC encoder is installed (HEVC Video Extensions or a GPU encoder).");
        var path = Path.Combine(_directory, "hevc.mp4");
        var settings = new VideoExportSettings(320, 180, new Fraction(30, 1)) { Format = VideoFormat.Mp4Hevc };

        var result = await ExportOrSkipAsync(new SolidFrames(10, 320, 180, Color.FromArgb(255, 200, 40, 40)), path, settings);

        Assert.Equal(10, result.FrameCount);
        var info = VideoFileReader.Read(path);
        Assert.Equal("HEVC", info.Codec);
        Assert.Equal((320, 180), (info.Width, info.Height));
        Assert.Equal(10, info.FrameCount);
        Assert.Equal(0, info.SampleTimes[0]);
    }
}
