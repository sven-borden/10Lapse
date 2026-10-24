using Microsoft.Graphics.Canvas;
using TenLapse.Core.Look;
using TenLapse.Core.Projects;
using TenLapse.Media.Export;
using TenLapse.Media.Rendering;
using TenLapse.Tests.Golden;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>
/// Frame blending in <see cref="FrameRenderer"/> (#47): photos averaged by their weights in linear light, each with its
/// own exposure, one photo drawn exactly as without blending, and the export's <see cref="BlendedPhotoSource"/>, which
/// shares each photo's decode across the frames that blend it.
/// </summary>
public sealed class FrameBlendTests
{
    [Fact]
    public void TheTablesTakeCodesToLinearLightAndBackWithinAHundredthOfALevel()
    {
        var linear = FrameRenderer.LinearTable(0);
        var encode = FrameRenderer.EncodeTable;
        Assert.All(Enumerable.Range(0, 256), code => Assert.Equal(LuminanceMeter.SrgbToLinear(code / 255.0), linear[code], 1e-6));
        Assert.All(Enumerable.Range(0, 256), level => Assert.Equal(LuminanceMeter.LinearToSrgb(Math.Pow(level / 255.0, 2.4)), encode[level], 1e-6));

        // Back to sRGB the light is raised to 1/2.4 and the effect interpolates between entries there, as here: every code,
        // and every light between two codes, comes back within a hundredth of a level (half a level on linear light).
        var worst = Enumerable.Range(0, 511).Max(half =>
        {
            var light = LuminanceMeter.SrgbToLinear(half / 510.0);
            return Math.Abs((Interpolate(encode, Math.Pow(light, FrameRenderer.EncodeExponent)) * 255) - (half / 2.0));
        });
        Assert.True(worst < 0.01, $"A level comes back {worst:0.0000} off.");

        // An exposure is folded into the way to linear light: +1 stop on code 118 is twice its light.
        Assert.Equal(LuminanceMeter.SrgbToLinear(FrameRenderer.ExposureTable(1)[118]), FrameRenderer.LinearTable(1)[118], 1e-6);
    }

    [Theory]
    [InlineData(255, 0, 0.5)]
    [InlineData(200, 50, 0.25)]
    [InlineData(30, 10, 0.125)]
    public void TwoPhotosAreAveragedInLinearLight(byte first, byte second, double share)
    {
        // White and black half and half is linear 0.5, sRGB 188, not the 128 an average of the codes would give.
        SkipWithoutGraphicsDevice();
        using var device = new CanvasDevice();
        using var a = Solid(device, 64, 36, first);
        using var b = Solid(device, 64, 36, second);

        var drawn = Draw(device, [new BlendedPhoto(a, 0, (float)share), new BlendedPhoto(b, 0, (float)(1 - share))], new PixelSize(64, 36));
        var expected = 255 * LuminanceMeter.LinearToSrgb((share * LuminanceMeter.SrgbToLinear(first / 255.0)) + ((1 - share) * LuminanceMeter.SrgbToLinear(second / 255.0)));

        Assert.All(Enumerable.Range(0, drawn.Length / 4), pixel => Assert.Equal(expected, drawn[pixel * 4], 1.0));
        Assert.Equal(255, drawn[3]);
    }

    [Fact]
    public void EachPhotoKeepsItsOwnExposure()
    {
        SkipWithoutGraphicsDevice();
        using var device = new CanvasDevice();
        using var a = Solid(device, 32, 18, 118);
        using var b = Solid(device, 32, 18, 118);

        var drawn = Draw(device, [new BlendedPhoto(a, 1, 0.5f), new BlendedPhoto(b, 0, 0.5f)], new PixelSize(32, 18));
        var expected = 255 * LuminanceMeter.LinearToSrgb(0.5 * (FrameRenderer.LinearTable(1)[118] + FrameRenderer.LinearTable(0)[118]));

        Assert.Equal(expected, drawn[0], 1.0);
    }

    [Fact]
    public void ThePhotoBlendedWithItselfIsThePhotoWithinALevel()
    {
        // Every code once, drawn one to one four times at a quarter each: the tables and the 16-bit float images between
        // them lose less than a level anywhere.
        SkipWithoutGraphicsDevice();
        using var device = new CanvasDevice();
        using var photo = Codes(device);

        var drawn = Draw(device, [.. Enumerable.Repeat(new BlendedPhoto(photo, 0, 0.25f), 4)], new PixelSize(256, 1), FitMode.Fit);

        var worst = Enumerable.Range(0, 256).Max(code => Math.Abs(drawn[code * 4] - code));
        Assert.True(worst <= 1, $"A code is {worst} levels off.");
    }

    [Fact]
    public void OnePhotoIsTheFrameWithoutBlendingToTheBit()
    {
        SkipWithoutGraphicsDevice();
        using var device = new CanvasDevice();
        using var photo = Codes(device);
        var output = new PixelSize(128, 1);
        var view = new Framing(0.5, 0.5, 0.5);

        Assert.Equal(DrawOne(device, photo, output, new FrameLook(0.4f, view)), Draw(device, [new BlendedPhoto(photo, 0.4f, 1)], output, FitMode.Fill, view));
    }

    [Fact]
    public void TheReframePathBlendsAsTheFrameDoes()
    {
        // The preview's 400 ms reframe places the photos by hand through DrawBlend (#35's exception): with each photo at a
        // one-to-one rectangle it draws what Draw draws for Fit into a frame of the photos' own shape.
        SkipWithoutGraphicsDevice();
        using var device = new CanvasDevice();
        using var a = Solid(device, 96, 64, 240);
        using var b = Codes(device, 96, 64);
        var whole = new Rect(0, 0, 96, 64);

        using var target = new CanvasRenderTarget(device, 96, 64, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 0, 0, 0));
            FrameRenderer.DrawBlend(session, whole, [new PlacedPhoto(a, whole, whole, -0.3f, 0.4f), new PlacedPhoto(b, whole, whole, 0.2f, 0.6f)]);
        }

        var drawn = Draw(device, [new BlendedPhoto(a, -0.3f, 0.4f), new BlendedPhoto(b, 0.2f, 0.6f)], new PixelSize(96, 64), FitMode.Fit);
        Assert.True(drawn.Zip(target.GetPixelBytes()).All(pair => Math.Abs(pair.First - pair.Second) <= 1));
    }

    [Fact]
    public void ABlendedPhotoOfAnotherSizeIsPlacedByItsOwnSize()
    {
        // A 3:2 photo and a 16:9 one in a 16:9 frame with Fit: the 3:2 photo has bars, which blend as black.
        SkipWithoutGraphicsDevice();
        using var device = new CanvasDevice();
        using var wide = Solid(device, 64, 36, 200);
        using var classic = Solid(device, 48, 32, 200);

        var drawn = Draw(device, [new BlendedPhoto(wide, 0, 0.5f), new BlendedPhoto(classic, 0, 0.5f)], new PixelSize(64, 36), FitMode.Fit);
        var middle = 255 * LuminanceMeter.LinearToSrgb(LuminanceMeter.SrgbToLinear(200 / 255.0));
        var bar = 255 * LuminanceMeter.LinearToSrgb(0.5 * LuminanceMeter.SrgbToLinear(200 / 255.0));

        Assert.Equal(middle, drawn[((18 * 64) + 32) * 4], 1.0);
        Assert.Equal(bar, drawn[18 * 64 * 4], 1.0);
    }

    [Fact]
    public async Task TheExportSourceDecodesEachPhotoOnceAndLetsItGo()
    {
        SkipWithoutGraphicsDevice();
        var folder = Directory.CreateTempSubdirectory("10lapse-blend-").FullName;
        try
        {
            var paths = new List<string>();
            for (var n = 0; n < 6; n++)
            {
                var path = Path.Combine(folder, $"photo{n}.png");
                await new GoldenImage(Grey(64, 36, (byte)(n * 40)), 64, 36).SavePngAsync(path);
                paths.Add(path);
            }

            // A boomerang: the way back blends the photos again, decoded anew rather than held between.
            int[] frames = [0, 1, 2, 3, 4, 5, 4, 3, 2, 1, 0];
            var blend = new BlendSettings(true, 3, 100);
            using var device = new CanvasDevice();
            using var source = new BlendedPhotoSource(paths, frames, blend);
            var output = new PixelSize(64, 36);
            var most = 0;
            for (var i = 0; i < frames.Length; i++)
            {
                using var bitmap = await source.LoadAsync(i, device, output, TestContext.Current.CancellationToken);
                most = Math.Max(most, source.HeldPhotos);
                Assert.Equal(FrameLook.None, source.LookOf(i));

                // The frame is the blend of the photos around its own: the average of their light.
                var pixels = bitmap.GetPixelBytes();
                var (first, last) = blend.Window(frames[i], paths.Count);
                var expected = 255 * LuminanceMeter.LinearToSrgb(Enumerable.Range(first, last - first + 1).Average(photo => LuminanceMeter.SrgbToLinear(photo * 40 / 255.0)));
                Assert.Equal(expected, pixels[0], 1.0);
            }

            // At most the window and the photo the next frame adds; nothing once the last frame is drawn.
            Assert.True(most <= 4, $"{most} photos held at once.");
            await WaitForAsync(() => source.HeldPhotos == 0);
            Assert.Equal(0, source.HeldPhotos);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AnExportStoppedInTheMiddleLetsItsPhotosGo()
    {
        SkipWithoutGraphicsDevice();
        var folder = Directory.CreateTempSubdirectory("10lapse-blend-").FullName;
        try
        {
            var paths = new List<string>();
            for (var n = 0; n < 5; n++)
            {
                var path = Path.Combine(folder, $"photo{n}.png");
                await new GoldenImage(Grey(32, 18, 90), 32, 18).SavePngAsync(path);
                paths.Add(path);
            }

            using var device = new CanvasDevice();
            var source = new BlendedPhotoSource(paths, [0, 1, 2, 3, 4], new BlendSettings(true, 4, 100));
            using (await source.LoadAsync(0, device, new PixelSize(32, 18), TestContext.Current.CancellationToken))
            {
            }

            Assert.True(source.HeldPhotos > 0);
            source.Dispose();
            await WaitForAsync(() => source.HeldPhotos == 0);
            Assert.Equal(0, source.HeldPhotos);
            // Refused at once, before any work starts.
            Assert.Throws<ObjectDisposedException>(() => { _ = source.LoadAsync(1, device, new PixelSize(32, 18), TestContext.Current.CancellationToken); });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ABlendOfManyPhotosDecodesAZoomedViewWithinTheBudget()
    {
        // 16 photos at 4K fill the budget at their own size; at 1080p they may be decoded twice as large; 4 at 4K too.
        Assert.Equal(1, ProjectExporter.BlendDecodeZoom(new BlendSettings(true, 16, 100), new PixelSize(3840, 2160)), 9);
        Assert.Equal(2, ProjectExporter.BlendDecodeZoom(new BlendSettings(true, 16, 100), new PixelSize(1920, 1080)), 9);
        Assert.Equal(2, ProjectExporter.BlendDecodeZoom(new BlendSettings(true, 4, 100), new PixelSize(3840, 2160)), 9);
        Assert.Equal(1, ProjectExporter.BlendDecodeZoom(new BlendSettings(true, 16, 100), new PixelSize(7680, 4320)), 9);
    }

    [Fact]
    public void TheExportBlendsOnlyWhenThereIsSomethingToBlend()
    {
        static ProjectSnapshot With(BlendSettings blend, int photos)
        {
            var project = new Project(TimingSettings.Default, FrameSettings.Default, OutputSettings.Default, SortMode.Manual, SequenceSettings.Default, LookSettings.Default, CropSettings.Default, MusicSettings.Default, blend);
            project.AddFrames(Enumerable.Range(0, photos).Select(n => new Frame($@"C:\Shoot\IMG_{n:0000}.jpg", 1000, DateTime.UnixEpoch)));
            return project.Snapshot;
        }

        Assert.True(ProjectExporter.BlendsFrames(With(new BlendSettings(true, 4, 100), 3)));
        Assert.False(ProjectExporter.BlendsFrames(With(new BlendSettings(false, 4, 100), 3)));
        Assert.False(ProjectExporter.BlendsFrames(With(new BlendSettings(true, 4, 0), 3)));
        Assert.False(ProjectExporter.BlendsFrames(With(new BlendSettings(true, 4, 100), 1)));
    }

    private static float Interpolate(float[] table, double value)
    {
        var at = Math.Clamp(value, 0, 1) * (table.Length - 1);
        var below = (int)Math.Floor(at);
        var above = Math.Min(table.Length - 1, below + 1);
        return (float)(table[below] + ((table[above] - table[below]) * (at - below)));
    }

    private static async Task WaitForAsync(Func<bool> done)
    {
        // The last frame's bitmap goes in a continuation of its upload; it has run by the time the load returned, but give
        // a slow PC a moment.
        for (var tries = 0; tries < 100 && !done(); tries++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static byte[] Grey(int width, int height, byte level)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = level;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    private static CanvasBitmap Solid(CanvasDevice device, int width, int height, byte level) =>
        CanvasBitmap.CreateFromBytes(device, Grey(width, height, level), width, height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96f, CanvasAlphaMode.Premultiplied);

    // Every code once from left to right (a 256 x 1 photo), or a ramp of them over a larger one.
    private static CanvasBitmap Codes(CanvasDevice device, int width = 256, int height = 1)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            var code = (byte)(i % width * 255 / Math.Max(1, width - 1));
            pixels[i * 4] = pixels[(i * 4) + 1] = pixels[(i * 4) + 2] = code;
            pixels[(i * 4) + 3] = 255;
        }

        return CanvasBitmap.CreateFromBytes(device, pixels, width, height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96f, CanvasAlphaMode.Premultiplied);
    }

    private static byte[] Draw(CanvasDevice device, BlendedPhoto[] photos, PixelSize output, FitMode fit = FitMode.Fill, Framing view = default)
    {
        using var target = new CanvasRenderTarget(device, output.Width, output.Height, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 128, 128, 128));
            FrameRenderer.Draw(session, photos, fit, output, view);
        }

        return target.GetPixelBytes();
    }

    private static byte[] DrawOne(CanvasDevice device, CanvasBitmap photo, PixelSize output, FrameLook look)
    {
        using var target = new CanvasRenderTarget(device, output.Width, output.Height, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 128, 128, 128));
            FrameRenderer.Draw(session, photo, FitMode.Fill, output, look);
        }

        return target.GetPixelBytes();
    }
}
