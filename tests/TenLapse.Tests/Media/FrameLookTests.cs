using Microsoft.Graphics.Canvas;
using TenLapse.Core.Look;
using TenLapse.Core.Projects;
using TenLapse.Media.Export;
using TenLapse.Media.Look;
using TenLapse.Media.Rendering;
using TenLapse.Tests.Golden;
using TenLapse.Tests.Look;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>
/// The look stage of <see cref="FrameRenderer"/> (#45): deflicker's exposure applied on the GPU in linear light, exactly
/// as the curve aims, and nothing at all for a neutral look; and the exposure meter that measures the photos.
/// </summary>
public sealed class FrameLookTests
{
    [Fact]
    public void TheTableIsTheExposureInLinearLightAndNoneIsTheIdentity()
    {
        var none = FrameRenderer.ExposureTable(0);
        Assert.All(Enumerable.Range(0, 256), code => Assert.Equal(code / 255.0, none[code], 1e-6));

        // One stop on sRGB 118 (linear 0.1812): linear 0.3624, sRGB 0.6327 (161.3). Clipped at white.
        var stop = FrameRenderer.ExposureTable(1);
        Assert.Equal(LuminanceMeter.LinearToSrgb(2 * LuminanceMeter.SrgbToLinear(118 / 255.0)), stop[118], 1e-6);
        Assert.Equal(1f, stop[255]);
        Assert.Equal(1f, stop[240]);
        Assert.True(FrameRenderer.ExposureTable(-1)[255] < 0.74);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-0.5f)]
    [InlineData(0.25f)]
    public void TheGpuDrawsEachCodeAsTheTableSays(float stops)
    {
        SkipWithoutGraphicsDevice();
        using var device = new CanvasDevice();

        // A 256 x 1 photo with every code once, drawn one to one into a 256 x 1 frame.
        var codes = new byte[256 * 4];
        for (var code = 0; code < 256; code++)
        {
            codes[(code * 4) + 0] = codes[(code * 4) + 1] = codes[(code * 4) + 2] = (byte)code;
            codes[(code * 4) + 3] = 255;
        }

        using var photo = CanvasBitmap.CreateFromBytes(device, codes, 256, 1, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96f, CanvasAlphaMode.Premultiplied);
        var drawn = Draw(device, photo, new PixelSize(256, 1), new FrameLook(stops));
        var table = FrameRenderer.ExposureTable(stops);

        var worst = Enumerable.Range(0, 256).Max(code => Math.Abs(drawn[code * 4] - (table[code] * 255)));
        Assert.True(worst <= 1, $"A code is {worst:0.0} levels off the table at {stops} stops.");
    }

    [Fact]
    public void ANeutralLookDrawsTheFrameExactlyAsBefore()
    {
        SkipWithoutGraphicsDevice();
        using var device = new CanvasDevice();
        using var photo = Gradient(device, 96, 64);

        Assert.Equal(Draw(device, photo, new PixelSize(64, 36), null), Draw(device, photo, new PixelSize(64, 36), FrameLook.None));
    }

    [Theory]
    [InlineData(0.7f)]
    [InlineData(-0.4f)]
    public void TheDrawnFrameMeasuresTheCorrectionItWasGiven(float stops)
    {
        // End to end on the GPU: the meter's brightness of the drawn frame moves by the stops the look asked for, which is
        // what lets deflicker aim at a curve and land on it. Fill crops a 3:2 photo to 16:9; the bars stay black.
        SkipWithoutGraphicsDevice();
        using var device = new CanvasDevice();
        using var photo = Gradient(device, 480, 320);
        var output = new PixelSize(320, 180);

        var before = LuminanceMeter.Measure(Draw(device, photo, output, FrameLook.None), output.Width, output.Height, output.Width * 4);
        var after = LuminanceMeter.Measure(Draw(device, photo, output, new FrameLook(stops)), output.Width, output.Height, output.Width * 4);

        Assert.Equal(stops, after - before, 0.03);
    }

    [Fact]
    public void TheReframePathAppliesTheSameLook()
    {
        // The preview's 400 ms reframe places the photo by hand through DrawPhoto (#35's exception): with the whole photo
        // at a one-to-one rectangle it draws what Draw draws for Fit into a frame of the photo's own shape.
        SkipWithoutGraphicsDevice();
        using var device = new CanvasDevice();
        using var photo = Gradient(device, 96, 64);
        var look = new FrameLook(0.6f);

        using var target = new CanvasRenderTarget(device, 96, 64, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 0, 0, 0));
            FrameRenderer.DrawPhoto(session, photo, new Rect(0, 0, 96, 64), new Rect(0, 0, 96, 64), look);
        }

        var drawn = Draw(device, photo, new PixelSize(96, 64), look, FitMode.Fit);
        var byHand = target.GetPixelBytes();
        Assert.True(drawn.Zip(byHand).All(pair => Math.Abs(pair.First - pair.Second) <= 1));
    }

    [Fact]
    public void TheExportSourceGivesEachFrameItsPhotosLook()
    {
        // #49's holds and loops show a photo twice: the export's frame list repeats it, and each repeat has its look.
        var frames = Enumerable.Range(0, 4).Select(i => GoldenExport.FrameOf($@"C:\Photos\Look\IMG_{i}.jpg") with { Exposure = new PhotoExposure(-2 + (0.4 * (i % 2))) }).ToArray();
        var project = new Project(TimingSettings.Default, FrameSettings.Default, OutputSettings.Default, SortMode.Manual, new LookSettings(true, 100));
        project.AddFrames(frames);
        var snapshot = project.Snapshot;
        int[] order = [0, 1, 1, 2, 3, 3, 0];

        var looks = ProjectExporter.LooksFor(snapshot, order)!;
        var source = new PhotoFileSource([.. order.Select(i => frames[i].SourcePath)], FitMode.Fill, looks);

        for (var n = 0; n < order.Length; n++)
        {
            Assert.Equal(FrameLook.For(snapshot, frames[order[n]].Id), source.LookOf(n));
        }

        Assert.NotEqual(0, source.LookOf(0).Stops);
        Assert.Null(ProjectExporter.LooksFor(GoldenExport.Snapshot(frames, TimingSettings.Default, FrameAspect.Ratio16x9, FitMode.Fill), order));
    }

    [Fact]
    public async Task APhotoThatWasNotMeasuredTakesItsNeighboursCorrectionInPreviewAndExport()
    {
        // A photo that could not be measured (#45 review): the export and the preview both give it the correction between
        // its neighbours', never none between corrected photos.
        var frames = Enumerable.Range(0, 5).Select(i => GoldenExport.FrameOf($@"C:\Photos\Look\IMG_{i}.jpg") with { Exposure = new PhotoExposure(-2 + (0.4 * (i % 2))) }).ToArray();
        frames[2] = frames[2] with { Exposure = null };
        var project = new Project(TimingSettings.Default, FrameSettings.Default, OutputSettings.Default, SortMode.Manual, new LookSettings(true, 100));
        project.AddFrames(frames);
        var snapshot = project.Snapshot;
        int[] order = [0, 1, 2, 3, 4];

        var export = new PhotoFileSource([.. order.Select(i => frames[i].SourcePath)], FitMode.Fill, ProjectExporter.LooksFor(snapshot, order)!);
        var ready = new TaskCompletionSource();
        if (!ExposureCurve.TryGet(snapshot, () => ready.TrySetResult(), out _))
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        Assert.True(ExposureCurve.TryGet(snapshot, null, out var preview));
        var between = (export.LookOf(1).Stops + export.LookOf(3).Stops) / 2;
        Assert.NotEqual(0, export.LookOf(2).Stops);
        Assert.Equal(between, export.LookOf(2).Stops, 1e-5);
        Assert.Equal(export.LookOf(2).Stops, (float)preview.Correction(frames[2].Id), 1e-6);
    }

    [Fact]
    public async Task TheMeterMeasuresAPhotoOnDiskAndAMissingOneFails()
    {
        SkipWithoutGraphicsDevice();
        var folder = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", "exposure-meter-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            // A 1200 x 800 gray at linear 0.1 (log2 -3.32): the meter decodes it at 256 px and finds its brightness.
            var path = Path.Combine(folder, "gray.png");
            await new GoldenImage(LuminanceMeterTests.Grays(1200, 800, (_, _) => 0.1), 1200, 800).SavePngAsync(path);
            var meter = new ExposureMeter();

            var measured = await meter.MeasureAsync(GoldenExport.FrameOf(path), TestContext.Current.CancellationToken);
            var missing = await meter.MeasureAsync(GoldenExport.FrameOf(Path.Combine(folder, "gone.png")), TestContext.Current.CancellationToken);

            Assert.Equal(Math.Log2(LuminanceMeter.SrgbToLinear(Math.Round(LuminanceMeter.LinearToSrgb(0.1) * 255) / 255)), measured.Exposure!.Luminance, 1.0 / 64);
            Assert.Null(measured.Exposure.ExposureTime);
            Assert.Null(missing.Exposure);
            Assert.NotNull(missing.Failure);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // A photo whose linear brightness climbs from 1/32 to 1/4 of white left to right, with a vertical ripple.
    private static CanvasBitmap Gradient(CanvasDevice device, int width, int height)
    {
        var pixels = LuminanceMeterTests.Grays(width, height, (x, y) => Math.Pow(2, -5 + (3.0 * x / width)) * (1 + (0.2 * Math.Sin(y * 0.3))));
        return CanvasBitmap.CreateFromBytes(device, pixels, width, height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96f, CanvasAlphaMode.Premultiplied);
    }

    // Draws through the renderer with the look (or the overload without one, for null) into a target first cleared to grey.
    private static byte[] Draw(CanvasDevice device, CanvasBitmap photo, PixelSize output, FrameLook? look, FitMode fit = FitMode.Fill)
    {
        using var target = new CanvasRenderTarget(device, output.Width, output.Height, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 128, 128, 128));
            var settings = new FrameSettings(FrameAspect.Ratio16x9, fit);
            if (look is { } given)
            {
                FrameRenderer.Draw(session, photo, settings, output, given);
            }
            else
            {
                FrameRenderer.Draw(session, photo, settings, output);
            }
        }

        return target.GetPixelBytes();
    }
}
