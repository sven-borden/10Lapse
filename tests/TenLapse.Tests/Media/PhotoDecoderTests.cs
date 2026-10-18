using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using Windows.Graphics.Imaging;
using static TenLapse.Tests.Media.PhotoFixtures;

namespace TenLapse.Tests.Media;

/// <summary>Every format and every broken file decodes or reports its reason, and nothing throws.</summary>
public sealed class PhotoDecoderTests : IDisposable
{
    private readonly PhotoFixtures _files = new();

    public void Dispose() => _files.Dispose();

    public static TheoryData<string, string> Encoders => new()
    {
        { "JPEG", ".jpg" },
        { "PNG", ".png" },
        { "TIFF", ".tif" },
        { "BMP", ".bmp" },
        { "JPEG XR", ".jxr" },
    };

    [Theory]
    [MemberData(nameof(Encoders))]
    public async Task EachBuiltInFormatDecodesWithItsPixelsInPlace(string format, string extension)
    {
        var path = await _files.WriteAsync("photo" + extension, EncoderFor(format));

        var info = await PhotoDecoder.ReadInfoAsync(path, Token);
        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(info.Succeeded, info.Failure?.Detail);
        Assert.Equal(new PixelSize(64, 32), info.Value.Size);
        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(new PixelSize(64, 32), photo.Size);
        Assert.Equal(BitmapPixelFormat.Bgra8, photo.Pixels.BitmapPixelFormat);
        Assert.Equal(BitmapAlphaMode.Premultiplied, photo.Pixels.BitmapAlphaMode);
        Assert.True(IsRed(Quadrant(photo, 0)), $"{format}: top-left is {Quadrant(photo, 0)}");
        Assert.False(IsRed(Quadrant(photo, 3)), $"{format}: bottom-right is {Quadrant(photo, 3)}");
    }

    [Fact]
    public async Task AnAnimatedGifDecodesItsFirstFrame()
    {
        var path = await _files.WriteAsync("two-frames.gif", BitmapEncoder.GifEncoderId, frames: 2);

        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(2, photo.Info.FrameCount);
        Assert.True(IsRed(Quadrant(photo, 0)), $"top-left is {Quadrant(photo, 0)}, not the first frame's red");
        Assert.Equal(255, Quadrant(photo, 3).B);
    }

    [Fact]
    public async Task WebPDecodesWhenItsDecoderIsInstalled()
    {
        Assert.SkipWhen(PhotoCodecs.DecoderFor(".webp") is null, "No WebP decoder on this PC (the Webp Image Extensions are missing).");
        // A 1x1 lossless WebP; WIC has no WebP encoder to write one.
        var path = _files.PathOf("pixel.webp");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA=="), Token);

        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(new PixelSize(1, 1), photo.Size);
    }

    [Fact]
    public async Task HeicDecodesWhenWindowsCanWriteAndReadIt()
    {
        Assert.SkipWhen(PhotoCodecs.DecoderFor(".heic") is null, "No HEIF decoder on this PC (the HEIF Image Extensions are missing).");
        var canEncode = BitmapEncoder.GetEncoderInformationEnumerator().Any(e => e.CodecId == BitmapEncoder.HeifEncoderId);
        Assert.SkipUnless(canEncode, "No HEIF encoder on this PC to write a test HEIC (it needs the HEIF and HEVC extensions).");
        string path;
        try
        {
            path = await _files.WriteAsync("photo.heic", BitmapEncoder.HeifEncoderId, width: 256, height: 128, orientation: 6);
        }
        catch (COMException exception)
        {
            Assert.Skip($"The HEIF encoder is listed but could not write a test file (0x{exception.HResult:X8}): {exception.Message}");
            throw;
        }

        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(BitmapDecoder.HeifDecoderId, photo.Info.Decoder.CodecId);
        // WIC's HEIF encoder stores orientation 6 as the container's own rotation, which the decoder applies
        // itself (it reports the turned size as stored, and orientation 1): the photo still shows as shot.
        Assert.Equal(new PixelSize(128, 256), photo.Size);
        Assert.Equal(photo.Info.Size, photo.Size);
        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            Assert.True(IsRed(Quadrant(photo, quadrant)) == (quadrant == 1), $"quadrant {quadrant} is {Quadrant(photo, quadrant)}");
        }
    }

    [Fact]
    public async Task AZeroByteFileIsEmpty()
    {
        var path = _files.PathOf("empty.jpg");
        await File.WriteAllBytesAsync(path, [], Token);

        await AssertFailsAsync(path, SkipReason.EmptyFile);
    }

    [Fact]
    public async Task ATruncatedJpegReportsThatItEndsEarly()
    {
        var whole = await _files.WriteAsync("whole.jpg", BitmapEncoder.JpegEncoderId, width: 512, height: 512, noise: true);
        var bytes = await File.ReadAllBytesAsync(whole, Token);
        var path = _files.PathOf("truncated.jpg");
        await File.WriteAllBytesAsync(path, bytes[..(bytes.Length / 2)], Token);

        // Import finds it (ReadInfoAsync), not only the export: the header alone would read fine.
        await AssertFailsAsync(path, SkipReason.Truncated);
    }

    public static TheoryData<string, string> TrailedFormats => new()
    {
        { "JPEG", ".jpg" },
        { "PNG", ".png" },
        { "GIF", ".gif" },
    };

    [Theory]
    [MemberData(nameof(TrailedFormats))]
    public async Task APhotoWithDataAfterTheImageIsWhole(string format, string extension)
    {
        // A motion photo carries its video after the JPEG's end-of-image marker, and tools append their own data
        // after a PNG's IEND or a GIF's trailer; random bytes stand in for it.
        var whole = await _files.WriteAsync("still" + extension, EncoderFor(format), width: 256, height: 256, noise: true);
        var bytes = await File.ReadAllBytesAsync(whole, Token);
        var trailer = new byte[200_000];
        new Random(7).NextBytes(trailer);
        var path = _files.PathOf("trailed" + extension);
        await File.WriteAllBytesAsync(path, [.. bytes, .. trailer], Token);

        var info = await PhotoDecoder.ReadInfoAsync(path, Token);
        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(info.Succeeded, info.Failure?.Detail);
        Assert.True(result.Succeeded, result.Failure?.Detail);
        result.Value.Dispose();
    }

    [Theory]
    [InlineData("JPEG", ".jpg", 0.99)]
    [InlineData("JPEG", ".jpg", 0.5)]
    [InlineData("JPEG", ".jpg", 0.1)]
    [InlineData("PNG", ".png", 0.9)]
    [InlineData("PNG", ".png", 0.5)]
    [InlineData("GIF", ".gif", 0.9)]
    [InlineData("GIF", ".gif", 0.5)]
    public async Task APhotoCutAnywhereInItsDataIsTruncated(string format, string extension, double kept)
    {
        // WIC decodes all three without an error, the missing part a flat band.
        var whole = await _files.WriteAsync("whole" + extension, EncoderFor(format), width: 256, height: 256, noise: true);
        var bytes = await File.ReadAllBytesAsync(whole, Token);
        var path = _files.PathOf("cut" + extension);
        await File.WriteAllBytesAsync(path, bytes[..(int)(bytes.Length * kept)], Token);

        await AssertFailsAsync(path, SkipReason.Truncated);
    }

    [Fact]
    public async Task APngNamedJpgDecodesByItsContent()
    {
        var path = _files.PathOf("really-a-png.jpg");
        await WriteToAsync(path, BitmapEncoder.PngEncoderId);

        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(BitmapDecoder.PngDecoderId, photo.Info.Decoder.CodecId);
        Assert.True(IsRed(Quadrant(photo, 0)));
    }

    [Fact]
    public async Task ASixteenBitPngConvertsToBgra8()
    {
        var path = await _files.WritePngAsync("deep.png", BitmapPixelFormat.Rgba16);

        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(BitmapPixelFormat.Bgra8, photo.Pixels.BitmapPixelFormat);
        Assert.Equal((0, 0, 255), Quadrant(photo, 0));
        Assert.Equal((255, 0, 0), Quadrant(photo, 3));
    }

    [Fact]
    public async Task AGrayscalePngConvertsToBgra8()
    {
        var path = _files.WriteGrayPng("gray.png");

        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal((200, 200, 200), Quadrant(photo, 0));
        Assert.Equal((40, 40, 40), Quadrant(photo, 3));
    }

    [Fact]
    public async Task APalettePngConvertsToBgra8()
    {
        var path = _files.WritePalettePng("palette.png");

        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal((0, 0, 255), Quadrant(photo, 0));
        Assert.Equal((255, 0, 0), Quadrant(photo, 3));
    }

    [Fact]
    public async Task ACmykTiffConvertsToBgra8()
    {
        // WinRT's encoders take no CMYK input, so no CMYK JPEG can be written here: #33's fixture brings a real
        // one. A hand-written CMYK TIFF goes through the same CMYK-to-BGRA conversion.
        var path = _files.WriteCmykTiff("cmyk.tif");

        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        var red = Quadrant(photo, 0);
        var blue = Quadrant(photo, 3);
        Assert.True(red.R > 150 && red.B < 100, $"magenta+yellow decoded as {red}");
        Assert.True(blue.B > 100 && blue.R < 100, $"cyan+magenta decoded as {blue}");
    }

    public static TheoryData<string> SrgbProfiles => ["hand-built", "Windows"];

    [Theory]
    [MemberData(nameof(SrgbProfiles))]
    public async Task AJpegWithAnSrgbProfileIsNotConverted(string which)
    {
        // WinRT converts any embedded profile at the stored size, before scaling: 2.8 times the decode time for
        // nothing when the profile is sRGB, as most cameras' and phones' are.
        Assert.SkipWhen(which == "Windows" && !File.Exists(IccProfiles.WindowsSrgbPath), "This PC has no sRGB Color Space Profile.icm.");
        var profile = which == "Windows" ? await File.ReadAllBytesAsync(IccProfiles.WindowsSrgbPath, Token) : IccProfiles.Srgb();
        var path = await WriteJpegWithProfileAsync("srgb.jpg", profile);

        var result = await PhotoDecoder.DecodeAsync(path, PhotoDecodeOptions.Cover(new PixelSize(192, 108)), Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(ColourConversion.None, photo.Colour);
        Assert.Equal(new PixelSize(192, 128), photo.Size);
        Assert.True(IsRed(Quadrant(photo, 0)), $"top-left is {Quadrant(photo, 0)}");
        var blue = Quadrant(photo, 3);
        Assert.True(blue.B > 240 && blue.R < 20, $"bottom-right is {blue}");
    }

    [Fact]
    public async Task AJpegWithAnotherProfileIsConvertedAfterScaling()
    {
        // Red and blue primaries swapped: what the file stores as red is blue on screen, and the other way round.
        var path = await WriteJpegWithProfileAsync("swapped.jpg", IccProfiles.SwappedRedAndBlue());

        var result = await PhotoDecoder.DecodeAsync(path, PhotoDecodeOptions.Cover(new PixelSize(192, 108)), Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(ColourConversion.AfterScaling, photo.Colour);
        Assert.Equal(new PixelSize(192, 128), photo.Size);
        var (topLeft, bottomRight) = (Quadrant(photo, 0), Quadrant(photo, 3));
        Assert.True(topLeft.B > 200 && topLeft.R < 60 && topLeft.G < 60, $"stored red shows as {topLeft}, not blue");
        Assert.True(IsRed(bottomRight), $"stored blue shows as {bottomRight}, not red");
    }

    [Fact]
    public async Task APhotoWithoutAProfileIsNotConverted()
    {
        var path = await _files.WriteAsync("plain.jpg", BitmapEncoder.JpegEncoderId);

        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(ColourConversion.None, photo.Colour);
    }

    // Stored 64x32 with red top-left. Orientation 3 turns it upside down; 6 is stored turned a quarter
    // anticlockwise, so it is shown turned clockwise (stored top-left to the top-right); 8 the other way.
    [Theory]
    [InlineData((ushort)3, 64, 32, 3)]
    [InlineData((ushort)6, 32, 64, 1)]
    [InlineData((ushort)8, 32, 64, 2)]
    public async Task ExifOrientationTurnsThePhotoAsShot(ushort orientation, int width, int height, int redQuadrant)
    {
        var path = await _files.WriteAsync($"orientation-{orientation}.jpg", BitmapEncoder.JpegEncoderId, orientation: orientation);

        var info = await PhotoDecoder.ReadInfoAsync(path, Token);
        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(info.Succeeded, info.Failure?.Detail);
        Assert.Equal(orientation, info.Value.Orientation);
        Assert.Equal(new PixelSize(width, height), info.Value.Size);
        Assert.Equal(new PixelSize(64, 32), info.Value.StoredSize);
        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(new PixelSize(width, height), photo.Size);
        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            Assert.True(IsRed(Quadrant(photo, quadrant)) == (quadrant == redQuadrant), $"quadrant {quadrant} is {Quadrant(photo, quadrant)}");
        }
    }

    [Fact]
    public async Task APhotoWithoutOrientationReadsAsOne()
    {
        var path = await _files.WriteAsync("plain.png", BitmapEncoder.PngEncoderId);

        var info = await PhotoDecoder.ReadInfoAsync(path, Token);

        Assert.True(info.Succeeded, info.Failure?.Detail);
        Assert.Equal(1, info.Value.Orientation);
        Assert.Equal(1, info.Value.FrameCount);
        Assert.Equal(BitmapDecoder.PngDecoderId, info.Value.Decoder.CodecId);
    }

    [Fact]
    public async Task AFileLockedByAnotherProgramIsInUse()
    {
        var path = await _files.WriteAsync("locked.jpg", BitmapEncoder.JpegEncoderId);
        using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await AssertFailsAsync(path, SkipReason.FileInUse);
    }

    [Fact]
    public async Task AFileAnotherProgramHasOpenForWritingStillDecodes()
    {
        // 10Lapse shares read, write and delete: an editor or a camera import that keeps the file open is fine.
        var path = await _files.WriteAsync("open-elsewhere.jpg", BitmapEncoder.JpegEncoderId);
        using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        result.Value.Dispose();
    }

    [Fact]
    public async Task AFileReadOrDecodedCanBeRewrittenInPlaceAndDeletedAtOnce()
    {
        // An editor saving over the photo truncates and rewrites it, which Windows refuses while any process maps
        // the file (ERROR_USER_MAPPED_FILE). No garbage collection in between: nothing may wait for a finalizer.
        var path = await _files.WriteAsync("rewritten.jpg", BitmapEncoder.JpegEncoderId);
        var original = await File.ReadAllBytesAsync(path, Token);
        var edited = await File.ReadAllBytesAsync(await _files.WriteAsync("edited.jpg", BitmapEncoder.JpegEncoderId, width: 32, height: 32), Token);

        var info = await PhotoDecoder.ReadInfoAsync(path, Token);
        await File.WriteAllBytesAsync(path, edited, Token);
        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            file.SetLength(0);
            await file.WriteAsync(original, Token);
        }

        File.Delete(path);

        Assert.True(info.Succeeded, info.Failure?.Detail);
        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(new PixelSize(32, 32), photo.Size);
        Assert.False(File.Exists(path));
    }

    public static TheoryData<string> PathForms => ["extended", "extended UNC", "UNC", "forward slashes", "dot segment", "dot-dot segment", "relative"];

    [Theory]
    [MemberData(nameof(PathForms))]
    public async Task EveryFormOfPathOpens(string form)
    {
        var path = await _files.WriteAsync("form.jpg", BitmapEncoder.JpegEncoderId);
        var share = @"\\localhost\" + path[0] + "$" + path[2..];
        var name = Path.GetFileName(_files.Directory);
        var written = form switch
        {
            "extended" => @"\\?\" + path,
            "extended UNC" => @"\\?\UNC\localhost\" + path[0] + "$" + path[2..],
            "UNC" => share,
            "forward slashes" => path.Replace('\\', '/'),
            "dot segment" => Path.Combine(_files.Directory, ".", "form.jpg"),
            "dot-dot segment" => Path.Combine(_files.Directory, "..", name, ".", "form.jpg"),
            "relative" => Path.GetRelativePath(Environment.CurrentDirectory, path),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };
        Assert.SkipWhen(form.Contains("UNC", StringComparison.Ordinal) && !File.Exists(share), $"The administrative share {share} is not reachable here.");

        var info = await PhotoDecoder.ReadInfoAsync(written, Token);
        var result = await PhotoDecoder.DecodeAsync(written, cancellationToken: Token);

        Assert.True(info.Succeeded, $"{written}: {info.Failure?.Detail}");
        Assert.True(result.Succeeded, $"{written}: {result.Failure?.Detail}");
        result.Value.Dispose();
    }

    [Fact]
    public async Task ADeletedFileOrFolderIsNotFound()
    {
        var path = await _files.WriteAsync("gone.jpg", BitmapEncoder.JpegEncoderId);
        File.Delete(path);

        await AssertFailsAsync(path, SkipReason.FileNotFound);
        await AssertFailsAsync(Path.Combine(_files.Directory, "no such folder", "x.jpg"), SkipReason.FileNotFound);
    }

    [Fact]
    public async Task AFileWindowsWillNotLetUsReadIsAccessDenied()
    {
        var path = await _files.WriteAsync("denied.jpg", BitmapEncoder.JpegEncoderId);
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny);
        security.AddAccessRule(rule);
        file.SetAccessControl(security);
        try
        {
            await AssertFailsAsync(path, SkipReason.AccessDenied);
        }
        finally
        {
            security.RemoveAccessRule(rule);
            file.SetAccessControl(security);
        }
    }

    [Fact]
    public async Task AFolderPassedAsAFileIsAccessDenied() =>
        await AssertFailsAsync(_files.Directory, SkipReason.AccessDenied);

    [Fact]
    public async Task ALongUnicodePathDecodes()
    {
        var directory = _files.Directory;
        while (directory.Length < 300)
        {
            directory = Path.Combine(directory, "Fotos für den Zeitraffer – 夕焼け");
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Été 2026 – 日の出 🌅.jpg");
        await WriteToAsync(path, BitmapEncoder.JpegEncoderId);

        var info = await PhotoDecoder.ReadInfoAsync(path, Token);
        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(path.Length > 260);
        Assert.True(info.Succeeded, info.Failure?.Detail);
        Assert.True(result.Succeeded, result.Failure?.Detail);
        result.Value.Dispose();
    }

    [Fact]
    public async Task AnImageTooLargeToDecodeWholeSaysSoButItsSizeReads()
    {
        var path = _files.WriteHugePngHeader("huge.png", 70_000, 70_000);

        var info = await PhotoDecoder.ReadInfoAsync(path, Token);
        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.True(info.Succeeded, info.Failure?.Detail);
        Assert.Equal(new PixelSize(70_000, 70_000), info.Value.Size);
        Assert.False(result.Succeeded);
        Assert.Equal(SkipReason.ImageTooLarge, result.Failure.Reason);
    }

    [Theory]
    [InlineData("notes.jpg")]
    [InlineData("notes.png")]
    [InlineData("notes.heic")]
    [InlineData("notes.cr2")]
    [InlineData("notes.txt")]
    public async Task TextIsNotAPhotoFormat(string name)
    {
        var path = _files.PathOf(name);
        await File.WriteAllTextAsync(path, "These are not the pixels you are looking for.", Token);

        // HEIF and RAW have their decoders here; on a PC without them, these would need the extension instead.
        var expected = PhotoCodecs.DecoderFor(Path.GetExtension(name)) is null && KnownPhotoFormats.ForPath(name)?.Extension is not null
            ? SkipReason.NeedsExtension
            : SkipReason.UnsupportedFormat;
        await AssertFailsAsync(path, expected);
    }

    [Fact]
    public void WithoutItsDecoderAHeicNeedsTheHeifExtension()
    {
        var unknown = new COMException("No component found.", unchecked((int)0x88982F50));

        var failure = PhotoFailures.Classify(unknown, @"C:\Photos\IMG_4402.HEIC", PhotoFailures.Stage.Open, _ => null);
        var raw = PhotoFailures.Classify(unknown, @"C:\Photos\DSC_0001.NEF", PhotoFailures.Stage.Open, _ => null);
        var withDecoder = PhotoFailures.Classify(unknown, @"C:\Photos\IMG_4402.HEIC", PhotoFailures.Stage.Open, e => PhotoCodecs.Decoders[0]);
        var codecMissing = PhotoFailures.Classify(unknown, @"C:\Photos\IMG_4402.HEIC", PhotoFailures.Stage.Decode, e => PhotoCodecs.Decoders[0]);
        var jpeg = PhotoFailures.Classify(unknown, @"C:\Photos\IMG_4402.jpg", PhotoFailures.Stage.Open, _ => null);

        Assert.Equal(SkipReason.NeedsExtension, failure.Reason);
        Assert.Equal(KnownPhotoFormats.HeifExtension, failure.Format?.Extension);
        Assert.Equal(SkipReason.NeedsExtension, raw.Reason);
        Assert.Equal(KnownPhotoFormats.RawExtension, raw.Format?.Extension);
        Assert.Equal(SkipReason.UnsupportedFormat, withDecoder.Reason);
        Assert.Equal(SkipReason.NeedsExtension, codecMissing.Reason);
        Assert.Equal(SkipReason.UnsupportedFormat, jpeg.Reason);
        Assert.Contains("WINCODEC_ERR_COMPONENTNOTFOUND", failure.Detail, StringComparison.Ordinal);
        Assert.Same(unknown, failure.Exception);
    }

    [Theory]
    [InlineData(0x80070020u, SkipReason.FileInUse)]
    [InlineData(0x80070021u, SkipReason.FileInUse)]
    [InlineData(0x80070005u, SkipReason.AccessDenied)]
    [InlineData(0x80070002u, SkipReason.FileNotFound)]
    [InlineData(0x80070003u, SkipReason.FileNotFound)]
    [InlineData(0x88982F61u, SkipReason.DecodeFailed)]
    [InlineData(0x88982F60u, SkipReason.DecodeFailed)]
    [InlineData(0x88982F70u, SkipReason.DecodeFailed)]
    [InlineData(0x88982F72u, SkipReason.Truncated)]
    [InlineData(0x80070026u, SkipReason.Truncated)]
    [InlineData(0x88982F51u, SkipReason.ImageTooLarge)]
    [InlineData(0x8007000Eu, SkipReason.ImageTooLarge)]
    [InlineData(0x88982F80u, SkipReason.UnsupportedFormat)]
    [InlineData(0x80004005u, SkipReason.Other)]
    public void HResultsMapToReasons(uint hresult, SkipReason expected)
    {
        var failure = PhotoFailures.Classify(new COMException("x", unchecked((int)hresult)), @"C:\Photos\a.jpg", PhotoFailures.Stage.Decode);

        Assert.Equal(expected, failure.Reason);
        Assert.Equal(expected, failure.ToSkippedFile().Reason);
        Assert.Contains($"0x{hresult:X8}", failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACoverSizeShrinksDuringDecodeAndNeverEnlarges()
    {
        var path = await _files.WriteAsync("big.jpg", BitmapEncoder.JpegEncoderId, width: 600, height: 400);

        var cover = await PhotoDecoder.DecodeAsync(path, PhotoDecodeOptions.Cover(new PixelSize(192, 108)), Token);
        var larger = await PhotoDecoder.DecodeAsync(path, PhotoDecodeOptions.Cover(new PixelSize(1920, 1080)), Token);
        var capped = await PhotoDecoder.DecodeAsync(path, new PhotoDecodeOptions { MaxSide = 300 }, Token);

        using var coverPhoto = cover.Value!;
        using var largerPhoto = larger.Value!;
        using var cappedPhoto = capped.Value!;
        Assert.Equal(new PixelSize(192, 128), coverPhoto.Size);
        Assert.Equal(new PixelSize(600, 400), coverPhoto.Info.Size);
        Assert.True(IsRed(Quadrant(coverPhoto, 0)));
        Assert.Equal(new PixelSize(600, 400), largerPhoto.Size);
        Assert.Equal(new PixelSize(300, 200), cappedPhoto.Size);
    }

    [Fact]
    public async Task ACoverSizeAppliesToThePhotoAsTurned()
    {
        // Stored 64x32, shown 32x64. Covering 16x16 halves it: 16x32 as shown.
        var path = await _files.WriteAsync("turned.jpg", BitmapEncoder.JpegEncoderId, orientation: 6);

        var result = await PhotoDecoder.DecodeAsync(path, PhotoDecodeOptions.Cover(new PixelSize(16, 16)), Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(new PixelSize(16, 32), photo.Size);
        Assert.True(IsRed(Quadrant(photo, 1)), $"top-right is {Quadrant(photo, 1)}");
    }

    [Theory]
    [InlineData(6000, 4000, 3840, 2160, 0.64)]
    [InlineData(4000, 6000, 3840, 2160, 0.96)]
    [InlineData(6000, 4000, 1920, 1080, 0.32)]
    [InlineData(1280, 720, 3840, 2160, 1.0)]
    public void TheCoverScaleFillsTheOutput(int width, int height, int outputWidth, int outputHeight, double expected)
    {
        var scale = PhotoDecoder.Scale(new PixelSize(width, height), PhotoDecodeOptions.Cover(new PixelSize(outputWidth, outputHeight)));

        Assert.Equal(expected, scale, 6);
        Assert.True(PhotoDecoder.Scaled(width, scale) >= Math.Min(width, outputWidth));
        Assert.True(PhotoDecoder.Scaled(height, scale) >= Math.Min(height, outputHeight));
    }

    [Fact]
    public async Task ACancelledDecodeThrowsCancellation()
    {
        var path = await _files.WriteAsync("cancelled.jpg", BitmapEncoder.JpegEncoderId);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PhotoDecoder.DecodeAsync(path, cancellationToken: cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PhotoDecoder.ReadInfoAsync(path, cancelled.Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // A 600x400 JPEG, red top-left and blue elsewhere, with the profile embedded.
    private async Task<string> WriteJpegWithProfileAsync(string name, byte[] profile)
    {
        var plain = await _files.WriteAsync("plain-" + name, BitmapEncoder.JpegEncoderId, width: 600, height: 400);
        var path = _files.PathOf(name);
        await File.WriteAllBytesAsync(path, IccProfiles.Embed(await File.ReadAllBytesAsync(plain, Token), profile), Token);
        return path;
    }

    private static Guid EncoderFor(string format) => format switch
    {
        "JPEG" => BitmapEncoder.JpegEncoderId,
        "PNG" => BitmapEncoder.PngEncoderId,
        "TIFF" => BitmapEncoder.TiffEncoderId,
        "BMP" => BitmapEncoder.BmpEncoderId,
        "JPEG XR" => BitmapEncoder.JpegXREncoderId,
        "GIF" => BitmapEncoder.GifEncoderId,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    // Both entry points report the same reason, with an exception for the log unless the decoder found it itself.
    private static async Task AssertFailsAsync(string path, SkipReason reason)
    {
        var info = await PhotoDecoder.ReadInfoAsync(path, Token);
        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);

        Assert.False(info.Succeeded, $"{Path.GetFileName(path)} read as {info.Value}");
        Assert.False(result.Succeeded, $"{Path.GetFileName(path)} decoded");
        Assert.Equal(reason, info.Failure.Reason);
        Assert.Equal(reason, result.Failure.Reason);
        Assert.Equal(path, result.Failure.Path);
        Assert.False(string.IsNullOrWhiteSpace(result.Failure.Detail));
    }
}
