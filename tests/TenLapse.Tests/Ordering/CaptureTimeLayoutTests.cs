using System.Buffers.Binary;
using TenLapse.Core.Ordering;

namespace TenLapse.Tests.Ordering;

/// <summary>
/// File layouts the capture-time reader must read as MetadataExtractor did (#85 review): several Exif blocks, junk
/// between JPEG segments, other types for the Exif IFD pointer, a RAF cut short and very large IFDs.
/// </summary>
public static class CaptureTimeLayoutTests
{
    private static readonly DateTime _original = new(2026, 5, 1, 18, 30, 7);
    private static readonly DateTime _digitized = new(2026, 5, 1, 18, 30, 9);

    private static ExifFiles.Tags OriginalOnly => new(Original: "2026:05:01 18:30:07");

    private static ExifFiles.Tags DigitizedOnly => new(Digitized: "2026:05:01 18:30:09");

    private static CaptureTimeReading? Read(byte[] file) => CaptureTimeReader.Read(new MemoryStream(file));

    private static byte[] Segment(ExifFiles.Tags tags) => ExifFiles.ExifSegment(ExifFiles.ExifTiff(tags));

    [Fact]
    public static void AnOriginalTimeInALaterExifBlockWinsOverADigitizedTimeInAnEarlierOne()
    {
        var reading = Read(ExifFiles.JpegOf(Segment(DigitizedOnly), Segment(OriginalOnly)));

        Assert.Equal(_original, reading?.Time);
        Assert.Equal(CaptureTimeSource.DateTimeOriginal, reading?.Source);
    }

    [Fact]
    public static void WithoutAnOriginalTimeTheFirstDigitizedTimeIsUsed()
    {
        var reading = Read(ExifFiles.JpegOf(Segment(new ExifFiles.Tags()), Segment(DigitizedOnly), Segment(new ExifFiles.Tags(Digitized: "2026:05:01 18:30:11"))));

        Assert.Equal(_digitized, reading?.Time);
        Assert.Equal(CaptureTimeSource.DateTimeDigitized, reading?.Source);
    }

    [Fact]
    public static void TheFirstOriginalTimeWinsOverALaterOne()
    {
        var reading = Read(ExifFiles.JpegOf(Segment(OriginalOnly), Segment(new ExifFiles.Tags(Original: "2026:05:01 18:30:11"))));

        Assert.Equal(_original, reading?.Time);
    }

    [Fact]
    public static void OnlyTheFirstFourExifBlocksAreTried()
    {
        // Real files have one Exif block; a file with hundreds must not cost hundreds of parses.
        var empty = Segment(new ExifFiles.Tags());

        Assert.Equal(_original, Read(ExifFiles.JpegOf(empty, empty, empty, Segment(OriginalOnly)))?.Time);
        Assert.Null(Read(ExifFiles.JpegOf(empty, empty, empty, empty, Segment(OriginalOnly))));
    }

    public static TheoryData<string, byte[]> JunkBetweenSegments => new()
    {
        { "two zero bytes before APP1", [0x00, 0x00] },
        { "5,000 fill bytes, more than the segment limit", [.. Enumerable.Repeat((byte)0xFF, 5000)] },
        { "stuffed 0xFF 0x00 pairs", [0xFF, 0x00, 0xFF, 0x00, 0x12] },
        { "60,000 bytes of junk, inside the skip budget", new byte[60_000] },
    };

    [Theory]
    [MemberData(nameof(JunkBetweenSegments))]
    public static void JunkBetweenSegmentsIsSkippedToTheNextMarker(string junk, byte[] bytes)
    {
        byte[] app0 = [0xFF, 0xE0, 0x00, 0x04, 0x4A, 0x46];

        Assert.True(Read(ExifFiles.JpegOf(bytes, Segment(OriginalOnly)))?.Time == _original, $"{junk} before the first segment");
        Assert.True(Read(ExifFiles.JpegOf(app0, bytes, Segment(OriginalOnly)))?.Time == _original, $"{junk} after an APP0 segment");
    }

    [Fact]
    public static void JunkBeyondTheSkipBudgetEndsTheSearch()
    {
        Assert.Null(Read(ExifFiles.JpegOf(new byte[ExifContainers.MaxJpegSkippedBytes + 2], Segment(OriginalOnly))));
    }

    [Theory]
    [InlineData((ushort)4)]  // LONG
    [InlineData((ushort)9)]  // SLONG
    [InlineData((ushort)11)] // FLOAT
    [InlineData((ushort)13)] // IFD
    public static void TheExifIfdPointerMayBeAnyFourByteType(ushort type)
    {
        Assert.Equal(_original, Read(ExifFiles.JpegOf(ExifFiles.ExifSegment(ExifFiles.ExifTiff(OriginalOnly, type))))?.Time);
    }

    [Theory]
    [InlineData((ushort)2)]  // ASCII
    [InlineData((ushort)3)]  // SHORT
    [InlineData((ushort)5)]  // RATIONAL
    public static void AnExifIfdPointerOfAnotherTypeIsNotFollowed(ushort type)
    {
        Assert.Null(Read(ExifFiles.JpegOf(ExifFiles.ExifSegment(ExifFiles.ExifTiff(OriginalOnly, type)))));
    }

    [Fact]
    public static void AnExifIfdPointerPastTheEndIsNotFollowed()
    {
        // IFD0 at 8: the entry count, then the pointer entry, whose value sits at 8 + 2 + 8.
        var tiff = ExifFiles.ExifTiff(OriginalOnly, pointerType: 9);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(18), 0x7FFFFFF0);

        Assert.Null(Read(ExifFiles.JpegOf(ExifFiles.ExifSegment(tiff))));
    }

    [Fact]
    public static void ARafCutShortStillGivesTheTimeOfItsEmbeddedJpeg()
    {
        // An interrupted card copy: the header promises the whole preview JPEG, the file holds its first part.
        var jpeg = ExifFiles.Jpeg(OriginalOnly, paddingBytes: 100_000);
        var raf = ExifFiles.Raf(jpeg);

        Assert.Equal(_original, Read(raf[..(raf.Length / 2)])?.Time);
        Assert.Equal(_original, Read(ExifFiles.Raf(jpeg, claimedLength: 50_000_000))?.Time);
        Assert.Null(Read(raf[..120]));
    }

    [Fact]
    public static void ALargeWellFormedExifIfdIsRead()
    {
        var tags = new ExifFiles.Tags(Original: "2026:05:01 18:30:07", SubSecOriginal: "42", OffsetOriginal: "+02:00");
        var tiff = ExifFiles.TiffWithLargeExifIfd(tags, otherEntries: 5000);

        foreach (var file in new[] { tiff, ExifFiles.JpegOf(ExifFiles.ExifSegment(tiff)) })
        {
            var reading = Read(file);
            Assert.Equal(_original.AddMilliseconds(420), reading?.Time);
            Assert.Equal(TimeSpan.FromHours(2), reading?.UtcOffset);
        }
    }

    [Fact]
    public static void TheLastOfManyDuplicateTimeEntriesWins()
    {
        // 65,535 DateTimeOriginal entries, the largest IFD there is: all unset but the last, as MetadataExtractor read it.
        Assert.Equal(_original, Read(ExifFiles.TiffWithRepeatedTimes(65_535, "2026:05:01 18:30:07"))?.Time);
        Assert.Null(Read(ExifFiles.TiffWithRepeatedTimes(65_535, "0000:00:00 00:00:00")));
    }
}
