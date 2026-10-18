using System.Buffers;

namespace TenLapse.Media.Decoding;

/// <summary>
/// Whether a JPEG file holds its whole image. WIC's JPEG decoder decodes a file cut short without an error (it
/// fills the missing rows with gray and only prints "premature end of data segment"), so a truncated photo would
/// pass for a whole one; this finds the end-of-image marker instead.
/// </summary>
internal static class JpegStructure
{
    private const int BufferSize = 256 * 1024;

    /// <summary>
    /// True when the file's image data reaches its end-of-image marker (EOI). A file that ends with EOI answers at
    /// once; one that does not (cut short, or with data after the image such as a motion photo's video) is
    /// walked marker by marker through its compressed data, which reads it whole. A file that does not start like
    /// a JPEG answers true: it is not this check's to judge.
    /// </summary>
    public static bool EndsCompletely(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.SequentialScan);
        if (file.Length < 4)
        {
            return false;
        }

        Span<byte> tail = stackalloc byte[2];
        file.Seek(-2, SeekOrigin.End);
        file.ReadExactly(tail);
        if (tail[0] == 0xFF && tail[1] == 0xD9)
        {
            return true;
        }

        file.Seek(0, SeekOrigin.Begin);
        var reader = new Reader(file);
        try
        {
            return reader.Walk();
        }
        finally
        {
            reader.Dispose();
        }
    }

    // A forward-only reader over the file in large chunks, so the scan of compressed data is a vectorised search.
    private sealed class Reader(Stream stream) : IDisposable
    {
        private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        private int _position;
        private int _length;

        public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);

        public bool Walk()
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

        private bool Skip(int count)
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

                var step = Math.Min(count, _length - _position);
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

            var read = stream.Read(_buffer, keep, BufferSize - keep);
            if (read <= 0)
            {
                return false;
            }

            _length = keep + read;
            _position = keep;
            return true;
        }
    }
}
