using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using TenLapse.Tests.Media;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Golden;

/// <summary>
/// The golden render test for timing (#32): 120 photos, each with its number drawn in as a <see cref="FrameCode"/>,
/// exported as a project at every common speed. The exporter must hand every frame to the encoder at exactly
/// <see cref="FrameTiming.FrameStart"/> for exactly <see cref="FrameTiming.FrameDuration"/> (our arithmetic, recorded
/// by <see cref="RecordingEncoder"/>), and the file, read back with Media Foundation's source reader, must hold every
/// frame in order, at its time within the MP4 timescale's documented rounding and no worse.
/// </summary>
[Collection(nameof(RealEncoderCollection))]
public sealed class GoldenTimingTests(GoldenTimingTests.NumberedPhotos photos) : IClassFixture<GoldenTimingTests.NumberedPhotos>
{
    private const string ClassName = "timing";
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
        var evidence = GoldenExport.EvidenceFolder(ClassName, Slug(label));
        var input = new Fraction(numerator, denominator);
        var timing = perPhoto ? TimingSettings.PerPhoto(input) : TimingSettings.PerSecond(input);
        var snapshot = GoldenExport.Snapshot((await photos.GetAsync()).Select(photo => GoldenExport.FrameOf(photo.Path, photo.Size)), timing, FrameAspect.Ratio16x9, FitMode.Fill, format);
        var rate = snapshot.EffectiveRate.Rate;
        Assert.Equal(perPhoto ? input.Reciprocal() : input, rate);

        var output = Path.Combine(photos.Folder, $"{Slug(label)}.mp4");
        var clock = Stopwatch.StartNew();
        var (result, record) = await GoldenExport.ExportSmallOrSkipAsync(snapshot, output);
        var exported = clock.Elapsed;

        var problems = new List<string>();
        var size = GoldenExport.SmallSize(snapshot.VideoSize);
        var info = VideoFileReader.Read(result.Path);
        var nominal = SinkWriterEncoder.NominalFrameRate(rate);
        Check(problems, "frames written", FrameCount, result.FrameCount);
        Check(problems, "frames in the file", FrameCount, info.FrameCount);
        Check(problems, "frame size", $"{size.Width}x{size.Height}", $"{info.Width}x{info.Height}");

        // The declared rate is the rate itself at 1 fps and above. Below, the encoder is given a nominal rate, a whole
        // multiple of it (#22), and the file declares one or the other: measured with NVENC at 0.5 fps, the real rate
        // in H.264 and the nominal one in HEVC.
        Check(
            problems,
            nominal == rate ? "declared frame rate" : $"declared frame rate (or the nominal {nominal.Numerator}/{nominal.Denominator})",
            (Fraction?)rate,
            info.FrameRate,
            ok: info.FrameRate == rate || info.FrameRate == nominal);

        // Our arithmetic: what the exporter handed the encoder, exactly FrameTiming's, from each frame's index.
        CheckWrites(problems, record, rate);

        // Media Foundation's: the sample times on the sink's timescale grid.
        var (earliest, latest) = GoldenExport.SampleTimeBound(rate);
        var count = Math.Min(FrameCount, info.FrameCount);
        var off = Enumerable.Range(0, count)
            .Select(i => (Index: i, Expected: FrameTiming.FrameStart(i, rate), Actual: info.SampleTimes[i]))
            .Where(sample => sample.Actual - sample.Expected < earliest || sample.Actual - sample.Expected > latest)
            .ToList();
        if (off.Count > 0)
        {
            problems.Add(
                $"{off.Count} sample times are off FrameTiming.FrameStart (expected {earliest:+0;-0;0} to {latest:+0;-0;0} ticks): " +
                string.Join("; ", off.Take(5).Select(s => $"frame {s.Index} expected {s.Expected}, got {s.Actual} ({s.Actual - s.Expected:+0;-0} ticks)")));
        }

        // Each sample's duration: the sink stores whole units, so within one tick of ours when the timescale is exact.
        var unit = GoldenExport.SinkTimescaleUnit(rate);
        var tolerance = GoldenExport.HasExactTimescale(rate) ? 1 : unit;
        var missing = Enumerable.Range(0, count).Where(i => info.SampleDurations[i] < 0).ToList();
        if (missing.Count > 0)
        {
            problems.Add($"{missing.Count} samples have no duration in the file: frames {string.Join(", ", missing.Take(10))}");
        }

        var durations = Enumerable.Range(0, count)
            .Where(i => info.SampleDurations[i] >= 0 && Math.Abs(info.SampleDurations[i] - FrameTiming.FrameDuration(i, rate)) > tolerance)
            .ToList();
        if (durations.Count > 0)
        {
            problems.Add(
                $"{durations.Count} sample durations are more than {tolerance} ticks off FrameTiming.FrameDuration: " +
                string.Join("; ", durations.Take(5).Select(i => $"frame {i} expected {FrameTiming.FrameDuration(i, rate)}, got {info.SampleDurations[i]}")));
        }

        // The header durations (mvhd, tkhd, mdhd) come from the MP4 sink, not from us: it writes them one unit short
        // of its own sample table at 24 and 60 fps (119,999 of 120,000 units, with the same headers from the software
        // encoder, and whatever last-sample duration we pass), exactly at 25, 30, 2 and 0.5 fps; at 29.97 fps the true
        // length is not a whole number of units. Playback is unaffected; only Explorer's Length (which truncates) shows
        // it, a second short on a whole-second clip. Accepted (#32 review); asserted so a change either way is noticed.
        var duration = FrameTiming.Duration(FrameCount, rate);
        Check(problems, $"header duration (ticks; at most {unit}, one timescale unit, short)", duration, info.Duration, ok: info.Duration <= duration && duration - info.Duration <= unit);

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
            $"{label}: export {exported.TotalMilliseconds:0} ms, read back {(clock.Elapsed - exported).TotalMilliseconds:0} ms, encoder {result.Encoder?.Name ?? "unknown"}, " +
            $"sample offsets {info.SampleTimes.Select((t, i) => t - FrameTiming.FrameStart(i, rate)).DefaultIfEmpty().Min()} to {info.SampleTimes.Select((t, i) => t - FrameTiming.FrameStart(i, rate)).DefaultIfEmpty().Max()} ticks, " +
            $"header duration {info.Duration} of {duration}");

        if (problems.Count > 0)
        {
            Directory.CreateDirectory(evidence);
            File.Copy(result.Path, Path.Combine(evidence, "export.mp4"), overwrite: true);
            if (firstWrong is not null)
            {
                // The decoded frame and the photo it should show.
                var index = wrong[0].Index;
                await firstWrong.SavePngAsync(Path.Combine(evidence, $"frame{index:D3}-export.png"));
                File.Copy(photos.PathOf(index), Path.Combine(evidence, $"frame{index:D3}-expected.png"), overwrite: true);
            }

            Assert.Fail(GoldenExport.Report($"{label} ({rate.Numerator}/{rate.Denominator} fps, encoder {result.Encoder?.Name ?? "unknown"}):", problems, $"{evidence} (the video, and the first wrong frame with the photo it should show)"));
        }

        File.Delete(result.Path);
    }

    // One encoder, FrameCount writes, each at exactly FrameStart(i) for exactly FrameDuration(i).
    private static void CheckWrites(List<string> problems, EncoderRecord record, Fraction rate)
    {
        Check(problems, "encoders opened", 1, record.EncodersOpened);
        var writes = record.Writes;
        Check(problems, "frames handed to the encoder", FrameCount, writes.Count);
        var off = Enumerable.Range(0, Math.Min(FrameCount, writes.Count))
            .Where(i => writes[i].Time != FrameTiming.FrameStart(i, rate) || writes[i].Duration != FrameTiming.FrameDuration(i, rate))
            .ToList();
        if (off.Count > 0)
        {
            problems.Add(
                $"{off.Count} frames were handed to the encoder at another time or duration than FrameTiming's (expected exactly): " +
                string.Join("; ", off.Take(5).Select(i =>
                    $"frame {i} at {writes[i].Time} for {writes[i].Duration}, expected {FrameTiming.FrameStart(i, rate)} for {FrameTiming.FrameDuration(i, rate)} ({writes[i].Time - FrameTiming.FrameStart(i, rate):+0;-0;0} ticks)")));
        }
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

        public string Folder { get; } = GoldenExport.StartClass(ClassName);

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
            GoldenExport.EndClass(Folder);
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
