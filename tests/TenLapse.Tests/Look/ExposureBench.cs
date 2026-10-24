using System.Diagnostics;
using TenLapse.Core.Look;
using TenLapse.Core.Projects;
using TenLapse.Media.Look;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Look;

/// <summary>
/// The cost of deflicker's exposure pass (#45), for the PR and for #41's matrix machines; not a check. Set
/// <c>TENLAPSE_LOOK_BENCH</c> to a folder of photos (for example the bench's 24 MP set, <c>scripts/bench.ps1 -Keep</c>)
/// and run this class: it measures every photo as the app does (a quarter of the cores, one to three at once) and prints
/// the time per photo, the CPU time and the process's peak working set. Without the variable it is skipped.
/// </summary>
public sealed class ExposureBench
{
    [Fact]
    public async Task MeasuresAFolderAsTheAppDoes()
    {
        var folder = Environment.GetEnvironmentVariable("TENLAPSE_LOOK_BENCH");
        Assert.SkipWhen(string.IsNullOrEmpty(folder) || !Directory.Exists(folder), "Set TENLAPSE_LOOK_BENCH to a folder of photos to measure the exposure pass.");

        var files = Directory.GetFiles(folder!, "*.jpg").Order(StringComparer.OrdinalIgnoreCase).ToList();
        var frames = files.Select(path => new Frame(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path))).ToList();
        var meter = new ExposureMeter();
        var parallelism = Math.Clamp(Environment.ProcessorCount / 4, 1, 3);
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var peak = 0L;
        var failed = 0;
        var clock = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            frames,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = TestContext.Current.CancellationToken },
            async (frame, token) =>
            {
                var result = await meter.MeasureAsync(frame, token);
                if (result.Exposure is null)
                {
                    Interlocked.Increment(ref failed);
                }

                process.Refresh();
                var now = process.WorkingSet64;
                long seen;
                while (now > (seen = Interlocked.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen)
                {
                }
            });
        clock.Stop();
        process.Refresh();
        var cpu = process.TotalProcessorTime - cpuBefore;

        // And the curve for 5,000 photos, as the preview computes it once per snapshot.
        var many = TestFrames.Folder("bench", 5000).Select((frame, i) => frame with { Exposure = new PhotoExposure(-3 + Math.Sin(i * 0.37)) }).ToList();
        var curveClock = Stopwatch.StartNew();
        _ = ExposureCurve.Compute(many, new LookSettings(true, 100));
        curveClock.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{frames.Count} photos in {clock.Elapsed.TotalSeconds:0.0} s with {parallelism} at once: {clock.Elapsed.TotalMilliseconds / frames.Count:0.0} ms a photo wall clock, " +
            $"{cpu.TotalMilliseconds / frames.Count:0.0} ms CPU a photo; {failed} failed; peak working set {peak / (1024.0 * 1024):0} MB; " +
            $"curve of 5,000 photos {curveClock.Elapsed.TotalMilliseconds:0.0} ms.");
    }
}
