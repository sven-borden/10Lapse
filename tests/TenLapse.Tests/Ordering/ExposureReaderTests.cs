using TenLapse.Core.Ordering;

namespace TenLapse.Tests.Ordering;

/// <summary>
/// The Exif exposure settings deflicker reads (#45): shutter time, f-number and ISO from the Exif IFD, through the same
/// bounded walk as the capture time, in either byte order and every container.
/// </summary>
public static class ExposureReaderTests
{
    private static readonly ExifFiles.Tags _sunset = new(
        Original: "2026:05:01 20:45:10",
        ExposureTime: (1, 250),
        FNumber: (56, 10),
        Iso: 400);

    private static ExposureSettings? Read(byte[] file) => ExposureReader.Read(new MemoryStream(file));

    [Fact]
    public static void ReadsShutterApertureAndIsoFromAJpeg()
    {
        Assert.Equal(new ExposureSettings(1.0 / 250, 5.6, 400), Read(ExifFiles.Jpeg(_sunset)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public static void ReadsThemFromATiffInEitherByteOrder(bool bigEndian)
    {
        Assert.Equal(new ExposureSettings(1.0 / 250, 5.6, 400), Read(ExifFiles.RawTiff(_sunset, bigEndian)));
    }

    [Fact]
    public static void ReadsThemFromEveryContainerTheCaptureTimeComesFrom()
    {
        var expected = new ExposureSettings(1.0 / 250, 5.6, 400);
        Assert.Equal(expected, Read(ExifFiles.Png(_sunset)));
        Assert.Equal(expected, Read(ExifFiles.WebP(_sunset)));
        Assert.Equal(expected, Read(ExifFiles.Heif(_sunset)));
        Assert.Equal(expected, Read(ExifFiles.Cr3(_sunset)));
        Assert.Equal(expected, Read(ExifFiles.Cr2(_sunset)));
    }

    [Fact]
    public static void AnIsoPastTheShortTagComesFromIsoSpeed()
    {
        // PhotographicSensitivity is a SHORT: a camera at ISO 102,400 writes 65,535 there and the real value in ISOSpeed.
        var high = _sunset with { Iso = 65535, IsoSpeed = 102_400 };

        Assert.Equal(102_400, Read(ExifFiles.Jpeg(high))!.Value.Iso);
        Assert.Equal(65_535, Read(ExifFiles.Jpeg(high with { IsoSpeed = null }))!.Value.Iso);
        Assert.Equal(800, Read(ExifFiles.Jpeg(high with { Iso = null, IsoSpeed = 800 }))!.Value.Iso);
    }

    [Fact]
    public static void AMissingOrZeroSettingIsUnknown()
    {
        // A manual lens writes no f-number (or 0/0); a photo without any setting has none to read.
        Assert.Equal(new ExposureSettings(1.0 / 250, null, 400), Read(ExifFiles.Jpeg(_sunset with { FNumber = null })));
        Assert.Equal(new ExposureSettings(1.0 / 250, null, 400), Read(ExifFiles.Jpeg(_sunset with { FNumber = (0, 0) })));
        Assert.Null(Read(ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 20:45:10"))));
        Assert.Null(Read([1, 2, 3]));
        Assert.Null(ExposureReader.ReadFile(@"C:\nowhere\missing.jpg"));
    }

    [Fact]
    public static void TheCaptureTimeIsReadAsBefore()
    {
        // The exposure tags sit in the same IFD as the time tags; reading them changes nothing about the time.
        Assert.Equal(new DateTime(2026, 5, 1, 20, 45, 10), CaptureTimeReader.Read(new MemoryStream(ExifFiles.Jpeg(_sunset)))!.Value.Time);
    }

    [Theory]
    [InlineData("jpeg-apple-iphone-6-plus.jpg")]
    [InlineData("nef-nikon-d850.nef")]
    [InlineData("nef-nikon-d90-big-endian.nef")]
    [InlineData("cr3-canon-eos-m200.cr3")]
    [InlineData("dng-apple-iphone-12-pro.dng")]
    [InlineData("heic-apple-iphone-img-2927.heic")]
    public static void RealCameraFilesHavePlausibleSettings(string file)
    {
        var settings = ExposureReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "Ordering", "Fixtures", file));

        Assert.NotNull(settings);
        Assert.InRange(settings.Value.ExposureTime!.Value, 1.0 / 64_000, 60);
        Assert.InRange(settings.Value.FNumber!.Value, 0.95, 64);
        Assert.InRange(settings.Value.Iso!.Value, 12, 409_600);
    }
}
