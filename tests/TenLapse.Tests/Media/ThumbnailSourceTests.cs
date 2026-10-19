using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Media.Preview;
using Windows.Graphics.Imaging;

namespace TenLapse.Tests.Media;

/// <summary>The filmstrip's thumbnails (#20): the embedded picture turned upright, else the proxy, else the photo.</summary>
public sealed class ThumbnailSourceTests : IDisposable
{
    private static readonly PixelSize _cover = new(48, 32);

    private readonly PhotoFixtures _photos = new();
    private readonly string _proxies = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", "thumb-proxies-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _photos.Dispose();
        try
        {
            if (Directory.Exists(_proxies))
            {
                Directory.Delete(_proxies, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Windows cleans the temp folder eventually.
        }
    }

    [Theory]
    [InlineData((ushort)1)]
    [InlineData((ushort)2)]
    [InlineData((ushort)3)]
    [InlineData((ushort)4)]
    [InlineData((ushort)5)]
    [InlineData((ushort)6)]
    [InlineData((ushort)7)]
    [InlineData((ushort)8)]
    public async Task TheEmbeddedPictureStandsUprightAsThePhotoDoes(ushort orientation)
    {
        var path = await WriteWithThumbnailAsync($"o{orientation}.jpg", 320, 200, 160, 100, orientation);

        using var embedded = await PhotoThumbnails.EmbeddedAsync(path, _cover, TestContext.Current.CancellationToken);
        var reference = await PhotoDecoder.DecodeAsync(path, new PhotoDecodeOptions { MaxSide = 160 }, TestContext.Current.CancellationToken);
        using var photo = reference.Value!;

        Assert.NotNull(embedded);
        Assert.Equal(photo.Size.Width > photo.Size.Height, embedded.Size.Width > embedded.Size.Height);
        Assert.True(embedded.Size.Width >= _cover.Width && embedded.Size.Height >= _cover.Height, $"{embedded.Size} does not cover the box");
        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            Assert.Equal(PhotoFixtures.IsRed(PhotoFixtures.Quadrant(photo, quadrant)), PhotoFixtures.IsRed(PhotoFixtures.Quadrant(embedded, quadrant)));
        }

        Assert.Equal(orientation, embedded.Info.Orientation);
    }

    [Fact]
    public async Task ItCoversTheBoxAtTheSmallestSize()
    {
        var path = await WriteWithThumbnailAsync("cover.jpg", 300, 200, 150, 100);

        using var embedded = await PhotoThumbnails.EmbeddedAsync(path, new PixelSize(48, 32), TestContext.Current.CancellationToken);

        Assert.Equal(new PixelSize(48, 32), embedded!.Size);
    }

    [Fact]
    public async Task APictureOfAnotherShapeOrTooSmallIsNotUsed()
    {
        // A 4:3 picture of a 3:2 photo has bars; one smaller than the box would be enlarged.
        var barred = await WriteWithThumbnailAsync("barred.jpg", 300, 200, 160, 120);
        var small = await WriteWithThumbnailAsync("small.jpg", 300, 200, 60, 40);

        Assert.Null(await PhotoThumbnails.EmbeddedAsync(barred, _cover, TestContext.Current.CancellationToken));
        Assert.Null(await PhotoThumbnails.EmbeddedAsync(small, new PixelSize(96, 64), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APhotoWithoutOneOrThatIsGoneHasNone()
    {
        var png = await _photos.WriteAsync("plain.png", BitmapEncoder.PngEncoderId);
        var jpeg = await _photos.WriteAsync("plain.jpg", BitmapEncoder.JpegEncoderId);

        Assert.Null(await PhotoThumbnails.EmbeddedAsync(png, _cover, TestContext.Current.CancellationToken));
        Assert.Null(await PhotoThumbnails.EmbeddedAsync(jpeg, _cover, TestContext.Current.CancellationToken));
        Assert.Null(await PhotoThumbnails.EmbeddedAsync(_photos.PathOf("gone.jpg"), _cover, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheSourceTakesTheEmbeddedPictureThenTheProxyThenThePhoto()
    {
        var withPicture = await WriteWithThumbnailAsync("with.jpg", 320, 200, 160, 100);
        var plain = await _photos.WriteAsync("plain.jpg", BitmapEncoder.JpegEncoderId, 320, 200);
        var proxied = await _photos.WriteAsync("proxied.jpg", BitmapEncoder.JpegEncoderId, 320, 200);
        var proxies = new ProxyCache(_proxies);
        var proxiedFrame = FrameOf(proxied);
        var build = await proxies.BuildAsync(proxied, proxies.KeyOf(proxiedFrame), TestContext.Current.CancellationToken);
        build.Photo!.Dispose();
        var source = new ThumbnailSource(proxies);
        var size = new ThumbnailSize(48, 32);

        using var embedded = (await source.LoadAsync(FrameOf(withPicture), size, TestContext.Current.CancellationToken)).Image;
        var fromProxy = await source.LoadAsync(proxiedFrame, size, TestContext.Current.CancellationToken);
        var fromPhoto = await source.LoadAsync(FrameOf(plain), size, TestContext.Current.CancellationToken);

        Assert.NotNull(embedded);
        Assert.Equal(ThumbnailOrigin.Proxy, fromProxy.Origin);
        Assert.Equal(ThumbnailOrigin.Photo, fromPhoto.Origin);
        Assert.Equal(32, fromPhoto.Image!.PixelHeight);
        Assert.InRange(fromPhoto.Image.PixelWidth, 48, 53);
        Assert.Equal((long)fromPhoto.Image.PixelWidth * fromPhoto.Image.PixelHeight * 4, fromPhoto.Bytes);
        fromProxy.Image!.Dispose();
        fromPhoto.Image.Dispose();
    }

    [Fact]
    public async Task AnUnreadablePhotoIsAFailureWithItsReason()
    {
        var path = _photos.PathOf("empty.jpg");
        await File.WriteAllBytesAsync(path, [], TestContext.Current.CancellationToken);
        var source = new ThumbnailSource(new ProxyCache(_proxies));

        var result = await source.LoadAsync(FrameOf(path), new ThumbnailSize(48, 32), TestContext.Current.CancellationToken);

        Assert.Null(result.Image);
        Assert.Equal(SkipReason.EmptyFile, result.Failure!.File.Reason);
    }

    [Fact]
    public async Task TheSourceSaysWhereEachCameFrom()
    {
        var withPicture = await WriteWithThumbnailAsync("origin.jpg", 320, 200, 160, 100);
        var source = new ThumbnailSource(new ProxyCache(_proxies));

        var result = await source.LoadAsync(FrameOf(withPicture), new ThumbnailSize(48, 32), TestContext.Current.CancellationToken);

        Assert.Equal(ThumbnailOrigin.Embedded, result.Origin);
        Assert.Equal(32, result.Image!.PixelHeight);
        Assert.Equal((long)result.Image.PixelWidth * result.Image.PixelHeight * 4, result.Bytes);
        result.Image!.Dispose();
    }

    private static Frame FrameOf(string path)
    {
        var info = new FileInfo(path);
        return new Frame(path, info.Length, info.LastWriteTimeUtc);
    }

    // A JPEG with WIC's own embedded thumbnail of the given size, red top-left and blue elsewhere, and an orientation.
    private async Task<string> WriteWithThumbnailAsync(string name, int width, int height, int thumbWidth, int thumbHeight, ushort? orientation = null)
    {
        var path = _photos.PathOf(name);
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, PhotoFixtures.Quadrants(width, height));
        encoder.IsThumbnailGenerated = true;
        encoder.GeneratedThumbnailWidth = (uint)thumbWidth;
        encoder.GeneratedThumbnailHeight = (uint)thumbHeight;
        if (orientation is { } value)
        {
            await encoder.BitmapProperties.SetPropertiesAsync(
                [new KeyValuePair<string, BitmapTypedValue>("System.Photo.Orientation", new BitmapTypedValue(value, Windows.Foundation.PropertyType.UInt16))]);
        }

        await encoder.FlushAsync();
        return path;
    }
}
