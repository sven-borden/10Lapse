using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Microsoft.Graphics.Canvas;
using Windows.Storage.Streams;

namespace TenLapse.Spike;

/// <summary>
/// #34: a folder of camera-like photos for the benchmark. A few distinct "base" JPEGs are drawn and graded with grain so they
/// weigh and decode like real photos, then each is given an EXIF block as a camera writes it (DateTimeOriginal, DateTime and
/// a small embedded picture, which the filmstrip reads first), and copied under as many names as the folder needs with the
/// capture time patched in place. The time is a fixed-length ASCII field, so a copy costs one read and one write.
/// </summary>
internal static class BenchPhotos
{
    /// <summary>The date the bases carry; a copy overwrites it. It is also what the patch search looks for.</summary>
    private const string Marker = "2000:01:01 00:00:00";

    private const int ThumbnailWidth = 160;

    public static string PathOf(string directory, int index) =>
        Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"IMG_{index + 1:00000}.JPG"));

    /// <summary>Bytes a base of this size is expected to weigh, to size the disk check before drawing anything (measured: 0.36 byte a pixel with grain 25, 8.6 MB at 24 MP).</summary>
    public static long EstimateBytes(int count, int width, int height) => (long)(count * width * (double)height * 0.36);

    public static async Task<int> RunAsync(Options options)
    {
        var directory = Path.GetFullPath(options.Require("dir"));
        var count = options.Int("count", 1000);
        var (width, height) = options.Size("size", (6000, 4000));
        var bases = Math.Min(count, Math.Max(1, options.Int("bases", 24)));
        var noise = options.Int("noise", 25);
        var embed = !options.Flag("no-thumbnail");
        var interval = options.Int("interval", 5);
        var start = options.Flag("start")
            ? DateTime.Parse(options.Text("start", string.Empty), CultureInfo.InvariantCulture)
            : new DateTime(2026, 6, 21, 6, 0, 0, DateTimeKind.Unspecified);

        Directory.CreateDirectory(directory);
        var free = new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace;
        Console.WriteLine($"bench-photos: {count} photos of {width}x{height} ({width * (double)height / 1e6:0.#} MP) into {directory}; {bases} distinct, grain {noise}, " +
            $"{(embed ? "with" : "without")} an embedded picture; about {EstimateBytes(count, width, height) / 1e9:0.0} GB needed, {free / 1e9:0.0} GB free.");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var made = new List<(byte[] Bytes, int[] DateOffsets)>();
        for (var b = 0; b < bases; b++)
        {
            made.Add(await MakeBaseAsync(b, width, height, noise, embed));
        }

        var average = made.Average(m => (double)m.Bytes.Length);
        var needed = (long)(average * count);
        Console.WriteLine($"bases drawn in {clock.Elapsed.TotalSeconds:0.0} s, {average / 1e6:0.0} MB each: {needed / 1e9:0.0} GB for {count} photos.");
        if (free - needed < 5L * 1024 * 1024 * 1024)
        {
            throw new IOException($"Not enough disk space on {Path.GetPathRoot(directory)}: {needed / 1e9:0.0} GB needed plus 5 GB spare, {free / 1e9:0.0} GB free.");
        }

        clock.Restart();
        var written = 0;
        await Parallel.ForEachAsync(
            Enumerable.Range(0, count),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) },
            async (index, token) =>
            {
                var (bytes, offsets) = made[index % bases];
                var path = PathOf(directory, index);
                var stamp = Encoding.ASCII.GetBytes(start.AddSeconds((double)interval * index).ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture));
                var copy = (byte[])bytes.Clone();
                foreach (var offset in offsets)
                {
                    stamp.CopyTo(copy, offset);
                }

                await File.WriteAllBytesAsync(path, copy, token);
                var done = Interlocked.Increment(ref written);
                if (done % 500 == 0)
                {
                    Console.WriteLine($"  {done} / {count}");
                }
            });
        var total = Enumerable.Range(0, count).Sum(i => new FileInfo(PathOf(directory, i)).Length);
        Console.WriteLine($"wrote {count} photos in {clock.Elapsed.TotalSeconds:0.0} s, {total / 1e9:0.00} GB");
        return 0;
    }

    private static async Task<(byte[] Bytes, int[] DateOffsets)> MakeBaseAsync(int index, int width, int height, int noise, bool embed)
    {
        var device = TenLapse.Media.GraphicsDevice.Shared;
        byte[] jpeg;
        byte[]? thumbnail = null;
        using (var target = new CanvasRenderTarget(device, width, height, 96f))
        {
            using (var session = target.CreateDrawingSession())
            {
                SyntheticImage.Draw(session, index, width, height);
            }

            if (noise > 0)
            {
                SyntheticJpegs.AddNoise(target, index, noise);
            }

            jpeg = await EncodeAsync(target);
            if (embed)
            {
                var thumbHeight = (int)Math.Round((double)ThumbnailWidth * height / width);
                using var small = new CanvasRenderTarget(device, ThumbnailWidth, thumbHeight, 96f);
                using (var session = small.CreateDrawingSession())
                {
                    session.DrawImage(target, new Windows.Foundation.Rect(0, 0, ThumbnailWidth, thumbHeight), new Windows.Foundation.Rect(0, 0, width, height));
                }

                thumbnail = await EncodeAsync(small);
            }
        }

        return WithExif(jpeg, thumbnail);
    }

    private static async Task<byte[]> EncodeAsync(CanvasRenderTarget target)
    {
        using var stream = new InMemoryRandomAccessStream();
        await target.SaveAsync(stream, CanvasBitmapFileFormat.Jpeg, 0.9f);
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// Inserts an APP1 Exif segment right after the start-of-image marker: IFD0 (DateTime, pointer to the Exif IFD), the Exif IFD
    /// (DateTimeOriginal) and, when there is a picture, IFD1 with the JPEG thumbnail. Little-endian, like most cameras. Returns the
    /// file and the offsets of the two 19-character date fields.
    /// </summary>
    private static (byte[] Bytes, int[] DateOffsets) WithExif(byte[] jpeg, byte[]? thumbnail)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new InvalidDataException("The encoder did not write a JPEG.");
        }

        const int ifd0 = 8;
        const int ifd0Size = 2 + (2 * 12) + 4;
        const int exifIfd = ifd0 + ifd0Size;
        const int exifIfdSize = 2 + 12 + 4;
        var ifd1 = exifIfd + exifIfdSize;
        var ifd1Size = thumbnail is null ? 0 : 2 + (3 * 12) + 4;
        var dateTime = ifd1 + ifd1Size;
        var dateTimeOriginal = dateTime + 20;
        var thumbnailAt = dateTimeOriginal + 20;
        var tiffLength = thumbnailAt + (thumbnail?.Length ?? 0);

        var tiff = new byte[tiffLength];
        var span = tiff.AsSpan();
        span[0] = (byte)'I';
        span[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], ifd0);

        static void Entry(Span<byte> at, ushort tag, ushort type, uint count, uint value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(at, tag);
            BinaryPrimitives.WriteUInt16LittleEndian(at[2..], type);
            BinaryPrimitives.WriteUInt32LittleEndian(at[4..], count);
            BinaryPrimitives.WriteUInt32LittleEndian(at[8..], value);
        }

        const ushort Ascii = 2;
        const ushort Short = 3;
        const ushort Long = 4;

        // IFD0: DateTime (0x0132), ExifIFD pointer (0x8769), then IFD1 when there is a picture.
        BinaryPrimitives.WriteUInt16LittleEndian(span[ifd0..], 2);
        Entry(span[(ifd0 + 2)..], 0x0132, Ascii, 20, (uint)dateTime);
        Entry(span[(ifd0 + 14)..], 0x8769, Long, 1, exifIfd);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(ifd0 + 26)..], thumbnail is null ? 0u : (uint)ifd1);

        // Exif IFD: DateTimeOriginal (0x9003).
        BinaryPrimitives.WriteUInt16LittleEndian(span[exifIfd..], 1);
        Entry(span[(exifIfd + 2)..], 0x9003, Ascii, 20, (uint)dateTimeOriginal);

        if (thumbnail is not null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[ifd1..], 3);
            Entry(span[(ifd1 + 2)..], 0x0103, Short, 1, 6);
            Entry(span[(ifd1 + 14)..], 0x0201, Long, 1, (uint)thumbnailAt);
            Entry(span[(ifd1 + 26)..], 0x0202, Long, 1, (uint)thumbnail.Length);
            thumbnail.CopyTo(tiff, thumbnailAt);
        }

        var marker = Encoding.ASCII.GetBytes(Marker);
        marker.CopyTo(tiff, dateTime);
        marker.CopyTo(tiff, dateTimeOriginal);

        var segmentLength = 2 + 6 + tiffLength;
        if (segmentLength > ushort.MaxValue)
        {
            throw new InvalidDataException("The embedded picture does not fit in one Exif segment.");
        }

        var file = new byte[jpeg.Length + 2 + segmentLength];
        file[0] = 0xFF;
        file[1] = 0xD8;
        file[2] = 0xFF;
        file[3] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), (ushort)segmentLength);
        Encoding.ASCII.GetBytes("Exif\0\0").CopyTo(file, 6);
        tiff.CopyTo(file, 12);
        jpeg.AsSpan(2).CopyTo(file.AsSpan(12 + tiffLength));
        return (file, [12 + dateTime, 12 + dateTimeOriginal]);
    }
}
