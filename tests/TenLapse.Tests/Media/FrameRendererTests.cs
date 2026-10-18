using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Media.Export;
using TenLapse.Media.Rendering;
using TenLapse.Tests.Fixtures;
using Windows.Foundation;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>The processing stage preview and export share: Fill crops the centre, Fit letterboxes in black.</summary>
public sealed class FrameRendererTests
{
    private static readonly Color _red = Color.FromArgb(255, 255, 0, 0);
    private static readonly Color _green = Color.FromArgb(255, 0, 255, 0);
    private static readonly Color _blue = Color.FromArgb(255, 0, 0, 255);

    [Fact]
    public void A3By2PhotoLosesItsTopAndBottomIn16By9() =>
        Assert.Equal(new Rect(0, 312.5, 6000, 3375), FrameRenderer.CoverSourceRect(new Size(6000, 4000), new Size(3840, 2160)));

    [Fact]
    public void APortraitPhotoKeepsItsMiddleBandIn16By9() =>
        Assert.Equal(new Rect(0, 718.75, 1000, 562.5), FrameRenderer.CoverSourceRect(new Size(1000, 2000), new Size(1920, 1080)));

    [Fact]
    public void AWidePhotoLosesItsSidesIn4By3() =>
        Assert.Equal(new Rect(666.6666666666666, 0, 2666.6666666666665, 2000), FrameRenderer.CoverSourceRect(new Size(4000, 2000), new Size(1440, 1080)));

    [Fact]
    public void TheSameAspectKeepsTheWholePhoto() =>
        Assert.Equal(new Rect(0, 0, 7680, 4320), FrameRenderer.CoverSourceRect(new Size(7680, 4320), new Size(1920, 1080)));

    [Fact]
    public void AnEmptySizeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => FrameRenderer.CoverSourceRect(new Size(0, 10), new Size(10, 10)));
        Assert.Throws<ArgumentException>(() => FrameRenderer.ContainRect(default, new PixelSize(10, 10)));
    }

    [Theory]
    [InlineData(6000, 4000, 1920, 1080, 150, 0, 1620, 1080)] // 3:2 in 16:9: bars left and right
    [InlineData(6000, 4000, 1080, 1920, 0, 600, 1080, 720)] // 3:2 in 9:16: bars top and bottom
    [InlineData(4000, 6000, 1920, 1080, 600, 0, 720, 1080)] // 2:3 in 16:9
    [InlineData(1920, 1080, 1920, 1080, 0, 0, 1920, 1080)] // same shape: no bars
    [InlineData(96, 64, 1280, 720, 100, 0, 1080, 720)] // a small photo is scaled up
    [InlineData(3000, 2001, 1921, 1080, 151, 0, 1619, 1080)] // odd output side: the photo side is odd too, so the bars are whole pixels
    public void FitPlacesThePhotoCentredWithWholePixelBars(int width, int height, int outputWidth, int outputHeight, double x, double y, double w, double h)
    {
        var (source, destination) = FrameRenderer.Layout(new PixelSize(width, height), new PixelSize(outputWidth, outputHeight), FitMode.Fit);

        Assert.Equal(new Rect(0, 0, width, height), source);
        Assert.Equal(new Rect(x, y, w, h), destination);
    }

    [Fact]
    public void FillCoversTheWholeFrame()
    {
        var (source, destination) = FrameRenderer.Layout(new PixelSize(6000, 4000), new PixelSize(1080, 1920), FitMode.Fill);

        Assert.Equal(new Rect(0, 0, 1080, 1920), destination);
        Assert.Equal(new Rect(1875, 0, 2250, 4000), source);
    }

    [Theory]
    [InlineData(6000, 4000, 3840, 2160, 3240, 2160)]
    [InlineData(4288, 2848, 1920, 1080, 1626, 1080)]
    [InlineData(4000, 6000, 3840, 2160, 1440, 2160)]
    [InlineData(3000, 2001, 1921, 1080, 1619, 1080)]
    [InlineData(1280, 720, 3840, 2160, 1280, 720)] // never enlarged
    [InlineData(6000, 4000, 1080, 1920, 1080, 720)]
    public void AContainDecodeFitsInsideWithTheParityOfTheOutput(int width, int height, int outputWidth, int outputHeight, int scaledWidth, int scaledHeight)
    {
        var output = new PixelSize(outputWidth, outputHeight);
        var scale = PhotoDecoder.Scale(new PixelSize(width, height), FrameRenderer.DecodeOptions(FitMode.Fit, output));

        var scaled = new PixelSize(PhotoDecoder.ScaledToContain(width, scale, outputWidth), PhotoDecoder.ScaledToContain(height, scale, outputHeight));

        Assert.Equal(new PixelSize(scaledWidth, scaledHeight), scaled);
        if (scale < 1)
        {
            // Decoded at that size, the photo lands on the frame one to one.
            var destination = FrameRenderer.ContainRect(scaled, output);
            Assert.Equal((double)scaled.Width, destination.Width);
            Assert.Equal((double)scaled.Height, destination.Height);
            Assert.Equal(Math.Round(destination.X), destination.X);
            Assert.Equal(Math.Round(destination.Y), destination.Y);
        }
    }

    [Fact]
    public void FillAndFitDecodeAtTheCoverAndTheContainSize()
    {
        var output = new PixelSize(1920, 1080);

        Assert.Equal(output, FrameRenderer.DecodeOptions(FitMode.Fill, output).CoverSize);
        Assert.Null(FrameRenderer.DecodeOptions(FitMode.Fill, output).ContainSize);
        Assert.Equal(output, FrameRenderer.DecodeOptions(FitMode.Fit, output).ContainSize);
        Assert.Null(FrameRenderer.DecodeOptions(FitMode.Fit, output).CoverSize);
    }

    [Fact]
    public void FitDrawsBlackBarsLeftAndRightOfALandscapePhotoInAWiderFrame()
    {
        SkipWithoutGraphicsDevice();
        using var device = VideoDevice.Create();
        using var photo = Solid(device.Canvas, 300, 200, _red);

        var frame = Draw(device.Canvas, photo, FitMode.Fit, new PixelSize(320, 180));

        // 300x200 scaled to 180 rows is 270 wide: 25 px bars.
        Assert.Equal(25, CountFromLeft(frame, 320, 90, IsBlack));
        Assert.Equal(25, CountFromRight(frame, 320, 90, IsBlack));
        Assert.True(IsColour(frame, 320, 160, 0, _red) && IsColour(frame, 320, 160, 179, _red), "the photo does not fill the height");
    }

    [Fact]
    public void FitDrawsBlackBarsAboveAndBelowALandscapePhotoInAPortraitFrame()
    {
        SkipWithoutGraphicsDevice();
        using var device = VideoDevice.Create();
        using var photo = Solid(device.Canvas, 300, 200, _red);

        var frame = Draw(device.Canvas, photo, FitMode.Fit, new PixelSize(180, 320));

        // 300x200 scaled to 180 columns is 120 tall: 100 px bars.
        for (var y = 0; y < 99; y++)
        {
            Assert.True(IsBlack(Pixel(frame, 180, 90, y)), $"row {y} is not a bar");
            Assert.True(IsBlack(Pixel(frame, 180, 90, 319 - y)), $"row {319 - y} is not a bar");
        }

        for (var y = 101; y < 219; y++)
        {
            Assert.True(IsColour(frame, 180, 0, y, _red) && IsColour(frame, 180, 179, y, _red), $"row {y} is not the photo edge to edge");
        }
    }

    [Fact]
    public void FillShowsTheCentreOfThePhoto()
    {
        SkipWithoutGraphicsDevice();
        using var device = VideoDevice.Create();

        // Three 100 px bands; a square frame keeps the middle one only.
        using var photo = new CanvasRenderTarget(device.Canvas, 300, 100, 96f);
        using (var session = photo.CreateDrawingSession())
        {
            session.FillRectangle(0, 0, 100, 100, _blue);
            session.FillRectangle(100, 0, 100, 100, _green);
            session.FillRectangle(200, 0, 100, 100, _red);
        }

        var frame = Draw(device.Canvas, photo, FitMode.Fill, new PixelSize(160, 160));

        foreach (var (x, y) in new[] { (2, 2), (157, 2), (2, 157), (157, 157), (80, 80) })
        {
            Assert.True(IsColour(frame, 160, x, y, _green), $"pixel {x},{y} is not the centre band");
        }
    }

    [Fact]
    public void DrawingIsTheSameEveryTime()
    {
        SkipWithoutGraphicsDevice();
        using var device = VideoDevice.Create();
        using var photo = new CanvasRenderTarget(device.Canvas, 257, 171, 96f);
        using (var session = photo.CreateDrawingSession())
        {
            session.Clear(_blue);
            session.FillEllipse(128, 85, 90, 60, _red);
        }

        foreach (var fit in new[] { FitMode.Fit, FitMode.Fill })
        {
            Assert.Equal(Draw(device.Canvas, photo, fit, new PixelSize(180, 320)), Draw(device.Canvas, photo, fit, new PixelSize(180, 320)));
        }
    }

    [Fact]
    public async Task AFitSourceDecodesAtTheContainSize()
    {
        SkipWithoutGraphicsDevice();
        using var device = VideoDevice.Create();
        var path = PhotoFixtureCatalogue.Get("progressive.jpg").Path; // 256x160
        var output = new PixelSize(160, 90);

        using var fit = await new PhotoFileSource([path], FitMode.Fit).LoadAsync(0, device.Canvas, output, TestContext.Current.CancellationToken);
        using var fill = await new PhotoFileSource([path], FitMode.Fill).LoadAsync(0, device.Canvas, output, TestContext.Current.CancellationToken);

        Assert.Equal((144u, 90u), (fit.SizeInPixels.Width, fit.SizeInPixels.Height));
        Assert.Equal((160u, 100u), (fill.SizeInPixels.Width, fill.SizeInPixels.Height));
    }

    private static CanvasRenderTarget Solid(CanvasDevice device, int width, int height, Color colour)
    {
        var target = new CanvasRenderTarget(device, width, height, 96f);
        using var session = target.CreateDrawingSession();
        session.Clear(colour);
        return target;
    }

    // Draws into a target first cleared to grey, so the bars are the renderer's black and not the target's own.
    private static byte[] Draw(CanvasDevice device, CanvasBitmap photo, FitMode fit, PixelSize output)
    {
        using var target = new CanvasRenderTarget(device, output.Width, output.Height, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 128, 128, 128));
            FrameRenderer.Draw(session, photo, new FrameSettings(FrameAspect.Ratio16x9, fit), output);
        }

        return target.GetPixelBytes();
    }

    private static (byte B, byte G, byte R) Pixel(byte[] frame, int width, int x, int y)
    {
        var i = ((y * width) + x) * 4;
        return (frame[i], frame[i + 1], frame[i + 2]);
    }

    private static bool IsBlack((byte B, byte G, byte R) pixel) => pixel is { B: < 3, G: < 3, R: < 3 };

    private static bool IsColour(byte[] frame, int width, int x, int y, Color colour)
    {
        var (b, g, r) = Pixel(frame, width, x, y);
        return Math.Abs(b - colour.B) < 3 && Math.Abs(g - colour.G) < 3 && Math.Abs(r - colour.R) < 3;
    }

    private static int CountFromLeft(byte[] frame, int width, int y, Func<(byte, byte, byte), bool> test)
    {
        var x = 0;
        while (x < width && test(Pixel(frame, width, x, y)))
        {
            x++;
        }

        return x;
    }

    private static int CountFromRight(byte[] frame, int width, int y, Func<(byte, byte, byte), bool> test)
    {
        var count = 0;
        while (count < width && test(Pixel(frame, width, width - 1 - count, y)))
        {
            count++;
        }

        return count;
    }
}
