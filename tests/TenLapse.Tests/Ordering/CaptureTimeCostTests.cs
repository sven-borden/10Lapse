using TenLapse.Core.Ordering;

namespace TenLapse.Tests.Ordering;

/// <summary>
/// The cost of one capture-time read does not grow with the file or with what its fields claim (#85 review, M2).
/// Costs are counted as stream reads, position changes and bytes, through <see cref="CountingStream"/>, which is
/// deterministic where a timing is not.
/// </summary>
public static class CaptureTimeCostTests
{
    private static CountingStream ReadCounted(byte[] file, out CaptureTimeReading? reading)
    {
        var stream = new CountingStream(new MemoryStream(file));
        reading = CaptureTimeReader.Read(stream);
        return stream;
    }

    /// <summary>A JPEG of <paramref name="segments"/> Exif APP1s, each a 5,400-entry Exif IFD of unset times (about 65 KB).</summary>
    private static byte[] JpegOfFullSegments(int segments)
    {
        var segment = ExifFiles.ExifSegment(ExifFiles.TiffWithRepeatedTimes(5400, "0000:00:00 00:00:00"));
        return ExifFiles.JpegOf([.. Enumerable.Repeat(segment, segments)]);
    }

    [Fact]
    public static void TheLargestIfdCostsAFewHundredReads()
    {
        // 65,535 entries are 786 KB of entry table: read in 4 KB blocks (193 reads), and the value of the last
        // DateTimeOriginal only, not one value per entry.
        var file = ExifFiles.TiffWithRepeatedTimes(65_535, "2026:05:01 18:30:07");

        var stream = ReadCounted(file, out var reading);

        Assert.NotNull(reading);
        Assert.InRange(stream.Reads, 1, 210);
        Assert.InRange(stream.Seeks, 1, 210);
        Assert.InRange(CaptureTimeReaderTests.AllocatedByRead(file, out _), 0, 4096);
    }

    [Fact]
    public static void ManyExifSegmentsCostNoMoreThanFour()
    {
        // 16 and 64 segments of 65 KB: the first four are each read in one go and parsed in memory, the rest never.
        var sixteen = ReadCounted(JpegOfFullSegments(16), out var none);
        var sixtyFour = ReadCounted(JpegOfFullSegments(64), out _);

        Assert.Null(none);
        Assert.Equal(sixteen.Reads, sixtyFour.Reads);
        Assert.Equal(sixteen.BytesRead, sixtyFour.BytesRead);
        Assert.InRange(sixteen.Reads, 1, 16);
        Assert.InRange(sixteen.BytesRead, 4 * 64_000, 4 * 66_000);
        Assert.InRange(CaptureTimeReaderTests.AllocatedByRead(JpegOfFullSegments(64), out _), 0, 4096);
    }

    [Fact]
    public static void AnExifSegmentIsReadInOneGo()
    {
        // The header, the marker, the Exif preamble and the segment: the IFDs are then walked in memory.
        var stream = ReadCounted(ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07", SubSecOriginal: "42", OffsetOriginal: "+02:00")), out var reading);

        Assert.NotNull(reading);
        Assert.InRange(stream.Reads, 1, 4);
    }

    [Fact]
    public static void ManyDamagedRawProfilesCostNoMoreThanFour()
    {
        // Each damaged zTXt fails inside the decompressor; each still counts as a block, so a PNG of thousands costs four.
        (string, byte[]) damaged = ("zTXt", [.. "Raw profile type exif"u8, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF]);
        var good = ExifFiles.TextChunk(ExifFiles.PngText.Compressed, "Raw profile type exif", ExifFiles.RawProfileText("exif", [.. "Exif\0\0"u8, .. ExifFiles.ExifTiff(new ExifFiles.Tags(Original: "2026:05:01 18:30:07"))]));

        var eight = ReadCounted(ExifFiles.PngOf([.. Enumerable.Repeat(damaged, 8), good]), out var none);
        var thousand = ReadCounted(ExifFiles.PngOf([.. Enumerable.Repeat(damaged, 1000), good]), out _);
        var threeThenGood = CaptureTimeReader.Read(new MemoryStream(ExifFiles.PngOf([.. Enumerable.Repeat(damaged, 3), good])));

        Assert.Null(none);
        Assert.Equal(eight.Reads, thousand.Reads);
        Assert.Equal(new DateTime(2026, 5, 1, 18, 30, 7), threeThenGood?.Time);
    }

    [Fact]
    public static void AHeifExifItemNamedSeveralTimesIsReadOnce()
    {
        var once = ReadCounted(ExifFiles.HeifRepeatedExifItem(names: 1, extents: 2, extentBytes: 1024), out _);
        var fourTimes = ReadCounted(ExifFiles.HeifRepeatedExifItem(names: 4, extents: 2, extentBytes: 1024), out _);

        // The longer iinf box is read with the meta box; the item's 2 KB is not read again.
        Assert.Equal(once.Reads, fourTimes.Reads);
        Assert.InRange(fourTimes.BytesRead - once.BytesRead, 0, 1024);
    }

    [Fact]
    public static void SplitHeifExifItemsShareOneCopyBudget()
    {
        // One 1 MB item named four times: copied at most once, never four times.
        var file = ExifFiles.HeifRepeatedExifItem(names: 4, extents: 16, extentBytes: 64 * 1024);

        var stream = ReadCounted(file, out _);

        Assert.InRange(stream.BytesRead, ExifContainers.MaxCopiedBytes, ExifContainers.MaxCopiedBytes + 4096);
    }
}
