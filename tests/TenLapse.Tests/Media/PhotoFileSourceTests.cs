using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Decoding;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
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

        var failure = await Assert.ThrowsAsync<PhotoDecodeException>(
            () => ExportOrSkipAsync(new PhotoFileSource([good, good, bad, good]), output, new VideoExportSettings(640, 360, new Fraction(30, 1))));

        Assert.Equal(2, failure.FrameIndex);
        Assert.Equal(SkipReason.UnsupportedFormat, failure.Failure.Reason);
        Assert.False(File.Exists(output));
    }
}
