using System.Buffers;
using System.Buffers.Binary;

namespace TenLapse.Media.Decoding;

/// <summary>Reads bytes of a file at <paramref name="offset"/> into <paramref name="buffer"/>; returns how many (0 past the end).</summary>
internal delegate int ReadAt(long offset, Span<byte> buffer);

/// <summary>
/// Whether a photo file holds its whole image. WIC decodes a JPEG, PNG or GIF cut short without an error (the
/// JPEG decoder fills the missing rows with gray and only prints "premature end of data segment"; PNG and GIF end
/// in a flat band), so a truncated photo would pass for a whole one; this finds the format's end marker instead.
/// </summary>
/// <remarks>
/// It reads through a <see cref="ReadAt"/> bounded by a length captured once: the bytes the decoder gets (the
/// in-memory copy) or the handle the header was read through, never the file again by its path.
/// </remarks>
internal static class PhotoStructure
{
    /// <summary>How many bytes from the start of the file the checks look at to tell the format.</summary>
    public const int HeaderLength = 64;

    private const int BufferSize = 256 * 1024;

    // The last chunk of every PNG: length 0, "IEND", its CRC.
    private static ReadOnlySpan<byte> PngEnd => [0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82];

    /// <summary>
    /// True when the file reaches its format's end: a JPEG its end-of-image marker (EOI), a PNG its IEND chunk, a
    /// GIF its trailer (0x3B). WIC decodes a JPEG, PNG or GIF cut short without an error, filling what is missing
    /// with a flat colour. A format this does not know answers true: it is not this check's to judge.
    /// </summary>
    /// <param name="header">The first bytes of the file (up to <see cref="HeaderLength"/>).</param>
    /// <param name="length">The file's length, as captured when it was opened.</param>
    /// <param name="read">Reads the file at an offset.</param>
    /// <remarks>
    /// The common case reads the last few bytes. A file with data after its image (a motion photo's video after
    /// the JPEG, a tool's trailer after a PNG) is walked through its structure; a GIF always is, as its last byte
    /// alone does not tell a trailer from data.
    /// </remarks>
    public static bool EndsCompletely(ReadOnlySpan<byte> header, long length, ReadAt read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (IsJpeg(header))
        {
            return JpegEndsCompletely(length, read);
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return PngEndsCompletely(length, read);
        }

        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8))
        {
            using var reader = new Reader(read, length);
            return reader.WalkGif();
        }

        return true;
    }

    /// <summary>A reader over bytes already in memory.</summary>
    public static ReadAt Over(byte[] bytes, int length) => (offset, buffer) =>
    {
        if (offset >= length)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, length - offset);
        bytes.AsSpan((int)offset, count).CopyTo(buffer);
        return count;
    };

    private static bool IsJpeg(ReadOnlySpan<byte> header) => header is [0xFF, 0xD8, 0xFF, ..];

    /// <remarks>
    /// A file that ends with EOI answers at once; one that does not (cut short, or with data after the image such
    /// as a motion photo's video) is walked marker by marker through its compressed data, which reads it whole.
    /// A JPEG whose data is complete but whose EOI alone is missing counts as cut: telling the two apart would
    /// take decoding the entropy-coded data, and such files are rare (a writer that stopped one marker short).
    /// </remarks>
    private static bool JpegEndsCompletely(long length, ReadAt read)
    {
        if (length < 4)
        {
            return false;
        }

        Span<byte> tail = stackalloc byte[2];
        if (ReadFully(read, length - 2, tail) && tail[0] == 0xFF && tail[1] == 0xD9)
        {
            return true;
        }

        using var reader = new Reader(read, length);
        return reader.WalkJpeg();
    }

    // Ends with the IEND chunk, or reaches it walking the chunks (8-byte header, data, 4-byte CRC each).
    private static bool PngEndsCompletely(long length, ReadAt read)
    {
        Span<byte> chunk = stackalloc byte[12];
        if (length >= 8 + 12 && ReadFully(read, length - 12, chunk) && chunk.SequenceEqual(PngEnd))
        {
            return true;
        }

        var head = chunk[..8];
        long offset = 8;
        while (offset + 8 <= length)
        {
            if (!ReadFully(read, offset, head))
            {
                return false;
            }

            if (head[4..].SequenceEqual("IEND"u8))
            {
                return true;
            }

            offset += 12L + BinaryPrimitives.ReadUInt32BigEndian(head);
        }

        return false;
    }

    private static bool ReadFully(ReadAt read, long offset, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var count = read(offset, buffer);
            if (count <= 0)
            {
                return false;
            }

            offset += count;
            buffer = buffer[count..];
        }

        return true;
    }

    // A forward-only reader in large chunks, so the scan of compressed data is a vectorised search.
    private sealed class Reader(ReadAt read, long length) : IDisposable
    {
        private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        private long _fileOffset;
        private int _position;
        private int _length;

        public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);

        public bool WalkJpeg()
        {
            if (Next() != 0xFF || Next() != 0xD8)
            {
                return true;
            }

            while (true)
            {
                // Markers start with 0xFF, possibly repeated as fill.
                var value = Next();
                if (value != 0xFF)
                {
                    // The end of the file is a cut; anything else is a structure WIC made sense of anyway.
                    return value >= 0;
                }

                int marker;
                do
                {
                    marker = Next();
                }
                while (marker == 0xFF);

                switch (marker)
                {
                    case < 0:
                        return false;
                    case 0xD9:
                        return true;
                    case 0x01 or (>= 0xD0 and <= 0xD8):
                        continue;
                }

                var high = Next();
                var low = Next();
                if (low < 0 || !Skip(((high << 8) | low) - 2))
                {
                    return false;
                }

                if (marker == 0xDA && !SkipEntropyCodedData())
                {
                    return false;
                }
            }
        }

        // Header, logical screen descriptor and colour table, then blocks up to the trailer: extensions and
        // images, each ending with a run of sub-blocks (a length byte, then that many bytes; 0 ends the run).
        public bool WalkGif()
        {
            if (!Skip(10))
            {
                return false;
            }

            var flags = Next();
            if (flags < 0 || !Skip(2) || !SkipColourTable(flags))
            {
                return false;
            }

            while (true)
            {
                switch (Next())
                {
                    case 0x3B:
                        return true;
                    case 0x21:
                        if (Next() < 0 || !SkipSubBlocks())
                        {
                            return false;
                        }

                        break;
                    case 0x2C:
                        if (!Skip(8))
                        {
                            return false;
                        }

                        var local = Next();
                        if (local < 0 || !SkipColourTable(local) || Next() < 0 || !SkipSubBlocks())
                        {
                            return false;
                        }

                        break;
                    case < 0:
                        return false;
                    default:
                        // Not a block: a structure WIC made sense of anyway, as for a JPEG.
                        return true;
                }
            }
        }

        private bool SkipColourTable(int flags) => (flags & 0x80) == 0 || Skip(3 << ((flags & 0x07) + 1));

        private bool SkipSubBlocks()
        {
            while (true)
            {
                var size = Next();
                if (size <= 0)
                {
                    return size == 0;
                }

                if (!Skip(size))
                {
                    return false;
                }
            }
        }

        // After a scan header: compressed data up to the next marker that is neither a stuffed 0xFF00 nor a
        // restart marker. Leaves the position on that marker's 0xFF.
        private bool SkipEntropyCodedData()
        {
            while (true)
            {
                if (_position >= _length && !Fill())
                {
                    return false;
                }

                var found = _buffer.AsSpan(_position, _length - _position).IndexOf((byte)0xFF);
                if (found < 0)
                {
                    _position = _length;
                    continue;
                }

                _position += found + 1;
                var following = Next();
                if (following < 0)
                {
                    return false;
                }

                if (following != 0x00 && following is not (>= 0xD0 and <= 0xD7))
                {
                    // Step back onto the 0xFF so the marker loop reads it.
                    _position -= 2;
                    return true;
                }
            }
        }

        private int Next()
        {
            if (_position >= _length && !Fill())
            {
                return -1;
            }

            return _buffer[_position++];
        }

        private bool Skip(long count)
        {
            if (count < 0)
            {
                return false;
            }

            while (count > 0)
            {
                if (_position >= _length && !Fill())
                {
                    return false;
                }

                var step = (int)Math.Min(count, _length - _position);
                _position += step;
                count -= step;
            }

            return true;
        }

        // Keeps the last byte read, so stepping back across a chunk boundary still works.
        private bool Fill()
        {
            var keep = _length > 0 ? 1 : 0;
            if (keep == 1)
            {
                _buffer[0] = _buffer[_length - 1];
            }

            var wanted = (int)Math.Min(BufferSize - keep, length - _fileOffset);
            var count = wanted > 0 ? read(_fileOffset, _buffer.AsSpan(keep, wanted)) : 0;
            if (count <= 0)
            {
                return false;
            }

            _fileOffset += count;
            _length = keep + count;
            _position = keep;
            return true;
        }
    }
}
