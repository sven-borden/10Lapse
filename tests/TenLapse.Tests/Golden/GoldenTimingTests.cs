using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Inspection;
using TenLapse.Tests.Media;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Golden;

/// <summary>
/// The golden render test for timing (#32): 120 photos, each with its number drawn in as a <see cref="FrameCode"/>,
/// exported as a project at every common speed and read back with Media Foundation's source reader. Every frame
/// must be there, at its exact time (<see cref="FrameTiming.FrameStart"/>, within the MP4 timescale's documented
/// rounding and no worse), in order, with the right total duration.
/// </summary>
[Collection(nameof(RealEncoderCollection))]
public sealed class GoldenTimingTests(GoldenTimingTests.NumberedPhotos photos) : IClassFixture<GoldenTimingTests.NumberedPhotos>
{
    private const int FrameCount = 120;

    [Theory]
    [InlineData("24 fps", 24, 1, false, VideoFormat.Mp4H264)]
    [InlineData("25 fps", 25, 1, false, VideoFormat.Mp4H264)]
    [InlineData("29.97 fps", 30000, 1001, false, VideoFormat.Mp4H264)]
    [InlineData("30 fps", 30, 1, false, VideoFormat.Mp4H264)]
    [InlineData("60 fps", 60, 1, false, VideoFormat.Mp4H264)]
    [InlineData("0.5 s per photo", 1, 2, true, VideoFormat.Mp4H264)]
    [InlineData("2 s per photo", 2, 1, true, VideoFormat.Mp4H264)]
    [InlineData("2 s per photo", 2, 1, true, VideoFormat.Mp4Hevc)]
    public async Task EveryFrameIsThereAtItsExactTimeAndShowsItsNumber(string speed, long numerator, long denominator, bool perPhoto, VideoFormat format)
    {
        SkipWithoutH264Encoder();
        if (format == VideoFormat.Mp4Hevc)
        {
            GoldenExport.SkipWithoutHevc();
        }

        var label = $"{speed}, {format}";
        var evidence = GoldenExport.EvidenceFolder(Slug(label));
        var input = new Fraction(numerator, denominator);
        var timing = perPhoto ? TimingSettings.PerPhoto(input) : TimingSettings.PerSecond(input);
        var snapshot = GoldenExport.Snapshot(await photos.GetAsync(), timing, FrameAspect.Ratio16x9, FitMode.Fill, format);
        var rate = snapshot.EffectiveRate.Rate;
        Assert.Equal(perPhoto ? input.Reciprocal() : input, rate);

        var output = Path.Combine(photos.Folder, $"{Slug(label)}.mp4");
        var clock = Stopwatch.StartNew();
        var result = await GoldenExport.ExportOrSkipAsync(snapshot, output);
        var exported = clock.Elapsed;

        var problems = new List<string>();
        var size = GoldenExport.SmallSize(snapshot.VideoSize);
        var info = VideoFileReader.Read(result.Path);
        Check(problems, "frames written", FrameCount, result.FrameCount);
        Check(problems, "frames in the file", FrameCount, info.FrameCount);
        Check(problems, "frame size", $"{size.Width}x{size.Height}", $"{info.Width}x{info.Height}");
        if (!perPhoto)
        {
            // The source reader names the broadcast rates exactly (24, 25, 30, 60, 30000/1001); below 1 fps it reports the nominal rate.
            Check(problems, "declared frame rate", rate, info.FrameRate);
        }

        var unit = GoldenExport.TimescaleUnit(rate);
        var exact = GoldenExport.HasExactTimescale(rate);
        var bound = exact ? "exactly" : $"0 to {unit} ticks late";
        var late = Enumerable.Range(0, Math.Min(FrameCount, info.FrameCount))
            .Select(i => (Index: i, Expected: FrameTiming.FrameStart(i, rate), Actual: info.SampleTimes[i]))
            .Where(sample => exact ? sample.Actual != sample.Expected : sample.Actual < sample.Expected || sample.Actual - sample.Expected > unit)
            .ToList();
        if (late.Count > 0)
        {
            problems.Add(
                $"{late.Count} sample times are off FrameTiming.FrameStart (expected {bound}): " +
                string.Join("; ", late.Take(5).Select(s => $"frame {s.Index} expected {s.Expected}, got {s.Actual} ({s.Actual - s.Expected:+0;-0} ticks)")));
        }

        var durations = Enumerable.Range(0, Math.Min(FrameCount, info.FrameCount))
            .Where(i => info.SampleDurations[i] >= 0 && Math.Abs(info.SampleDurations[i] - FrameTiming.FrameDuration(i, rate)) > unit)
            .ToList();
        if (durations.Count > 0)
        {
            problems.Add(
                $"{durations.Count} sample durations are more than {unit} ticks off FrameTiming.FrameDuration: " +
                string.Join("; ", durations.Take(5).Select(i => $"frame {i} expected {FrameTiming.FrameDuration(i, rate)}, got {info.SampleDurations[i]}")));
        }

        // The container duration is rounded down to the timescale (PR #65).
        var duration = FrameTiming.Duration(FrameCount, rate);
        Check(problems, $"total duration (ticks; at most {unit} short)", duration, info.Duration, ok: info.Duration <= duration && duration - info.Duration <= unit);
        Check(problems, "export result duration", duration, result.Duration);

        // Decoded: frame n shows number n, at sample n's time.
        var decoded = 0;
        var wrong = new List<(int Index, int Number)>();
        var mistimed = new List<(int Index, long Time)>();
        GoldenImage? firstWrong = null;
        VideoFileReader.ReadFrames(result.Path, frame =>
        {
            decoded++;
            var number = FrameCode.Read(frame.Pixels, frame.Width, frame.Height, frame.Stride);
            if (number != frame.Index)
            {
                wrong.Add((frame.Index, number));
                firstWrong ??= GoldenImage.Copy(frame.Pixels, frame.Width, frame.Height, frame.Stride);
            }

            if (frame.Index >= info.SampleTimes.Count || frame.Time != info.SampleTimes[frame.Index])
            {
                mistimed.Add((frame.Index, frame.Time));
            }
        });
        Check(problems, "frames decoded", FrameCount, decoded);
        if (wrong.Count > 0)
        {
            problems.Add($"{wrong.Count} decoded frames show another number: " + string.Join("; ", wrong.Take(5).Select(w => $"frame {w.Index} shows {w.Number}")));
        }

        if (mistimed.Count > 0)
        {
            problems.Add($"{mistimed.Count} decoded frames come at another time than their sample: " + string.Join("; ", mistimed.Take(5).Select(m => $"frame {m.Index} at {m.Time}")));
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{label}: export {exported.TotalMilliseconds:0} ms, read back {(clock.Elapsed - exported).TotalMilliseconds:0} ms, encoder {result.Encoder?.Name ?? "unknown"}, worst lateness {info.SampleTimes.Select((t, i) => t - FrameTiming.FrameStart(i, rate)).DefaultIfEmpty().Max()} ticks, duration {info.Duration} of {duration}");

        if (problems.Count > 0)
        {
            Directory.CreateDirectory(evidence);
            File.Copy(result.Path, Path.Combine(evidence, "export.mp4"));
            if (firstWrong is not null)
            {
                // The decoded frame and the photo it should show.
                var index = wrong[0].Index;
                await firstWrong.SavePngAsync(Path.Combine(evidence, $"frame{index:D3}-export.png"));
                File.Copy(photos.PathOf(index), Path.Combine(evidence, $"frame{index:D3}-expected.png"));
            }

            Assert.Fail(GoldenExport.Report($"{label} ({rate.Numerator}/{rate.Denominator} fps, encoder {result.Encoder?.Name ?? "unknown"}):", problems, $"{evidence} (the video, and the first wrong frame with the photo it should show)"));
        }

        File.Delete(result.Path);
    }

    private static void Check<T>(List<string> problems, string what, T expected, T actual, bool? ok = null)
    {
        if (!(ok ?? EqualityComparer<T>.Default.Equals(expected, actual)))
        {
            problems.Add($"{what}: expected {expected}, got {actual}");
        }
    }

    private static string Slug(string label) =>
        string.Concat(label.Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '-'));

    /// <summary>
    /// The 120 photos, written once per class: 16:9 at the export's small size, a colour that changes with the
    /// index, and the index as a <see cref="FrameCode"/> drawn by Win2D, saved as PNG.
    /// </summary>
    public sealed class NumberedPhotos : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private IReadOnlyList<(string Path, PixelSize Size)>? _photos;

        public string Folder { get; } = GoldenExport.NewScratchFolder("timing");

        public string PathOf(int index) => System.IO.Path.Combine(Folder, $"photo{index:D3}.png");

        /// <summary>Writes the photos on first use, after the test has checked for a graphics device.</summary>
        public async Task<IReadOnlyList<(string Path, PixelSize Size)>> GetAsync()
        {
            await _gate.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                return _photos ??= await WriteAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            _gate.Dispose();
            GoldenExport.DeleteFolder(Folder);
        }

        private async Task<IReadOnlyList<(string Path, PixelSize Size)>> WriteAsync()
        {
            var size = GoldenExport.SmallSize(OutputSize.For(OutputResolution.Hd720, FrameAspect.Ratio16x9));
            using var device = new CanvasDevice();
            var photos = new List<(string Path, PixelSize Size)>();
            for (var index = 0; index < FrameCount; index++)
            {
                byte[] pixels;
                using (var target = new CanvasRenderTarget(device, size.Width, size.Height, 96f))
                {
                    using (var session = target.CreateDrawingSession())
                    {
                        session.Clear(Color.FromArgb(255, (byte)(index * 2), 96, (byte)(255 - (index * 2))));
                        FrameCode.Draw(session, index, size.Width, size.Height);
                    }

                    pixels = target.GetPixelBytes();
                }

                await new GoldenImage(pixels, size.Width, size.Height).SavePngAsync(PathOf(index));
                photos.Add((PathOf(index), size));
            }

            return photos;
        }
    }
}
