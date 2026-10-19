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

    /// <summary>The TIFF block of an Exif segment or item: IFD0 pointing to the Exif IFD, little-endian.</summary>
    public static byte[] ExifTiff(Tags tags) => Tiff(tags, bigEndian: false, ifd0Offset: 8, prefix: []);

    /// <summary>
    /// A PNG: signature, IHDR, the Exif block in an <c>eXIf</c> chunk before IDAT (or after it), one IDAT, IEND, with
    /// real CRCs. <paramref name="preamble"/> puts "Exif\0\0" in front of the TIFF block, as some writers do.
    /// </summary>
    public static byte[] Png(Tags tags, bool afterIdat = false, bool preamble = false, int idatBytes = 16)
    {
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        PngChunk(output, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
        byte[] exif = preamble ? [.. "Exif\0\0"u8, .. ExifTiff(tags)] : ExifTiff(tags);
        if (!afterIdat)
        {
            PngChunk(output, "eXIf", exif);
        }

        PngChunk(output, "IDAT", new byte[idatBytes]);
        if (afterIdat)
        {
            PngChunk(output, "eXIf", exif);
        }

        PngChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>A WebP: RIFF, a VP8X header that announces Exif, a tiny VP8L chunk and the <c>EXIF</c> chunk.</summary>
    public static byte[] WebP(Tags tags, bool preamble = false)
    {
        using var body = new MemoryStream();
        body.Write("WEBP"u8);
        RiffChunk(body, "VP8X", [0x08, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        RiffChunk(body, "VP8L", [0x2F, 0, 0, 0, 0x00]);
        RiffChunk(body, "EXIF", preamble ? [.. "Exif\0\0"u8, .. ExifTiff(tags)] : ExifTiff(tags));
        using var output = new MemoryStream();
        output.Write("RIFF"u8);
        output.Write(LittleEndian32((uint)body.Length));
        body.WriteTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// A Canon CR3 as far as metadata goes: <c>ftyp crx</c>, then <c>moov</c> holding Canon's <c>uuid</c> box with
    /// CMT1 (IFD0) and CMT2 (the Exif IFD as a TIFF of its own, times in its first IFD).
    /// </summary>
    public static byte[] Cr3(Tags tags)
    {
        byte[] canonUuid = [0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48];
        var cmt1 = Tiff(new Tags(), bigEndian: false, ifd0Offset: 8, prefix: []);
        var cmt2 = Tiff(tags, bigEndian: false, ifd0Offset: 8, prefix: [], timesInIfd0: true);
        byte[] uuid = [.. canonUuid, .. Box("CNCV", "CanonCR3_001/01.09.00/00.00.00"u8.ToArray()), .. Box("CMT1", cmt1), .. Box("CMT2", cmt2)];
        return [.. Box("ftyp", [.. "crx "u8, 0, 0, 0, 1, .. "crx "u8, .. "isom"u8]), .. Box("moov", Box("uuid", uuid)), .. Box("mdat", new byte[32])];
    }

    /// <summary>How a hand-built HEIF stores its Exif item.</summary>
    public enum HeifExif
    {
        /// <summary>One extent in <c>mdat</c> (iloc version 0).</summary>
        OneExtent,

        /// <summary>Two extents in <c>mdat</c> that together make the item (iloc version 1).</summary>
        TwoExtents,

        /// <summary>One extent in the meta box's <c>idat</c> (iloc version 1, construction method 1).</summary>
        InIdat,
    }

    /// <summary>
    /// A HEIF as far as metadata goes: <c>ftyp heic</c>, a <c>meta</c> box with <c>hdlr</c>, <c>pitm</c>, <c>iinf</c>
    /// (an <c>hvc1</c> item and an <c>Exif</c> item), <c>iloc</c>, <c>iprp</c> with an <c>ipma</c>, then <c>mdat</c>.
    /// The Exif item is a 4-byte header offset (6), "Exif\0\0" and the TIFF block.
    /// </summary>
    public static byte[] Heif(Tags tags, HeifExif layout = HeifExif.OneExtent)
    {
        byte[] exifItem = [0, 0, 0, 6, .. "Exif\0\0"u8, .. ExifTiff(tags)];
        byte[] ftyp = Box("ftyp", [.. "heic"u8, 0, 0, 0, 0, .. "mif1"u8, .. "heic"u8]);
        byte[] hdlr = FullBox("hdlr", 0, [0, 0, 0, 0, .. "pict"u8, .. new byte[12], 0]);
        byte[] pitm = FullBox("pitm", 0, [0, 1]);
        byte[] iinf = FullBox("iinf", 0, [0, 2, .. FullBox("infe", 2, [0, 1, 0, 0, .. "hvc1"u8, 0]), .. FullBox("infe", 2, [0, 2, 0, 0, .. "Exif"u8, 0])]);
        byte[] ipma = FullBox("ipma", 0, [0, 0, 0, 1, 0, 1, 1, 0x81]);
        byte[] iprp = Box("iprp", [.. Box("ipco", FullBox("ispe", 0, [0, 0, 0, 1, 0, 0, 0, 1])), .. ipma]);
        byte[] idat = layout == HeifExif.InIdat ? Box("idat", exifItem) : [];
        var version = layout == HeifExif.OneExtent ? (byte)0 : (byte)1;

        // The iloc length is fixed by its layout, so the meta box length is known before the offsets are filled in.
        byte[] Iloc(uint mdatData)
        {
            using var s = new MemoryStream();
            s.Write([0x44, 0x00]);          // offset size 4, length size 4, base offset size 0, index size 0
            s.Write([0, 2]);                // item count
            s.Write([0, 1]);                // item 1, the image
            if (version == 1)
            {
                s.Write([0, 0]);            // construction method 0
            }

            s.Write([0, 0, 0, 1]);          // data reference index 0, one extent
            s.Write(BigEndian32(mdatData + (layout == HeifExif.InIdat ? 0 : (uint)exifItem.Length)));
            s.Write(BigEndian32(8));
            s.Write([0, 2]);                // item 2, Exif
            if (version == 1)
            {
                s.Write([0, (byte)(layout == HeifExif.InIdat ? 1 : 0)]);
            }

            s.Write([0, 0]);
            if (layout == HeifExif.TwoExtents)
            {
                var half = (uint)exifItem.Length / 2;
                s.Write([0, 2]);
                s.Write(BigEndian32(mdatData));
                s.Write(BigEndian32(half));
                s.Write(BigEndian32(mdatData + half));
                s.Write(BigEndian32((uint)exifItem.Length - half));
            }
            else
            {
                s.Write([0, 1]);
                s.Write(BigEndian32(layout == HeifExif.InIdat ? 0 : mdatData));
                s.Write(BigEndian32((uint)exifItem.Length));
            }

            return FullBox("iloc", version, s.ToArray());
        }

        byte[] Meta(uint mdatData) => FullBox("meta", 0, [.. hdlr, .. pitm, .. Iloc(mdatData), .. iinf, .. iprp, .. idat]);

        var mdatDataStart = (uint)(ftyp.Length + Meta(0).Length + 8);
        byte[] mdat = Box("mdat", [.. (layout == HeifExif.InIdat ? [] : exifItem), .. new byte[8]]);
        return [.. ftyp, .. Meta(mdatDataStart), .. mdat];
    }

    private static byte[] Box(string type, byte[] payload) => [.. BigEndian32((uint)payload.Length + 8), .. Encoding.ASCII.GetBytes(type), .. payload];

    private static byte[] FullBox(string type, byte version, byte[] payload) => Box(type, [version, 0, 0, 0, .. payload]);

    private static byte[] BigEndian32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] LittleEndian32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static void PngChunk(Stream output, string type, byte[] data)
    {
        byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
        output.Write(BigEndian32((uint)data.Length));
        output.Write(typed);
        output.Write(BigEndian32(Crc32(typed)));
    }

    private static void RiffChunk(Stream output, string type, byte[] data)
    {
        output.Write(Encoding.ASCII.GetBytes(type));
        output.Write(LittleEndian32((uint)data.Length));
        output.Write(data);
        if (data.Length % 2 == 1)
        {
            output.WriteByte(0);
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static byte[] Tiff(Tags tags, bool bigEndian, int ifd0Offset, byte[] prefix, bool timesInIfd0 = false)
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

        // IFD0 holds one entry: the pointer to the Exif IFD that follows it (or, as in a CR3's CMT2, is the Exif IFD).
        var exifIfdOffset = ifd0Offset;
        if (!timesInIfd0)
        {
            const int ifd0Size = 2 + 12 + 4;
            exifIfdOffset = ifd0Offset + ifd0Size;
            Write16(1);
            Write16(0x8769);
            Write16(4);
            Write32(1);
            Write32((uint)exifIfdOffset);
            Write32(0);
        }

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
