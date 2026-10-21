using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;

namespace TenLapse.Tests.Media;

/// <summary>A disk that fills up while a GIF is written (#51), simulated at the stream the WIC GIF encoder writes to.</summary>
public sealed class GifDiskFullTests : IDisposable
{
    private static readonly VideoExportSettings _settings = new(480, 270, new Fraction(10, 1)) { Format = VideoFormat.Gif };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "10lapse-gif-full-tests", Guid.NewGuid().ToString("N"));

    public GifDiskFullTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by a failed run: the temp folder's own clean-up has it.
        }
    }

    [Theory]
    [InlineData(30_000)] // inside the first frame
    [InlineData(150_000)] // a few frames in
    public void AFullDiskFailsTheWriteWithTheDisksOwnError(long capacity)
    {
        var path = Path.Combine(_folder, $"full-{capacity}.gif");
        var encoder = new GifEncoder(path, _settings, open: file => new FullDiskStream(file, capacity));
        try
        {
            var sunset = GifExportTests.Sunset(_settings.Width, _settings.Height);
            var failure = Assert.Throws<IOException>(() =>
            {
                for (var i = 0; i < 20; i++)
                {
                    encoder.WriteFrame(sunset, 10);
                }

                encoder.Finish();
            });

            // ERROR_DISK_FULL, which the export reads as a full disk.
            Assert.Equal(FullDiskStream.DiskFull, failure.HResult);
            Assert.Equal(VideoExportFailure.DiskFull, ExportFailures.FromHResult(failure.HResult));
        }
        finally
        {
            encoder.Dispose();
        }
    }

    [Fact]
    public void TheGifsLastByteThatDoesNotFitFailsFinish()
    {
        // Everything fits but the one-byte trailer, written as the encoder commits. WIC ignores that failed write and
        // reports success (measured: it goes on to patch the header), so without the stream's own record the export
        // would save a GIF with no end and say nothing.
        var size = WriteOneFrame(Path.Combine(_folder, "whole.gif"), open: null);
        var path = Path.Combine(_folder, "trailer.gif");
        var encoder = new GifEncoder(path, _settings, open: file => new FullDiskStream(file, size - 1));
        try
        {
            encoder.WriteFrame(GifExportTests.Sunset(_settings.Width, _settings.Height), 10);
            var failure = Assert.Throws<IOException>(encoder.Finish);
            Assert.Equal(FullDiskStream.DiskFull, failure.HResult);
        }
        finally
        {
            encoder.Dispose();
        }
    }

    private static long WriteOneFrame(string path, Func<string, Stream>? open)
    {
        var encoder = new GifEncoder(path, _settings, open: open);
        try
        {
            encoder.WriteFrame(GifExportTests.Sunset(_settings.Width, _settings.Height), 10);
            encoder.Finish();
        }
        finally
        {
            encoder.Dispose();
        }

        return new FileInfo(path).Length;
    }
}

/// <summary>A file that takes <c>capacity</c> bytes, then fails every write as a full disk does (ERROR_DISK_FULL).</summary>
internal sealed class FullDiskStream(string path, long capacity) : Stream
{
    /// <summary>HRESULT_FROM_WIN32(ERROR_DISK_FULL).</summary>
    public const int DiskFull = unchecked((int)0x80070070);

    private readonly FileStream _file = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);

    public override bool CanRead => false;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length => _file.Length;

    public override long Position
    {
        get => _file.Position;
        set => _file.Position = value;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_file.Position + buffer.Length > capacity)
        {
            throw new IOException("There is not enough space on the disk.", DiskFull);
        }

        _file.Write(buffer);
    }

    public override void Flush() => _file.Flush();

    public override long Seek(long offset, SeekOrigin origin) => _file.Seek(offset, origin);

    public override void SetLength(long value) => _file.SetLength(value);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _file.Dispose();
        }

        base.Dispose(disposing);
    }
}
