using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace TenLapse.Media.Export;

/// <summary>
/// A write-only COM <see cref="IStream"/> over a .NET <see cref="Stream"/>, for the WIC GIF encoder (#51). The encoder
/// writes the GIF through it instead of a file WIC opens itself, so a failed write keeps its own exception
/// (<see cref="Failure"/>): a full disk stays ERROR_DISK_FULL whatever HRESULT WIC turns it into, and the export shows
/// the disk-full message, not "the encoder stopped". It is also the seam where a test fills the disk.
/// </summary>
internal sealed unsafe class ComStreamOutput(Stream stream) : IStream
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <summary>The first write, seek or size change that failed, as the stream threw it; null while none did.</summary>
    public Exception? Failure { get; private set; }

    public HRESULT Read(void* pv, uint cb, uint* pcbRead)
    {
        if (pcbRead is not null)
        {
            *pcbRead = 0;
        }

        // E_NOTIMPL: the encoder only writes.
        return (HRESULT)unchecked((int)0x80004001);
    }

    public HRESULT Write(void* pv, uint cb, uint* pcbWritten)
    {
        if (pcbWritten is not null)
        {
            *pcbWritten = 0;
        }

        try
        {
            _stream.Write(new ReadOnlySpan<byte>(pv, checked((int)cb)));
            if (pcbWritten is not null)
            {
                *pcbWritten = cb;
            }

            return HRESULT.S_OK;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException)
        {
            return Fail(exception);
        }
    }

    public void Seek(long dlibMove, SeekOrigin dwOrigin, ulong* plibNewPosition)
    {
        try
        {
            var position = _stream.Seek(dlibMove, dwOrigin);
            if (plibNewPosition is not null)
            {
                *plibNewPosition = (ulong)position;
            }
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            Marshal.ThrowExceptionForHR(Fail(exception));
        }
    }

    public void SetSize(ulong libNewSize)
    {
        try
        {
            _stream.SetLength(checked((long)libNewSize));
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            Marshal.ThrowExceptionForHR(Fail(exception));
        }
    }

    public void CopyTo(IStream pstm, ulong cb, ulong* pcbRead, ulong* pcbWritten) => throw new NotImplementedException();

    public void Commit(STGC grfCommitFlags)
    {
        try
        {
            _stream.Flush();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            Marshal.ThrowExceptionForHR(Fail(exception));
        }
    }

    public void Revert() => throw new NotImplementedException();

    public void LockRegion(ulong libOffset, ulong cb, LOCKTYPE dwLockType) => throw new NotImplementedException();

    public void UnlockRegion(ulong libOffset, ulong cb, uint dwLockType) => throw new NotImplementedException();

    public void Stat(STATSTG* pstatstg, STATFLAG grfStatFlag) => throw new NotImplementedException();

    public void Clone(out IStream ppstm) => throw new NotImplementedException();

    // The stream's own HRESULT when it has one (ERROR_DISK_FULL is 0x80070070), else STG_E_WRITEFAULT.
    private HRESULT Fail(Exception exception)
    {
        Failure ??= exception;
        return exception.HResult < 0 ? (HRESULT)exception.HResult : (HRESULT)unchecked((int)0x8003001D);
    }
}
