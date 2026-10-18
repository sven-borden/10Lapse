using System.Buffers.Binary;
using System.Text;

namespace TenLapse.Tests.Ordering;

/// <summary>
/// Builds the smallest photo-like files that carry the Exif tags the capture-time reader looks at: a JPEG with
/// an Exif APP1, plain TIFF containers like DNG, NEF and ARW (little or big endian), and a CR2 header
/// (a HEIC is encoded by Windows Imaging in the tests instead). No pixels except the optional padding, which stands in for image data the reader must not read.
/// </summary>
internal static class ExifFiles
{
    public const ushort DateTimeOriginal = 0x9003;
    public const ushort DateTimeDigitized = 0x9004;
    public const ushort OffsetTimeOriginal = 0x9011;
    public const ushort OffsetTimeDigitized = 0x9012;
    public const ushort SubSecTimeOriginal = 0x9291;
    public const ushort SubSecTimeDigitized = 0x9292;

    /// <summary>Exif tags for one photo; a null text leaves the tag out.</summary>
    public sealed record Tags(
        string? Original = null,
        string? SubSecOriginal = null,
        string? OffsetOriginal = null,
        string? Digitized = null,
        string? SubSecDigitized = null,
        string? OffsetDigitized = null);

    /// <summary>"2026:05:01 18:30:00" for a time, the way cameras write it.</summary>
    public static string Stamp(DateTime time) => time.ToString("yyyy:MM:dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A JPEG: SOI, Exif APP1, optionally a start-of-scan header followed by <paramref name="paddingBytes"/> of fake scan data, EOI.</summary>
    public static byte[] Jpeg(Tags tags, int paddingBytes = 0)
    {
        var tiff = Tiff(tags, bigEndian: false, ifd0Offset: 8, prefix: []);
        using var output = new MemoryStream();
        output.Write([0xFF, 0xD8]);
        var app1Length = 2 + 6 + tiff.Length;
        output.Write([0xFF, 0xE1, (byte)(app1Length >> 8), (byte)app1Length]);
        output.Write("Exif\0\0"u8);
        output.Write(tiff);
        if (paddingBytes > 0)
        {
            // Start of scan: marker, length 12, a 3-component header, then entropy-coded bytes without 0xFF.
            output.Write([0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00]);
            var scan = new byte[paddingBytes];
            Array.Fill(scan, (byte)0x55);
            output.Write(scan);
        }

        output.Write([0xFF, 0xD9]);
        return output.ToArray();
    }

    /// <summary>A TIFF container with the tags in an Exif IFD: what a DNG, ARW or NEF is at the top level.</summary>
    public static byte[] RawTiff(Tags tags, bool bigEndian = false, int paddingBytes = 0)
    {
        var tiff = Tiff(tags, bigEndian, ifd0Offset: 8, prefix: []);
        var padded = new byte[tiff.Length + paddingBytes];
        tiff.CopyTo(padded, 0);
        Array.Fill(padded, (byte)0x55, tiff.Length, paddingBytes);
        return padded;
    }

    /// <summary>A Canon CR2: a TIFF whose header carries "CR" 2.0 at byte 8 and whose first IFD starts at 16.</summary>
    public static byte[] Cr2(Tags tags) =>
        Tiff(tags, bigEndian: false, ifd0Offset: 16, prefix: [(byte)'C', (byte)'R', 2, 0, 0, 0, 0, 0]);

    private static byte[] Tiff(Tags tags, bool bigEndian, int ifd0Offset, byte[] prefix)
    {
        var entries = new SortedDictionary<ushort, string>();
        void Add(ushort tag, string? text)
        {
            if (text is not null)
            {
                entries[tag] = text;
            }
        }

        Add(DateTimeOriginal, tags.Original);
        Add(DateTimeDigitized, tags.Digitized);
        Add(OffsetTimeOriginal, tags.OffsetOriginal);
        Add(OffsetTimeDigitized, tags.OffsetDigitized);
        Add(SubSecTimeOriginal, tags.SubSecOriginal);
        Add(SubSecTimeDigitized, tags.SubSecDigitized);

        using var output = new MemoryStream();
        void Write16(ushort value)
        {
            Span<byte> bytes = stackalloc byte[2];
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            }

            output.Write(bytes);
        }

        void Write32(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            }

            output.Write(bytes);
        }

        output.Write(bigEndian ? "MM"u8 : "II"u8);
        Write16(42);
        Write32((uint)ifd0Offset);
        output.Write(prefix);
        while (output.Length < ifd0Offset)
        {
            output.WriteByte(0);
        }

        // IFD0 holds one entry: the pointer to the Exif IFD that follows it.
        const int ifd0Size = 2 + 12 + 4;
        var exifIfdOffset = ifd0Offset + ifd0Size;
        Write16(1);
        Write16(0x8769);
        Write16(4);
        Write32(1);
        Write32((uint)exifIfdOffset);
        Write32(0);

        // The Exif IFD: the entries, then their values (text longer than 3 characters does not fit in the entry).
        var exifIfdSize = 2 + (entries.Count * 12) + 4;
        var dataOffset = exifIfdOffset + exifIfdSize;
        var data = new MemoryStream();
        Write16((ushort)entries.Count);
        foreach (var (tag, text) in entries)
        {
            var bytes = Encoding.ASCII.GetBytes(text + "\0");
            Write16(tag);
            Write16(2);
            Write32((uint)bytes.Length);
            if (bytes.Length <= 4)
            {
                output.Write(bytes);
                output.Write(new byte[4 - bytes.Length]);
            }
            else
            {
                Write32((uint)(dataOffset + data.Length));
                data.Write(bytes);
            }
        }

        Write32(0);
        output.Write(data.ToArray());
        return output.ToArray();
    }

}
