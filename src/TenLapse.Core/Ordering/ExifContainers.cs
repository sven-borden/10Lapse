using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace TenLapse.Core.Ordering;

/// <summary>
/// Finds the Exif blocks in each photo container the app imports and reads their time tags, touching only the bytes
/// metadata lives in and never allocating on the strength of a length or count field (#84).
/// </summary>
/// <remarks>
/// <para>Per format (found by content, not extension):</para>
/// <list type="bullet">
/// <item><b>JPEG</b>: the marker segments from SOI to SOS, resynchronising on junk between segments as
/// MetadataExtractor did (at most <see cref="MaxJpegSkippedBytes"/> skipped). Each APP1 that starts with
/// <c>Exif\0\0</c> is read whole into the pooled block buffer (at most 65,527 bytes, the segment limit) and parsed
/// there. Other segments are skipped by seeking.</item>
/// <item><b>TIFF and TIFF-based RAW</b> (CR2, NEF, ARW, DNG, ORF, RW2, PEF and the rest that start with a TIFF
/// header): IFD0 and the Exif IFD, read in place (see <see cref="ExifTimeTags.ReadTiff"/>).</item>
/// <item><b>PNG</b>: chunk headers until IEND, skipping data by seeking; <c>eXIf</c> is read in place, and a
/// <c>tEXt</c>, <c>zTXt</c> or <c>iTXt</c> "raw profile" (ImageMagick, exiv2) is inflated and hex-decoded into the
/// block buffer: at most <see cref="MaxPngTextChunkBytes"/> of chunk, <see cref="MaxProfileTextBytes"/> of text and
/// <see cref="MaxBlockBytes"/> of Exif.</item>
/// <item><b>WebP</b>: RIFF chunk headers; each <c>EXIF</c> chunk is read in place.</item>
/// <item><b>HEIF, HEIC, AVIF</b>: the top-level <c>meta</c> box is loaded into a pooled buffer (at most
/// <see cref="MaxMetaBoxBytes"/>) and <c>iinf</c> and <c>iloc</c> parsed in a loop bounded by the box's bytes, never
/// sized by its counts; each distinct <c>Exif</c> item is read in place when it is one extent, or copied into a pooled
/// buffer when it is several (at most <see cref="MaxCopiedBytes"/> copied per read, all items together).</item>
/// <item><b>CR3</b>: <c>moov</c>, Canon's <c>uuid</c> box and its <c>CMT2</c> box (the Exif IFD as a TIFF), in place.</item>
/// <item><b>RAF</b>: the embedded JPEG the header points to, as JPEG, cut to the file when the file is cut short.</item>
/// </list>
/// <para>
/// Anything else (BMP, GIF, JPEG XR, JPEG XL, other RAW) has no capture time. At most <see cref="MaxExifBlocks"/>
/// Exif blocks are tried per file: the first <c>DateTimeOriginal</c> among them wins, else the first
/// <c>DateTimeDigitized</c>, as with MetadataExtractor. So a read makes a bounded number of stream reads whatever the
/// file's size and fields say. Its buffers (the 64 KB block buffer, a meta box, a split Exif item) come from the
/// shared array pool, at most about 2 MB live; once the pool is warm, a read allocates a few hundred bytes of objects
/// and at most six short strings.
/// </para>
/// </remarks>
internal sealed class ExifContainers : IDisposable
{
    /// <summary>The largest HEIF <c>meta</c> box loaded. Real ones are a few kilobytes (an iPhone grid of 48 tiles is about 10 KB).</summary>
    internal const int MaxMetaBoxBytes = 1024 * 1024;

    /// <summary>The most bytes copied from split (several-extent) HEIF Exif items in one read; a single extent is read in place.</summary>
    internal const int MaxCopiedBytes = 1024 * 1024;

    /// <summary>The Exif blocks (JPEG APP1s, PNG chunks, WebP chunks, HEIF items) tried per file. Real files have one.</summary>
    internal const int MaxExifBlocks = 4;

    /// <summary>The pooled buffer a JPEG Exif segment or a decoded PNG raw profile is parsed from.</summary>
    internal const int MaxBlockBytes = 64 * 1024;

    /// <summary>Fill and junk bytes skipped between JPEG segments, per file.</summary>
    internal const int MaxJpegSkippedBytes = 64 * 1024;

    /// <summary>The largest PNG text chunk looked into for a raw profile (a 64 KB Exif block is about 130 KB of hex).</summary>
    internal const int MaxPngTextChunkBytes = 256 * 1024;

    /// <summary>The most raw-profile text inflated and decoded from one chunk, so a deflate bomb stops here.</summary>
    internal const int MaxProfileTextBytes = 256 * 1024;

    // Walk limits: each step reads a few bytes, and a damaged file must not turn into millions of tiny reads.
    private const int MaxJpegSegments = 4096;
    private const int MaxPngChunks = 100_000;
    private const int MaxRiffChunks = 4096;
    private const int MaxBoxes = 4096;
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

    // PNG chunk types, the same way.
    private const uint ExifChunk = 0x65584966; // "eXIf"
    private const uint TextChunk = 0x74455874; // "tEXt"
    private const uint CompressedTextChunk = 0x7A545874; // "zTXt"
    private const uint InternationalTextChunk = 0x69545874; // "iTXt"
    private const uint EndChunk = 0x49454E44; // "IEND"

    private byte[]? _block;
    private int _blocks;
    private long _copyBudget = MaxCopiedBytes;
    private CaptureTimeReading? _original;
    private CaptureTimeReading? _digitized;

    private ExifContainers()
    {
    }

    /// <summary>States of the raw-profile text parser.</summary>
    private enum ProfileState
    {
        BeforeName,
        Name,
        BeforeLength,
        Length,
        Hex,
    }

    private static ReadOnlySpan<byte> ExifPreamble => "Exif\0\0"u8;

    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> RafSignature => "FUJIFILMCCD-RAW "u8;

    private static ReadOnlySpan<byte> ExifProfileKeyword => "Raw profile type exif"u8;

    private static ReadOnlySpan<byte> App1ProfileKeyword => "Raw profile type APP1"u8;

    // Canon's CR3 metadata box: uuid 85c0b687-820f-11e0-8111-f4ce462b6a48.
    private static ReadOnlySpan<byte> CanonUuid =>
        [0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48];

    /// <summary>True once an original time is found or <see cref="MaxExifBlocks"/> blocks were tried.</summary>
    private bool Done => _original is not null || _blocks >= MaxExifBlocks;

    /// <summary>The pooled block buffer, rented on first use and returned when the read ends.</summary>
    private byte[] Block => _block ??= ArrayPool<byte>.Shared.Rent(MaxBlockBytes);

    /// <summary>
    /// The capture time of the file: the first <c>DateTimeOriginal</c> among its first <see cref="MaxExifBlocks"/> Exif
    /// blocks, else the first <c>DateTimeDigitized</c>; null when none has a usable time.
    /// </summary>
    public static CaptureTimeReading? Read(ByteWindow file)
    {
        using var search = new ExifContainers();
        search.Walk(file);
        return search._original ?? search._digitized;
    }

    public void Dispose()
    {
        if (_block is { } block)
        {
            _block = null;
            ArrayPool<byte>.Shared.Return(block);
        }
    }

    /// <summary>The tags of an Exif block that may start with the <c>Exif\0\0</c> preamble (PNG and WebP writers differ).</summary>
    private static ExifTimeTags? ExifBlockTags(ByteWindow? block)
    {
        if (block is not null && block.Matches(0, ExifPreamble))
        {
            block = block.Slice(ExifPreamble.Length, block.Length - ExifPreamble.Length);
        }

        return block is null ? null : ExifTimeTags.ReadTiff(block);
    }

    /// <summary>
    /// The position of the next JPEG marker at or after <paramref name="position"/>: 0xFF followed by a byte that is
    /// neither 0x00 nor 0xFF, as MetadataExtractor looked for it. Fill and junk bytes skipped on the way are taken from
    /// <paramref name="skipBudget"/>; -1 when the budget or the window runs out.
    /// </summary>
    private static long NextMarker(ByteWindow jpeg, long position, ref int skipBudget)
    {
        Span<byte> chunk = stackalloc byte[256];
        while (true)
        {
            var count = jpeg.ReadAtMost(position, chunk);
            for (var i = 0; i + 1 < count; i++)
            {
                if (chunk[i] == 0xFF && chunk[i + 1] is not (0x00 or 0xFF))
                {
                    if (i > skipBudget)
                    {
                        return -1;
                    }

                    skipBudget -= i;
                    return position + i;
                }
            }

            // No marker starts in this chunk; its last byte may be the 0xFF of the next one.
            if (count < 2 || count - 1 > skipBudget)
            {
                return -1;
            }

            skipBudget -= count - 1;
            position += count - 1;
        }
    }

    private static int HexValue(byte c) => c switch
    {
        >= (byte)'0' and <= (byte)'9' => c - '0',
        >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
        _ => -1,
    };

    /// <summary>
    /// The ids of the distinct <c>Exif</c> items in <c>iinf</c>, at most as many as <paramref name="ids"/> holds (version 2
    /// and 3 <c>infe</c> entries carry the item type).
    /// </summary>
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
            if (!meta.TryRead(entry.PayloadStart + 4, infe[..(idBytes + 6)]) || !infe.Slice(idBytes + 2, 4).SequenceEqual("Exif"u8))
            {
                continue;
            }

            var id = idBytes == 2 ? BinaryPrimitives.ReadUInt16BigEndian(infe) : BinaryPrimitives.ReadUInt32BigEndian(infe);
            if (!ids[..found].Contains(id))
            {
                ids[found++] = id;
            }
        }

        return found;
    }

    /// <summary>An Exif item's tags: it starts with a 32-bit offset to the TIFF header (past an "Exif\0\0" that most writers put there).</summary>
    private static ExifTimeTags? ItemTags(ByteWindow? item)
    {
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

    private void Walk(ByteWindow file)
    {
        Span<byte> head = stackalloc byte[16];
        head = head[..(int)Math.Min(head.Length, file.Length)];
        if (!file.TryRead(0, head) || head.Length < 8)
        {
            return;
        }

        if (head[0] == 0xFF && head[1] == 0xD8)
        {
            FromJpeg(file);
        }
        else if (head.StartsWith(PngSignature))
        {
            FromPng(file);
        }
        else if (head.StartsWith("II"u8) || head.StartsWith("MM"u8))
        {
            Offer(ExifTimeTags.ReadTiff(file));
        }
        else if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            FromWebP(file);
        }
        else if (head[4..8].SequenceEqual("ftyp"u8))
        {
            if (head.Length >= 12 && head[8..12].SequenceEqual("crx "u8))
            {
                FromCr3(file);
            }
            else
            {
                FromHeif(file);
            }
        }
        else if (head.Length == 16 && head.SequenceEqual(RafSignature))
        {
            FromRaf(file);
        }
    }

    /// <summary>Counts one Exif block as tried and keeps its time: an original ends the search, the first digitized time waits for one.</summary>
    private void Offer(ExifTimeTags? tags)
    {
        _blocks++;
        if (tags is { } t && CaptureTimeReader.FromTags(t) is { } reading)
        {
            if (reading.Source == CaptureTimeSource.DateTimeOriginal)
            {
                _original = reading;
            }
            else
            {
                _digitized ??= reading;
            }
        }
    }

    private void FromJpeg(ByteWindow jpeg)
    {
        // The marker and the segment length.
        Span<byte> header = stackalloc byte[4];
        long position = 2;
        var skipBudget = MaxJpegSkippedBytes;
        for (var segment = 0; segment < MaxJpegSegments && !Done; segment++)
        {
            var got = jpeg.ReadAtMost(position, header);
            if (got >= 2 && (header[0] != 0xFF || header[1] is 0x00 or 0xFF))
            {
                position = NextMarker(jpeg, position, ref skipBudget);
                got = position < 0 ? 0 : jpeg.ReadAtMost(position, header);
            }

            if (got < 2)
            {
                return;
            }

            var type = header[1];
            if (type is 0xDA or 0xD9)
            {
                // Start of scan or end of image: metadata segments come before both.
                return;
            }

            if (type is 0x01 or 0xD8 or (>= 0xD0 and <= 0xD7))
            {
                // Markers without a length.
                position += 2;
                continue;
            }

            var length = got == 4 ? BinaryPrimitives.ReadUInt16BigEndian(header[2..]) : 0;
            if (length < 2)
            {
                return;
            }

            var payload = length - 2;
            if (type == 0xE1 && payload >= ExifPreamble.Length && jpeg.Matches(position + 4, ExifPreamble))
            {
                // One read for the whole segment; the IFDs are then walked in memory, however they are laid out.
                var tiffBytes = payload - ExifPreamble.Length;
                if (!jpeg.TryRead(position + 4 + ExifPreamble.Length, Block.AsSpan(0, tiffBytes)))
                {
                    return;
                }

                Offer(ExifTimeTags.ReadTiff(ByteWindow.Over(Block, tiffBytes)));
            }

            position += 2 + length;
        }
    }

    private void FromPng(ByteWindow png)
    {
        Span<byte> header = stackalloc byte[8];
        long position = PngSignature.Length;
        for (var step = 0; step < MaxPngChunks && !Done && png.TryRead(position, header); step++)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            if (type == ExifChunk)
            {
                Offer(ExifBlockTags(png.Slice(position + 8, length)));
            }
            else if (type is TextChunk or CompressedTextChunk or InternationalTextChunk
                && length <= MaxPngTextChunkBytes
                && png.Slice(position + 8, length) is { } text
                && RawExifProfile(text, type) is { } profile)
            {
                Offer(ExifBlockTags(profile));
            }
            else if (type == EndChunk)
            {
                return;
            }

            // Length, type, data, CRC.
            position += 12L + length;
        }
    }

    /// <summary>
    /// The Exif bytes of a PNG text chunk keyed <c>Raw profile type exif</c> or <c>Raw profile type APP1</c>, the
    /// "raw profile" ImageMagick and exiv2 write: the text is <c>\n&lt;name&gt;\n&lt;length&gt;\n</c> and then the bytes
    /// in hex, deflated in <c>zTXt</c> and in a compressed <c>iTXt</c>. Decoded into the block buffer; null for any
    /// other chunk or a damaged one.
    /// </summary>
    private ByteWindow? RawExifProfile(ByteWindow chunk, uint type)
    {
        // The keyword: 1 to 79 Latin-1 characters and a NUL.
        Span<byte> head = stackalloc byte[80];
        var got = chunk.ReadAtMost(0, head);
        var keyword = head[..got].IndexOf((byte)0);
        if (keyword < 0 || !(head[..keyword].SequenceEqual(ExifProfileKeyword) || head[..keyword].SequenceEqual(App1ProfileKeyword)))
        {
            return null;
        }

        long textStart = keyword + 1;
        var compressed = false;
        if (type == CompressedTextChunk)
        {
            // Compression method 0, zlib, is the only one defined.
            if (!chunk.TryRead(textStart, head[..1]) || head[0] != 0)
            {
                return null;
            }

            textStart++;
            compressed = true;
        }
        else if (type == InternationalTextChunk)
        {
            // Compression flag and method, then a language tag and a translated keyword, each ending in a NUL.
            got = chunk.ReadAtMost(textStart, head);
            var language = got < 2 || head[1] != 0 ? -1 : head[2..got].IndexOf((byte)0);
            var translated = language < 0 ? -1 : head[(3 + language)..got].IndexOf((byte)0);
            if (translated < 0)
            {
                return null;
            }

            compressed = head[0] == 1;
            textStart += 2 + language + 1 + translated + 1;
        }

        if (chunk.Slice(textStart, chunk.Length - textStart) is not { } text)
        {
            return null;
        }

        try
        {
            using var source = text.AsStream();
            if (!compressed)
            {
                return DecodeRawProfile(source);
            }

            using var inflated = new ZLibStream(source, CompressionMode.Decompress);
            return DecodeRawProfile(inflated);
        }
        catch (InvalidDataException)
        {
            // Damaged deflate data: no Exif from this chunk.
            return null;
        }
    }

    /// <summary>
    /// Decodes raw-profile text (<c>\n&lt;name&gt;\n&lt;length&gt;\n</c>, then hex with whitespace anywhere) into the block
    /// buffer: at most <see cref="MaxProfileTextBytes"/> of text is read and at most <see cref="MaxBlockBytes"/> decoded.
    /// </summary>
    private ByteWindow? DecodeRawProfile(Stream text)
    {
        var output = Block;
        Span<byte> chunk = stackalloc byte[1024];
        var state = ProfileState.BeforeName;
        long declared = 0;
        int lengthDigits = 0, written = 0, limit = 0, high = -1, read = 0;
        while (read < MaxProfileTextBytes && !(state == ProfileState.Hex && written == limit))
        {
            var count = text.Read(chunk[..Math.Min(chunk.Length, MaxProfileTextBytes - read)]);
            if (count == 0)
            {
                break;
            }

            read += count;
            foreach (var c in chunk[..count])
            {
                var space = c is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t';
                if (state == ProfileState.BeforeName)
                {
                    state = space ? state : ProfileState.Name;
                }
                else if (state == ProfileState.Name)
                {
                    state = c == '\n' ? ProfileState.BeforeLength : state;
                }
                else if (state is ProfileState.BeforeLength or ProfileState.Length)
                {
                    if (c is >= (byte)'0' and <= (byte)'9' && lengthDigits < 9)
                    {
                        declared = (declared * 10) + (c - '0');
                        lengthDigits++;
                        state = ProfileState.Length;
                    }
                    else if (space && state == ProfileState.Length)
                    {
                        limit = (int)Math.Min(declared, MaxBlockBytes);
                        state = ProfileState.Hex;
                    }
                    else if (!space)
                    {
                        return null;
                    }
                }
                else if (!space)
                {
                    var nibble = HexValue(c);
                    if (nibble < 0 || written == limit)
                    {
                        // The end of the hex, or of what is decoded.
                        return written == 0 ? null : ByteWindow.Over(output, written);
                    }

                    if (high < 0)
                    {
                        high = nibble;
                    }
                    else
                    {
                        output[written++] = (byte)((high << 4) | nibble);
                        high = -1;
                    }
                }
            }
        }

        return written == 0 ? null : ByteWindow.Over(output, written);
    }

    private void FromWebP(ByteWindow webp)
    {
        if (!webp.TryReadUInt32(4, bigEndian: false, out var riffSize))
        {
            return;
        }

        var end = Math.Min(webp.Length, 8L + riffSize);
        Span<byte> header = stackalloc byte[8];
        long position = 12;
        for (var step = 0; step < MaxRiffChunks && !Done && position + 8 <= end && webp.TryRead(position, header); step++)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (header[..4].SequenceEqual("EXIF"u8))
            {
                Offer(ExifBlockTags(webp.Slice(position + 8, size)));
            }

            // Chunks are padded to an even size.
            position += 8L + size + (size & 1);
        }
    }

    private void FromRaf(ByteWindow raf)
    {
        // The header holds the embedded JPEG's offset and length (big-endian) at 84 and 88; the JPEG carries the Exif.
        // A file cut short (an interrupted card copy) keeps what it has of the JPEG, whose Exif segment comes first.
        if (!raf.TryReadUInt32(84, bigEndian: true, out var offset) || !raf.TryReadUInt32(88, bigEndian: true, out var length)
            || offset >= raf.Length)
        {
            return;
        }

        if (raf.Slice(offset, Math.Min(length, raf.Length - offset)) is { } jpeg && jpeg.Matches(0, [0xFF, 0xD8]))
        {
            FromJpeg(jpeg);
        }
    }

    private void FromCr3(ByteWindow file)
    {
        var moov = FindBox(file, 0, file.Length, MoovBox);
        var canon = moov is { } m ? FindBox(file, m.PayloadStart, m.End, UuidBox, CanonUuid) : null;
        if (canon is { } c && FindBox(file, c.PayloadStart, c.End, Cmt2Box) is { } exif)
        {
            Offer(ExifTimeTags.ReadTiff(file.Slice(exif.PayloadStart, exif.End - exif.PayloadStart)!, firstIfdIsExif: true));
        }
    }

    private void FromHeif(ByteWindow file)
    {
        if (FindBox(file, 0, file.Length, MetaBox) is not { } metaBox || metaBox.End - metaBox.PayloadStart is > MaxMetaBoxBytes or < 4)
        {
            return;
        }

        var length = (int)(metaBox.End - metaBox.PayloadStart);
        var meta = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            if (!file.TryRead(metaBox.PayloadStart, meta.AsSpan(0, length)))
            {
                return;
            }

            // meta is a full box: version and flags, then its children.
            var children = ByteWindow.Over(meta, length);
            Span<uint> exifItems = stackalloc uint[MaxExifBlocks];
            var exifItemCount = FindExifItems(children, FindBox(children, 4, length, IinfBox), exifItems);
            if (exifItemCount == 0 || FindBox(children, 4, length, IlocBox) is not { } iloc)
            {
                return;
            }

            var idat = FindBox(children, 4, length, IdatBox);
            var ilocBytes = meta.AsSpan((int)iloc.PayloadStart, (int)(iloc.End - iloc.PayloadStart));
            foreach (var item in exifItems[..exifItemCount])
            {
                if (Done)
                {
                    return;
                }

                Offer(ReadExifItem(file, children, ilocBytes, idat, item));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(meta);
        }
    }

    /// <summary>
    /// The time tags of one Exif item, located through <c>iloc</c>. The loop over items and extents stops at the end of
    /// the box: a count larger than the bytes behind it is a damaged box, never an allocation.
    /// </summary>
    private ExifTimeTags? ReadExifItem(ByteWindow file, ByteWindow meta, ReadOnlySpan<byte> iloc, Box? idat, uint itemId)
    {
        var reader = new SpanReader(iloc);
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
                // Cannot fail: the extents were checked against Remaining above.
                _ = reader.Skip((int)extentCount * extentBytes);
                continue;
            }

            // Construction method 0 is an offset in the file, 1 an offset in idat; 2 (another item's data) is not used for Exif.
            if ((method & 0xF) > 1 || extentCount is 0 or > MaxExtents)
            {
                return null;
            }

            var source = (method & 0xF) == 0 ? file : idat is { } d ? meta.Slice(d.PayloadStart, d.End - d.PayloadStart) : null;
            return source is null ? null : ReadExtents(source, ref reader, (int)extentCount, indexSize, offsetSize, lengthSize, baseOffset);
        }

        return null;
    }

    private ExifTimeTags? ReadExtents(
        ByteWindow source, ref SpanReader reader, int extentCount, int indexSize, int offsetSize, int lengthSize, ulong baseOffset)
    {
        Span<(ulong Offset, ulong Length)> extents = stackalloc (ulong, ulong)[extentCount];
        ulong total = 0;
        for (var i = 0; i < extentCount; i++)
        {
            if (!reader.Skip(indexSize) || !reader.TryRead(offsetSize, out var offset) || !reader.TryRead(lengthSize, out var length)
                || length == 0 || offset > long.MaxValue / 2 || baseOffset > long.MaxValue / 2 || length > MaxCopiedBytes)
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

        if (extentCount == 1)
        {
            return ItemTags(source.Slice((long)extents[0].Offset, (long)extents[0].Length));
        }

        // Split items share one copy budget per read, so several items (or one named several times) cannot multiply it.
        if (total > (ulong)_copyBudget)
        {
            return null;
        }

        _copyBudget -= (long)total;
        var bytes = ArrayPool<byte>.Shared.Rent((int)total);
        try
        {
            var at = 0;
            foreach (var (offset, length) in extents)
            {
                if (!source.TryRead((long)offset, bytes.AsSpan(at, (int)length)))
                {
                    return null;
                }

                at += (int)length;
            }

            return ItemTags(ByteWindow.Over(bytes, at));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
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
