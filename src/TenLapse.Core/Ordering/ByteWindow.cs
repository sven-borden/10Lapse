using System.Buffers.Binary;

namespace TenLapse.Core.Ordering;

/// <summary>
/// A bounded, read-only view of part of a seekable stream or a byte array, read at offsets relative to its start.
/// Every read checks the bounds first, so nothing is read or allocated on the strength of a length field alone: a
/// count or size that points past the window is simply a failed read.
/// </summary>
internal sealed class ByteWindow
{
    private readonly Stream? _stream;
    private readonly byte[]? _bytes;
    private readonly long _start;

    private ByteWindow(Stream? stream, byte[]? bytes, long start, long length)
    {
        _stream = stream;
        _bytes = bytes;
        _start = start;
        Length = length;
    }

    /// <summary>The number of bytes in the window.</summary>
    public long Length { get; }

    /// <summary>The stream from its current position to its end. The stream must be seekable; it is not disposed.</summary>
    public static ByteWindow Over(Stream stream) => new(stream, null, stream.Position, Math.Max(0, stream.Length - stream.Position));

    /// <summary>The first <paramref name="length"/> bytes of the array (a pooled buffer is often longer than its content).</summary>
    public static ByteWindow Over(byte[] bytes, int length)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)length, (uint)bytes.Length, nameof(length));
        return new(null, bytes, 0, length);
    }

    /// <summary>True when <paramref name="count"/> bytes from <paramref name="offset"/> lie inside the window.</summary>
    public bool Contains(long offset, long count) => offset >= 0 && count >= 0 && offset <= Length && count <= Length - offset;

    /// <summary>The part from <paramref name="offset"/> on, <paramref name="length"/> bytes long; null when it does not fit.</summary>
    public ByteWindow? Slice(long offset, long length) =>
        Contains(offset, length) ? new ByteWindow(_stream, _bytes, _start + offset, length) : null;

    /// <summary>Fills <paramref name="destination"/> from <paramref name="offset"/>; false when the bytes are outside the window or missing.</summary>
    public bool TryRead(long offset, Span<byte> destination) =>
        Contains(offset, destination.Length) && ReadAtMost(offset, destination) == destination.Length;

    /// <summary>
    /// Reads from <paramref name="offset"/> into <paramref name="destination"/>, stopping at the end of the window: the
    /// number of bytes read, 0 when <paramref name="offset"/> is at or past the end.
    /// </summary>
    public int ReadAtMost(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset >= Length)
        {
            return 0;
        }

        var count = (int)Math.Min(destination.Length, Length - offset);
        if (_bytes is not null)
        {
            _bytes.AsSpan((int)(_start + offset), count).CopyTo(destination);
            return count;
        }

        _stream!.Position = _start + offset;
        return _stream.ReadAtLeast(destination[..count], count, throwOnEndOfStream: false);
    }

    public bool TryReadUInt16(long offset, bool bigEndian, out ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        var read = TryRead(offset, bytes);
        value = !read ? (ushort)0 : bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        return read;
    }

    public bool TryReadUInt32(long offset, bool bigEndian, out uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        var read = TryRead(offset, bytes);
        value = !read ? 0 : bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return read;
    }

    /// <summary>True when the window holds <paramref name="expected"/> at <paramref name="offset"/>.</summary>
    public bool Matches(long offset, ReadOnlySpan<byte> expected)
    {
        Span<byte> bytes = stackalloc byte[expected.Length];
        return TryRead(offset, bytes) && bytes.SequenceEqual(expected);
    }

    /// <summary>The window as a read-only stream from its start (for a decompressor); the underlying stream is not disposed.</summary>
    public Stream AsStream() => new WindowStream(this);

    private sealed class WindowStream(ByteWindow window) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = window.ReadAtMost(_position, buffer);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
