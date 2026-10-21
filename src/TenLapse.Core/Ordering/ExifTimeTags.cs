using System.Buffers.Binary;
using System.Text;

namespace TenLapse.Core.Ordering;

/// <summary>
/// The six Exif SubIFD tags a capture time is made of, as raw text; null where a tag is absent or unusable. Read in the same
/// pass: the exposure settings deflicker uses (#45), <see cref="ExposureTime"/>, <see cref="FNumber"/> and <see cref="Iso"/>.
/// </summary>
internal readonly record struct ExifTimeTags(
    string? Original,
    string? SubSecOriginal,
    string? OffsetOriginal,
    string? Digitized,
    string? SubSecDigitized,
    string? OffsetDigitized)
{
    /// <summary>The shutter time in seconds (<c>ExposureTime</c>, a RATIONAL), or null.</summary>
    public double? ExposureTime { get; init; }

    /// <summary>The f-number (<c>FNumber</c>, a RATIONAL), or null.</summary>
    public double? FNumber { get; init; }

    /// <summary>The ISO (<c>PhotographicSensitivity</c>; <c>ISOSpeed</c> when that is missing or saturated at 65,535), or null.</summary>
    public double? Iso { get; init; }

    // Exif tag numbers.
    private const ushort ExifIfdPointerTag = 0x8769;
    private const ushort DateTimeOriginalTag = 0x9003;
    private const ushort DateTimeDigitizedTag = 0x9004;
    private const ushort OffsetTimeOriginalTag = 0x9011;
    private const ushort OffsetTimeDigitizedTag = 0x9012;
    private const ushort SubSecTimeOriginalTag = 0x9291;
    private const ushort SubSecTimeDigitizedTag = 0x9292;
    private const ushort ExposureTimeTag = 0x829A;
    private const ushort FNumberTag = 0x829D;
    private const ushort PhotographicSensitivityTag = 0x8827;
    private const ushort IsoSpeedTag = 0x8833;

    // The six text slots, then the four exposure slots, in SlotOf's order.
    private const int Slots = 10;

    // TIFF numeric types the exposure tags use.
    private const ushort ShortType = 3;
    private const ushort LongType = 4;
    private const ushort RationalType = 5;
    private const ushort SignedRationalType = 10;

    // TIFF field types that can carry text: BYTE, ASCII, UNDEFINED. All are one byte per value.
    private const ushort ByteType = 1;
    private const ushort AsciiType = 2;
    private const ushort UndefinedType = 7;

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

    /// <summary>IFD entries read at once: 4 KB of entry table per read, so a 65,535-entry IFD takes 193 reads.</summary>
    private const int EntriesPerRead = 341;

    private const int EntryBytes = 12;

    /// <summary>
    /// Reads the time tags from a TIFF structure (an Exif block's "II*\0" or "MM\0*" header and the IFDs it points to):
    /// IFD0, the Exif IFD pointer in it, then the Exif IFD. With <paramref name="firstIfdIsExif"/>, IFD0 itself is read as
    /// the Exif IFD (Canon CR3 keeps the Exif IFD as a TIFF of its own). Offsets are relative to the window, which starts
    /// at the TIFF header. Null when the structure is not TIFF or has no Exif IFD.
    /// </summary>
    /// <remarks>
    /// Bounded whatever the fields say: it reads the entry tables of two IFDs in blocks of <see cref="EntriesPerRead"/>
    /// entries (at most 65,535 entries, so at most 193 reads, per IFD), remembers the last entry of each time tag, then
    /// reads at most six values of at most <see cref="MaxTextBytes"/> each. It allocates only those six strings and
    /// skips any value that does not fit in the window, as a damaged count or offset would.
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

    /// <summary>
    /// The Exif IFD's offset from IFD0's pointer tag, or -1. As in MetadataExtractor, the pointer may be any type whose
    /// values are 4 bytes (LONG, SLONG, FLOAT, IFD); the first value is the offset, inside the entry when there is one
    /// value and at the entry's (bounds-checked) offset when there are more. The caller bounds-checks the IFD it gives.
    /// </summary>
    private static long FindExifIfd(ByteWindow tiff, long ifd, bool bigEndian)
    {
        Span<byte> entries = stackalloc byte[EntriesPerRead * EntryBytes];
        var count = EntryCount(tiff, ifd, bigEndian);
        for (var first = 0; first < count; first += EntriesPerRead)
        {
            var block = entries[..((int)Math.Min(EntriesPerRead, count - first) * EntryBytes)];
            if (!tiff.TryRead(ifd + 2 + (first * (long)EntryBytes), block))
            {
                return -1;
            }

            for (var at = 0; at < block.Length; at += EntryBytes)
            {
                var entry = block.Slice(at, EntryBytes);
                var valueCount = Read32(entry, 4, bigEndian);
                if (Read16(entry, 0, bigEndian) != ExifIfdPointerTag || !IsFourByteType(Read16(entry, 2, bigEndian)) || valueCount == 0)
                {
                    continue;
                }

                if (valueCount == 1)
                {
                    return Read32(entry, 8, bigEndian);
                }

                return tiff.TryReadUInt32(Read32(entry, 8, bigEndian), bigEndian, out var pointer) ? pointer : -1;
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

        // The last entry of each of the ten tags, by SlotOf (a later duplicate replaces an earlier one, as
        // MetadataExtractor did). Values are read once the table is done, so duplicates cost no reads.
        Span<byte> last = stackalloc byte[Slots * EntryBytes];
        Span<bool> seen = stackalloc bool[Slots];
        Span<byte> entries = stackalloc byte[EntriesPerRead * EntryBytes];
        var count = EntryCount(tiff, ifd, bigEndian);
        for (var first = 0; first < count; first += EntriesPerRead)
        {
            var block = entries[..((int)Math.Min(EntriesPerRead, count - first) * EntryBytes)];
            if (!tiff.TryRead(ifd + 2 + (first * (long)EntryBytes), block))
            {
                break;
            }

            for (var at = 0; at < block.Length; at += EntryBytes)
            {
                var slot = SlotOf(Read16(block, at, bigEndian));
                if (slot >= 0)
                {
                    block.Slice(at, EntryBytes).CopyTo(last.Slice(slot * EntryBytes, EntryBytes));
                    seen[slot] = true;
                }
            }
        }

        Span<byte> text = stackalloc byte[MaxTextBytes];
        return new ExifTimeTags(
            ValueOf(0, tiff, last, seen, bigEndian, text),
            ValueOf(1, tiff, last, seen, bigEndian, text),
            ValueOf(2, tiff, last, seen, bigEndian, text),
            ValueOf(3, tiff, last, seen, bigEndian, text),
            ValueOf(4, tiff, last, seen, bigEndian, text),
            ValueOf(5, tiff, last, seen, bigEndian, text))
        {
            ExposureTime = NumberOf(6, tiff, last, seen, bigEndian),
            FNumber = NumberOf(7, tiff, last, seen, bigEndian),
            Iso = NumberOf(8, tiff, last, seen, bigEndian) is { } iso and < 65535d ? iso : NumberOf(9, tiff, last, seen, bigEndian) ?? NumberOf(8, tiff, last, seen, bigEndian),
        };
    }

    /// <summary>The remembered entry's first value as a positive number, or null when the tag is absent, of another type, zero or out of the window.</summary>
    private static double? NumberOf(int slot, ByteWindow tiff, ReadOnlySpan<byte> last, ReadOnlySpan<bool> seen, bool bigEndian)
    {
        if (!seen[slot])
        {
            return null;
        }

        var entry = last.Slice(slot * EntryBytes, EntryBytes);
        var type = Read16(entry, 2, bigEndian);
        if (Read32(entry, 4, bigEndian) == 0)
        {
            return null;
        }

        double value;
        switch (type)
        {
            case ShortType:
                value = Read16(entry, 8, bigEndian);
                break;
            case LongType:
                value = Read32(entry, 8, bigEndian);
                break;
            case RationalType or SignedRationalType:
                // Eight bytes do not fit in the entry: they are at its offset, bounds-checked.
                Span<byte> pair = stackalloc byte[8];
                if (!tiff.TryRead(Read32(entry, 8, bigEndian), pair))
                {
                    return null;
                }

                var numerator = type == RationalType ? Read32(pair, 0, bigEndian) : (double)(int)Read32(pair, 0, bigEndian);
                var denominator = type == RationalType ? Read32(pair, 4, bigEndian) : (double)(int)Read32(pair, 4, bigEndian);
                if (denominator == 0)
                {
                    return null;
                }

                value = numerator / denominator;
                break;
            default:
                return null;
        }

        return double.IsFinite(value) && value > 0 ? value : null;
    }

    /// <summary>The text of the remembered entry in <paramref name="slot"/>, or null when the tag was not in the IFD.</summary>
    private static string? ValueOf(int slot, ByteWindow tiff, ReadOnlySpan<byte> last, ReadOnlySpan<bool> seen, bool bigEndian, Span<byte> text) =>
        seen[slot] ? ReadText(tiff, last.Slice(slot * EntryBytes, EntryBytes), bigEndian, text) : null;

    /// <summary>The slot of one of the ten tags: the six text tags in the order of the record's parameters, then the exposure tags; -1 for any other tag.</summary>
    private static int SlotOf(ushort tag) => tag switch
    {
        DateTimeOriginalTag => 0,
        SubSecTimeOriginalTag => 1,
        OffsetTimeOriginalTag => 2,
        DateTimeDigitizedTag => 3,
        SubSecTimeDigitizedTag => 4,
        OffsetTimeDigitizedTag => 5,
        ExposureTimeTag => 6,
        FNumberTag => 7,
        PhotographicSensitivityTag => 8,
        IsoSpeedTag => 9,
        _ => -1,
    };

    /// <summary>LONG, SLONG, FLOAT and IFD: the TIFF types whose values are 4 bytes each.</summary>
    private static bool IsFourByteType(ushort type) => type is 4 or 9 or 11 or 13;

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
