using System.Diagnostics;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using TenLapse.Tests.Golden;
using Windows.Win32.Graphics.Imaging;

namespace TenLapse.Tests.Media;

/// <summary>
/// The animated GIF encode stage (#51) through the real pipeline and the WIC GIF encoder, read back with the WIC GIF
/// decoder: frame count, delays, the loop extension, the size, and each frame's own picture.
/// </summary>
[Collection(nameof(RealEncoderCollection))]
public sealed class GifExportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "10lapse-gif-tests", Guid.NewGuid().ToString("N"));

    public GifExportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by a failed run: the temp folder's own clean-up has it.
        }
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(25, 2)]
    [InlineData(15, 1)]
    [InlineData(24, 1)]
    [InlineData(30000, 1001)]
    public async Task EveryFrameIsWrittenWithItsDelayAndTheGifLoopsForever(long numerator, long denominator)
    {
        var rate = new Fraction(numerator, denominator);
        const int count = 37;
        var path = Path.Combine(_folder, $"numbered-{numerator}-{denominator}.gif");
        var settings = new VideoExportSettings(160, 90, rate) { Format = VideoFormat.Gif };

        var result = await MediaSkips.ExportOrSkipAsync(new NumberedFrames(count, 160, 90), path, settings);

        Assert.Equal(count, result.FrameCount);
        Assert.Equal(GifEncoder.Info, result.Encoder);
        var info = GifFileReader.Read(path);
        Assert.Equal((160, 90), (info.Width, info.Height));
        Assert.Equal(count, info.FrameCount);
        Assert.Equal("NETSCAPE2.0", info.Application);
        Assert.Equal(0, info.LoopCount);
        Assert.All(info.FrameRects, rect => Assert.Equal((0, 0, 160, 90), rect));
        Assert.Equal(Enumerable.Range(0, count).Select(i => GifTiming.Delay(i, rate)), info.Delays);

        // The whole length is the last frame's end rounded once: no drift.
        Assert.Equal(GifTiming.Centiseconds(FrameTiming.Duration(count, rate)), info.TotalCentiseconds);

        // Each frame shows its own number after the palette and the dithering.
        var shown = new List<int>();
        GifFileReader.ReadFrames(path, frame => shown.Add(FrameCode.Read(frame.Pixels, frame.Width, frame.Height, frame.Stride)));
        Assert.Equal(Enumerable.Range(0, count), shown);
    }

    [Fact]
    public async Task AFrameBelowOneFpsKeepsItsWholeDelay()
    {
        // 10 s per photo, the slowest the Timing panel allows: 1,000 cs a frame, well inside GIF's 16 bits.
        var rate = new Fraction(1, 10);
        var path = Path.Combine(_folder, "slow.gif");
        var settings = new VideoExportSettings(64, 36, rate) { Format = VideoFormat.Gif };

        await MediaSkips.ExportOrSkipAsync(new NumberedFrames(3, 64, 36), path, settings);

        Assert.Equal([1000, 1000, 1000], GifFileReader.Read(path).Delays);
    }

    [Fact]
    public async Task ACancelledGifLeavesNoFile()
    {
        var path = Path.Combine(_folder, "cancelled.gif");
        var settings = new VideoExportSettings(160, 90, new Fraction(10, 1)) { Format = VideoFormat.Gif };
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var source = new NumberedFrames(200, 160, 90) { BeforeLoad = index => { if (index == 20) { cancel.Cancel(); } } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoExporter.ExportAsync(source, path, settings, null, cancel.Token));
        await ExportCleanup.WhenDone(Path.GetFullPath(path));

        Assert.False(File.Exists(path), "the unfinished GIF is deleted");
    }

    /// <summary>
    /// The dither the export uses, chosen on a sunset (#51): a sky that goes from deep blue through violet to orange with
    /// a bright sun low in it, far more colours than a 256-colour palette holds. What the eye sees as banding is the error
    /// that is left after a blur takes the fine grain away; error diffusion keeps that lowest, below the nearest-colour
    /// mapping (bands) and an ordered dither (a cross-hatch).
    /// </summary>
    [Fact]
    public void ErrorDiffusionDrawsTheSmoothestSunset()
    {
        const int width = 480;
        const int height = 270;
        var sunset = Sunset(width, height);
        var results = new Dictionary<WICBitmapDitherType, (double Banding, long Bytes)>();
        foreach (var dither in new[]
        {
            WICBitmapDitherType.WICBitmapDitherTypeNone,
            WICBitmapDitherType.WICBitmapDitherTypeOrdered8x8,
            WICBitmapDitherType.WICBitmapDitherTypeErrorDiffusion,
        })
        {
            var path = Path.Combine(_folder, $"sunset-{dither}.gif");
            var encoder = new GifEncoder(path, new VideoExportSettings(width, height, new Fraction(10, 1)) { Format = VideoFormat.Gif }, dither);
            try
            {
                encoder.WriteFrame(sunset, 10);
                encoder.Finish();
            }
            finally
            {
                encoder.Dispose();
            }

            GoldenImage? decoded = null;
            GifFileReader.ReadFrames(path, frame => decoded = GoldenImage.Copy(frame.Pixels, frame.Width, frame.Height, frame.Stride));
            results[dither] = (BlurredError(new GoldenImage(sunset, width, height), decoded!), new FileInfo(path).Length);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(
            Environment.NewLine,
            results.Select(entry => $"{entry.Key}: banding {entry.Value.Banding:0.000} levels after a 7x7 blur, {entry.Value.Bytes:N0} bytes")));
        var diffusion = results[WICBitmapDitherType.WICBitmapDitherTypeErrorDiffusion].Banding;
        Assert.True(diffusion < results[WICBitmapDitherType.WICBitmapDitherTypeNone].Banding, "error diffusion bands less than the nearest colour");
        Assert.True(diffusion < results[WICBitmapDitherType.WICBitmapDitherTypeOrdered8x8].Banding, "error diffusion bands less than an ordered dither");
    }

    [Fact]
    public void IdenticalFramesInARowAreStoredAsOneFrameLastingTheRun()
    {
        // A 3 s hold at 15 fps (6⅔ cs a frame): one frame, then 45 identical ones, then another. The file stores three
        // frames, and the held one lasts from its rounded start to the next frame's rounded start, so the whole GIF is as
        // long as the 47 frames' rounded end (review of PR #134).
        const int width = 64;
        const int height = 36;
        var rate = new Fraction(15, 1);
        var path = Path.Combine(_folder, "held.gif");
        var first = Solid(width, height, 40);
        var held = Solid(width, height, 120);
        var last = Solid(width, height, 200);
        var encoder = new GifEncoder(path, new VideoExportSettings(width, height, rate) { Format = VideoFormat.Gif });
        try
        {
            var frames = new[] { first }.Concat(Enumerable.Repeat(held, 45)).Append(last).ToArray();
            for (var i = 0; i < frames.Length; i++)
            {
                // A fresh copy each time: the encoder compares the pixels, not the arrays.
                encoder.Accept([.. frames[i]], FrameTiming.FrameStart(i, rate), FrameTiming.FrameStart(i + 1, rate));
            }

            encoder.Finish();
        }
        finally
        {
            encoder.Dispose();
        }

        var info = GifFileReader.Read(path);
        Assert.Equal(3, info.FrameCount);
        Assert.Equal(
            new[] { GifTiming.Delay(0, rate), (int)(GifTiming.Centiseconds(FrameTiming.FrameStart(46, rate)) - GifTiming.Centiseconds(FrameTiming.FrameStart(1, rate))), GifTiming.Delay(46, rate) },
            info.Delays);
        Assert.Equal(300, info.Delays[1]);
        Assert.Equal(GifTiming.Centiseconds(FrameTiming.Duration(47, rate)), info.TotalCentiseconds);

        // A run longer than a GIF delay can hold (655.35 s) is split, still adding up to its length.
        var longPath = Path.Combine(_folder, "long.gif");
        var slow = new Fraction(1, 10);
        var longEncoder = new GifEncoder(longPath, new VideoExportSettings(width, height, slow) { Format = VideoFormat.Gif });
        try
        {
            for (var i = 0; i < 70; i++)
            {
                longEncoder.Accept(held, FrameTiming.FrameStart(i, slow), FrameTiming.FrameStart(i + 1, slow));
            }

            longEncoder.Finish();
        }
        finally
        {
            longEncoder.Dispose();
        }

        var longInfo = GifFileReader.Read(longPath);
        Assert.Equal(2, longInfo.FrameCount);
        Assert.All(longInfo.Delays, delay => Assert.InRange(delay, 1, GifTiming.MaxDelay));
        Assert.Equal(70_000, longInfo.TotalCentiseconds);
    }

    // Top-down BGRX pixels of one grey.
    private static byte[] Solid(int width, int height, byte grey)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = grey;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    [Fact]
    public void TheEncoderRefusesASizeAGifCannotHold()
    {
        var settings = new VideoExportSettings(70_000, 2, new Fraction(10, 1)) { Format = VideoFormat.Gif };
        Assert.Throws<ArgumentException>(() => new GifEncoder(Path.Combine(_folder, "huge.gif"), settings));
    }

    [Fact]
    public async Task EncodingSpeedIsReported()
    {
        // Not a limit: the time per frame at the largest preset, for the PR and the test matrix.
        var size = GifOutput.Size(GifSize.Large, FrameAspect.Ratio16x9);
        var path = Path.Combine(_folder, "speed.gif");
        var settings = new VideoExportSettings(size.Width, size.Height, new Fraction(10, 1)) { Format = VideoFormat.Gif };
        var clock = Stopwatch.StartNew();
        await MediaSkips.ExportOrSkipAsync(new NumberedFrames(60, size.Width, size.Height), path, settings);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{size.Width}x{size.Height}: 60 frames in {clock.ElapsedMilliseconds} ms ({clock.ElapsedMilliseconds / 60.0:0.0} ms a frame), {new FileInfo(path).Length:N0} bytes");
    }

    /// <summary>
    /// The measurement behind <see cref="GifOutput.BytesPerPixel"/>: each photo in the folder named by
    /// <c>TENLAPSE_GIF_MEASURE</c>, filled into 16:9 at every GIF size and written as one GIF frame the way the export
    /// writes it, with its bytes per pixel. Skipped without the variable; #41 runs it on real timelapse photos.
    /// </summary>
    [Fact]
    public async Task MeasureBytesPerPixel()
    {
        var folder = Environment.GetEnvironmentVariable("TENLAPSE_GIF_MEASURE");
        Assert.SkipWhen(string.IsNullOrEmpty(folder), "Set TENLAPSE_GIF_MEASURE to a folder of JPEG photos to measure GIF bytes per pixel.");
        var samples = new List<(string Name, GifSize Size, double BytesPerPixel)>();
        using var device = new Microsoft.Graphics.Canvas.CanvasDevice();
        foreach (var photo in Directory.GetFiles(folder!, "*.jpg"))
        {
            using var bitmap = await Microsoft.Graphics.Canvas.CanvasBitmap.LoadAsync(device, photo);
            foreach (var size in Enum.GetValues<GifSize>())
            {
                var pixels = GifOutput.Size(size, FrameAspect.Ratio16x9);
                using var target = new Microsoft.Graphics.Canvas.CanvasRenderTarget(device, pixels.Width, pixels.Height, 96f);
                using (var session = target.CreateDrawingSession())
                {
                    TenLapse.Media.Rendering.FrameRenderer.Draw(session, bitmap, FitMode.Fill, pixels);
                }

                var path = Path.Combine(_folder, $"measure-{size}.gif");
                var encoder = new GifEncoder(path, new VideoExportSettings(pixels.Width, pixels.Height, new Fraction(10, 1)) { Format = VideoFormat.Gif });
                try
                {
                    encoder.WriteFrame(target.GetPixelBytes(), 10);
                    encoder.Finish();
                }
                finally
                {
                    encoder.Dispose();
                }

                // One frame: the file less the header and the frame's palette and headers (the estimate of a one-pixel
                // frame, whose own byte rounds away) is the picture.
                var picture = new FileInfo(path).Length - GifOutput.EstimateBytes(1, new PixelSize(1, 1));
                samples.Add((Path.GetFileName(photo), size, picture / ((double)pixels.Width * pixels.Height)));
            }
        }

        var output = TestContext.Current.TestOutputHelper;
        foreach (var sample in samples)
        {
            output?.WriteLine($"{sample.Name} {sample.Size}: {sample.BytesPerPixel:0.000} bytes per pixel");
        }

        foreach (var group in samples.GroupBy(sample => sample.Size))
        {
            output?.WriteLine($"{group.Key}: mean {group.Average(s => s.BytesPerPixel):0.000}, min {group.Min(s => s.BytesPerPixel):0.000}, max {group.Max(s => s.BytesPerPixel):0.000}");
        }

        output?.WriteLine($"all: mean {samples.Average(s => s.BytesPerPixel):0.000}, min {samples.Min(s => s.BytesPerPixel):0.000}, max {samples.Max(s => s.BytesPerPixel):0.000}");
    }

    // A sunset sky: deep blue at the top through violet to orange at the horizon, a warm glow and a bright sun low in
    // the middle, as BGRX.
    internal static byte[] Sunset(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var t = (double)y / (height - 1);
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = t < 0.6
                    ? Mix((20, 30, 90), (150, 70, 140), t / 0.6)
                    : Mix((150, 70, 140), (250, 150, 60), (t - 0.6) / 0.4);
                var dx = (x - (width * 0.5)) / width;
                var dy = (y - (height * 0.82)) / height;
                var glow = Math.Exp(-((dx * dx) + (dy * dy)) * 18);
                r += (255 - r) * glow;
                g += (220 - g) * glow * 0.9;
                b += (160 - b) * glow * 0.5;
                var i = ((y * width) + x) * 4;
                pixels[i] = (byte)Math.Clamp(Math.Round(b), 0, 255);
                pixels[i + 1] = (byte)Math.Clamp(Math.Round(g), 0, 255);
                pixels[i + 2] = (byte)Math.Clamp(Math.Round(r), 0, 255);
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static (double R, double G, double B) Mix((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
        (a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t));

    // The mean absolute difference per channel between the two after a 7x7 box blur of each: the low-frequency error
    // (bands, a pattern) the eye sees from a normal distance, with the pixel-level grain averaged away.
    internal static double BlurredError(GoldenImage original, GoldenImage decoded)
    {
        var a = Blur(original);
        var b = Blur(decoded);
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += Math.Abs(a[i] - b[i]);
        }

        return sum / a.Length;
    }

    private static double[] Blur(GoldenImage image)
    {
        const int radius = 3;
        var (width, height) = (image.Width, image.Height);
        var output = new double[(width - (2 * radius)) * (height - (2 * radius)) * 3];
        var n = 0;
        for (var y = radius; y < height - radius; y++)
        {
            for (var x = radius; x < width - radius; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    double sum = 0;
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        for (var dx = -radius; dx <= radius; dx++)
                        {
                            sum += image.Pixels[((((y + dy) * width) + x + dx) * 4) + c];
                        }
                    }

                    output[n++] = sum / ((2 * radius) + 1) / ((2 * radius) + 1);
                }
            }
        }

        return output;
    }
}
