using TenLapse.Media.Export;
using TenLapse.Media.Inspection;

namespace TenLapse.Tests.Media;

/// <summary>The skip conditions every export test shares: an H.264 encoder and a usable graphics device.</summary>
internal static class MediaSkips
{
    public static void SkipWithoutH264Encoder() =>
        Assert.SkipWhen(
            H264Encoders.Find().Count == 0,
            "No H.264 encoder is installed (Media Foundation lists none), so the export cannot run here. Run this test on a PC with one before merging.");

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
        catch (VideoExportException exception) when (exception.Failure == VideoExportFailure.EncoderUnavailable)
        {
            Assert.Skip($"The sink writer found no H.264 encoder ({exception.Message}). Run this test on a PC with one before merging.");
            throw;
        }
    }
}
