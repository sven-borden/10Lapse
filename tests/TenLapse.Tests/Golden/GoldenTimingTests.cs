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

    /// <summary>
    /// The animated GIF (#51) at the common speeds and presets: the exporter hands GIF frame k to the encoder at exactly
    /// <see cref="FrameTiming.FrameStart"/> of k at the GIF's rate, the file holds each frame with the delay of its rounded
    /// start and end (<see cref="GifTiming.Delay(long, Fraction)"/>), loops forever, and frame k shows the photo the video
    /// shows at that moment (<see cref="GifOutput.SourceFrame"/>).
    /// </summary>
    [Theory]
    [InlineData("30 fps as up to 10 fps", 30, 1, false, GifFrameRate.Fps10)]
    [InlineData("24 fps as up to 25 fps", 24, 1, false, GifFrameRate.Fps25)]
    [InlineData("29.97 fps as up to 25 fps", 30000, 1001, false, GifFrameRate.Fps25)]
    [InlineData("29.97 fps as up to 20 fps", 30000, 1001, false, GifFrameRate.Fps20)]
    [InlineData("12.5 fps as up to 20 fps", 25, 2, false, GifFrameRate.Fps20)]
    [InlineData("2 s per photo as up to 10 fps", 2, 1, true, GifFrameRate.Fps10)]
    public async Task EveryGifFrameShowsThePhotoOnScreenAtItsStartWithItsExactDelay(string speed, long numerator, long denominator, bool perPhoto, GifFrameRate preset)
    {
        SkipWithoutGraphicsDevice();
        var label = $"GIF, {speed}";
        var evidence = GoldenExport.EvidenceFolder(ClassName, Slug(label));
        var input = new Fraction(numerator, denominator);
        var timing = perPhoto ? TimingSettings.PerPhoto(input) : TimingSettings.PerSecond(input);
        var project = new Project(timing, new FrameSettings(FrameAspect.Ratio16x9, FitMode.Fill), OutputSettings.Default with { Format = VideoFormat.Gif, Gif = GifSettings.Default with { Rate = preset } }, SortMode.Manual);
        project.AddFrames((await photos.GetAsync()).Select(photo => GoldenExport.FrameOf(photo.Path, photo.Size)));
        var snapshot = project.Snapshot;
        var videoRate = snapshot.EffectiveRate.Rate;
        var gifRate = GifOutput.EffectiveRate(videoRate, preset);
        var expectedFrames = GifOutput.SourceFrames(FrameCount, videoRate, gifRate);

        var output = Path.Combine(photos.Folder, $"{Slug(label)}.gif");
        var record = new EncoderRecord();
        var clock = Stopwatch.StartNew();
        var result = await ExportOrSkipAsync(snapshot, output, options: new ProjectExportOptions
        {
            CreateEncoder = (file, settings, device) => new RecordingEncoder(VideoExporter.OpenEncoder(file, settings, device), record),
        });
        var exported = clock.Elapsed;

        var problems = new List<string>();
        Check(problems, "GIF rate", gifRate, result.Rate);
        Check(problems, "frames written", expectedFrames.Length, result.FrameCount);
        Check(problems, "encoders opened", 1, record.EncodersOpened);
        var writes = record.Writes;
        var mistimedWrites = Enumerable.Range(0, Math.Min(writes.Count, expectedFrames.Length))
            .Where(k => writes[k].Time != FrameTiming.FrameStart(k, gifRate) || writes[k].Duration != FrameTiming.FrameDuration(k, gifRate))
            .ToList();
        if (mistimedWrites.Count > 0)
        {
            problems.Add($"{mistimedWrites.Count} GIF frames were handed to the encoder at another time than FrameTiming's at the GIF rate: frames {string.Join(", ", mistimedWrites.Take(10))}");
        }

        var info = GifFileReader.Read(result.Path);
        Check(problems, "frames in the file", expectedFrames.Length, info.FrameCount);
        Check(problems, "size", "480x270", $"{info.Width}x{info.Height}");
        Check(problems, "application extension", "NETSCAPE2.0", info.Application);
        Check(problems, "loop count (0: forever)", (int?)0, info.LoopCount);
        var wrongDelays = Enumerable.Range(0, Math.Min(info.FrameCount, expectedFrames.Length)).Where(k => info.Delays[k] != GifTiming.Delay(k, gifRate)).ToList();
        if (wrongDelays.Count > 0)
        {
            problems.Add($"{wrongDelays.Count} delays differ from the rounded starts: " + string.Join("; ", wrongDelays.Take(5).Select(k => $"frame {k} has {info.Delays[k]} cs, expected {GifTiming.Delay(k, gifRate)}")));
        }

        Check(problems, "length in cs", GifTiming.Centiseconds(FrameTiming.Duration(expectedFrames.Length, gifRate)), info.TotalCentiseconds);

        var shown = new List<int>();
        GoldenImage? firstWrong = null;
        GifFileReader.ReadFrames(result.Path, frame =>
        {
            var number = FrameCode.Read(frame.Pixels, frame.Width, frame.Height, frame.Stride);
            if (frame.Index < expectedFrames.Length && number != expectedFrames[frame.Index])
            {
                firstWrong ??= GoldenImage.Copy(frame.Pixels, frame.Width, frame.Height, frame.Stride);
            }

            shown.Add(number);
        });
        var wrong = Enumerable.Range(0, Math.Min(shown.Count, expectedFrames.Length)).Where(k => shown[k] != expectedFrames[k]).ToList();
        if (wrong.Count > 0)
        {
            problems.Add($"{wrong.Count} GIF frames show another photo: " + string.Join("; ", wrong.Take(5).Select(k => $"frame {k} shows {shown[k]}, expected {expectedFrames[k]}")));
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{label}: {expectedFrames.Length} GIF frames at {gifRate.Numerator}/{gifRate.Denominator} fps, export {exported.TotalMilliseconds:0} ms, " +
            $"{new FileInfo(result.Path).Length:N0} bytes, {info.TotalCentiseconds} cs, delays {string.Join(",", info.Delays.Take(8))}…");

        if (problems.Count > 0)
        {
            Directory.CreateDirectory(evidence);
            File.Copy(result.Path, Path.Combine(evidence, "export.gif"), overwrite: true);
            if (firstWrong is not null)
            {
                await firstWrong.SavePngAsync(Path.Combine(evidence, $"frame{wrong[0]:D3}-export.png"));
            }

            Assert.Fail(GoldenExport.Report($"{label}:", problems, $"{evidence} (the GIF, and the first wrong frame)"));
        }

        File.Delete(result.Path);
    }

    /// <summary>
    /// Holds, plays and a boomerang (#49) at odd rates: the exporter hands video frame k to the encoder at exactly
    /// <see cref="FrameTiming.FrameStart"/> of k, the file holds every frame at its time, and frame k shows the photo the
    /// sequence puts there (<see cref="FrameSequence.PhotoAt"/>), which is what the preview shows at that moment.
    /// </summary>
    [Theory]
    [InlineData("29.97 fps, boomerang, 0.5 s and 1 s held", 30000, 1001, false, 1, true, 5, 10)]
    [InlineData("25 fps, twice, 1 s held last", 25, 1, false, 2, false, 0, 10)]
    [InlineData("60 fps, boomerang twice, 0.3 s held first", 60, 1, false, 2, true, 3, 0)]
    [InlineData("2 s per photo, boomerang, 0.5 s held at both ends", 2, 1, true, 1, true, 5, 5)]
    public async Task EveryVideoFrameShowsThePhotoTheSequencePutsThere(string speed, long numerator, long denominator, bool perPhoto, int plays, bool boomerang, int holdStartTenths, int holdEndTenths)
    {
        SkipWithoutH264Encoder();
        var label = $"sequence, {speed}";
        var evidence = GoldenExport.EvidenceFolder(ClassName, Slug(label));
        var input = new Fraction(numerator, denominator);
        var timing = perPhoto ? TimingSettings.PerPhoto(input) : TimingSettings.PerSecond(input);
        var sequence = new SequenceSettings(TimeSpan.FromTicks(holdStartTenths * 1_000_000L), TimeSpan.FromTicks(holdEndTenths * 1_000_000L), plays, boomerang);
        var snapshot = GoldenExport.Snapshot((await photos.GetAsync()).Select(photo => GoldenExport.FrameOf(photo.Path, photo.Size)), timing, FrameAspect.Ratio16x9, FitMode.Fill, sequence: sequence);
        var rate = snapshot.EffectiveRate.Rate;
        var video = snapshot.Playback;
        var length = video.Length;

        var output = Path.Combine(photos.Folder, $"{Slug(label)}.mp4");
        var (result, record) = await GoldenExport.ExportSmallOrSkipAsync(snapshot, output);

        var problems = new List<string>();
        var info = VideoFileReader.Read(result.Path);
        Check(problems, "frames written", length, result.FrameCount);
        Check(problems, "frames in the file", length, info.FrameCount);
        Check(problems, "clip duration (ticks)", snapshot.Duration, result.Duration);
        Check(problems, "frames handed to the encoder", length, record.Writes.Count);
        var mistimedWrites = Enumerable.Range(0, Math.Min(length, record.Writes.Count))
            .Where(i => record.Writes[i].Time != FrameTiming.FrameStart(i, rate) || record.Writes[i].Duration != FrameTiming.FrameDuration(i, rate))
            .ToList();
        if (mistimedWrites.Count > 0)
        {
            problems.Add($"{mistimedWrites.Count} frames were handed to the encoder at another time than FrameTiming's: frames {string.Join(", ", mistimedWrites.Take(10))}");
        }

        var (earliest, latest) = GoldenExport.SampleTimeBound(rate);
        var off = Enumerable.Range(0, Math.Min(length, info.FrameCount))
            .Where(i => info.SampleTimes[i] - FrameTiming.FrameStart(i, rate) < earliest || info.SampleTimes[i] - FrameTiming.FrameStart(i, rate) > latest)
            .ToList();
        if (off.Count > 0)
        {
            problems.Add($"{off.Count} sample times are off FrameTiming.FrameStart: frames {string.Join(", ", off.Take(10))}");
        }

        var unit = GoldenExport.SinkTimescaleUnit(rate);
        Check(problems, $"header duration (ticks; at most {unit} short)", snapshot.Duration, info.Duration, ok: info.Duration <= snapshot.Duration && snapshot.Duration - info.Duration <= unit);

        var wrong = new List<(int Index, int Number)>();
        GoldenImage? firstWrong = null;
        VideoFileReader.ReadFrames(result.Path, frame =>
        {
            var number = FrameCode.Read(frame.Pixels, frame.Width, frame.Height, frame.Stride);
            if (frame.Index >= length || number != video.PhotoAt(frame.Index))
            {
                wrong.Add((frame.Index, number));
                firstWrong ??= GoldenImage.Copy(frame.Pixels, frame.Width, frame.Height, frame.Stride);
            }
        });
        if (wrong.Count > 0)
        {
            problems.Add($"{wrong.Count} decoded frames show another photo: " + string.Join("; ", wrong.Take(5).Select(w => $"frame {w.Index} shows {w.Number}, expected {(w.Index < length ? video.PhotoAt(w.Index) : -1)}")));
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{label}: {length} frames ({video.HoldStartFrames} + {video.CoreLength} + {video.HoldEndFrames}) at {rate.Numerator}/{rate.Denominator} fps, " +
            $"{snapshot.Duration} ticks expected, header {info.Duration}, encoder {result.Encoder?.Name ?? "unknown"}");

        if (problems.Count > 0)
        {
            Directory.CreateDirectory(evidence);
            File.Copy(result.Path, Path.Combine(evidence, "export.mp4"), overwrite: true);
            if (firstWrong is not null)
            {
                await firstWrong.SavePngAsync(Path.Combine(evidence, $"frame{wrong[0].Index:D4}-export.png"));
            }

            Assert.Fail(GoldenExport.Report($"{label}:", problems, $"{evidence} (the video, and the first wrong frame)"));
        }

        File.Delete(result.Path);
    }

    /// <summary>
    /// A held, boomeranged GIF (#49): one play (a GIF loops by itself), each GIF frame showing the photo the video's
    /// sequence shows at that GIF frame's start, with the delays of the rounded starts, as long as the sequence.
    /// </summary>
    [Theory]
    [InlineData("30 fps as up to 10 fps", 30, 1, GifFrameRate.Fps10)]
    [InlineData("29.97 fps as up to 25 fps", 30000, 1001, GifFrameRate.Fps25)]
    public async Task EveryGifFrameShowsThePhotoTheSequenceShowsAtItsStart(string speed, long numerator, long denominator, GifFrameRate preset)
    {
        SkipWithoutGraphicsDevice();
        var label = $"GIF sequence, {speed}";
        var evidence = GoldenExport.EvidenceFolder(ClassName, Slug(label));
        var sequence = new SequenceSettings(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.5), 3, boomerang: true);
        var project = new Project(
            TimingSettings.PerSecond(new Fraction(numerator, denominator)),
            new FrameSettings(FrameAspect.Ratio16x9, FitMode.Fill),
            OutputSettings.Default with { Format = VideoFormat.Gif, Gif = GifSettings.Default with { Rate = preset } },
            SortMode.Manual,
            sequence);
        project.AddFrames((await photos.GetAsync()).Select(photo => GoldenExport.FrameOf(photo.Path, photo.Size)));
        var snapshot = project.Snapshot;
        var videoRate = snapshot.EffectiveRate.Rate;
        var gifRate = GifOutput.EffectiveRate(videoRate, preset);
        var video = snapshot.Playback;
        Assert.Equal(1, video.Plays);
        var expected = GifOutput.SourceFrames(video.Length, videoRate, gifRate).Select(video.PhotoAt).ToArray();

        var output = Path.Combine(photos.Folder, $"{Slug(label)}.gif");
        var record = new EncoderRecord();
        var result = await ExportOrSkipAsync(snapshot, output, options: new ProjectExportOptions
        {
            CreateEncoder = (file, settings, device) => new RecordingEncoder(VideoExporter.OpenEncoder(file, settings, device), record),
        });

        var problems = new List<string>();
        Check(problems, "frames written", expected.Length, result.FrameCount);
        var mistimed = Enumerable.Range(0, Math.Min(record.Writes.Count, expected.Length))
            .Where(k => record.Writes[k].Time != FrameTiming.FrameStart(k, gifRate) || record.Writes[k].Duration != FrameTiming.FrameDuration(k, gifRate))
            .ToList();
        if (mistimed.Count > 0)
        {
            problems.Add($"{mistimed.Count} GIF frames were handed to the encoder at another time than FrameTiming's: frames {string.Join(", ", mistimed.Take(10))}");
        }

        var info = GifFileReader.Read(result.Path);
        Check(problems, "frames in the file", expected.Length, info.FrameCount);
        Check(problems, "loop count (0: forever)", (int?)0, info.LoopCount);
        var wrongDelays = Enumerable.Range(0, Math.Min(info.FrameCount, expected.Length)).Where(k => info.Delays[k] != GifTiming.Delay(k, gifRate)).ToList();
        if (wrongDelays.Count > 0)
        {
            problems.Add($"{wrongDelays.Count} delays differ from the rounded starts: frames {string.Join(", ", wrongDelays.Take(10))}");
        }

        // As long as the video's sequence, to within the GIF's last frame (which ends on its own grid).
        Check(problems, "length in cs", GifTiming.Centiseconds(FrameTiming.Duration(expected.Length, gifRate)), info.TotalCentiseconds);
        var over = FrameTiming.Duration(expected.Length, gifRate) - snapshot.Duration;
        Check(problems, "GIF length past the video's (ticks, 0 to one GIF frame)", 0L, over, ok: over >= 0 && over <= FrameTiming.FrameDuration(0, gifRate) + 1);

        var shown = new List<int>();
        GifFileReader.ReadFrames(result.Path, frame => shown.Add(FrameCode.Read(frame.Pixels, frame.Width, frame.Height, frame.Stride)));
        var wrong = Enumerable.Range(0, Math.Min(shown.Count, expected.Length)).Where(k => shown[k] != expected[k]).ToList();
        if (wrong.Count > 0)
        {
            problems.Add($"{wrong.Count} GIF frames show another photo: " + string.Join("; ", wrong.Take(5).Select(k => $"frame {k} shows {shown[k]}, expected {expected[k]}")));
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{label}: {expected.Length} GIF frames for {video.Length} video frames, {info.TotalCentiseconds} cs, video {snapshot.Duration / 100_000} cs");

        if (problems.Count > 0)
        {
            Directory.CreateDirectory(evidence);
            File.Copy(result.Path, Path.Combine(evidence, "export.gif"), overwrite: true);
            Assert.Fail(GoldenExport.Report($"{label}:", problems, $"{evidence} (the GIF)"));
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
