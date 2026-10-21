using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Look;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Look;
using TenLapse.Media.Preview;
using TenLapse.Media.Rendering;
using TenLapse.Tests.Look;
using TenLapse.Tests.Media;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Golden;

/// <summary>
/// The golden render test for deflicker (#45): a flickering sequence, measured by the real <see cref="ExposureMeter"/>,
/// exported as MP4 and as GIF with deflicker on; each decoded frame is compared with what the preview draws for it (its
/// proxy through <see cref="FrameRenderer.Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize, FrameLook)"/>
/// with the photo's <see cref="FrameLook"/>), and the export's own brightness must have lost its flicker.
/// </summary>
/// <remarks>
/// The match is held to the geometry test's limits (#81). And because a correction of a few tenths of a stop is only
/// some levels on a mid-tone, each corrected frame must also be at least twice as close to the preview with its look as
/// to the preview without one: an export that forgot the look fails here even where the mean error alone would pass.
/// </remarks>
[Collection(nameof(RealEncoderCollection))]
public sealed class GoldenLookTests(GoldenLookTests.FlickeringPhotos photos) : IClassFixture<GoldenLookTests.FlickeringPhotos>
{
    private const string ClassName = "look";

    // The geometry test's limits for the same encoders (GoldenGeometryTests).
    private const double MeanErrorLimit = 4;
    private const double GifMeanErrorLimit = 6;

    [Theory]
    [InlineData(VideoFormat.Mp4H264)]
    [InlineData(VideoFormat.Gif)]
    public async Task TheExportCarriesThePreviewsCorrectionAndLosesTheFlicker(VideoFormat format)
    {
        if (format == VideoFormat.Gif)
        {
            SkipWithoutGraphicsDevice();
        }
        else
        {
            SkipWithoutH264Encoder();
        }

        var label = format == VideoFormat.Gif ? "GIF" : "MP4";
        var folder = GoldenExport.EvidenceFolder(ClassName, label);
        var (frames, proxies) = await photos.GetAsync();
        var snapshot = GoldenExport.Snapshot(frames, TimingSettings.PerPhoto(new Fraction(2, 1)), FrameAspect.Ratio16x9, FitMode.Fill, format, look: new LookSettings(true, 100));
        var curve = ExposureCurve.For(snapshot);
        Assert.Equal(frames.Count, curve.Count);
        Assert.Contains(frames, frame => Math.Abs(curve.Correction(frame.Id)) > 0.2);

        var clock = Stopwatch.StartNew();
        List<GoldenImage> exported;
        PixelSize size;
        string written;
        if (format == VideoFormat.Gif)
        {
            size = snapshot.VideoSize;
            var result = await ExportOrSkipAsync(snapshot, Path.Combine(photos.Folder, "look.gif"));
            written = result.Path;
            exported = [];
            TenLapse.Media.Inspection.GifFileReader.ReadFrames(written, frame => exported.Add(GoldenImage.Copy(frame.Pixels, frame.Width, frame.Height, frame.Stride)));
        }
        else
        {
            size = GoldenExport.SmallSize(snapshot.VideoSize);
            var (result, _) = await GoldenExport.ExportSmallOrSkipAsync(snapshot, Path.Combine(photos.Folder, "look.mp4"));
            written = result.Path;
            exported = GoldenExport.ReadAllFrames(written).Frames;
        }

        var limit = format == VideoFormat.Gif ? GifMeanErrorLimit : MeanErrorLimit;
        var problems = new List<string>();
        var evidence = new List<(int Index, GoldenImage Reference, GoldenImage Exported)>();
        Assert.Equal(frames.Count, exported.Count);
        var (worstWith, worstRatio) = (0.0, 0.0);
        var exportedBrightness = new List<double>();
        using (var device = new CanvasDevice())
        {
            var preview = new PreviewImageSource(proxies, () => device);
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = snapshot.Frames[index];
                var look = FrameLook.For(snapshot, frame.Id);
                var with = await PreviewDrawAsync(device, preview, frame, snapshot.FrameSettings, size, look);
                var without = await PreviewDrawAsync(device, preview, frame, snapshot.FrameSettings, size, FrameLook.None);
                var errorWith = Worst(GoldenImage.MeanAbsoluteError(with, exported[index]));
                var errorWithout = Worst(GoldenImage.MeanAbsoluteError(without, exported[index]));
                exportedBrightness.Add(LuminanceMeter.Measure(exported[index].Pixels, size.Width, size.Height, size.Width * 4));
                worstWith = Math.Max(worstWith, errorWith);
                if (errorWith > limit)
                {
                    problems.Add($"frame {index}: mean error against the preview with its look expected at most {limit}, got {errorWith:0.00}");
                    evidence.Add((index, with, exported[index]));
                }

                if (Math.Abs(look.Stops) > 0.2)
                {
                    worstRatio = Math.Max(worstRatio, errorWith / errorWithout);
                    if (errorWith * 2 > errorWithout)
                    {
                        problems.Add($"frame {index} ({look.Stops:+0.00;-0.00} stops): not clearly the corrected picture: {errorWith:0.00} levels from the preview with its look, {errorWithout:0.00} without");
                        evidence.Add((index, with, exported[index]));
                    }
                }
            }
        }

        var source = frames.Select(frame => frame.Exposure!.Luminance).ToList();
        var spreadBefore = source.Max() - source.Min();
        var spreadAfter = exportedBrightness.Max() - exportedBrightness.Min();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{label} {size.Width}x{size.Height}: worst mean error {worstWith:0.00} levels against the preview with its look, worst with/without ratio {worstRatio:0.00}; " +
            $"brightness spread over the photos {spreadBefore:0.000} stops, over the exported frames {spreadAfter:0.000}; {clock.ElapsedMilliseconds} ms");
        if (spreadAfter > spreadBefore / 3)
        {
            problems.Add($"the exported frames still spread {spreadAfter:0.000} stops in brightness (the photos {spreadBefore:0.000})");
        }

        if (problems.Count > 0)
        {
            Directory.CreateDirectory(folder);
            File.Copy(written, Path.Combine(folder, Path.GetFileName(written)), overwrite: true);
            foreach (var (index, reference, frame) in evidence)
            {
                await reference.SavePngAsync(Path.Combine(folder, $"frame{index}-preview.png"));
                await frame.SavePngAsync(Path.Combine(folder, $"frame{index}-export.png"));
            }

            Assert.Fail(GoldenExport.Report($"{label} with deflicker, preview against export:", problems, folder));
        }

        File.Delete(written);
    }

    private static double Worst((double B, double G, double R) error) => Math.Max(error.B, Math.Max(error.G, error.R));

    // The preview's drawing, as GoldenGeometryTests does it, with the photo's look.
    private static async Task<GoldenImage> PreviewDrawAsync(CanvasDevice device, PreviewImageSource preview, Frame frame, FrameSettings settings, PixelSize size, FrameLook look)
    {
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(FrameProxyResult.Ready, await preview.EnsureProxyAsync(frame, token));
        var loaded = await preview.LoadAsync(frame, FrameImageQuality.Proxy, token);
        using var bitmap = loaded.Image ?? throw new InvalidOperationException($"{frame.SourcePath}: the preview could not load it ({loaded.Failure}).");
        using var target = new CanvasRenderTarget(device, size.Width, size.Height, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 128, 128, 128));
            FrameRenderer.Draw(session, bitmap, settings, size, look);
        }

        return new GoldenImage(target.GetPixelBytes(), size.Width, size.Height);
    }

    /// <summary>
    /// Eight 960 x 640 photos of one scene, blocks of soft colours from about 4 % to 30 % of white in linear light, each
    /// photo brighter or darker by a known flicker of up to 0.3 stop; measured once with the real meter.
    /// </summary>
    public sealed class FlickeringPhotos : IDisposable
    {
        private static readonly double[] _flicker = [0, 0.3, -0.25, 0.2, -0.3, 0.1, -0.15, 0.25];

        private readonly SemaphoreSlim _gate = new(1, 1);
        private (IReadOnlyList<Frame> Frames, ProxyCache Proxies)? _written;

        public string Folder { get; } = GoldenExport.StartClass(ClassName);

        public async Task<(IReadOnlyList<Frame> Frames, ProxyCache Proxies)> GetAsync()
        {
            await _gate.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                return _written ??= await WriteAsync();
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

        private async Task<(IReadOnlyList<Frame>, ProxyCache)> WriteAsync()
        {
            const int width = 960;
            const int height = 640;
            var meter = new ExposureMeter();
            var frames = new List<Frame>();
            for (var n = 0; n < _flicker.Length; n++)
            {
                var gain = Math.Pow(2, _flicker[n]);
                var pixels = new byte[width * height * 4];
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        // 8 x 6 blocks; each channel its own level, so the blocks have soft hues.
                        var block = ((x * 8 / width) * 7) + ((y * 6 / height) * 3);
                        var i = ((y * width) + x) * 4;
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var linear = (0.04 + (0.26 * (((block * (channel + 3)) % 11) / 10.0))) * gain;
                            pixels[i + channel] = (byte)Math.Round(LuminanceMeter.LinearToSrgb(Math.Min(1, linear)) * 255);
                        }

                        pixels[i + 3] = 255;
                    }
                }

                var path = Path.Combine(Folder, $"photo{n}.png");
                await new GoldenImage(pixels, width, height).SavePngAsync(path);
                var frame = GoldenExport.FrameOf(path, new PixelSize(width, height));
                var measured = await meter.MeasureAsync(frame, TestContext.Current.CancellationToken);
                frames.Add(frame with { Exposure = measured.Exposure ?? throw new InvalidOperationException($"{path}: {measured.Failure}") });
            }

            var proxies = new ProxyCache(Path.Combine(Folder, "proxies"));
            await proxies.OpenAsync();
            return (frames, proxies);
        }
    }
}
