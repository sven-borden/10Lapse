using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Media.Encoding;
using TenLapse.Media.Export;
using TenLapse.Media.Rendering;
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
        Assert.SkipWhen(PhotoCodecs.DecoderFor(".webp") is null, "No WebP decoder on this PC (the WebP Image Extension are missing).");
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
        Assert.SkipWhen(PhotoCodecs.DecoderFor(".heic") is null, "No HEIF decoder on this PC (the HEIF Image Extension are missing).");
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
        var expected = PhotoCodecs.DecoderFor(Path.GetExtension(name)) is null && KnownPhotoFormats.ForPath(name)?.Container is not null
            ? SkipReason.NeedsExtension
            : SkipReason.UnsupportedFormat;
        await AssertFailsAsync(path, expected);
    }

    // ftyp boxes as a phone writes them: an iPhone HEIC, an AVIF, and a HEIF that names no codec.
    private static readonly byte[] _heicHeader = [0, 0, 0, 0x18, .. "ftypheic"u8, 0, 0, 0, 0, .. "mif1heic"u8];
    private static readonly byte[] _avifHeader = [0, 0, 0, 0x1C, .. "ftypavif"u8, 0, 0, 0, 0, .. "avifmif1miaf"u8];
    private static readonly byte[] _plainHeifHeader = [0, 0, 0, 0x14, .. "ftypmif1"u8, 0, 0, 0, 0, .. "mif1"u8];

    // name, stage, HRESULT, is the decoder for the extension installed, header, the extension expected (null: unreadable, not NeedsExtension)
    private static readonly byte[] _textHeader = [.. "Just some notes, not a photo at all."u8];

    public static TheoryData<string, string, uint, bool, string, string?> MissingComponents => new()
    {
        // No decoder claims the extension: the extension that adds the decoder.
        { "IMG_4402.HEIC", "Open", 0x88982F50, false, "", "HEIF Image Extension" },
        { "IMG_4402.avif", "Open", 0x88982F50, false, "", "HEIF Image Extension" },
        { "DSC_0001.NEF", "Open", 0x88982F50, false, "", "Raw Image Extension" },
        { "photo.webp", "Open", 0x88982F50, false, "", "WebP Image Extension" },
        { "photo.jxl", "Open", 0x88982F50, false, "", "JPEG XL Image Extension" },
        // The HEIF decoder is there (a fresh Windows 11) but cannot decode the image inside: the codec.
        { "IMG_4402.HEIC", "Open", 0x88982F81, true, "heic", "HEVC Video Extensions" },
        { "IMG_4402.HEIC", "Open", 0x88982F50, true, "heic", "HEVC Video Extensions" },
        { "IMG_4402.HEIC", "Decode", 0xC00D5212, true, "heic", "HEVC Video Extensions" },
        { "IMG_4402.HEIC", "Decode", 0x88982F50, true, "", "HEVC Video Extensions" },
        { "IMG_4402.avif", "Open", 0x88982F81, true, "avif", "AV1 Video Extension" },
        { "renamed.jpg", "Open", 0x88982F81, true, "heic", "HEVC Video Extensions" },
        // Text named .heic or .avif on a PC with no HEIF decoder: the first bytes are no HEIF file, so it is unreadable, not a Store visit.
        { "notes.heic", "Open", 0x88982F50, false, "text", null },
        { "notes.avif", "Open", 0x88982F50, false, "text", null },
        // RAW has no cheap check of its first bytes: with no decoder it is told by its extension.
        { "DSC_0001.NEF", "Open", 0x88982F50, false, "text", "Raw Image Extension" },
        // Unreadable, not a missing extension: text named .heic, a HEIF naming no codec opened, a JPEG, RAW with its decoder.
        { "notes.heic", "Open", 0x88982F50, true, "", null },
        { "IMG_4402.HEIC", "Open", 0x88982F50, true, "mif1", null },
        { "IMG_4402.jpg", "Decode", 0x88982F50, false, "", null },
        { "DSC_0001.NEF", "Decode", 0x88982F50, true, "", null },
    };

    [Theory]
    [MemberData(nameof(MissingComponents))]
    public void AMissingComponentNamesTheExtensionToInstall(string name, string stage, uint hresult, bool decoderInstalled, string brand, string? expected)
    {
        // The HEVC codec is installed here, so the fresh-Windows case is injected: the HRESULTs are those seen
        // with a HEIC whose image is coded in a codec this PC lacks (see AHeicWhoseCodecIsMissingNeedsTheCodecExtension).
        var header = brand switch { "heic" => _heicHeader, "avif" => _avifHeader, "mif1" => _plainHeifHeader, "text" => _textHeader, _ => null };
        var exception = new COMException("Component missing.", unchecked((int)hresult));

        var failure = PhotoFailures.Classify(exception, @"C:\Photos\" + name, Enum.Parse<PhotoFailures.Stage>(stage), _ => decoderInstalled ? PhotoCodecs.Decoders[0] : null, header);

        Assert.Equal(expected, failure.MissingExtension?.Name);
        Assert.Equal(expected is null ? SkipReason.UnsupportedFormat : SkipReason.NeedsExtension, failure.Reason);
        Assert.Same(exception, failure.Exception);
        if (expected is not null)
        {
            Assert.Contains(failure.MissingExtension!.StoreProductId, failure.Detail, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(MissingComponents))]
    public void TheSkippedFileCarriesTheExtensionsInInstallOrder(string name, string stage, uint hresult, bool decoderInstalled, string brand, string? expected)
    {
        // #14: a missing decoder adds the format's codec as a second candidate (the import asks the PC whether it has
        // it); a missing codec, or a missing RAW decoder, is alone; a broken file carries none.
        var header = brand switch { "heic" => _heicHeader, "avif" => _avifHeader, "mif1" => _plainHeifHeader, "text" => _textHeader, _ => null };
        var exception = new COMException("Component missing.", unchecked((int)hresult));

        var skipped = PhotoFailures.Classify(exception, @"C:\Photos\" + name, Enum.Parse<PhotoFailures.Stage>(stage), _ => decoderInstalled ? PhotoCodecs.Decoders[0] : null, header).ToSkippedFile();

        var names = skipped.MissingExtensions.Select(extension => extension.Name).ToArray();
        string[] expectedNames = (name.ToLowerInvariant(), expected) switch
        {
            (_, null) => [],
            (var n, "HEIF Image Extension") when n.EndsWith(".heic", StringComparison.Ordinal) => ["HEIF Image Extension", "HEVC Video Extensions"],
            (var n, "HEIF Image Extension") when n.EndsWith(".avif", StringComparison.Ordinal) => ["HEIF Image Extension", "AV1 Video Extension"],
            (_, var only) => [only],
        };
        Assert.Equal(expectedNames, names);
        Assert.Equal(expected is null ? SkipReason.UnsupportedFormat : SkipReason.NeedsExtension, skipped.Reason);
    }

    [Fact]
    public void OnlyASoftwareHevcDecoderCountsAsTheHevcExtensionBeingInstalled()
    {
        // A vendor's hardware HEVC decoder (Intel, AMD, an OEM) is not what WIC's HEIF decoder uses: HEIC would still fail.
        static DecoderMft Decoder(bool hardware) => new(VideoFormat.Mp4Hevc, "HEVC decoder", hardware, Guid.NewGuid());

        Assert.False(PhotoCodecs.IsInstalled(KnownPhotoFormats.HevcExtension, _ => []));
        Assert.False(PhotoCodecs.IsInstalled(KnownPhotoFormats.HevcExtension, _ => [Decoder(hardware: true)]));
        Assert.True(PhotoCodecs.IsInstalled(KnownPhotoFormats.HevcExtension, _ => [Decoder(hardware: true), Decoder(hardware: false)]));
        Assert.True(PhotoCodecs.IsInstalled(KnownPhotoFormats.HevcExtension, _ => [Decoder(hardware: false)]));

        // When Media Foundation cannot be asked, the answer is "installed": "missing" must be certain.
        Assert.True(PhotoCodecs.IsInstalled(KnownPhotoFormats.HevcExtension, _ => throw new COMException("No Media Foundation.", unchecked((int)0xC00D36B0))));
    }

    [Fact]
    public void TheHeifDecoderOnThisPcIsFoundWhenItIsThereAndAnUnknownExtensionCountsAsInstalled()
    {
        // Asked of Windows each time: a refresh in between changes nothing about a PC that did not change.
        var before = PhotoCodecs.IsInstalled(KnownPhotoFormats.HeifExtension);
        PhotoCodecs.Refresh();
        var after = PhotoCodecs.IsInstalled(KnownPhotoFormats.HeifExtension);

        Assert.Equal(before, after);
        Assert.Equal(PhotoCodecs.DecoderFor(".heic") is not null, after);
        Assert.Equal(PhotoCodecs.DecoderFor(".cr2") is not null, PhotoCodecs.IsInstalled(KnownPhotoFormats.RawExtension));

        // The AV1 codec cannot be told here, so it is never reported as missing.
        Assert.True(PhotoCodecs.IsInstalled(KnownPhotoFormats.Av1Extension));
    }

    [Fact]
    public void TheDecoderListIsReadAgainAfterARefreshSoAnInstallDuringTheSessionIsSeen()
    {
        var before = PhotoCodecs.Decoders;
        PhotoCodecs.Refresh();

        var after = PhotoCodecs.Decoders;

        Assert.NotSame(before, after);
        Assert.Equal(before.Select(d => d.CodecId), after.Select(d => d.CodecId));
    }

    [Fact]
    public async Task AHeicWhoseCodecIsMissingNeedsTheCodecExtension()
    {
        // HEVC is installed here, so a missing codec is provoked: a HEIC written by WIC with its image item and
        // decoder configuration renamed from HEVC (hvc1, hvcC) to VVC (vvc1, vvcC), which this PC cannot decode.
        // The HEIF decoder takes the file and refuses the image, as it does a HEVC image without the HEVC codec.
        Assert.SkipWhen(PhotoCodecs.DecoderFor(".heic") is null, "No HEIF decoder on this PC.");
        Assert.SkipUnless(BitmapEncoder.GetEncoderInformationEnumerator().Any(e => e.CodecId == BitmapEncoder.HeifEncoderId), "No HEIF encoder on this PC to write a test HEIC.");
        var heic = await _files.WriteAsync("hevc.heic", BitmapEncoder.HeifEncoderId, width: 256, height: 128);
        var bytes = await File.ReadAllBytesAsync(heic, Token);
        Assert.Equal(1, Replace(bytes, "hvc1"u8, "vvc1"u8));
        Assert.Equal(1, Replace(bytes, "hvcC"u8, "vvcC"u8));
        var path = _files.PathOf("vvc.heic");
        await File.WriteAllBytesAsync(path, bytes, Token);

        await AssertFailsAsync(path, SkipReason.NeedsExtension);
        var result = await PhotoDecoder.DecodeAsync(path, cancellationToken: Token);
        Assert.Equal(KnownPhotoFormats.HevcExtension, result.Failure?.MissingExtension);
    }

    [Fact]
    public async Task ATruncatedHeicIsDamaged()
    {
        Assert.SkipWhen(PhotoCodecs.DecoderFor(".heic") is null, "No HEIF decoder on this PC.");
        Assert.SkipUnless(BitmapEncoder.GetEncoderInformationEnumerator().Any(e => e.CodecId == BitmapEncoder.HeifEncoderId), "No HEIF encoder on this PC to write a test HEIC.");
        var heic = await _files.WriteAsync("whole.heic", BitmapEncoder.HeifEncoderId, width: 256, height: 128);
        var bytes = await File.ReadAllBytesAsync(heic, Token);
        var path = _files.PathOf("cut.heic");
        await File.WriteAllBytesAsync(path, bytes[..(bytes.Length / 2)], Token);

        // The HEIF decoder says MF_E_INVALID_FILE_FORMAT.
        await AssertFailsAsync(path, SkipReason.DecodeFailed);
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
    [InlineData(0xC00D36BEu, SkipReason.DecodeFailed)]
    [InlineData(0x800700A1u, SkipReason.Other)]
    [InlineData(0x80070040u, SkipReason.FileNotFound)]
    [InlineData(0x8007003Bu, SkipReason.FileNotFound)]
    [InlineData(0x8007016Au, SkipReason.CloudFileOffline)]
    [InlineData(0x80070185u, SkipReason.CloudFileOffline)]
    [InlineData(0x80070780u, SkipReason.CloudFileOffline)]
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

    // Stored 64x32 with red top-left, shown 32x64 for orientations 5 to 8: 5 transposes (red stays top-left), 6 turns
    // clockwise (top-right), 7 transposes the other way (bottom-right), 8 turns anticlockwise (bottom-left).
    // Contained in 32x32, it halves: 16x32 as shown.
    [Theory]
    [InlineData((ushort)5, 0)]
    [InlineData((ushort)6, 1)]
    [InlineData((ushort)7, 3)]
    [InlineData((ushort)8, 2)]
    public async Task AContainSizeAppliesToThePhotoAsTurned(ushort orientation, int redQuadrant)
    {
        var path = await _files.WriteAsync($"contain-{orientation}.jpg", BitmapEncoder.JpegEncoderId, orientation: orientation);

        var result = await PhotoDecoder.DecodeAsync(path, PhotoDecodeOptions.Contain(new PixelSize(32, 32)), Token);

        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(new PixelSize(16, 32), photo.Size);
        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            Assert.True(IsRed(Quadrant(photo, quadrant)) == (quadrant == redQuadrant), $"quadrant {quadrant} is {Quadrant(photo, quadrant)}");
        }
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

    [Theory]
    [InlineData(4288, 2848, 3840, 2160, 3840, 2550)]
    [InlineData(640, 425, 320, 180, 320, 212)]
    [InlineData(6000, 4000, 3840, 2160, 3840, 2560)]
    [InlineData(4000, 6000, 3840, 2160, 3840, 5760)]
    [InlineData(3000, 2001, 1921, 1080, 1921, 1282)]
    [InlineData(1280, 720, 3840, 2160, 1280, 720)]
    public void TheSideACoverCropsHasTheParityOfTheOutput(int width, int height, int outputWidth, int outputHeight, int scaledWidth, int scaledHeight)
    {
        // The crop's margin splits evenly, so the Fill crop starts on a whole pixel: 4288x2848 to 3840x2160 used to
        // decode 2551 rows and crop from row 195.5.
        var output = new PixelSize(outputWidth, outputHeight);
        var scale = PhotoDecoder.Scale(new PixelSize(width, height), PhotoDecodeOptions.Cover(output));

        var scaled = (PhotoDecoder.Scaled(width, scale, outputWidth), PhotoDecoder.Scaled(height, scale, outputHeight));

        Assert.Equal((scaledWidth, scaledHeight), scaled);
        var crop = FrameRenderer.CoverSourceRect(new Windows.Foundation.Size(scaled.Item1, scaled.Item2), new Windows.Foundation.Size(outputWidth, outputHeight));
        Assert.Equal(Math.Round(crop.X), crop.X);
        Assert.Equal(Math.Round(crop.Y), crop.Y);
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

    private static int Replace(byte[] bytes, ReadOnlySpan<byte> from, ReadOnlySpan<byte> to)
    {
        var count = 0;
        for (var i = bytes.AsSpan().IndexOf(from); i >= 0; i = bytes.AsSpan().IndexOf(from))
        {
            to.CopyTo(bytes.AsSpan(i));
            count++;
        }

        return count;
    }

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
