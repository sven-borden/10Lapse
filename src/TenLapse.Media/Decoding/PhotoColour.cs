using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Win32;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace TenLapse.Media.Decoding;

/// <summary>How a decode brought the photo's colour to sRGB.</summary>
internal enum ColourConversion
{
    /// <summary>Nothing to convert: no embedded profile, or one that is sRGB in effect.</summary>
    None,

    /// <summary>Converted from the embedded profile after scaling, at the decoded size.</summary>
    AfterScaling,

    /// <summary>Converted by WIC while decoding, at the stored size: a profile that is not RGB (CMYK, gray), or one this could not read.</summary>
    AtSource,
}

/// <summary>What the photo's embedded colour asks of a decode.</summary>
/// <param name="Conversion">When to convert.</param>
/// <param name="Profile">The ICC profile to convert from, for <see cref="ColourConversion.AfterScaling"/>; or null with <paramref name="ExifColourSpace"/>.</param>
/// <param name="ExifColourSpace">The EXIF colour space to convert from when there is no profile (2: Adobe RGB).</param>
internal sealed record ColourPlan(ColourConversion Conversion, byte[]? Profile = null, uint ExifColourSpace = 0)
{
    public static ColourPlan None { get; } = new(ColourConversion.None);

    public static ColourPlan AtSource { get; } = new(ColourConversion.AtSource);
}

/// <summary>
/// The embedded colour of a photo, read and converted through WIC directly: WinRT's <see cref="BitmapDecoder"/>
/// converts at the stored size before scaling, which makes a 24 MP JPEG with any embedded profile, even sRGB,
/// decode about 2.8 times slower.
/// </summary>
/// <remarks>
/// A WIC factory per call: it costs microseconds, and keeps the COM objects on the calling thread whatever its
/// apartment. Every COM object is released before returning.
/// </remarks>
internal static class PhotoColour
{
    private const uint ExifSrgb = 1;
    private const uint ExifAdobeRgb = 2;

    /// <summary>
    /// What a decode of the photo in <paramref name="stream"/> must do with its colour, from the first colour
    /// context of its first frame (the one WIC converts from). Reads a clone, so the stream's position stays.
    /// </summary>
    public static ColourPlan Plan(IRandomAccessStream stream)
    {
        using var clone = stream.CloneStream();
        IWICImagingFactory? factory = null;
        IStream? source = null;
        IWICBitmapDecoder? decoder = null;
        IWICBitmapFrameDecode? frame = null;
        IWICColorContext[] contexts = [];
        try
        {
            factory = CreateFactory();
            PInvoke.CreateStreamOverRandomAccessStream(clone, out source).ThrowOnFailure();
            unsafe
            {
                decoder = factory.CreateDecoderFromStream(source, null, WICDecodeOptions.WICDecodeMetadataCacheOnDemand);
            }

            decoder.GetFrame(0, out frame);
            frame.GetColorContexts(0, null!, out var count);
            if (count == 0)
            {
                return ColourPlan.None;
            }

            contexts = new IWICColorContext[count];
            for (var i = 0; i < contexts.Length; i++)
            {
                factory.CreateColorContext(out contexts[i]);
            }

            frame.GetColorContexts(count, contexts, out _);
            return PlanFor(contexts[0]);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            // A decoder that keeps its colour to itself: let WinRT convert as it always did.
            return ColourPlan.AtSource;
        }
        finally
        {
            foreach (var context in contexts)
            {
                Release(context);
            }

            Release(frame);
            Release(decoder);
            Release(source);
            Release(factory);
        }
    }

    /// <summary>
    /// Converts <paramref name="pixels"/> (BGRA8) in place from the plan's profile to sRGB. The pixels are
    /// treated as straight alpha: exact for photos, which are opaque.
    /// </summary>
    public static void ConvertToSrgb(SoftwareBitmap pixels, ColourPlan plan)
    {
        var width = pixels.PixelWidth;
        var height = pixels.PixelHeight;
        var stride = width * 4;
        var size = stride * height;
        var bytes = ArrayPool<byte>.Shared.Rent(size);
        IWICImagingFactory? factory = null;
        IWICColorContext? from = null;
        IWICColorContext? to = null;
        IWICBitmap? bitmap = null;
        IWICColorTransform? transform = null;
        try
        {
            pixels.CopyToBuffer(bytes.AsBuffer(0, size));
            factory = CreateFactory();
            factory.CreateColorContext(out from);
            if (plan.Profile is { } profile)
            {
                from.InitializeFromMemory(profile, (uint)profile.Length);
            }
            else
            {
                from.InitializeFromExifColorSpace(plan.ExifColourSpace);
            }

            factory.CreateColorContext(out to);
            to.InitializeFromExifColorSpace(ExifSrgb);
            var format = PInvoke.GUID_WICPixelFormat32bppBGRA;
            unsafe
            {
                factory.CreateBitmapFromMemory((uint)width, (uint)height, &format, (uint)stride, (uint)size, bytes, out bitmap);
                factory.CreateColorTransformer(out transform);
                transform.Initialize(bitmap, from, to, &format);
                transform.CopyPixels(null, (uint)stride, (uint)size, bytes);
            }

            pixels.CopyFromBuffer(bytes.AsBuffer(0, size));
        }
        finally
        {
            Release(transform);
            Release(bitmap);
            Release(to);
            Release(from);
            Release(factory);
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private static ColourPlan PlanFor(IWICColorContext context)
    {
        WICColorContextType type;
        unsafe
        {
            context.GetType(&type);
        }

        if (type == WICColorContextType.WICColorContextExifColorSpace)
        {
            context.GetExifColorSpace(out var space);
            return space == ExifAdobeRgb ? new ColourPlan(ColourConversion.AfterScaling, ExifColourSpace: space) : ColourPlan.None;
        }

        context.GetProfileBytes(0, null!, out var length);
        if (length == 0)
        {
            return ColourPlan.None;
        }

        var profile = new byte[length];
        context.GetProfileBytes(length, profile, out _);
        if (IccProfile.IsSrgb(profile))
        {
            return ColourPlan.None;
        }

        // CMYK and gray profiles describe the data before WIC turns it into BGRA: only WIC's own conversion,
        // at the source, can apply them.
        return IccProfile.IsRgb(profile) ? new ColourPlan(ColourConversion.AfterScaling, profile) : ColourPlan.AtSource;
    }

    private static IWICImagingFactory CreateFactory()
    {
        PInvoke.CoCreateInstance(PInvoke.CLSID_WICImagingFactory2, null, CLSCTX.CLSCTX_INPROC_SERVER, out IWICImagingFactory factory).ThrowOnFailure();
        return factory;
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }
}
