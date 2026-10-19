using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace TenLapse.Tests.Ordering;

/// <summary>
/// Builds the smallest photo-like files that carry the Exif tags the capture-time reader looks at: a JPEG with
/// an Exif APP1, plain TIFF containers like DNG, NEF and ARW (little or big endian), CR2, CR3, RAF, PNG (eXIf and raw
/// profiles), WebP and HEIF. No pixels except the optional padding, which stands in for image data the reader must
/// not read.
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

    /// <summary>
    /// The TIFF block of an Exif segment or item: IFD0 pointing to the Exif IFD, little-endian. The pointer's TIFF type
    /// is LONG (4) unless <paramref name="pointerType"/> says otherwise (SLONG is 9, IFD 13, SHORT 3).
    /// </summary>
    public static byte[] ExifTiff(Tags tags, ushort pointerType = 4) => Tiff(tags, bigEndian: false, ifd0Offset: 8, prefix: [], pointerType: pointerType);

    /// <summary>An Exif APP1 segment: marker, length, "Exif\0\0" and <paramref name="tiff"/>.</summary>
    public static byte[] ExifSegment(byte[] tiff)
    {
        var length = 2 + 6 + tiff.Length;
        return [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. "Exif\0\0"u8, .. tiff];
    }

    /// <summary>A JPEG made of SOI, the given bytes (segments, or junk between them), a start of scan and EOI.</summary>
    public static byte[] JpegOf(params byte[][] parts) =>
        [0xFF, 0xD8, .. parts.SelectMany(part => part), 0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xD9];

    /// <summary>
    /// A Fujifilm RAF: the 16-byte signature, a header whose bytes 84 and 88 hold the embedded JPEG's offset and length
    /// (big-endian), then the JPEG. <paramref name="claimedLength"/> overrides the length, as in a file cut short.
    /// </summary>
    public static byte[] Raf(byte[] jpeg, uint? claimedLength = null)
    {
        const int jpegOffset = 160;
        var raf = new byte[jpegOffset + jpeg.Length];
        "FUJIFILMCCD-RAW 0201FF383501"u8.CopyTo(raf);
        BinaryPrimitives.WriteUInt32BigEndian(raf.AsSpan(84), jpegOffset);
        BinaryPrimitives.WriteUInt32BigEndian(raf.AsSpan(88), claimedLength ?? (uint)jpeg.Length);
        jpeg.CopyTo(raf, jpegOffset);
        return raf;
    }

    /// <summary>How a PNG raw-profile text chunk is stored.</summary>
    public enum PngText
    {
        /// <summary><c>tEXt</c>: the text as it is.</summary>
        Plain,

        /// <summary><c>zTXt</c>: the text deflated (zlib).</summary>
        Compressed,

        /// <summary><c>iTXt</c>, uncompressed, with a language tag and a translated keyword.</summary>
        International,

        /// <summary><c>iTXt</c>, compressed.</summary>
        InternationalCompressed,
    }

    /// <summary>
    /// The raw-profile text ImageMagick writes: a newline, the profile name, the byte count right-aligned in 8
    /// characters, then the bytes in hex, 72 characters to a line.
    /// </summary>
    public static byte[] RawProfileText(string name, byte[] bytes)
    {
        var text = new StringBuilder();
        text.Append('\n').Append(name).Append('\n').Append(bytes.Length.ToString(CultureInfo.InvariantCulture).PadLeft(8)).Append('\n');
        for (var i = 0; i < bytes.Length; i++)
        {
            text.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            if (i % 36 == 35)
            {
                text.Append('\n');
            }
        }

        text.Append('\n');
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>
    /// A PNG whose Exif is a raw profile ("Exif\0\0" and the TIFF block, in hex) in a text chunk keyed
    /// <paramref name="keyword"/>, as ImageMagick and exiv2 write it.
    /// </summary>
    public static byte[] PngRawProfile(Tags tags, PngText storage, string keyword = "Raw profile type exif") =>
        PngWithText(storage, keyword, RawProfileText(keyword.EndsWith("APP1", StringComparison.Ordinal) ? "APP1" : "exif", [.. "Exif\0\0"u8, .. ExifTiff(tags)]));

    /// <summary>A PNG with one text chunk keyed <paramref name="keyword"/> holding <paramref name="text"/>, stored as <paramref name="storage"/> says.</summary>
    public static byte[] PngWithText(PngText storage, string keyword, byte[] text)
    {
        byte[] name = [.. Encoding.Latin1.GetBytes(keyword), 0];
        var (type, data) = storage switch
        {
            PngText.Plain => ("tEXt", (byte[])[.. name, .. text]),
            PngText.Compressed => ("zTXt", [.. name, 0, .. Deflate(text)]),
            PngText.International => ("iTXt", [.. name, 0, 0, .. "en\0"u8, .. "Exif\0"u8, .. text]),
            _ => ("iTXt", [.. name, 1, 0, .. "en\0"u8, .. "Exif\0"u8, .. Deflate(text)]),
        };

        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        PngChunk(output, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
        PngChunk(output, "IDAT", new byte[16]);
        PngChunk(output, type, data);
        PngChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>zlib-compressed bytes, as <c>zTXt</c> and a compressed <c>iTXt</c> store them.</summary>
    public static byte[] Deflate(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(bytes);
        }

        return output.ToArray();
    }

    /// <summary>
    /// A little-endian TIFF whose Exif IFD has <paramref name="entries"/> <c>DateTimeOriginal</c> entries: each points
    /// at an unset time ("0000:00:00 00:00:00") except the last, which points at <paramref name="lastTime"/>. The
    /// largest IFD the format allows has 65,535 entries.
    /// </summary>
    public static byte[] TiffWithRepeatedTimes(int entries, string lastTime)
    {
        const int exifIfd = 8 + 2 + 12 + 4;
        var values = exifIfd + 2 + (entries * 12) + 4;
        var tiff = new byte[values + 40];
        "II"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), 0x8769);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(18), exifIfd);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(exifIfd), (ushort)entries);
        for (var i = 0; i < entries; i++)
        {
            var entry = exifIfd + 2 + (i * 12);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry), DateTimeOriginal);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry + 2), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 4), 20);
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 8), (uint)(i == entries - 1 ? values + 20 : values));
        }

        "0000:00:00 00:00:00\0"u8.CopyTo(tiff.AsSpan(values));
        Encoding.ASCII.GetBytes(lastTime + "\0").CopyTo(tiff, values + 20);
        return tiff;
    }

    /// <summary>
    /// A little-endian TIFF like <see cref="ExifTiff"/> whose Exif IFD also holds <paramref name="otherEntries"/>
    /// entries of other tags (one SHORT each) after the time tags, sorted by tag as a real IFD is.
    /// </summary>
    public static byte[] TiffWithLargeExifIfd(Tags tags, int otherEntries)
    {
        var plain = ExifTiff(tags);
        const int exifIfd = 8 + 2 + 12 + 4;
        var timeEntries = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(exifIfd));
        var shift = otherEntries * 12;
        var tail = exifIfd + 2 + (timeEntries * 12);
        var tiff = new byte[plain.Length + shift];
        plain.AsSpan(0, tail).CopyTo(tiff);
        plain.AsSpan(tail).CopyTo(tiff.AsSpan(tail + shift));
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(exifIfd), (ushort)(timeEntries + otherEntries));
        for (var i = 0; i < timeEntries; i++)
        {
            // Values longer than 4 bytes move with the data behind the larger table.
            var entry = exifIfd + 2 + (i * 12);
            if (BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(entry + 4)) > 4)
            {
                var offset = BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(entry + 8));
                BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 8), offset + (uint)shift);
            }
        }

        for (var i = 0; i < otherEntries; i++)
        {
            var entry = tail + (i * 12);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry), (ushort)(0xA000 + i));
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry + 2), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 8), 0);
        }

        return tiff;
    }

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
    /// The <c>iloc</c> layout of a hand-built HEIF: the box version (0, 1 or 2; version 2 has 32-bit item ids and
    /// counts); the size in bytes (0, 4 or 8) of each extent offset, extent length, base offset and extent index (the
    /// index only in versions 1 and 2); how many extents the Exif item is cut into; and whether it lives in <c>idat</c>.
    /// With a base offset, each item's base is where its data starts and its extent offsets are relative to it.
    /// </summary>
    public sealed record HeifIloc(byte Version = 0, int OffsetSize = 4, int LengthSize = 4, int BaseOffsetSize = 0, int IndexSize = 0, int Extents = 1, bool InIdat = false);

    /// <summary>
    /// A HEIF as far as metadata goes: <c>ftyp heic</c>, a <c>meta</c> box with <c>hdlr</c>, <c>pitm</c>, <c>iinf</c>
    /// (an <c>hvc1</c> item and an <c>Exif</c> item), <c>iloc</c>, <c>iprp</c> with an <c>ipma</c>, then <c>mdat</c>.
    /// The Exif item is a 4-byte header offset (6), "Exif\0\0" and the TIFF block.
    /// </summary>
    public static byte[] Heif(Tags tags, HeifExif layout = HeifExif.OneExtent) => Heif(tags, layout switch
    {
        HeifExif.OneExtent => new HeifIloc(),
        HeifExif.TwoExtents => new HeifIloc(Version: 1, Extents: 2),
        _ => new HeifIloc(Version: 1, InIdat: true),
    });

    /// <summary>A HEIF like <see cref="Heif(Tags, HeifExif)"/> with the <c>iloc</c> layout <paramref name="iloc"/> describes.</summary>
    public static byte[] Heif(Tags tags, HeifIloc iloc)
    {
        byte[] exifItem = [0, 0, 0, 6, .. "Exif\0\0"u8, .. ExifTiff(tags)];
        byte[] ftyp = Box("ftyp", [.. "heic"u8, 0, 0, 0, 0, .. "mif1"u8, .. "heic"u8]);
        byte[] hdlr = FullBox("hdlr", 0, [0, 0, 0, 0, .. "pict"u8, .. new byte[12], 0]);
        byte[] pitm = FullBox("pitm", 0, [0, 1]);
        byte[] iinf = FullBox("iinf", 0, [0, 2, .. FullBox("infe", 2, [0, 1, 0, 0, .. "hvc1"u8, 0]), .. FullBox("infe", 2, [0, 2, 0, 0, .. "Exif"u8, 0])]);
        byte[] ipma = FullBox("ipma", 0, [0, 0, 0, 1, 0, 1, 1, 0x81]);
        byte[] iprp = Box("iprp", [.. Box("ipco", FullBox("ispe", 0, [0, 0, 0, 1, 0, 0, 0, 1])), .. ipma]);
        byte[] idat = iloc.InIdat ? Box("idat", exifItem) : [];

        // The iloc length is fixed by its layout, so the meta box length is known before the offsets are filled in.
        byte[] Iloc(ulong mdatData)
        {
            using var s = new MemoryStream();
            void Field(int size, ulong value)
            {
                for (var shift = (size - 1) * 8; shift >= 0; shift -= 8)
                {
                    s.WriteByte((byte)(value >> shift));
                }
            }

            var idSize = iloc.Version < 2 ? 2 : 4;
            var indexSize = iloc.Version >= 1 ? iloc.IndexSize : 0;
            s.WriteByte((byte)((iloc.OffsetSize << 4) | iloc.LengthSize));
            s.WriteByte((byte)((iloc.BaseOffsetSize << 4) | indexSize));
            Field(idSize, 2);                                   // item count

            // Item 1, the image: 8 bytes of mdat after the Exif item (at its start when the Exif item is in idat).
            var imageData = mdatData + (iloc.InIdat ? 0 : (ulong)exifItem.Length);
            var imageBase = iloc.BaseOffsetSize > 0 ? imageData : 0;
            Field(idSize, 1);
            if (iloc.Version >= 1)
            {
                Field(2, 0);                                    // construction method 0
            }

            Field(2, 0);                                        // data reference index
            Field(iloc.BaseOffsetSize, imageBase);
            Field(2, 1);                                        // one extent
            Field(indexSize, 0);
            Field(iloc.OffsetSize, imageData - imageBase);
            Field(iloc.LengthSize, 8);

            // Item 2, Exif, cut into equal extents (the last takes the rest).
            var exifData = iloc.InIdat ? 0 : mdatData;
            var exifBase = iloc.BaseOffsetSize > 0 ? exifData : 0;
            Field(idSize, 2);
            if (iloc.Version >= 1)
            {
                Field(2, iloc.InIdat ? 1UL : 0);
            }

            Field(2, 0);
            Field(iloc.BaseOffsetSize, exifBase);
            Field(2, (ulong)iloc.Extents);
            var part = (ulong)exifItem.Length / (ulong)iloc.Extents;
            for (var i = 0; i < iloc.Extents; i++)
            {
                var start = exifData + ((ulong)i * part);
                var length = i == iloc.Extents - 1 ? (ulong)exifItem.Length - ((ulong)i * part) : part;
                Field(indexSize, 0);
                Field(iloc.OffsetSize, start - exifBase);
                Field(iloc.LengthSize, length);
            }

            return FullBox("iloc", iloc.Version, s.ToArray());
        }

        byte[] Meta(ulong mdatData) => FullBox("meta", 0, [.. hdlr, .. pitm, .. Iloc(mdatData), .. iinf, .. iprp, .. idat]);

        var mdatDataStart = (ulong)(ftyp.Length + Meta(0).Length + 8);
        byte[] mdat = Box("mdat", [.. (iloc.InIdat ? [] : exifItem), .. new byte[8]]);
        return [.. ftyp, .. Meta(mdatDataStart), .. mdat];
    }

    /// <summary>
    /// A HEIF whose <c>iinf</c> names one Exif item <paramref name="names"/> times and whose <c>iloc</c> cuts it into
    /// <paramref name="extents"/> extents of <paramref name="extentBytes"/>, all pointing at the same bytes of
    /// <c>mdat</c>. The item has no Exif IFD, so no name of it gives a time.
    /// </summary>
    public static byte[] HeifRepeatedExifItem(int names, int extents, int extentBytes)
    {
        var ftyp = Box("ftyp", [.. "heic"u8, 0, 0, 0, 0, .. "mif1"u8, .. "heic"u8]);
        var infe = FullBox("infe", 2, [0, 2, 0, 0, .. "Exif"u8, 0]);
        var iinf = FullBox("iinf", 0, [0, (byte)names, .. Enumerable.Repeat(infe, names).SelectMany(box => box)]);
        byte[] Iloc(uint mdatData)
        {
            byte[] extent = [.. BigEndian32(mdatData), .. BigEndian32((uint)extentBytes)];
            return FullBox("iloc", 1, [0x44, 0x00, 0, 1, 0, 2, 0, 0, 0, 0, 0, (byte)extents, .. Enumerable.Repeat(extent, extents).SelectMany(e => e)]);
        }

        byte[] Meta(uint mdatData) => FullBox("meta", 0, [.. iinf, .. Iloc(mdatData)]);
        var mdatData = (uint)(ftyp.Length + Meta(0).Length + 8);
        var data = new byte[extentBytes];
        byte[] item = [0, 0, 0, 6, .. "Exif\0\0"u8, .. "II"u8, 42, 0, 8, 0, 0, 0, 0, 0];
        item.CopyTo(data, 0);
        return [.. ftyp, .. Meta(mdatData), .. Box("mdat", data)];
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

    private static byte[] Tiff(Tags tags, bool bigEndian, int ifd0Offset, byte[] prefix, bool timesInIfd0 = false, ushort pointerType = 4)
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
            Write16(pointerType);
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
