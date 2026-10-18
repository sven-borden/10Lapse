using TenLapse.Media.Decoding;

namespace TenLapse.Tests.Media;

public sealed class PhotoCodecsTests
{
    [Fact]
    public void TheBuiltInDecodersAreListedWithTheirExtensions()
    {
        foreach (var decoder in PhotoCodecs.Decoders)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"{decoder.Name}: {string.Join(" ", decoder.Extensions)} ({string.Join(", ", decoder.MimeTypes)})");
        }

        Assert.All(["photo.jpg", "photo.JPEG", "photo.jpe", "photo.png", "photo.tif", "photo.tiff", "photo.bmp", "photo.gif", "photo.jxr"], name =>
            Assert.True(PhotoCodecs.IsReadable(name), $"{name} should be readable on any Windows"));
        Assert.Contains(PhotoCodecs.Decoders, d => d.MimeTypes.Contains("image/jpeg"));
        Assert.All(PhotoCodecs.Extensions, e => Assert.Matches("^\\.[a-z0-9]+$", e));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("clip.mp4")]
    [InlineData("no-extension")]
    [InlineData("trailing.")]
    public void OtherFilesAreNotReadable(string name) => Assert.False(PhotoCodecs.IsReadable(name));

    [Fact]
    public void TheMissingFormatsAreTheKnownOnesNoDecoderReads()
    {
        var missing = PhotoCodecs.MissingFormats;
        TestContext.Current.TestOutputHelper?.WriteLine($"Missing here: {(missing.Count == 0 ? "none" : string.Join(", ", missing.Select(f => f.Name)))}");

        Assert.All(missing, format => Assert.False(PhotoCodecs.IsReadable("x" + format.Extensions[0])));
        Assert.All(KnownPhotoFormats.All.Except(missing), format => Assert.True(PhotoCodecs.IsReadable("x" + format.Extensions[0])));
        Assert.All(missing, format => Assert.NotNull(format.Container));
    }

    [Fact]
    public void WindowsOwnDngDecoderDoesNotMakeRawAvailable()
    {
        // Windows reads .dng without the Raw Image Extension: that must not hide that .cr2, .nef and the rest need it.
        var missing = KnownPhotoFormats.Missing(new HashSet<string>(KnownPhotoFormats.All.Except([KnownPhotoFormats.Raw]).SelectMany(f => f.Extensions).Append(".dng")));

        Assert.Same(KnownPhotoFormats.Raw, Assert.Single(missing));
    }

    [Fact]
    public void WithoutDecodersEveryKnownFormatIsMissingAndHeifAndRawNameTheirStoreExtension()
    {
        var onlyJpeg = KnownPhotoFormats.Missing(new HashSet<string>([".jpg"]));

        Assert.Equal(KnownPhotoFormats.All.Count - 1, onlyJpeg.Count);
        Assert.Contains(KnownPhotoFormats.Heif, onlyJpeg);
        Assert.Equal("9PMMSR1CGPWG", KnownPhotoFormats.Heif.Container?.StoreProductId);
        Assert.Equal("9NCTDW2W1BH8", KnownPhotoFormats.Raw.Container?.StoreProductId);
    }

    [Theory]
    [InlineData("JPEG", null, null)]
    [InlineData("PNG", null, null)]
    [InlineData("HEIF", "9PMMSR1CGPWG", "9NMZLZ57R3T7")]
    [InlineData("AVIF", "9PMMSR1CGPWG", "9MVZQVXJBQ9V")]
    [InlineData("WebP", "9PG2DK419DRG", null)]
    [InlineData("JPEG XL", "9MZPRTH5C0TB", null)]
    [InlineData("Camera RAW", "9NCTDW2W1BH8", null)]
    public void EachFormatNamesTheExtensionsForItsContainerAndCodec(string name, string? container, string? codec)
    {
        var format = Assert.Single(KnownPhotoFormats.All, f => f.Name == name);

        Assert.Equal(container, format.Container?.StoreProductId);
        Assert.Equal(codec, format.Codec?.StoreProductId);
    }

    [Theory]
    [InlineData(".HEIC", "HEIF")]
    [InlineData("heif", "HEIF")]
    [InlineData(".jpeg", "JPEG")]
    [InlineData(".CR3", "Camera RAW")]
    [InlineData(".nef", "Camera RAW")]
    [InlineData(".webp", "WebP")]
    public void KnownFormatsAreFoundByExtension(string extension, string name)
    {
        Assert.Equal(name, KnownPhotoFormats.Find(extension)?.Name);
        Assert.True(KnownPhotoFormats.Find(extension)!.HasExtension(extension));
        Assert.Equal(name, KnownPhotoFormats.ForPath(@"C:\Photos\IMG_0001" + PhotoCodecs.NormalizeExtension(extension))?.Name);
    }

    [Fact]
    public void EachExtensionBelongsToOneKnownFormat()
    {
        var extensions = KnownPhotoFormats.All.SelectMany(f => f.Extensions).ToArray();

        Assert.Equal(extensions.Length, extensions.Distinct(StringComparer.Ordinal).Count());
        Assert.All(extensions, e => Assert.Matches("^\\.[a-z0-9]+$", e));
        Assert.Null(KnownPhotoFormats.Find(".txt"));
    }
}
