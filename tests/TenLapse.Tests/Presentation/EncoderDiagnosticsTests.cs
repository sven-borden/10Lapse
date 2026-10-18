using TenLapse.Core.Projects;
using TenLapse.Media.Encoding;
using TenLapse.Media.Export;
using TenLapse.Presentation.Services;
using TenLapse.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>The Encoders section of Copy diagnostics: what is known, never a probe.</summary>
public sealed class EncoderDiagnosticsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "10Lapse tests", "encoders " + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void WithoutACacheItSaysNotProbedYetAndReadsNothing()
    {
        var read = false;
        var section = new EncoderDiagnostics(Path.Combine(_folder, "encoder-capabilities.json"), readCache: _ =>
        {
            read = true;
            return Capabilities();
        });

        Assert.Equal("## Encoders\n" + EncoderDiagnostics.NotProbed + "\n", Text(section));
        Assert.False(read);
    }

    [Fact]
    public void ACacheForAnotherConfigurationIsNotUsed()
    {
        var path = CacheFile();

        var text = Text(new EncoderDiagnostics(path, readCache: _ => null));

        Assert.Contains(EncoderDiagnostics.CacheOutdated, text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCachedResultGivesTheSummaryTheMatrixAndTheTestEncodes()
    {
        var text = Text(new EncoderDiagnostics(CacheFile(), readCache: _ => Capabilities()));

        Assert.Contains("Source: the cache of the last probe", text, StringComparison.Ordinal);
        Assert.Contains("H.264: This PC's graphics hardware exports up to 1440p; larger sizes use the slower software encoder. (16:9 at 30 fps)", text, StringComparison.Ordinal);
        Assert.Contains("H.264 available: yes, hardware, NVIDIA H.264 Encoder MFT", text, StringComparison.Ordinal);
        Assert.Contains("HEVC available: no (no decoder: Windows could not play it back)", text, StringComparison.Ordinal);
        Assert.Contains("H.264 16:9: 120 fps none; 60 fps up to 1440p hardware, 4K software; 30 fps up to 1440p hardware, 4K software", text, StringComparison.Ordinal);
        Assert.Contains("HEVC 9:16: 120 fps none; 60 fps none; 30 fps none", text, StringComparison.Ordinal);
        Assert.Contains("Encoder 1: H.264, NVIDIA H.264 Encoder MFT, hardware", text, StringComparison.Ordinal);
        Assert.Contains("Test encode 1: H.264 3840 x 2160 at 60 fps: software, H264 Encoder MFT, 300 ms", text, StringComparison.Ordinal);
        Assert.Contains("Test encode 3: H.264 3840 x 2160 at 120 fps: failed (SettingsRejected), 550 ms", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThisSessionsResultComesFirst()
    {
        var read = false;
        var section = new EncoderDiagnostics(CacheFile(), loaded: Capabilities, readCache: _ =>
        {
            read = true;
            return null;
        });

        Assert.Contains("Source: this session's probe", Text(section), StringComparison.Ordinal);
        Assert.False(read);
    }

    private static string Text(IDiagnosticsContributor section)
    {
        var report = new DiagnosticsReport();
        report.AddSection(section.Title);
        section.Contribute(report);
        return report.ToString().ReplaceLineEndings("\n");
    }

    private string CacheFile()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "encoder-capabilities.json");
        File.WriteAllText(path, "{}");
        return path;
    }

    // A GPU that encodes H.264 up to 1440p60 (the software encoder takes 4K60), and HEVC without a decoder.
    private static EncoderCapabilities Capabilities() => new(
        [new EncoderMft(VideoFormat.Mp4H264, "NVIDIA H.264 Encoder MFT", IsHardware: true, Guid.NewGuid()), new EncoderMft(VideoFormat.Mp4Hevc, "NVIDIA HEVC Encoder MFT", IsHardware: true, Guid.NewGuid())],
        [
            new ProbedSize(VideoFormat.Mp4H264, 3840, 2160, 60, EncoderKind.Software, "H264 Encoder MFT", null, 300),
            new ProbedSize(VideoFormat.Mp4H264, 2560, 1440, 60, EncoderKind.Hardware, "NVIDIA H.264 Encoder MFT", null, 40),
            new ProbedSize(VideoFormat.Mp4H264, 3840, 2160, 120, EncoderKind.None, null, VideoExportFailure.SettingsRejected, 550),
            new ProbedSize(VideoFormat.Mp4H264, 2160, 3840, 60, EncoderKind.Software, "H264 Encoder MFT", null, 300),
            new ProbedSize(VideoFormat.Mp4H264, 1440, 2560, 60, EncoderKind.Hardware, "NVIDIA H.264 Encoder MFT", null, 40),
        ],
        TimeSpan.FromMilliseconds(1230));
}
