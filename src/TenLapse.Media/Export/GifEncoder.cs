using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Timing;
using TenLapse.Media.Inspection;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace TenLapse.Media.Export;

/// <summary>
/// The encode stage for an animated GIF (#51), on the WIC GIF encoder every Windows has: each drawn frame is read back
/// from the GPU, given its own 256-colour palette made from its pixels, dithered to that palette by error diffusion and
/// written with its delay. The file loops forever (the NETSCAPE2.0 application extension).
/// </summary>
/// <remarks>
/// <para>
/// <b>Delays.</b> Frame n's delay is <see cref="GifTiming.Delay(long, long)"/> of its start and end as the pipeline
/// hands them (<see cref="FrameTiming.FrameStart"/> of n and n + 1), so each start is rounded from its exact time and the
/// GIF's length never drifts.
/// </para>
/// <para>
/// <b>Repeated frames.</b> A hold (#49), or any run of frames that come out the same, is written as one frame lasting the
/// whole run: each frame waits until the next one arrives, and a next frame whose pixels are byte for byte the same only
/// moves the waiting frame's end. Its delay is still the rounded end minus the rounded start, so the summed delays equal
/// the difference of the rounded starts and the timing stays exact; a 3 s hold at 10 fps is one frame, not 30. The
/// rendered pixels are compared, not the photos they show, so a later per-frame effect cannot be merged away. The
/// comparison is a vectorised byte compare that stops at the first difference: the waiting frame's pixels are kept
/// anyway, so a hash would only add a full pass over every frame. A run longer than <see cref="GifTiming.MaxDelay"/>
/// (655 s) is split.
/// </para>
/// <para>
/// <b>Palette and dithering.</b> A palette per frame (<c>IWICPalette.InitializeFromBitmap</c>, 256 colours, no
/// transparent one) follows the light as a timelapse goes from day to dusk; one palette for the whole clip would have
/// to share 256 colours between noon and night. Error diffusion (<see cref="WICBitmapDitherType.WICBitmapDitherTypeErrorDiffusion"/>)
/// spreads each pixel's rounding error to its neighbours, so a sunset sky shows a smooth gradient instead of the bands
/// a nearest-colour mapping draws. WIC's ordered dithers take only its fixed palettes, and on the halftone palette a
/// sunset comes out far coarser (measured by <c>GifExportTests.ErrorDiffusionDrawsTheSmoothestSunset</c>). The cost is
/// a somewhat larger file and a fine grain that changes from frame to frame.
/// </para>
/// <para>
/// Created on a thread pool (MTA) thread and then called from one thread at a time, like every encoder; the WIC objects
/// are free-threaded. Creating it creates (or truncates) the file.
/// </para>
/// <para>
/// <b>The file.</b> WIC writes through <see cref="ComStreamOutput"/> over a .NET <see cref="FileStream"/>, not a file it
/// opens itself, so a failed write is thrown as the file's own exception: a full disk is ERROR_DISK_FULL and the export
/// says the disk is full. WIC passes a failed frame write's HRESULT through but ignores a failed trailer write at
/// Commit (both measured in <c>GifDiskFullTests</c>), so the stream's record is checked after every call. The CPU
/// encodes, so no failure here is the graphics driver's.
/// </para>
/// </remarks>
internal sealed class GifEncoder : IVideoEncoder
{
    /// <summary>What the export reports as the encoder that ran.</summary>
    public static readonly VideoEncoderInfo Info = new("Windows Imaging Component GIF encoder", IsHardware: false);

    // GIF's NETSCAPE2.0 application extension: the identifier, and its data sub-block: 3 bytes follow, sub-block 1 (the
    // loop), the loop count as 16 bits little-endian (0: forever), then the terminator.
    private static readonly byte[] _netscape = "NETSCAPE2.0"u8.ToArray();
    private static readonly byte[] _loopForever = [3, 1, 0, 0, 0];

    // The graphic control extension's disposal method 1: leave the frame in place (every frame covers the whole image).
    private const byte DisposalKeep = 1;

    private readonly IWICImagingFactory _factory;
    private readonly Stream? _file;
    private readonly ComStreamOutput? _output;
    private readonly IWICBitmapEncoder _encoder;
    private readonly string _path;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _pixels;

    // The frame that waits for the next one (a run of identical frames grows here), and its start and end in ticks.
    private readonly byte[] _pending;
    private bool _hasPending;
    private long _pendingStart;
    private long _pendingEnd;
    private bool _disposed;

    public GifEncoder(string path, VideoExportSettings settings, VideoDevice device)
        : this(path, settings)
    {
        ArgumentNullException.ThrowIfNull(device);
    }

    /// <summary>Opens the GIF; the device is not needed (the frames are read back on the CPU).</summary>
    /// <param name="open">
    /// Opens the stream the GIF is written to, for a test that fills the disk; by default the file at
    /// <paramref name="path"/>, created or truncated (CREATE_ALWAYS).
    /// </param>
    internal GifEncoder(
        string path,
        VideoExportSettings settings,
        WICBitmapDitherType dither = WICBitmapDitherType.WICBitmapDitherTypeErrorDiffusion,
        Func<string, Stream>? open = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Width > ushort.MaxValue || settings.Height > ushort.MaxValue)
        {
            throw new ArgumentException($"A GIF is at most {ushort.MaxValue} pixels on a side; got {settings.Width}x{settings.Height}.", nameof(settings));
        }

        _path = path;
        _width = settings.Width;
        _height = settings.Height;
        Dither = dither;
        _pixels = new byte[checked(_width * _height * 4)];
        _pending = new byte[_pixels.Length];
        var opened = false;
        try
        {
            _factory = Wic.CreateFactory();
            _file = open is null ? new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16) : open(path);
            opened = true;
            _output = new ComStreamOutput(_file);
            unsafe
            {
                var container = PInvoke.GUID_ContainerFormatGif;
                _encoder = _factory.CreateEncoder(&container, null);
            }

            _encoder.Initialize(_output, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache);
            IWICMetadataQueryWriter? metadata = null;
            try
            {
                _encoder.GetMetadataQueryWriter(out metadata);
                Wic.SetBytes(metadata, "/appext/application", _netscape);
                Wic.SetBytes(metadata, "/appext/data", _loopForever);
                Wic.SetUInt16(metadata, "/logscrdesc/Width", (ushort)_width);
                Wic.SetUInt16(metadata, "/logscrdesc/Height", (ushort)_height);
            }
            finally
            {
                Wic.Release(metadata);
            }
        }
        catch
        {
            Dispose();
            if (opened)
            {
                TryDelete(path);
            }

            throw;
        }
    }

    /// <summary>The dither the frames are mapped to their palettes with: error diffusion in the product.</summary>
    internal WICBitmapDitherType Dither { get; }

    /// <inheritdoc />
    public VideoEncoderInfo? Encoder => Info;

    /// <inheritdoc />
    public void Write(CanvasRenderTarget target, long time, long duration)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Win2D's B8G8R8A8 premultiplied, read back from the GPU. The frames are opaque (FrameRenderer fills the frame), so
        // the bytes are plain BGR with alpha 255.
        target.GetPixelBytes(_pixels.AsBuffer());
        Accept(_pixels, time, time + duration);
    }

    /// <summary>
    /// Takes one frame of top-down BGRX pixels lasting from <paramref name="start"/> to <paramref name="end"/> (ticks): a
    /// frame identical to the waiting one extends it; any other writes the waiting one and waits in its place. The last
    /// one is written by <see cref="Finish"/>.
    /// </summary>
    internal void Accept(byte[] pixels, long start, long end)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, _pending.Length, nameof(pixels));
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hasPending
            && start == _pendingEnd
            && GifTiming.Centiseconds(end) - GifTiming.Centiseconds(_pendingStart) <= GifTiming.MaxDelay
            && pixels.AsSpan().SequenceEqual(_pending))
        {
            _pendingEnd = end;
            return;
        }

        WritePending();
        pixels.AsSpan().CopyTo(_pending);
        _pendingStart = start;
        _pendingEnd = end;
        _hasPending = true;
    }

    // The waiting frame, with the delay of its whole run.
    private void WritePending()
    {
        if (!_hasPending)
        {
            return;
        }

        _hasPending = false;
        WriteFrame(_pending, GifTiming.Delay(_pendingStart, _pendingEnd));
    }

    /// <summary>Writes one frame of top-down BGRX pixels, <c>width × height × 4</c> bytes, lasting <paramref name="delay"/> cs.</summary>
    internal void WriteFrame(byte[] pixels, int delay)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, _pixels.Length, nameof(pixels));
        ArgumentOutOfRangeException.ThrowIfNegative(delay);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, GifTiming.MaxDelay);

        IWICBitmap? bitmap = null;
        IWICPalette? palette = null;
        IWICFormatConverter? indexed = null;
        IWICBitmapFrameEncode? frame = null;
        IWICMetadataQueryWriter? metadata = null;
        try
        {
            unsafe
            {
                var bgr = PInvoke.GUID_WICPixelFormat32bppBGR;
                _factory.CreateBitmapFromMemory((uint)_width, (uint)_height, &bgr, (uint)(_width * 4), (uint)pixels.Length, pixels, out bitmap);
            }

            // WIC takes an ordered dither only with one of its fixed palettes (E_INVALIDARG with a custom one), so the
            // comparison in GifExportTests runs it on the 256-colour halftone palette; the product never does.
            var fixedPalette = Dither != WICBitmapDitherType.WICBitmapDitherTypeErrorDiffusion && Dither != WICBitmapDitherType.WICBitmapDitherTypeNone;
            var translate = fixedPalette ? WICBitmapPaletteType.WICBitmapPaletteTypeFixedHalftone256 : WICBitmapPaletteType.WICBitmapPaletteTypeCustom;
            _factory.CreatePalette(out palette);
            if (fixedPalette)
            {
                palette.InitializePredefined(translate, false);
            }
            else
            {
                palette.InitializeFromBitmap(bitmap, 256, false);
            }

            _factory.CreateFormatConverter(out indexed);
            unsafe
            {
                var eight = PInvoke.GUID_WICPixelFormat8bppIndexed;
                indexed.Initialize(bitmap, &eight, Dither, palette, 0, translate);
            }

            IPropertyBag2 options = null!;
            _encoder.CreateNewFrame(out frame, ref options);
            try
            {
                frame.Initialize(options);
            }
            finally
            {
                Wic.Release(options);
            }

            frame.SetSize((uint)_width, (uint)_height);
            unsafe
            {
                var eight = PInvoke.GUID_WICPixelFormat8bppIndexed;
                frame.SetPixelFormat(&eight);
            }

            frame.SetPalette(palette);
            frame.GetMetadataQueryWriter(out metadata);
            Wic.SetUInt16(metadata, "/grctlext/Delay", (ushort)delay);
            Wic.SetByte(metadata, "/grctlext/Disposal", DisposalKeep);
            unsafe
            {
                frame.WriteSource(indexed, null);
            }

            frame.Commit();
            ThrowIfWriteFailed();
        }
        catch (COMException) when (_output?.Failure is { } failure)
        {
            // The file's own failure (a full disk), not what WIC made of it.
            ExceptionDispatchInfo.Throw(failure);
        }
        finally
        {
            Wic.Release(metadata);
            Wic.Release(frame);
            Wic.Release(indexed);
            Wic.Release(palette);
            Wic.Release(bitmap);
        }
    }

    /// <inheritdoc />
    public void Finish()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WritePending();
        try
        {
            _encoder.Commit();
        }
        catch (COMException) when (_output?.Failure is { } failure)
        {
            ExceptionDispatchInfo.Throw(failure);
        }

        // WIC reports success even when the trailer's write failed; the stream's own record says.
        ThrowIfWriteFailed();

        // The last buffered bytes reach the disk here, so a full disk fails the export, not the clean-up. The file closes
        // with the stream; the export moves it into place right after.
        _file?.Flush();
        Dispose();
    }

    // WIC does not report every failed write (it ignores the trailer's at Commit), so after each call the stream's own
    // record decides, and its exception is the one thrown: a full disk is ERROR_DISK_FULL, never a generic encoder error.
    private void ThrowIfWriteFailed()
    {
        if (_output?.Failure is { } failure)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Wic.Release(_encoder);
        Wic.Release(_factory);
        try
        {
            _file?.Dispose();
        }
        catch (IOException)
        {
            // A failed export's last buffered bytes: the file is deleted anyway, and the failure was already thrown.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the caller's clean-up and the journal.
        }
    }
}

/// <summary>WIC helpers the GIF encoder and reader share: the factory, metadata values, releasing.</summary>
internal static class Wic
{
    public static IWICImagingFactory CreateFactory()
    {
        PInvoke.CoCreateInstance(PInvoke.CLSID_WICImagingFactory2, null, CLSCTX.CLSCTX_INPROC_SERVER, out IWICImagingFactory factory).ThrowOnFailure();
        return factory;
    }

    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    public static unsafe void SetBytes(IWICMetadataQueryWriter writer, string name, byte[] value)
    {
        fixed (byte* bytes = value)
        {
            var variant = default(PROPVARIANT);
            variant.vt = VARENUM.VT_UI1 | VARENUM.VT_VECTOR;
            variant.caub.cElems = (uint)value.Length;
            variant.caub.pElems = bytes;
            writer.SetMetadataByName(name, in variant);
        }
    }

    public static void SetUInt16(IWICMetadataQueryWriter writer, string name, ushort value)
    {
        var variant = default(PROPVARIANT);
        variant.vt = VARENUM.VT_UI2;
        variant.uiVal = value;
        writer.SetMetadataByName(name, in variant);
    }

    public static void SetByte(IWICMetadataQueryWriter writer, string name, byte value)
    {
        var variant = default(PROPVARIANT);
        variant.vt = VARENUM.VT_UI1;
        variant.bVal = value;
        writer.SetMetadataByName(name, in variant);
    }

    /// <summary>A metadata value read as a number (VT_UI1, VT_UI2 or VT_UI4), or null when it is missing or of another type.</summary>
    public static uint? GetNumber(IWICMetadataQueryReader reader, string name)
    {
        var variant = default(PROPVARIANT);
        try
        {
            reader.GetMetadataByName(name, ref variant);
        }
        catch (COMException)
        {
            return null;
        }

        try
        {
            return variant.vt switch
            {
                VARENUM.VT_UI1 => variant.bVal,
                VARENUM.VT_UI2 => variant.uiVal,
                VARENUM.VT_UI4 => variant.ulVal,
                _ => null,
            };
        }
        finally
        {
            PInvoke.PropVariantClear(ref variant);
        }
    }

    /// <summary>A metadata value read as bytes (VT_UI1 | VT_VECTOR), or null when it is missing or of another type.</summary>
    public static unsafe byte[]? GetBytes(IWICMetadataQueryReader reader, string name)
    {
        var variant = default(PROPVARIANT);
        try
        {
            reader.GetMetadataByName(name, ref variant);
        }
        catch (COMException)
        {
            return null;
        }

        try
        {
            return variant.vt == (VARENUM.VT_UI1 | VARENUM.VT_VECTOR)
                ? new ReadOnlySpan<byte>(variant.caub.pElems, (int)variant.caub.cElems).ToArray()
                : null;
        }
        finally
        {
            PInvoke.PropVariantClear(ref variant);
        }
    }
}
