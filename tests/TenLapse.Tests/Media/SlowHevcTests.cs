using System.Diagnostics;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>
/// HEVC below 1 fps on the HEVC Video Extensions' software encoder, which killed the process with an access
/// violation when its media type said less than 1 fps. The export runs in a child process (this test host again,
/// filtered to this one test), so a crash fails the test instead of taking the whole run down.
/// </summary>
public sealed class SlowHevcTests
{
    private const string ChildVariable = "TENLAPSE_SLOW_HEVC_CHILD";

    [Fact]
    public async Task TheSoftwareHevcEncoderWritesRatesBelowOneFpsWithExactTimes()
    {
        if (Environment.GetEnvironmentVariable(ChildVariable) is { } directory)
        {
            await ExportInThisProcessAsync(directory);
            return;
        }

        SkipWithoutGraphicsDevice();
        Assert.SkipUnless(
            EncoderCatalog.Find(VideoFormat.Mp4Hevc).Any(encoder => !encoder.IsHardware),
            "No software HEVC encoder is installed (the HEVC Video Extensions).");

        var folder = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // The test host is its own executable (xUnit v3); run as "dotnet TenLapse.Tests.dll", it needs the dll.
            var host = Environment.ProcessPath!;
            var start = new ProcessStartInfo(host)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                start.ArgumentList.Add(typeof(SlowHevcTests).Assembly.Location);
            }

            start.ArgumentList.Add("-method");
            start.ArgumentList.Add($"{typeof(SlowHevcTests).FullName}.{nameof(TheSoftwareHevcEncoderWritesRatesBelowOneFpsWithExactTimes)}");
            start.ArgumentList.Add("-noColor");
            start.Environment[ChildVariable] = folder;

            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = child.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                await child.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                child.Kill(entireProcessTree: true);
                throw;
            }

            var log = $"{await output}{await error}";
            Assert.True(child.ExitCode == 0, $"The child export exited with 0x{child.ExitCode:X8}:{Environment.NewLine}{Tail(log)}");
            Assert.Contains("Total: 1, Errors: 0, Failed: 0, Skipped: 0", log, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file still being closed; Windows cleans %TEMP% eventually.
            }
        }
    }

    [Fact]
    public void OnlyHevcBelowOneFpsGetsANominalRate()
    {
        Assert.Equal(new Fraction(1, 1), SinkWriterEncoder.NominalFrameRate(VideoFormat.Mp4Hevc, new Fraction(1, 10)));
        Assert.Equal(new Fraction(6, 5), SinkWriterEncoder.NominalFrameRate(VideoFormat.Mp4Hevc, new Fraction(3, 10)));
        Assert.Equal(new Fraction(999, 500), SinkWriterEncoder.NominalFrameRate(VideoFormat.Mp4Hevc, new Fraction(999, 1000)));
        Assert.Equal(new Fraction(1, 1), SinkWriterEncoder.NominalFrameRate(VideoFormat.Mp4Hevc, new Fraction(1, 9999)));
        Assert.Equal(new Fraction(1, 1), SinkWriterEncoder.NominalFrameRate(VideoFormat.Mp4Hevc, new Fraction(1, 1)));
        Assert.Equal(new Fraction(3, 2), SinkWriterEncoder.NominalFrameRate(VideoFormat.Mp4Hevc, new Fraction(3, 2)));
        Assert.Equal(new Fraction(1, 10), SinkWriterEncoder.NominalFrameRate(VideoFormat.Mp4H264, new Fraction(1, 10)));

        // Each frame keeps its bits: ten times the rate, ten times the bitrate.
        Assert.Equal(10_000_000u, SinkWriterEncoder.NominalBitrate(1_000_000, new Fraction(1, 10), new Fraction(1, 1)));
        Assert.Equal(8_000_000u, SinkWriterEncoder.NominalBitrate(8_000_000, new Fraction(30, 1), new Fraction(30, 1)));
    }

    // In the child: the rates the crash was seen at, on the software encoder only (hardware transforms off).
    private static async Task ExportInThisProcessAsync(string directory)
    {
        foreach (var rate in new[] { new Fraction(1, 2), new Fraction(1, 5), new Fraction(1, 10), new Fraction(3, 10), new Fraction(1000, 1001) })
        {
            var path = Path.Combine(directory, $"hevc-{rate.Numerator}-{rate.Denominator}.mp4");
            var settings = new VideoExportSettings(320, 180, rate) { Format = VideoFormat.Mp4Hevc, HardwareAcceleration = false };

            var result = await VideoExporter.ExportAsync(
                new SolidFrames(4, 320, 180, Color.FromArgb(255, 40, 120, 200)), path, settings, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.Encoder?.IsHardware ?? false, $"{result.Encoder?.Name} is not the software encoder.");
            var info = VideoFileReader.Read(path);
            Assert.Equal("HEVC", info.Codec);
            Assert.Equal(4, info.FrameCount);

            // The MP4 sink's timescale is floor(nominal rate x 1000) units a second, as it is floor(rate x 1000) at
            // every other rate: when that is whole (1/2, 1/5, 1/10, 3/10 here) every time is exact; otherwise
            // (1000/1001) each lands less than one unit after its start, as StreamingExportTests checks for H.264.
            var nominal = SinkWriterEncoder.NominalFrameRate(VideoFormat.Mp4Hevc, rate);
            var timescale = (long)nominal.Numerator * 1000 / nominal.Denominator;
            var unit = (FrameTiming.TicksPerSecond + timescale - 1) / timescale;
            if ((long)nominal.Numerator * 1000 % nominal.Denominator == 0)
            {
                Assert.Equal(Enumerable.Range(0, 4).Select(i => FrameTiming.FrameStart(i, rate)), info.SampleTimes);
            }
            else
            {
                for (var i = 0; i < 4; i++)
                {
                    Assert.InRange(info.SampleTimes[i] - FrameTiming.FrameStart(i, rate), 0, unit);
                }
            }

            // Durations are whole timescale units too: within one unit of ours.
            for (var i = 0; i < 4; i++)
            {
                Assert.InRange(info.SampleDurations[i], FrameTiming.FrameDuration(i, rate) - unit, FrameTiming.FrameDuration(i, rate) + unit);
            }

            Assert.InRange(info.Duration, FrameTiming.Duration(4, rate) - unit, FrameTiming.Duration(4, rate) + unit);
        }
    }

    private static string Tail(string log)
    {
        var lines = log.Split('\n');
        return string.Join('\n', lines[Math.Max(0, lines.Length - 30)..]);
    }
}
