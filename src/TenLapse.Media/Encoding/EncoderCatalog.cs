using System.Runtime.InteropServices;
using TenLapse.Core.Projects;
using TenLapse.Media.Inspection;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;

namespace TenLapse.Media.Encoding;

/// <summary>An encoder transform (MFT) Media Foundation lists for H.264 or HEVC.</summary>
/// <param name="Format">The codec it writes.</param>
/// <param name="Name">Its friendly name, such as "NVIDIA HEVC Encoder MFT".</param>
/// <param name="IsHardware">True for a GPU encoder (NVENC, Quick Sync, AMF, a Qualcomm or Arm SoC block).</param>
/// <param name="Clsid">Its class id; the list of them is part of the probe's cache key.</param>
public sealed record EncoderMft(VideoFormat Format, string Name, bool IsHardware, Guid Clsid);

/// <summary>
/// Lists the H.264 and HEVC encoders installed (<c>MFTEnumEx</c>), hardware first, then software, in the order
/// Media Foundation prefers them. A listed encoder is not proof it works: <see cref="EncoderProbe"/> checks that.
/// </summary>
public static class EncoderCatalog
{
    /// <summary>Every H.264 encoder, then every HEVC encoder; hardware before software for each.</summary>
    public static IReadOnlyList<EncoderMft> Find()
    {
        using var session = new MediaFoundationSession();
        return [.. Enumerate(VideoFormat.Mp4H264), .. Enumerate(VideoFormat.Mp4Hevc)];
    }

    /// <summary>The encoders for one codec, hardware first.</summary>
    public static IReadOnlyList<EncoderMft> Find(VideoFormat format)
    {
        using var session = new MediaFoundationSession();
        return Enumerate(format);
    }

    private static List<EncoderMft> Enumerate(VideoFormat format)
    {
        const MFT_ENUM_FLAG Sorted = MFT_ENUM_FLAG.MFT_ENUM_FLAG_SORTANDFILTER;
        return
        [
            .. Enumerate(format, MFT_ENUM_FLAG.MFT_ENUM_FLAG_HARDWARE | Sorted, isHardware: true),
            .. Enumerate(
                format,
                MFT_ENUM_FLAG.MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_ASYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_LOCALMFT | Sorted,
                isHardware: false),
        ];
    }

    private static unsafe List<EncoderMft> Enumerate(VideoFormat format, MFT_ENUM_FLAG flags, bool isHardware)
    {
        var output = new MFT_REGISTER_TYPE_INFO
        {
            guidMajorType = PInvoke.MFMediaType_Video,
            guidSubtype = format == VideoFormat.Mp4Hevc ? PInvoke.MFVideoFormat_HEVC : PInvoke.MFVideoFormat_H264,
        };
        PInvoke.MFTEnumEx(PInvoke.MFT_CATEGORY_VIDEO_ENCODER, flags, null, output, out var activates, out var count).ThrowOnFailure();

        var encoders = new List<EncoderMft>((int)count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                encoders.Add(new EncoderMft(format, FriendlyName(activates[i]), isHardware, Clsid(activates[i])));
            }
        }
        finally
        {
            for (var i = 0; i < count; i++)
            {
                activates[i]->Release();
            }

            PInvoke.CoTaskMemFree(activates);
        }

        return encoders;
    }

    private static unsafe string FriendlyName(IMFActivate_unmanaged* activate)
    {
        var key = PInvoke.MFT_FRIENDLY_NAME_Attribute;
        PWSTR name;
        try
        {
            activate->GetAllocatedString(&key, &name, out _);
        }
        catch (COMException)
        {
            return "(unnamed encoder)";
        }

        try
        {
            return name.ToString();
        }
        finally
        {
            PInvoke.CoTaskMemFree(name.Value);
        }
    }

    private static unsafe Guid Clsid(IMFActivate_unmanaged* activate)
    {
        var key = PInvoke.MFT_TRANSFORM_CLSID_Attribute;
        Guid clsid;
        try
        {
            activate->GetGUID(&key, &clsid);
            return clsid;
        }
        catch (COMException)
        {
            // Every registered MFT has one; an activate without it still counts in the key, as the empty id.
            return Guid.Empty;
        }
    }
}
