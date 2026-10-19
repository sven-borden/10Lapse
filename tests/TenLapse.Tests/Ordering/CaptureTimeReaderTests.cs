using System.Diagnostics;
using System.Runtime.InteropServices;
using TenLapse.Core.Ordering;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TenLapse.Tests.Ordering;

public static class CaptureTimeReaderTests
{
    private static readonly DateTime _taken = new(2026, 5, 1, 18, 30, 7, DateTimeKind.Unspecified);

    private static CaptureTimeReading? Read(byte[] file) => CaptureTimeReader.Read(new MemoryStream(file));

    [Fact]
    public static void ReadsDateTimeOriginalWithSubSeconds()
    {
        var reading = Read(ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07", SubSecOriginal: "42")));

        Assert.NotNull(reading);
        Assert.Equal(_taken.AddMilliseconds(420), reading.Value.Time);
        Assert.Equal(DateTimeKind.Unspecified, reading.Value.Time.Kind);
        Assert.Equal(CaptureTimeSource.DateTimeOriginal, reading.Value.Source);
        Assert.Null(reading.Value.UtcOffset);
    }

    [Fact]
    public static void WithoutSubSecondsTheTimeIsWholeSeconds()
    {
        Assert.Equal(_taken, Read(ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07")))!.Value.Time);
    }

    [Theory]
    [InlineData("3", 3_000_000)]
    [InlineData("03", 300_000)]
    [InlineData("003", 30_000)]
    [InlineData("123", 1_230_000)]
    [InlineData("12", 1_200_000)]
    [InlineData("99", 9_900_000)]
    [InlineData("1234567", 1_234_567)]
    [InlineData("123456789", 1_234_567)]
    [InlineData("0", 0)]
    [InlineData("000", 0)]
    [InlineData("  ", 0)]
    [InlineData("ab", 0)]
    [InlineData("1x", 0)]
    public static void SubSecondDigitsAreTheDecimalFraction(string subSeconds, long expectedTicks)
    {
        Assert.Equal(expectedTicks, CaptureTimeReader.ParseSubSeconds(subSeconds));
    }

    [Fact]
    public static void BurstFramesWithinOneSecondKeepTheirOrder()
    {
        var times = new[] { "03", "12", "250", "9", "499" }
            .Select(sub => Read(ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07", SubSecOriginal: sub)))!.Value.Time)
            .ToArray();

        Assert.Equal([30, 120, 250, 900, 499], times.Select(t => t.Millisecond));
        Assert.True(times[0] < times[1] && times[1] < times[2] && times[2] < times[4] && times[4] < times[3]);
    }

    [Theory]
    [InlineData("+02:00", 2, 0)]
    [InlineData("-05:30", -5, -30)]
    [InlineData("+00:00", 0, 0)]
    [InlineData("+0530", 5, 30)]
    [InlineData("Z", 0, 0)]
    public static void ReadsTheOffsetWhenThereIsOne(string text, int hours, int minutes)
    {
        var reading = Read(ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07", OffsetOriginal: text)));

        Assert.Equal(new TimeSpan(hours, minutes, 0), reading!.Value.UtcOffset);
        Assert.Equal(_taken, reading.Value.Time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("local")]
    [InlineData("+25:00")]
    [InlineData("+02:99")]
    [InlineData("02:00")]
    public static void AnUnreadableOffsetIsLeftOut(string text)
    {
        var reading = Read(ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07", OffsetOriginal: text)));

        Assert.Null(reading!.Value.UtcOffset);
        Assert.Equal(_taken, reading.Value.Time);
    }

    [Fact]
    public static void FallsBackToDateTimeDigitizedOnlyWhenThereIsNoOriginal()
    {
        var digitizedOnly = Read(ExifFiles.Jpeg(new ExifFiles.Tags(Digitized: "2026:05:01 18:30:09", SubSecDigitized: "5", OffsetDigitized: "+01:00")))!.Value;
        Assert.Equal(new DateTime(2026, 5, 1, 18, 30, 9, 500), digitizedOnly.Time);
        Assert.Equal(CaptureTimeSource.DateTimeDigitized, digitizedOnly.Source);
        Assert.Equal(TimeSpan.FromHours(1), digitizedOnly.UtcOffset);

        var both = Read(ExifFiles.Jpeg(new ExifFiles.Tags(
            Original: "2026:05:01 18:30:07",
            SubSecOriginal: "25",
            Digitized: "2026:05:01 18:30:09",
            SubSecDigitized: "5")))!.Value;
        Assert.Equal(_taken.AddMilliseconds(250), both.Time);
        Assert.Equal(CaptureTimeSource.DateTimeOriginal, both.Source);
    }

    [Theory]
    [InlineData("0000:00:00 00:00:00")]
    [InlineData("                   ")]
    [InlineData("2026:13:01 18:30:07")]
    [InlineData("2026:02:30 18:30:07")]
    [InlineData("2026:05:01 24:30:07")]
    [InlineData("2026:05:01")]
    [InlineData("yesterday at noon")]
    [InlineData("")]
    public static void AnUnusableOriginalFallsBackToDigitized(string original)
    {
        var reading = Read(ExifFiles.Jpeg(new ExifFiles.Tags(Original: original, Digitized: "2026:05:01 18:30:09")));

        Assert.Equal(new DateTime(2026, 5, 1, 18, 30, 9), reading!.Value.Time);
        Assert.Equal(CaptureTimeSource.DateTimeDigitized, reading.Value.Source);
        Assert.Null(Read(ExifFiles.Jpeg(new ExifFiles.Tags(Original: original))));
    }

    [Theory]
    [InlineData("2026-05-01 18:30:07")]
    [InlineData("2026/05/01 18:30:07")]
    [InlineData("2026:05:01 18:30:07  ")]
    public static void LenientSeparatorsAndPaddingAreAccepted(string original)
    {
        Assert.Equal(_taken, Read(ExifFiles.Jpeg(new ExifFiles.Tags(Original: original)))!.Value.Time);
    }

    [Fact]
    public static void AFileWithoutTimesGivesNullNotTheFileTime()
    {
        Assert.Null(Read(ExifFiles.Jpeg(new ExifFiles.Tags())));

        var path = Path.Combine(Path.GetTempPath(), $"tenlapse-notime-{Guid.NewGuid():N}.jpg");
        try
        {
            File.WriteAllBytes(path, ExifFiles.Jpeg(new ExifFiles.Tags()));
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 5, 1, 18, 30, 7, DateTimeKind.Utc));
            Assert.Null(CaptureTimeReader.ReadFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static TheoryData<string, byte[]> Containers
    {
        get
        {
            var tags = new ExifFiles.Tags(Original: "2026:05:01 18:30:07", SubSecOriginal: "42", OffsetOriginal: "+02:00");
            return new TheoryData<string, byte[]>
            {
                { "jpeg", ExifFiles.Jpeg(tags) },
                { "tiff little-endian (DNG, ARW)", ExifFiles.RawTiff(tags) },
                { "tiff big-endian (NEF)", ExifFiles.RawTiff(tags, bigEndian: true) },
                { "cr2", ExifFiles.Cr2(tags) },
            };
        }
    }

    [Theory]
    [MemberData(nameof(Containers))]
    public static void ReadsEveryContainer(string container, byte[] file)
    {
        var reading = Read(file);

        Assert.True(reading is not null, $"no capture time in the {container} file");
        Assert.Equal(_taken.AddMilliseconds(420), reading.Value.Time);
        Assert.Equal(TimeSpan.FromHours(2), reading.Value.UtcOffset);
    }

    [Fact]
    public static void ReadsAJpegThatWindowsImagingEncoded()
    {
        var file = EncodeWithWic(BitmapEncoder.JpegEncoderId, "2026:05:01 18:30:07", "42")!;

        Assert.Equal(new byte[] { 0xFF, 0xD8 }, file[..2]);
        var reading = Read(file);
        Assert.NotNull(reading);
        Assert.Equal(_taken.AddMilliseconds(420), reading.Value.Time);
    }

    [Fact]
    public static void ReadsAHeicThatWindowsImagingEncoded()
    {
        // The HEIF encoder ships with Windows 11 but can be missing (it needs the HEVC codec on some editions).
        var file = EncodeWithWic(BitmapEncoder.HeifEncoderId, "2026:05:01 18:30:07", "42");
        Assert.SkipWhen(file is null, "This PC has no HEIF encoder, so there is no HEIC to read.");

        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(file![4..8]));
        var reading = Read(file);
        Assert.NotNull(reading);
        Assert.Equal(_taken.AddMilliseconds(420), reading.Value.Time);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public static void NeverThrowsOnDamagedData(int variant)
    {
        var good = ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07"));
        var random = new Random(15);
        byte[] bytes = variant switch
        {
            0 => [],
            1 => [.. Enumerable.Range(0, 5000).Select(_ => (byte)random.Next(256))],
            _ => good[..(good.Length / 2)],
        };

        Assert.Null(Read(bytes));
        Assert.Null(Read([.. good[..2], .. Enumerable.Range(0, 400).Select(_ => (byte)0xFF)]));
    }

    [Theory]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0x7FFFFFFFu)]
    [InlineData(0x40000000u)]
    [InlineData(0x10000000u)]
    public static void AHostileValueCountIsAFailureOfThatFileNotAnOutOfMemoryException(uint count)
    {
        // A batch of 5,000 photos must survive one file whose IFD claims billions of bytes: the entry for
        // DateTimeOriginal says it holds `count` values, so a reader that trusts it tries to allocate that much.
        // (Whether MetadataExtractor refuses before allocating or allocates and fails, the result is null and no exception.)
        var file = ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07"));

        // The entry: tag 0x9003, type 2 (ASCII), count 20 (19 characters and the terminator), little-endian.
        ReadOnlySpan<byte> entry = [0x03, 0x90, 0x02, 0x00, 0x14, 0x00, 0x00, 0x00];
        var at = file.AsSpan().IndexOf(entry);
        Assert.True(at > 0, "The DateTimeOriginal entry was not found in the test file.");
        BitConverter.GetBytes(count).CopyTo(file, at + 4);

        Assert.Null(Read(file));

        var path = Path.Combine(Path.GetTempPath(), $"tenlapse-hostile-{Guid.NewGuid():N}.jpg");
        try
        {
            File.WriteAllBytes(path, file);
            Assert.Null(CaptureTimeReader.ReadFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public static void NeverThrowsOnEveryTruncationOrCorruptionOfARealisticFile()
    {
        foreach (var file in new[]
        {
            ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07", SubSecOriginal: "42", OffsetOriginal: "+02:00")),
            ExifFiles.RawTiff(new ExifFiles.Tags(Original: "2026:05:01 18:30:07", SubSecOriginal: "42")),
            EncodeWithWic(BitmapEncoder.HeifEncoderId, "2026:05:01 18:30:07", "42") ?? [],
        })
        {
            for (var length = 0; length < file.Length; length++)
            {
                _ = Read(file[..length]);
            }

            var random = new Random(150);
            for (var i = 0; i < 300; i++)
            {
                var copy = (byte[])file.Clone();
                copy[random.Next(copy.Length)] = (byte)random.Next(256);
                copy[random.Next(copy.Length)] = (byte)random.Next(256);
                _ = Read(copy);
            }
        }
    }

    [Fact]
    public static void AStreamThatCannotSeekGivesNull()
    {
        var file = ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07"));

        Assert.Null(CaptureTimeReader.Read(new NonSeekableStream(file)));
    }

    [Fact]
    public static void AMissingOrLockedFileGivesNull()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"tenlapse-missing-{Guid.NewGuid():N}.jpg");
        Assert.Null(CaptureTimeReader.ReadFile(missing));
        Assert.Null(CaptureTimeReader.ReadFile(Path.GetTempPath()));
        Assert.Null(CaptureTimeReader.ReadFile(string.Empty));
    }

    [Fact]
    public static void ALargeFileIsReadOnlyForItsMetadata()
    {
        var tags = new ExifFiles.Tags(Original: "2026:05:01 18:30:07");
        foreach (var (name, file) in new[]
        {
            ("jpeg", ExifFiles.Jpeg(tags, paddingBytes: 8_000_000)),
            ("tiff", ExifFiles.RawTiff(tags, paddingBytes: 8_000_000)),
        })
        {
            var counting = new CountingStream(new MemoryStream(file));
            var reading = CaptureTimeReader.Read(counting);

            Assert.True(reading is not null, name);
            Assert.True(counting.BytesRead < 100_000, $"{name}: read {counting.BytesRead:N0} of {file.Length:N0} bytes");
        }
    }

    [Fact]
    public static void ReadingFiveThousandPhotosFromDiskStaysAtMetadataCost()
    {
        var folder = Directory.CreateTempSubdirectory("tenlapse-captime-");
        try
        {
            // 5,000 JPEGs of about 250 KB: the Exif block at the front, scan data behind it.
            var template = ExifFiles.Jpeg(new ExifFiles.Tags(Original: "2026:05:01 18:30:07", SubSecOriginal: "42"), paddingBytes: 250_000);
            var paths = new List<string>();
            for (var i = 0; i < 5000; i++)
            {
                var path = Path.Combine(folder.FullName, $"IMG_{i:0000}.jpg");
                File.WriteAllBytes(path, template);
                paths.Add(path);
            }

            // The files were written a moment ago, so the first open of each can wait for the virus scanner (about
            // 4 ms each here, one thread); eight readers overlap those waits. The bound only catches a reader that
            // starts decoding or reading whole files.
            var watch = Stopwatch.StartNew();
            var found = paths.AsParallel().WithDegreeOfParallelism(8).Count(path => CaptureTimeReader.ReadFile(path) is not null);
            watch.Stop();

            TestContext.Current.SendDiagnosticMessage(
                $"Capture time: {found} of {paths.Count} fresh files read in {watch.Elapsed.TotalMilliseconds:N0} ms with 8 readers.");
            Assert.Equal(paths.Count, found);
            Assert.True(watch.Elapsed.TotalSeconds < 30, $"5,000 files took {watch.Elapsed.TotalSeconds:N1} s");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A real 16 by 16 image from a Windows Imaging encoder with the Exif tags written by its metadata writer
    /// (JPEG keeps them under <c>/app1/ifd/exif</c>, HEIF under <c>/ifd/exif</c>); null when the encoder is not installed.
    /// </summary>
    private static byte[]? EncodeWithWic(Guid encoderId, string dateTimeOriginal, string subSecond)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            var encoder = BitmapEncoder.CreateAsync(encoderId, stream).AsTask().GetAwaiter().GetResult();
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 64, 64, 96, 96, new byte[64 * 64 * 4]);
            var root = encoderId == BitmapEncoder.JpegEncoderId ? "/app1/ifd/exif" : "/ifd/exif";
            var properties = new BitmapPropertySet
            {
                [$"{root}/{{ushort=36867}}"] = new BitmapTypedValue(dateTimeOriginal, Windows.Foundation.PropertyType.String),
                [$"{root}/{{ushort=37521}}"] = new BitmapTypedValue(subSecond, Windows.Foundation.PropertyType.String),
            };
            encoder.BitmapProperties.SetPropertiesAsync(properties).AsTask().GetAwaiter().GetResult();
            encoder.FlushAsync().AsTask().GetAwaiter().GetResult();

            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            reader.LoadAsync((uint)stream.Size).AsTask().GetAwaiter().GetResult();
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
