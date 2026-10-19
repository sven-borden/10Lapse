using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Decoding;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using TenLapse.Media.Rendering;
using Windows.Graphics.Imaging;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>The export's photo source decodes through <see cref="PhotoDecoder"/> at the output's cover size.</summary>
public sealed class PhotoFileSourceTests : IDisposable
{
    private readonly PhotoFixtures _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public async Task FramesDecodeAtTheCoverSizeOfTheOutput()
    {
        SkipWithoutGraphicsDevice();
        var landscape = await _files.WriteAsync("landscape.jpg", BitmapEncoder.JpegEncoderId, width: 600, height: 400);
        var portrait = await _files.WriteAsync("portrait.jpg", BitmapEncoder.JpegEncoderId, width: 600, height: 400, orientation: 6);
        var small = await _files.WriteAsync("small.png", BitmapEncoder.PngEncoderId, width: 64, height: 32);
        var source = new PhotoFileSource([landscape, portrait, small]);
        using var device = VideoDevice.Create();
        var output = new PixelSize(192, 108);

        using var first = await source.LoadAsync(0, device.Canvas, output, TestContext.Current.CancellationToken);
        using var second = await source.LoadAsync(1, device.Canvas, output, TestContext.Current.CancellationToken);
        using var third = await source.LoadAsync(2, device.Canvas, output, TestContext.Current.CancellationToken);

        // 600x400 covering 192x108: 192x128. Shown 400x600: 192x288. Smaller than the output: never enlarged.
        Assert.Equal((192u, 128u), (first.SizeInPixels.Width, first.SizeInPixels.Height));
        Assert.Equal((192u, 288u), (second.SizeInPixels.Width, second.SizeInPixels.Height));
        Assert.Equal((64u, 32u), (third.SizeInPixels.Width, third.SizeInPixels.Height));
    }

    [Fact]
    public async Task AFrameThatCannotBeDecodedNamesItsIndexAndReason()
    {
        SkipWithoutGraphicsDevice();
        var good = await _files.WriteAsync("good.jpg", BitmapEncoder.JpegEncoderId);
        var empty = _files.PathOf("empty.jpg");
        await File.WriteAllBytesAsync(empty, [], TestContext.Current.CancellationToken);
        var source = new PhotoFileSource([good, empty]);
        using var device = VideoDevice.Create();

        var failure = await Assert.ThrowsAsync<PhotoDecodeException>(
            () => source.LoadAsync(1, device.Canvas, new PixelSize(64, 36), TestContext.Current.CancellationToken));

        Assert.Equal(1, failure.FrameIndex);
        Assert.Equal(SkipReason.EmptyFile, failure.Failure.Reason);
        Assert.Equal(empty, failure.Failure.Path);
        Assert.Contains("empty.jpg", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACoverDecodedFrameLooksLikeTheFullPhotoDrawnTheSameWay()
    {
        // 640x425 covering 320x180: half size, 212.5 rows, of which the crop keeps 180. The frame drawn from the
        // cover decode must match the full photo drawn by the same processing code: same framing, no softening.
        SkipWithoutGraphicsDevice();
        var path = await _files.WritePatternAsync("pattern.png", 640, 425, (x, y) => (
            (byte)(128 + (100 * Math.Sin(x * 2 * Math.PI / 23))),
            (byte)(128 + (100 * Math.Sin(y * 2 * Math.PI / 17))),
            (byte)(255 * y / 425)));
        using var device = VideoDevice.Create();
        var output = new PixelSize(320, 180);
        using var cover = await new PhotoFileSource([path]).LoadAsync(0, device.Canvas, output, TestContext.Current.CancellationToken);
        var full = await PhotoDecoder.DecodeAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        using var fullPhoto = full.Value!;
        using var fullBitmap = CanvasBitmap.CreateFromSoftwareBitmap(device.Canvas, fullPhoto.Pixels);

        var fromCover = Draw(device.Canvas, cover, output);
        var fromFull = Draw(device.Canvas, fullBitmap, output);

        // Against the full photo: the two scalers differ (Fant, then high-quality cubic), so a small tolerance.
        var differences = fromCover.Zip(fromFull, (a, b) => Math.Abs(a - b)).ToArray();
        var mean = differences.Average();
        // Against the decoded pixels: the crop starts on a whole pixel, so drawing copies them one to one. With
        // 213 rows it started at row 16.5 and every pixel was resampled between two rows.
        var decoded = cover.GetPixelBytes();
        var (width, height) = ((int)cover.SizeInPixels.Width, (int)cover.SizeInPixels.Height);
        var crop = FrameRenderer.CoverSourceRect(new Windows.Foundation.Size(width, height), new Windows.Foundation.Size(output.Width, output.Height));
        var copied = 0;
        for (var y = 0; y < output.Height; y++)
        {
            for (var x = 0; x < output.Width * 4; x++)
            {
                var source = decoded[((((int)crop.Y + y) * width) + (int)crop.X) * 4 + x];
                copied = Math.Max(copied, Math.Abs(source - fromCover[(y * output.Width * 4) + x]));
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"cover {width}x{height}, crop at {crop.Y}: against the full photo mean {mean:0.00}, max {differences.Max()}; against the decoded pixels max {copied}");
        Assert.Equal((320, 212), (width, height));
        Assert.True(mean < 2.0, $"mean difference {mean:0.00} levels from the full photo drawn the same way");
        Assert.True(copied <= 1, $"the drawn frame differs from the decoded pixels by up to {copied} levels: resampled, not copied");
    }

    private static byte[] Draw(CanvasDevice device, CanvasBitmap bitmap, PixelSize output)
    {
        using var target = new CanvasRenderTarget(device, output.Width, output.Height, 96);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Microsoft.UI.Colors.Black);
            FrameRenderer.Draw(session, bitmap, FitMode.Fill, output);
        }

        return target.GetPixelBytes();
    }

    [Fact]
    public async Task AnExportOfPhotosInSeveralFormatsAndOrientationsPlays()
    {
        SkipWithoutH264Encoder();
        string[] paths =
        [
            await _files.WriteAsync("a.jpg", BitmapEncoder.JpegEncoderId, width: 320, height: 200),
            await _files.WriteAsync("b.png", BitmapEncoder.PngEncoderId, width: 320, height: 200),
            await _files.WriteAsync("c.jpg", BitmapEncoder.JpegEncoderId, width: 320, height: 200, orientation: 8),
            await _files.WriteAsync("d.gif", BitmapEncoder.GifEncoderId, width: 320, height: 200, frames: 2),
        ];
        var output = _files.PathOf("out.mp4");

        var result = await ExportOrSkipAsync(new PhotoFileSource(paths), output, new VideoExportSettings(640, 360, new Fraction(30, 1)));

        Assert.Equal(4, result.FrameCount);
        Assert.Equal(4, VideoFileReader.Read(output).FrameCount);
    }

    [Fact]
    public async Task AnExportWithABrokenPhotoFailsWithItsFrameAndLeavesNoFile()
    {
        SkipWithoutH264Encoder();
        var good = await _files.WriteAsync("good.jpg", BitmapEncoder.JpegEncoderId);
        var bad = _files.PathOf("text.jpg");
        await File.WriteAllTextAsync(bad, "not a photo", TestContext.Current.CancellationToken);
        var output = _files.PathOf("broken.mp4");

        var exception = await Assert.ThrowsAsync<VideoExportException>(
            () => ExportOrSkipAsync(new PhotoFileSource([good, good, bad, good]), output, new VideoExportSettings(640, 360, new Fraction(30, 1))));
        await ExportCleanup.WhenDone(output);

        Assert.Equal(VideoExportFailure.FrameUnreadable, exception.Failure);
        Assert.Equal((2, bad), (exception.FrameIndex, exception.SourcePath));
        var failure = Assert.IsType<PhotoDecodeException>(exception.InnerException);
        Assert.Equal(2, failure.FrameIndex);
        Assert.Equal(SkipReason.UnsupportedFormat, failure.Failure.Reason);
        Assert.False(File.Exists(output));
    }
}
