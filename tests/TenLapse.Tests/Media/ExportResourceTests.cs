using System.Diagnostics;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>Runs alone: process-wide counters are meaningless while other tests export at the same time.</summary>
[CollectionDefinition(nameof(ExportResourceCollection), DisableParallelization = true)]
public sealed class ExportResourceCollection;

/// <summary>What an export leaves behind in the process once it has returned.</summary>
[Collection(nameof(ExportResourceCollection))]
public sealed class ExportResourceTests : IDisposable
{
    private const int Exports = 10;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", Guid.NewGuid().ToString("N"));

    public ExportResourceTests() => Directory.CreateDirectory(_directory);

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
    public async Task ExportsInARowKeepPrivateMemoryAndHandlesFlat()
    {
        // Each export creates its own Direct3D device. One left for the GC cost about 230 MB of video memory,
        // 300 MB private and 185 handles per 4K export. No GC.Collect here: the finalizer must not be what frees it.
        SkipWithoutH264Encoder();
        var settings = new VideoExportSettings(640, 360, new Fraction(30, 1));

        // Two exports first: Media Foundation, the encoder DLLs and the thread pools load once per process.
        for (var i = 0; i < 2; i++)
        {
            await ExportOrSkipAsync(new NumberedFrames(10, 640, 360), Path.Combine(_directory, $"warm{i}.mp4"), settings);
        }

        var (bytesBefore, handlesBefore) = Measure();
        for (var i = 0; i < Exports; i++)
        {
            await ExportOrSkipAsync(new NumberedFrames(10, 640, 360), Path.Combine(_directory, $"run{i}.mp4"), settings);
        }

        var (bytesAfter, handlesAfter) = Measure();
        var megabytes = (bytesAfter - bytesBefore) / 1048576.0;
        var handles = handlesAfter - handlesBefore;
        TestContext.Current.TestOutputHelper?.WriteLine($"{Exports} exports: private bytes {megabytes:+0.0;-0.0} MB, handles {handles:+0;-0}");
        // Measured on an RTX 4060 Ti: about 0 MB and 34 handles per export after the fix, 90 MB and 164 handles
        // before it. The 34 handles stay with the NVIDIA encoder MFT (3 with the software encoder, 2 for the
        // device alone) and a GC does not free them; the bounds leave room for that, not for a device.
        Assert.True(megabytes < 60, $"Private bytes grew by {megabytes:0.0} MB over {Exports} exports.");
        Assert.True(handles < 60 * Exports, $"The handle count grew by {handles} over {Exports} exports.");
    }

    private static (long PrivateBytes, int Handles) Measure()
    {
        using var process = Process.GetCurrentProcess();
        return (process.PrivateMemorySize64, process.HandleCount);
    }
}
