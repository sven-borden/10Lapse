using TenLapse.Media.Export;
using TenLapse.Media.Inspection;

namespace TenLapse.Tests.Media;

/// <summary>The skip conditions every export test shares: a usable graphics device and an H.264 encoder.</summary>
internal static class MediaSkips
{
    private const string RunElsewhere = "Run this test on a PC with one before merging.";

    // Null when a device can be made; otherwise why not. Checked once per test run.
    private static readonly Lazy<string?> _noDevice = new(() =>
    {
        try
        {
            VideoDevice.Create().Dispose();
            return null;
        }
        catch (VideoExportException exception) when (exception.Failure == VideoExportFailure.NoGraphicsDevice)
        {
            return exception.Message;
        }
    });

    /// <summary>
    /// Skips on a PC (or a VM, or a CI runner on WARP) without a hardware Direct3D 11 device with video
    /// support: the export cannot run there, even with hardware acceleration off.
    /// </summary>
    public static void SkipWithoutGraphicsDevice() =>
        Assert.SkipWhen(
            _noDevice.Value is not null,
            $"No usable graphics device ({_noDevice.Value}): the export needs a hardware Direct3D 11 device with video support. {RunElsewhere}");

    public static void SkipWithoutH264Encoder()
    {
        SkipWithoutGraphicsDevice();
        Assert.SkipWhen(
            H264Encoders.Find().Count == 0,
            $"No H.264 encoder is installed (Media Foundation lists none), so the export cannot run here. {RunElsewhere}");
    }

    public static async Task<VideoExportResult> ExportOrSkipAsync(
        IFrameSource source,
        string path,
        VideoExportSettings settings,
        IProgress<int>? progress = null)
    {
        try
        {
            return await VideoExporter.ExportAsync(source, path, settings, progress, TestContext.Current.CancellationToken);
        }
        catch (VideoExportException exception) when (exception.Failure is VideoExportFailure.EncoderUnavailable or VideoExportFailure.NoGraphicsDevice)
        {
            Assert.Skip($"The export cannot run here ({exception.Message}). {RunElsewhere}");
            throw;
        }
    }
}
