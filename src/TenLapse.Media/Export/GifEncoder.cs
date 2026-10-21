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
/// <b>Palette and dithering.</b> A palette per frame (<c>IWICPalette.InitializeFromBitmap</c>, 256 colours, no
/// transparent one) follows the light as a timelapse goes from day to dusk; one palette for the whole clip would have
/// to share 256 colours between noon and night. Error diffusion (<see cref="WICBitmapDitherType.WICBitmapDitherTypeErrorDiffusion"/>)
/// spreads each pixel's rounding error to its neighbours, so a sunset sky shows a smooth gradient instead of the bands
/// a nearest-colour mapping draws; an ordered dither leaves a visible cross-hatch on the same sky (#51, measured by
/// <c>GifDitherTests</c>). Its cost is a slightly larger file and a fine grain that changes from frame to frame.
/// </para>
/// <para>
/// Created on a thread pool (MTA) thread and then called from one thread at a time, like every encoder; the WIC objects
/// are free-threaded. Creating it creates (or truncates) the file.
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

    // GENERIC_WRITE: IWICStream opens the file with CREATE_ALWAYS.
    private const uint GenericWrite = 0x40000000;

    // The graphic control extension's disposal method 1: leave the frame in place (every frame covers the whole image).
    private const byte DisposalKeep = 1;

    private readonly IWICImagingFactory _factory;
    private readonly IWICStream _stream;
    private readonly IWICBitmapEncoder _encoder;
    private readonly string _path;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _pixels;
    private bool _disposed;

    public GifEncoder(string path, VideoExportSettings settings, VideoDevice device)
        : this(path, settings)
    {
        ArgumentNullException.ThrowIfNull(device);
    }

    /// <summary>Opens the GIF; the device is not needed (the frames are read back on the CPU).</summary>
    internal GifEncoder(string path, VideoExportSettings settings, WICBitmapDitherType dither = WICBitmapDitherType.WICBitmapDitherTypeErrorDiffusion)
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
        var opened = false;
        try
        {
            _factory = Wic.CreateFactory();
            _factory.CreateStream(out _stream);
            _stream.InitializeFromFilename(path, GenericWrite);
            opened = true;
            unsafe
            {
                var container = PInvoke.GUID_ContainerFormatGif;
                _encoder = _factory.CreateEncoder(&container, null);
            }

            _encoder.Initialize((IStream)_stream, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache);
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
        var delay = GifTiming.Delay(time, time + duration);

        // Win2D's B8G8R8A8 premultiplied, read back from the GPU. The frames are opaque (FrameRenderer fills the frame), so
        // the bytes are plain BGR with alpha 255.
        target.GetPixelBytes(_pixels.AsBuffer());
        WriteFrame(_pixels, delay);
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

            _factory.CreatePalette(out palette);
            palette.InitializeFromBitmap(bitmap, 256, false);
            _factory.CreateFormatConverter(out indexed);
            unsafe
            {
                var eight = PInvoke.GUID_WICPixelFormat8bppIndexed;
                indexed.Initialize(bitmap, &eight, Dither, palette, 0, WICBitmapPaletteType.WICBitmapPaletteTypeCustom);
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
        _encoder.Commit();

        // The file closes with the stream; the export moves it into place right after.
        Dispose();
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
        Wic.Release(_stream);
        Wic.Release(_factory);
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
