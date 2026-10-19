namespace TenLapse.Tests.Ordering;

/// <summary>
/// A read-only stream over another that counts what a reader asks of it: <see cref="Reads"/> calls, the bytes they
/// returned and <see cref="Seeks"/> (position changes). The counts are deterministic, unlike a timing.
/// </summary>
internal sealed class CountingStream(Stream inner) : Stream
{
    public long BytesRead { get; private set; }

    public int Reads { get; private set; }

    public int Seeks { get; private set; }

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set
        {
            Seeks++;
            inner.Position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        Reads++;
        var read = inner.Read(buffer);
        BytesRead += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Seeks++;
        return inner.Seek(offset, origin);
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
