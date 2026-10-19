using System.Buffers.Binary;
using System.Text;

namespace TenLapse.Core.Ordering;

/// <summary>The six Exif SubIFD tags a capture time is made of, as raw text; null where a tag is absent or unusable.</summary>
internal readonly record struct ExifTimeTags(
    string? Original,
    string? SubSecOriginal,
    string? OffsetOriginal,
    string? Digitized,
    string? SubSecDigitized,
    string? OffsetDigitized)
{
    // Exif tag numbers.
    private const ushort ExifIfdPointerTag = 0x8769;
    private const ushort DateTimeOriginalTag = 0x9003;
    private const ushort DateTimeDigitizedTag = 0x9004;
    private const ushort OffsetTimeOriginalTag = 0x9011;
    private const ushort OffsetTimeDigitizedTag = 0x9012;
    private const ushort SubSecTimeOriginalTag = 0x9291;
    private const ushort SubSecTimeDigitizedTag = 0x9292;

    // TIFF field types that can carry text: BYTE, ASCII, UNDEFINED. All are one byte per value.
    private const ushort ByteType = 1;
    private const ushort AsciiType = 2;
    private const ushort UndefinedType = 7;
    private const ushort LongType = 4;
    private const ushort IfdType = 13;

    // Byte 2 of the TIFF header: 42 for TIFF and the TIFF-based RAW formats; Olympus ORF writes "RO" or "RS" and
    // Panasonic RW2 0x55 in its place, with an otherwise standard layout.
    private const ushort TiffMagic = 42;
    private const ushort OrfMagic = 0x4F52;
    private const ushort OrfSMagic = 0x5352;
    private const ushort Rw2Magic = 0x55;

    /// <summary>
    /// The longest text value read. The time is 19 characters plus a terminator, the others fewer; a longer value is
    /// read only this far (the parsers look at the first characters).
    /// </summary>
    private const int MaxTextBytes = 64;

    /// <summary>
    /// Reads the time tags from a TIFF structure (an Exif block's "II*\0" or "MM\0*" header and the IFDs it points to):
    /// IFD0, the Exif IFD pointer in it, then the Exif IFD. With <paramref name="firstIfdIsExif"/>, IFD0 itself is read as
    /// the Exif IFD (Canon CR3 keeps the Exif IFD as a TIFF of its own). Offsets are relative to the window, which starts
    /// at the TIFF header. Null when the structure is not TIFF or has no Exif IFD.
    /// </summary>
    /// <remarks>
    /// Bounded: it reads the IFD entry tables (12 bytes per entry, at most 65,535 entries per IFD) and at most
    /// <see cref="MaxTextBytes"/> per time tag, allocates only the six strings, and skips any value that does not fit
    /// in the window, as a damaged count or offset would.
    /// </remarks>
    public static ExifTimeTags? ReadTiff(ByteWindow tiff, bool firstIfdIsExif = false)
    {
        if (!tiff.TryReadUInt16(0, bigEndian: false, out var order))
        {
            return null;
        }

        bool bigEndian;
        if (order == 0x4949)
        {
            bigEndian = false;
        }
        else if (order == 0x4D4D)
        {
            bigEndian = true;
        }
        else
        {
            return null;
        }

        if (!tiff.TryReadUInt16(2, bigEndian, out var magic)
            || magic is not (TiffMagic or OrfMagic or OrfSMagic or Rw2Magic)
            || !tiff.TryReadUInt32(4, bigEndian, out var ifd0))
        {
            return null;
        }

        // An IFD0 offset past the end is a damaged header; IFD0 almost always follows it directly, so try there, as
        // MetadataExtractor did.
        if (ifd0 >= tiff.Length - 1)
        {
            ifd0 = 8;
        }

        long exifIfd = ifd0;
        if (!firstIfdIsExif)
        {
            exifIfd = FindExifIfd(tiff, ifd0, bigEndian);
            if (exifIfd < 0)
            {
                return null;
            }
        }

        return ReadTimeTags(tiff, exifIfd, bigEndian);
    }

    /// <summary>The Exif IFD's offset from IFD0's pointer tag, or -1.</summary>
    private static long FindExifIfd(ByteWindow tiff, long ifd, bool bigEndian)
    {
        Span<byte> entry = stackalloc byte[12];
        var count = EntryCount(tiff, ifd, bigEndian);
        for (var i = 0; i < count; i++)
        {
            if (!tiff.TryRead(ifd + 2 + (i * 12L), entry))
            {
                return -1;
            }

            if (Read16(entry, 0, bigEndian) == ExifIfdPointerTag && Read16(entry, 2, bigEndian) is LongType or IfdType)
            {
                return Read32(entry, 8, bigEndian);
            }
        }

        return -1;
    }

    private static ExifTimeTags? ReadTimeTags(ByteWindow tiff, long ifd, bool bigEndian)
    {
        if (!tiff.Contains(ifd, 2))
        {
            return null;
        }

        string? original = null, subSecOriginal = null, offsetOriginal = null;
        string? digitized = null, subSecDigitized = null, offsetDigitized = null;
        Span<byte> entry = stackalloc byte[12];
        Span<byte> text = stackalloc byte[MaxTextBytes];
        var count = EntryCount(tiff, ifd, bigEndian);
        for (var i = 0; i < count; i++)
        {
            if (!tiff.TryRead(ifd + 2 + (i * 12L), entry))
            {
                break;
            }

            var tag = Read16(entry, 0, bigEndian);
            if (tag is not (DateTimeOriginalTag or DateTimeDigitizedTag or OffsetTimeOriginalTag or OffsetTimeDigitizedTag
                or SubSecTimeOriginalTag or SubSecTimeDigitizedTag))
            {
                continue;
            }

            // A later duplicate replaces an earlier one, as MetadataExtractor did.
            var value = ReadText(tiff, entry, bigEndian, text);
            switch (tag)
            {
                case DateTimeOriginalTag: original = value; break;
                case DateTimeDigitizedTag: digitized = value; break;
                case OffsetTimeOriginalTag: offsetOriginal = value; break;
                case OffsetTimeDigitizedTag: offsetDigitized = value; break;
                case SubSecTimeOriginalTag: subSecOriginal = value; break;
                default: subSecDigitized = value; break;
            }
        }

        return new ExifTimeTags(original, subSecOriginal, offsetOriginal, digitized, subSecDigitized, offsetDigitized);
    }

    /// <summary>
    /// The entry's value as text, up to the first NUL. Null when the type is not one-byte text or the value does not
    /// lie inside the window (a damaged or hostile count is skipped, never read or allocated).
    /// </summary>
    private static string? ReadText(ByteWindow tiff, ReadOnlySpan<byte> entry, bool bigEndian, Span<byte> buffer)
    {
        var type = Read16(entry, 2, bigEndian);
        var count = Read32(entry, 4, bigEndian);
        if (type is not (AsciiType or ByteType or UndefinedType))
        {
            return null;
        }

        ReadOnlySpan<byte> bytes;
        if (count <= 4)
        {
            bytes = entry.Slice(8, (int)count);
        }
        else
        {
            long offset = Read32(entry, 8, bigEndian);
            if (!tiff.Contains(offset, count))
            {
                return null;
            }

            var read = buffer[..(int)Math.Min(count, buffer.Length)];
            if (!tiff.TryRead(offset, read))
            {
                return null;
            }

            bytes = read;
        }

        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
    }

    /// <summary>The IFD's entry count, cut to the entries that fit in the window.</summary>
    private static long EntryCount(ByteWindow tiff, long ifd, bool bigEndian)
    {
        if (!tiff.TryReadUInt16(ifd, bigEndian, out var count))
        {
            return 0;
        }

        return Math.Min(count, (tiff.Length - ifd - 2) / 12);
    }

    private static ushort Read16(ReadOnlySpan<byte> bytes, int at, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes[at..]) : BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]);

    private static uint Read32(ReadOnlySpan<byte> bytes, int at, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes[at..]) : BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);
}
