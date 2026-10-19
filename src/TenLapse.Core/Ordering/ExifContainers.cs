using System.Buffers.Binary;

namespace TenLapse.Core.Ordering;

/// <summary>
/// Finds the Exif block in each photo container the app imports and reads its time tags, touching only the bytes
/// metadata lives in and never allocating on the strength of a length or count field (#84).
/// </summary>
/// <remarks>
/// <para>Per format (found by content, not extension):</para>
/// <list type="bullet">
/// <item><b>JPEG</b>: the marker segments from SOI to SOS; each APP1 that starts with <c>Exif\0\0</c> is read in place
/// (at most 65,527 bytes, the segment limit). Other segments are skipped by seeking.</item>
/// <item><b>TIFF and TIFF-based RAW</b> (CR2, NEF, ARW, DNG, ORF, RW2, PEF and the rest that start with a TIFF
/// header): IFD0 and the Exif IFD, read in place; only entry tables and the time values are read.</item>
/// <item><b>PNG</b>: chunk headers until IEND, skipping data by seeking; the first <c>eXIf</c> chunk is read in place.</item>
/// <item><b>WebP</b>: RIFF chunk headers; the <c>EXIF</c> chunk is read in place.</item>
/// <item><b>HEIF, HEIC, AVIF</b>: the top-level <c>meta</c> box is loaded (at most <see cref="MaxMetaBoxBytes"/>) and
/// <c>iinf</c> and <c>iloc</c> parsed in a loop bounded by the box's bytes, never sized by its counts; the
/// <c>Exif</c> item is read in place when it is one extent, or copied (at most <see cref="MaxExifItemBytes"/>) when
/// it is several.</item>
/// <item><b>CR3</b>: <c>moov</c>, Canon's <c>uuid</c> box and its <c>CMT2</c> box (the Exif IFD as a TIFF), in place.</item>
/// <item><b>RAF</b>: the embedded JPEG the header points to, as JPEG.</item>
/// </list>
/// <para>
/// Anything else (BMP, GIF, JPEG XR, JPEG XL, other RAW) has no capture time. So the most a file can make the reader
/// allocate is the HEIF meta box plus a split Exif item, about 2 MB, whatever its count and length fields say.
/// </para>
/// </remarks>
internal static class ExifContainers
{
    /// <summary>The largest HEIF <c>meta</c> box loaded. Real ones are a few kilobytes (an iPhone grid of 48 tiles is about 10 KB).</summary>
    internal const int MaxMetaBoxBytes = 1024 * 1024;

    /// <summary>The largest Exif item copied together from several extents (a single extent is read in place).</summary>
    internal const int MaxExifItemBytes = 1024 * 1024;

    // Walk limits: each step reads a few bytes, and a damaged file must not turn into millions of tiny reads.
    private const int MaxJpegSegments = 4096;
    private const int MaxPngChunks = 100_000;
    private const int MaxRiffChunks = 4096;
    private const int MaxBoxes = 4096;
    private const int MaxExifItems = 4;
    private const int MaxExtents = 16;

    // Box types: four characters as the big-endian number a box header stores.
    private const uint MoovBox = 0x6D6F6F76; // "moov"
    private const uint UuidBox = 0x75756964; // "uuid"
    private const uint Cmt2Box = 0x434D5432; // "CMT2"
    private const uint MetaBox = 0x6D657461; // "meta"
    private const uint IinfBox = 0x69696E66; // "iinf"
    private const uint InfeBox = 0x696E6665; // "infe"
    private const uint IlocBox = 0x696C6F63; // "iloc"
    private const uint IdatBox = 0x69646174; // "idat"

    private static ReadOnlySpan<byte> ExifPreamble => "Exif\0\0"u8;

    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> RafSignature => "FUJIFILMCCD-RAW "u8;

    // Canon's CR3 metadata box: uuid 85c0b687-820f-11e0-8111-f4ce462b6a48.
    private static ReadOnlySpan<byte> CanonUuid =>
        [0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48];

    /// <summary>The capture time in the file's first Exif block that has one, or null.</summary>
    public static CaptureTimeReading? Read(ByteWindow file)
    {
        Span<byte> head = stackalloc byte[16];
        head = head[..(int)Math.Min(head.Length, file.Length)];
        if (!file.TryRead(0, head) || head.Length < 8)
        {
            return null;
        }

        if (head[0] == 0xFF && head[1] == 0xD8)
        {
            return FromJpeg(file);
        }

        if (head.StartsWith(PngSignature))
        {
            return FromPng(file);
        }

        if (head.StartsWith("II"u8) || head.StartsWith("MM"u8))
        {
            return FromTags(ExifTimeTags.ReadTiff(file));
        }

        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return FromWebP(file);
        }

        if (head[4..8].SequenceEqual("ftyp"u8))
        {
            return head.Length >= 12 && head[8..12].SequenceEqual("crx "u8) ? FromCr3(file) : FromHeif(file);
        }

        if (head.Length == 16 && head.SequenceEqual(RafSignature))
        {
            return FromRaf(file);
        }

        return null;
    }

    private static CaptureTimeReading? FromTags(ExifTimeTags? tags) => tags is { } t ? CaptureTimeReader.FromTags(t) : null;

    /// <summary>Exif in a block that may start with the <c>Exif\0\0</c> preamble (PNG and WebP writers differ).</summary>
    private static CaptureTimeReading? FromExifBlock(ByteWindow? block)
    {
        if (block is null)
        {
            return null;
        }

        if (block.Matches(0, ExifPreamble))
        {
            block = block.Slice(ExifPreamble.Length, block.Length - ExifPreamble.Length);
        }

        return block is null ? null : FromTags(ExifTimeTags.ReadTiff(block));
    }

    private static CaptureTimeReading? FromJpeg(ByteWindow jpeg)
    {
        Span<byte> marker = stackalloc byte[2];
        long position = 2;
        for (var step = 0; step < MaxJpegSegments && jpeg.TryRead(position, marker); step++)
        {
            if (marker[0] != 0xFF)
            {
                return null;
            }

            var type = marker[1];
            if (type == 0xFF)
            {
                // Fill byte before a marker.
                position++;
                continue;
            }

            if (type is 0xDA or 0xD9)
            {
                // Start of scan or end of image: metadata segments come before both.
                return null;
            }

            if (type is 0x01 or 0xD8 or (>= 0xD0 and <= 0xD7))
            {
                // Markers without a length.
                position += 2;
                continue;
            }

            if (!jpeg.TryReadUInt16(position + 2, bigEndian: true, out var length) || length < 2)
            {
                return null;
            }

            var payload = jpeg.Slice(position + 4, length - 2);
            if (payload is null)
            {
                return null;
            }

            if (type == 0xE1 && payload.Matches(0, ExifPreamble)
                && FromTags(ExifTimeTags.ReadTiff(payload.Slice(ExifPreamble.Length, payload.Length - ExifPreamble.Length)!)) is { } reading)
            {
                return reading;
            }

            position += 2 + length;
        }

        return null;
    }

    private static CaptureTimeReading? FromPng(ByteWindow png)
    {
        Span<byte> header = stackalloc byte[8];
        long position = PngSignature.Length;
        for (var step = 0; step < MaxPngChunks && png.TryRead(position, header); step++)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = header[4..];
            if (type.SequenceEqual("eXIf"u8))
            {
                return FromExifBlock(png.Slice(position + 8, length));
            }

            if (type.SequenceEqual("IEND"u8))
            {
                return null;
            }

            // Length, type, data, CRC.
            position += 12L + length;
        }

        return null;
    }

    private static CaptureTimeReading? FromWebP(ByteWindow webp)
    {
        if (!webp.TryReadUInt32(4, bigEndian: false, out var riffSize))
        {
            return null;
        }

        var end = Math.Min(webp.Length, 8L + riffSize);
        Span<byte> header = stackalloc byte[8];
        long position = 12;
        for (var step = 0; step < MaxRiffChunks && position + 8 <= end && webp.TryRead(position, header); step++)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (header[..4].SequenceEqual("EXIF"u8))
            {
                return FromExifBlock(webp.Slice(position + 8, size));
            }

            // Chunks are padded to an even size.
            position += 8L + size + (size & 1);
        }

        return null;
    }

    private static CaptureTimeReading? FromRaf(ByteWindow raf)
    {
        // The header holds the embedded JPEG's offset and length (big-endian) at 84 and 88; the JPEG carries the Exif.
        if (!raf.TryReadUInt32(84, bigEndian: true, out var offset) || !raf.TryReadUInt32(88, bigEndian: true, out var length))
        {
            return null;
        }

        var jpeg = raf.Slice(offset, length);
        return jpeg is not null && jpeg.Matches(0, [0xFF, 0xD8]) ? FromJpeg(jpeg) : null;
    }

    private static CaptureTimeReading? FromCr3(ByteWindow file)
    {
        var moov = FindBox(file, 0, file.Length, MoovBox);
        var canon = moov is { } m ? FindBox(file, m.PayloadStart, m.End, UuidBox, CanonUuid) : null;
        var exif = canon is { } c ? FindBox(file, c.PayloadStart, c.End, Cmt2Box) : null;
        return exif is { } e ? FromTags(ExifTimeTags.ReadTiff(file.Slice(e.PayloadStart, e.End - e.PayloadStart)!, firstIfdIsExif: true)) : null;
    }

    private static CaptureTimeReading? FromHeif(ByteWindow file)
    {
        if (FindBox(file, 0, file.Length, MetaBox) is not { } metaBox || metaBox.End - metaBox.PayloadStart is > MaxMetaBoxBytes or < 4)
        {
            return null;
        }

        var meta = new byte[metaBox.End - metaBox.PayloadStart];
        if (!file.TryRead(metaBox.PayloadStart, meta))
        {
            return null;
        }

        // meta is a full box: version and flags, then its children.
        var children = ByteWindow.Over(meta);
        Span<uint> exifItems = stackalloc uint[MaxExifItems];
        var exifItemCount = FindExifItems(children, FindBox(children, 4, meta.Length, IinfBox), exifItems);
        if (exifItemCount == 0 || FindBox(children, 4, meta.Length, IlocBox) is not { } iloc)
        {
            return null;
        }

        var idat = FindBox(children, 4, meta.Length, IdatBox);
        foreach (var item in exifItems[..exifItemCount])
        {
            if (FromTags(ReadExifItem(file, meta, iloc, idat, item)) is { } reading)
            {
                return reading;
            }
        }

        return null;
    }

    /// <summary>The ids of the <c>Exif</c> items in <c>iinf</c> (version 2 and 3 <c>infe</c> entries carry the item type).</summary>
    private static int FindExifItems(ByteWindow meta, Box? iinf, Span<uint> ids)
    {
        Span<byte> version = stackalloc byte[1];
        if (iinf is not { } box || !meta.TryRead(box.PayloadStart, version))
        {
            return 0;
        }

        // Version, flags, then the entry count (2 bytes in version 0, else 4). The entries are walked as boxes up to
        // the end of iinf, so the count is never trusted.
        var found = 0;
        var position = box.PayloadStart + 4 + (version[0] == 0 ? 2 : 4);
        Span<byte> infe = stackalloc byte[12];
        for (var step = 0; step < MaxBoxes && found < ids.Length && TryReadBox(meta, position, box.End, out var entry); step++)
        {
            position = entry.End;
            if (entry.Type != InfeBox || !meta.TryRead(entry.PayloadStart, infe[..1]) || infe[0] < 2)
            {
                continue;
            }

            // Version 2: 16-bit item id; version 3: 32-bit. Then a 16-bit protection index and the 4-character type.
            var idBytes = infe[0] == 2 ? 2 : 4;
            if (!meta.TryRead(entry.PayloadStart + 4, infe[..(idBytes + 6)]))
            {
                continue;
            }

            if (infe.Slice(idBytes + 2, 4).SequenceEqual("Exif"u8))
            {
                ids[found++] = idBytes == 2 ? BinaryPrimitives.ReadUInt16BigEndian(infe) : BinaryPrimitives.ReadUInt32BigEndian(infe);
            }
        }

        return found;
    }

    /// <summary>
    /// The time tags of one Exif item, located through <c>iloc</c>. The loop over items and extents stops at the end of
    /// the box: a count larger than the bytes behind it is a damaged box, never an allocation.
    /// </summary>
    private static ExifTimeTags? ReadExifItem(ByteWindow file, byte[] meta, Box iloc, Box? idat, uint itemId)
    {
        var box = meta.AsSpan((int)iloc.PayloadStart, (int)(iloc.End - iloc.PayloadStart));
        var reader = new SpanReader(box);
        if (!reader.TryRead(1, out var version) || version > 2 || !reader.Skip(3)
            || !reader.TryRead(1, out var sizes) || !reader.TryRead(1, out var moreSizes))
        {
            return null;
        }

        int offsetSize = (int)(sizes >> 4), lengthSize = (int)(sizes & 0xF), baseOffsetSize = (int)(moreSizes >> 4);
        var indexSize = version >= 1 ? (int)(moreSizes & 0xF) : 0;
        if (!IsFieldSize(offsetSize) || !IsFieldSize(lengthSize) || !IsFieldSize(baseOffsetSize) || !IsFieldSize(indexSize)
            || !reader.TryRead(version < 2 ? 2 : 4, out var itemCount))
        {
            return null;
        }

        var extentBytes = indexSize + offsetSize + lengthSize;
        for (ulong item = 0; item < itemCount; item++)
        {
            ulong method = 0;
            if (!reader.TryRead(version < 2 ? 2 : 4, out var id)
                || (version >= 1 && !reader.TryRead(2, out method))
                || !reader.Skip(2)
                || !reader.TryRead(baseOffsetSize, out var baseOffset)
                || !reader.TryRead(2, out var extentCount)
                || extentCount * (ulong)extentBytes > (ulong)reader.Remaining)
            {
                return null;
            }

            if (id != itemId)
            {
                reader.Skip((int)extentCount * extentBytes);
                continue;
            }

            // Construction method 0 is an offset in the file, 1 an offset in idat; 2 (another item's data) is not used for Exif.
            if ((method & 0xF) > 1 || extentCount is 0 or > MaxExtents)
            {
                return null;
            }

            var source = (method & 0xF) == 0 ? file : idat is { } d ? ByteWindow.Over(meta).Slice(d.PayloadStart, d.End - d.PayloadStart) : null;
            return source is null ? null : ReadExtents(source, ref reader, (int)extentCount, indexSize, offsetSize, lengthSize, baseOffset);
        }

        return null;
    }

    private static ExifTimeTags? ReadExtents(
        ByteWindow source, ref SpanReader reader, int extentCount, int indexSize, int offsetSize, int lengthSize, ulong baseOffset)
    {
        Span<(ulong Offset, ulong Length)> extents = stackalloc (ulong, ulong)[extentCount];
        ulong total = 0;
        for (var i = 0; i < extentCount; i++)
        {
            if (!reader.Skip(indexSize) || !reader.TryRead(offsetSize, out var offset) || !reader.TryRead(lengthSize, out var length)
                || length == 0 || offset > long.MaxValue / 2 || baseOffset > long.MaxValue / 2 || length > MaxExifItemBytes)
            {
                return null;
            }

            // Every extent must be there before anything is copied, so a damaged length never sizes an allocation.
            extents[i] = (baseOffset + offset, length);
            total += length;
            if (!source.Contains((long)extents[i].Offset, (long)length))
            {
                return null;
            }
        }

        ByteWindow? item;
        if (extentCount == 1)
        {
            item = source.Slice((long)extents[0].Offset, (long)extents[0].Length);
        }
        else
        {
            if (total > MaxExifItemBytes)
            {
                return null;
            }

            var bytes = new byte[total];
            var at = 0;
            foreach (var (offset, length) in extents)
            {
                if (!source.TryRead((long)offset, bytes.AsSpan(at, (int)length)))
                {
                    return null;
                }

                at += (int)length;
            }

            item = ByteWindow.Over(bytes);
        }

        // An Exif item starts with a 32-bit offset to the TIFF header (past an "Exif\0\0" that most writers put there).
        if (item is null || !item.TryReadUInt32(0, bigEndian: true, out var headerOffset) || headerOffset > item.Length)
        {
            return null;
        }

        var tiff = item.Slice(4 + headerOffset, item.Length - 4 - headerOffset);
        return tiff is null ? null : ExifTimeTags.ReadTiff(tiff);
    }

    private static bool IsFieldSize(int size) => size is 0 or 4 or 8;

    /// <summary>The first box of <paramref name="type"/> (and, for <c>uuid</c>, <paramref name="userType"/>) between two offsets.</summary>
    private static Box? FindBox(ByteWindow window, long start, long end, uint type, ReadOnlySpan<byte> userType = default)
    {
        var position = start;
        for (var step = 0; step < MaxBoxes && TryReadBox(window, position, end, out var box); step++)
        {
            if (box.Type == type && (userType.IsEmpty || window.Matches(box.PayloadStart - 16, userType)))
            {
                return box;
            }

            position = box.End;
        }

        return null;
    }

    /// <summary>
    /// Reads an ISO base media box header at <paramref name="position"/>: 32-bit size and type, a 64-bit size when the
    /// size is 1, to the end when it is 0, and a 16-byte user type for <c>uuid</c>. False when the box does not fit
    /// before <paramref name="end"/>.
    /// </summary>
    private static bool TryReadBox(ByteWindow window, long position, long end, out Box box)
    {
        box = default;
        Span<byte> header = stackalloc byte[16];
        if (position < 0 || position + 8 > end || !window.TryRead(position, header[..8]))
        {
            return false;
        }

        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        long headerLength = 8;
        if (size == 1)
        {
            if (position + 16 > end || !window.TryRead(position + 8, header[8..]))
            {
                return false;
            }

            var large = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
            size = large > long.MaxValue ? -1 : (long)large;
            headerLength = 16;
        }
        else if (size == 0)
        {
            size = end - position;
        }

        var type = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        if (type == UuidBox)
        {
            // "uuid": the user type follows the header.
            headerLength += 16;
        }

        if (size < headerLength || size > end - position)
        {
            return false;
        }

        box = new Box(type, position + headerLength, position + size);
        return true;
    }


    /// <summary>An ISO base media box: its four-character type, where its payload starts and where it ends.</summary>
    private readonly record struct Box(uint Type, long PayloadStart, long End);

    /// <summary>Big-endian fields of 0 to 8 bytes from a span, bounds-checked.</summary>
    private ref struct SpanReader(ReadOnlySpan<byte> span)
    {
        private readonly ReadOnlySpan<byte> _span = span;
        private int _position;

        public readonly int Remaining => _span.Length - _position;

        public bool TryRead(int size, out ulong value)
        {
            value = 0;
            if (size > Remaining)
            {
                return false;
            }

            foreach (var b in _span.Slice(_position, size))
            {
                value = (value << 8) | b;
            }

            _position += size;
            return true;
        }

        public bool Skip(int count)
        {
            if (count < 0 || count > Remaining)
            {
                return false;
            }

            _position += count;
            return true;
        }
    }
}
