using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Look;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using TenLapse.Media.Preview;
using TenLapse.Media.Rendering;
using TenLapse.Tests.Media;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Golden;

/// <summary>
/// The golden render test for frame blending (#47): a white square moving across a dark sky, exported as MP4 with
/// blending on; each decoded frame is compared with what the preview draws for it (the proxies of its blend's photos
/// through <see cref="FrameRenderer.Draw(CanvasDrawingSession, IReadOnlyList{BlendedPhoto}, FitMode, PixelSize, Framing)"/>),
/// and the square must leave a trail in the export: at each of the blend's other photos' places, the share of its light
/// averaged in linear light.
/// </summary>
[Collection(nameof(RealEncoderCollection))]
public sealed class GoldenBlendTests(GoldenBlendTests.MovingLight photos) : IClassFixture<GoldenBlendTests.MovingLight>
{
    private const string ClassName = "blend";

    // The geometry test's limit for the same encoder (GoldenGeometryTests).
    private const double MeanErrorLimit = 4;

    // How far the middle of the trail may be from the light averaged in linear light; an average of the codes is about 40
    // levels darker, and no blending 95.
    private const double TrailLimit = 10;

    [Fact]
    public async Task TheExportBlendsAsThePreviewDoesAndTheLightLeavesATrail()
    {
        SkipWithoutH264Encoder();
        var folder = GoldenExport.EvidenceFolder(ClassName, "MP4");
        var (frames, proxies) = await photos.GetAsync();
        var blend = new BlendSettings(true, 4, 100);
        var snapshot = GoldenExport.Snapshot(frames, TimingSettings.PerPhoto(new Fraction(2, 1)), FrameAspect.Ratio16x9, FitMode.Fill, VideoFormat.Mp4H264, blend: blend);

        var clock = Stopwatch.StartNew();
        var size = GoldenExport.SmallSize(snapshot.VideoSize);
        var (result, _) = await GoldenExport.ExportSmallOrSkipAsync(snapshot, Path.Combine(photos.Folder, "blend.mp4"));
        var exported = GoldenExport.ReadAllFrames(result.Path).Frames;
        Assert.Equal(frames.Count, exported.Count);

        var problems = new List<string>();
        var evidence = new List<(int Index, GoldenImage Reference, GoldenImage Exported)>();
        var (worstError, worstTrail) = (0.0, 0.0);
        using (var device = new CanvasDevice())
        {
            var preview = new PreviewImageSource(proxies, () => device);
            for (var index = 0; index < frames.Count; index++)
            {
                var layers = blend.Layers(index, frames.Count);
                var reference = await PreviewDrawAsync(device, preview, snapshot, layers, size);
                var error = Worst(GoldenImage.MeanAbsoluteError(reference, exported[index]));
                worstError = Math.Max(worstError, error);
                if (error > MeanErrorLimit)
                {
                    problems.Add($"frame {index}: mean error against the preview's blend expected at most {MeanErrorLimit}, got {error:0.00}");
                    evidence.Add((index, reference, exported[index]));
                }

                // Where each photo of the blend had the light, a share of it: the light's weight of white, the rest sky.
                foreach (var layer in layers)
                {
                    var expected = 255 * LuminanceMeter.LinearToSrgb(layer.Weight + ((1 - layer.Weight) * MovingLight.SkyAtLight));
                    var found = Patch(exported[index], MovingLight.LightCentre(layer.Photo, size));
                    worstTrail = Math.Max(worstTrail, Math.Abs(found - expected));
                    if (Math.Abs(found - expected) > TrailLimit)
                    {
                        problems.Add($"frame {index}: the light of photo {layer.Photo} ({layer.Weight:0.000} of the frame) should be {expected:0.0} there, got {found:0.0}");
                        evidence.Add((index, reference, exported[index]));
                    }
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"MP4 {size.Width}x{size.Height}, {frames.Count} photos blending {blend.Frames}: worst mean error {worstError:0.00} levels against the preview, " +
            $"worst trail {worstTrail:0.0} levels off the linear average; {clock.ElapsedMilliseconds} ms");
        if (problems.Count > 0)
        {
            Directory.CreateDirectory(folder);
            File.Copy(result.Path, Path.Combine(folder, Path.GetFileName(result.Path)), overwrite: true);
            foreach (var (index, reference, frame) in evidence.DistinctBy(item => item.Index))
            {
                await reference.SavePngAsync(Path.Combine(folder, $"frame{index}-preview.png"));
                await frame.SavePngAsync(Path.Combine(folder, $"frame{index}-export.png"));
            }

            Assert.Fail(GoldenExport.Report("MP4 with frame blending, preview against export:", problems, folder));
        }

        File.Delete(result.Path);
    }

    [Fact]
    public async Task APhotoTheBlendCannotReadFailsTheExportNamingThatPhoto()
    {
        // Photo 2 is only a neighbour of the first frame that fails (frame 1 blends photos 0 to 2): the message names photo 2
        // and its file, not the frame being drawn.
        SkipWithoutH264Encoder();
        var (frames, _) = await photos.GetAsync();
        var missing = Path.Combine(photos.Folder, "gone.png");
        var withGap = frames.Take(5).Select((frame, n) => n == 2 ? GoldenExport.FrameOf(missing, frame.PixelSize) : frame);
        var snapshot = GoldenExport.Snapshot(withGap, TimingSettings.PerPhoto(new Fraction(2, 1)), FrameAspect.Ratio16x9, FitMode.Fill, VideoFormat.Mp4H264, blend: new BlendSettings(true, 4, 100));

        var failure = await Assert.ThrowsAsync<VideoExportException>(() => ExportOrSkipAsync(snapshot, Path.Combine(photos.Folder, "gap.mp4")));

        Assert.Equal(VideoExportFailure.FrameUnreadable, failure.Failure);
        Assert.Equal(2, failure.FrameIndex);
        Assert.Equal(missing, failure.SourcePath);
        Assert.False(File.Exists(Path.Combine(photos.Folder, "gap.mp4")));
    }

    private static double Worst((double B, double G, double R) error) => Math.Max(error.B, Math.Max(error.G, error.R));

    // The mean green of the 5 x 5 pixels around a point.
    private static double Patch(GoldenImage image, (int X, int Y) centre)
    {
        var sum = 0.0;
        for (var y = centre.Y - 2; y <= centre.Y + 2; y++)
        {
            for (var x = centre.X - 2; x <= centre.X + 2; x++)
            {
                sum += image.Pixels[(((y * image.Width) + x) * 4) + 1];
            }
        }

        return sum / 25;
    }

    // The preview's drawing of a blended frame: the proxies of its photos, through the renderer's blend.
    private static async Task<GoldenImage> PreviewDrawAsync(CanvasDevice device, PreviewImageSource preview, ProjectSnapshot snapshot, BlendLayer[] layers, PixelSize size)
    {
        var token = TestContext.Current.CancellationToken;
        var bitmaps = new List<CanvasBitmap>();
        try
        {
            var blended = new List<BlendedPhoto>();
            foreach (var layer in layers)
            {
                var frame = snapshot.Frames[layer.Photo];
                Assert.Equal(FrameProxyResult.Ready, await preview.EnsureProxyAsync(frame, token));
                var loaded = await preview.LoadAsync(frame, FrameImageQuality.Proxy, token);
                var bitmap = loaded.Image ?? throw new InvalidOperationException($"{frame.SourcePath}: the preview could not load it ({loaded.Failure}).");
                bitmaps.Add(bitmap);
                blended.Add(new BlendedPhoto(bitmap, FrameLook.For(snapshot, frame.Id).Stops, (float)layer.Weight));
            }

            using var target = new CanvasRenderTarget(device, size.Width, size.Height, 96f);
            using (var session = target.CreateDrawingSession())
            {
                session.Clear(Color.FromArgb(255, 128, 128, 128));
                FrameRenderer.Draw(session, blended, snapshot.FrameSettings.Fit, size, Framing.Whole);
            }

            return new GoldenImage(target.GetPixelBytes(), size.Width, size.Height);
        }
        finally
        {
            bitmaps.ForEach(bitmap => bitmap.Dispose());
        }
    }

    /// <summary>
    /// Eight 640 x 360 photos of a dark sky that brightens towards the bottom, with a 24 px white square that moves 64 px
    /// to the right from one photo to the next, at mid-height.
    /// </summary>
    public sealed class MovingLight : IDisposable
    {
        private const int Width = 640;
        private const int Height = 360;
        private const int Side = 24;
        private const int Count = 8;

        private readonly SemaphoreSlim _gate = new(1, 1);
        private (IReadOnlyList<Frame> Frames, ProxyCache Proxies)? _written;

        /// <summary>The sky's light at the square's height, in linear light.</summary>
        public static double SkyAtLight => Sky(Height / 2);

        public string Folder { get; } = GoldenExport.StartClass(ClassName);

        /// <summary>Where the square of photo <paramref name="photo"/> is centred in a frame of <paramref name="size"/>.</summary>
        public static (int X, int Y) LightCentre(int photo, PixelSize size) =>
            ((int)Math.Round((80 + (photo * 64)) * size.Width / (double)Width), size.Height / 2);

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

        // The sky in linear light: 1 % of white at the top to 5 % at the bottom.
        private static double Sky(int y) => 0.01 + (0.04 * y / Height);

        private async Task<(IReadOnlyList<Frame>, ProxyCache)> WriteAsync()
        {
            var frames = new List<Frame>();
            for (var n = 0; n < Count; n++)
            {
                var centre = 80 + (n * 64);
                var pixels = new byte[Width * Height * 4];
                for (var y = 0; y < Height; y++)
                {
                    var sky = (byte)Math.Round(LuminanceMeter.LinearToSrgb(Sky(y)) * 255);
                    for (var x = 0; x < Width; x++)
                    {
                        var light = Math.Abs(x - centre) < Side / 2 && Math.Abs(y - (Height / 2)) < Side / 2;
                        var i = ((y * Width) + x) * 4;
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = light ? (byte)255 : sky;
                        pixels[i + 3] = 255;
                    }
                }

                var path = Path.Combine(Folder, $"photo{n}.png");
                await new GoldenImage(pixels, Width, Height).SavePngAsync(path);
                frames.Add(GoldenExport.FrameOf(path, new PixelSize(Width, Height)));
            }

            var proxies = new ProxyCache(Path.Combine(Folder, "proxies"));
            await proxies.OpenAsync();
            return (frames, proxies);
        }
    }
}
