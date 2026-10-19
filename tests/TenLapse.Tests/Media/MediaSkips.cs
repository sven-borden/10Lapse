using TenLapse.Core.Projects;
using TenLapse.Media.Encoding;
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

    /// <summary>
    /// True when an export failure means this PC cannot run the export at all, so the test should skip: it failed as
    /// <see cref="VideoExportFailure.EncoderUnavailable"/> and Media Foundation lists no encoder for the format, or as
    /// <see cref="VideoExportFailure.NoGraphicsDevice"/> and the device probe fails too. On a PC that has both, the
    /// same failure is a regression (a media type the encoder now refuses, say) and must fail the test.
    /// </summary>
    public static bool CannotRunHere(VideoExportException exception, VideoFormat format) =>
        exception.Failure switch
        {
            VideoExportFailure.EncoderUnavailable => EncoderCatalog.Find(format).Count == 0,
            VideoExportFailure.NoGraphicsDevice => _noDevice.Value is not null,
            _ => false,
        };

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
        catch (VideoExportException exception) when (CannotRunHere(exception, settings.Format))
        {
            Assert.Skip($"The export cannot run here ({exception.Message}). {RunElsewhere}");
            throw;
        }
    }

    /// <summary>Exports a project as the app does, with the test seams in <paramref name="options"/>; skips only when <see cref="CannotRunHere"/>.</summary>
    public static async Task<ProjectExportResult> ExportOrSkipAsync(
        ProjectSnapshot snapshot,
        string path,
        IProgress<ExportProgress>? progress = null,
        ProjectExportOptions? options = null)
    {
        try
        {
            return await ProjectExporter.ExportAsync(snapshot, path, progress, TestContext.Current.CancellationToken, options ?? new ProjectExportOptions());
        }
        catch (VideoExportException exception) when (CannotRunHere(exception, snapshot.Output.Format))
        {
            Assert.Skip($"The {snapshot.Output.Format} export cannot run here ({exception.Message}). {RunElsewhere}");
            throw;
        }
    }
}
