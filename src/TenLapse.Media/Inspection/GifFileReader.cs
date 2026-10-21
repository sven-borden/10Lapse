using System.Runtime.InteropServices;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Imaging;

namespace TenLapse.Media.Inspection;

/// <summary>What an animated GIF holds, read back with the WIC GIF decoder (#51).</summary>
/// <param name="Width">The logical screen's width in pixels.</param>
/// <param name="Height">The logical screen's height in pixels.</param>
/// <param name="Delays">Each frame's delay in centiseconds, in order.</param>
/// <param name="FrameRects">Each frame's left, top, width and height on the logical screen.</param>
/// <param name="Application">The application extension's identifier ("NETSCAPE2.0"), or null when there is none.</param>
/// <param name="LoopCount">The NETSCAPE2.0 loop count (0: forever), or null when the file has no loop extension.</param>
public sealed record GifFileInfo(
    int Width,
    int Height,
    IReadOnlyList<int> Delays,
    IReadOnlyList<(int Left, int Top, int Width, int Height)> FrameRects,
    string? Application,
    int? LoopCount)
{
    /// <summary>The number of frames.</summary>
    public int FrameCount => Delays.Count;

    /// <summary>The length in centiseconds: the delays added up.</summary>
    public long TotalCentiseconds => Delays.Sum(delay => (long)delay);
}

/// <summary>Reads an animated GIF back through the WIC GIF decoder, for the tests and the test matrix (#51).</summary>
public static class GifFileReader
{
    /// <summary>The GIF's size, frames, delays and loop extension.</summary>
    public static GifFileInfo Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        IWICImagingFactory? factory = null;
        IWICBitmapDecoder? decoder = null;
        IWICMetadataQueryReader? global = null;
        try
        {
            factory = Wic.CreateFactory();
            decoder = Open(factory, path);
            decoder.GetMetadataQueryReader(out global);
            var width = (int)(Wic.GetNumber(global, "/logscrdesc/Width") ?? 0);
            var height = (int)(Wic.GetNumber(global, "/logscrdesc/Height") ?? 0);
            var application = Wic.GetBytes(global, "/appext/application") is { } id ? System.Text.Encoding.ASCII.GetString(id) : null;
            int? loops = Wic.GetBytes(global, "/appext/data") is { Length: >= 4 } data && data[0] >= 3 && data[1] == 1
                ? data[2] | (data[3] << 8)
                : null;

            decoder.GetFrameCount(out var count);
            var delays = new List<int>((int)count);
            var rects = new List<(int, int, int, int)>((int)count);
            for (var i = 0u; i < count; i++)
            {
                IWICBitmapFrameDecode? frame = null;
                IWICMetadataQueryReader? metadata = null;
                try
                {
                    decoder.GetFrame(i, out frame);
                    frame.GetMetadataQueryReader(out metadata);
                    delays.Add((int)(Wic.GetNumber(metadata, "/grctlext/Delay") ?? 0));
                    rects.Add((
                        (int)(Wic.GetNumber(metadata, "/imgdesc/Left") ?? 0),
                        (int)(Wic.GetNumber(metadata, "/imgdesc/Top") ?? 0),
                        (int)(Wic.GetNumber(metadata, "/imgdesc/Width") ?? 0),
                        (int)(Wic.GetNumber(metadata, "/imgdesc/Height") ?? 0)));
                }
                finally
                {
                    Wic.Release(metadata);
                    Wic.Release(frame);
                }
            }

            return new GifFileInfo(width, height, delays, rects, application, loops);
        }
        finally
        {
            Wic.Release(global);
            Wic.Release(decoder);
            Wic.Release(factory);
        }
    }

    /// <summary>
    /// Decodes every frame to top-down BGRA and hands it to <paramref name="handler"/> with its start time (the delays
    /// before it, in 100 ns ticks). Each frame is decoded on its own: right for the GIFs 10Lapse writes, whose frames all
    /// cover the whole image.
    /// </summary>
    /// <returns>The number of frames.</returns>
    public static unsafe int ReadFrames(string path, DecodedFrameHandler handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(handler);
        IWICImagingFactory? factory = null;
        IWICBitmapDecoder? decoder = null;
        try
        {
            factory = Wic.CreateFactory();
            decoder = Open(factory, path);
            decoder.GetFrameCount(out var count);
            long time = 0;
            for (var i = 0u; i < count; i++)
            {
                IWICBitmapFrameDecode? frame = null;
                IWICMetadataQueryReader? metadata = null;
                IWICFormatConverter? converter = null;
                try
                {
                    decoder.GetFrame(i, out frame);
                    frame.GetMetadataQueryReader(out metadata);
                    var delay = (long)(Wic.GetNumber(metadata, "/grctlext/Delay") ?? 0);
                    frame.GetSize(out var width, out var height);
                    factory.CreateFormatConverter(out converter);
                    var bgra = PInvoke.GUID_WICPixelFormat32bppBGRA;
                    converter.Initialize(frame, &bgra, WICBitmapDitherType.WICBitmapDitherTypeNone, null!, 0, WICBitmapPaletteType.WICBitmapPaletteTypeCustom);
                    var stride = (int)width * 4;
                    var pixels = new byte[stride * (int)height];
                    converter.CopyPixels(null, (uint)stride, (uint)pixels.Length, pixels);
                    handler(new DecodedVideoFrame((int)i, time, (int)width, (int)height, stride, pixels));
                    time += delay * GifTiming.TicksPerCentisecond;
                }
                finally
                {
                    Wic.Release(converter);
                    Wic.Release(metadata);
                    Wic.Release(frame);
                }
            }

            return (int)count;
        }
        finally
        {
            Wic.Release(decoder);
            Wic.Release(factory);
        }
    }

    private static unsafe IWICBitmapDecoder Open(IWICImagingFactory factory, string path)
    {
        try
        {
            return factory.CreateDecoderFromFilename(path, null, GENERIC_ACCESS_RIGHTS.GENERIC_READ, WICDecodeOptions.WICDecodeMetadataCacheOnLoad);
        }
        catch (COMException exception)
        {
            throw new IOException($"{path} could not be opened as a GIF: {exception.Message}", exception);
        }
    }
}
