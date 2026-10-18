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
/// <param name="Clsid">
/// Its class id, part of the probe's cache key; <see cref="Guid.Empty"/> for a Store codec, which is listed
/// without one (the HEVC Video Extensions' encoder).
/// </param>
public sealed record EncoderMft(VideoFormat Format, string Name, bool IsHardware, Guid Clsid);

/// <summary>A decoder transform (MFT) Media Foundation lists for H.264 or HEVC: what lets Windows play the file back.</summary>
/// <param name="Format">The codec it reads.</param>
/// <param name="Name">Its friendly name, such as "Microsoft H265 Video Decoder MFT".</param>
/// <param name="IsHardware">True for a decoder registered as a hardware MFT.</param>
/// <param name="Clsid">Its class id; <see cref="Guid.Empty"/> for a Store codec listed without one.</param>
public sealed record DecoderMft(VideoFormat Format, string Name, bool IsHardware, Guid Clsid);

/// <summary>
/// Lists the H.264 and HEVC encoders installed (<c>MFTEnumEx</c>), hardware first, then software, in the order
/// Media Foundation prefers them, and the decoders. A listed encoder is not proof it works:
/// <see cref="EncoderProbe"/> checks that.
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

    /// <summary>
    /// The decoders for one codec (video in that codec, any output), hardware first. The H.264 decoder ships with
    /// Windows; an HEVC decoder comes only with the HEVC Video Extensions (or a PC maker's copy of them).
    /// </summary>
    public static IReadOnlyList<DecoderMft> FindDecoders(VideoFormat format)
    {
        using var session = new MediaFoundationSession();
        List<EncoderMft> found =
        [
            .. Enumerate(format, decoders: true, HardwareFlags, isHardware: true),
            .. Enumerate(format, decoders: true, SoftwareFlags, isHardware: false),
        ];
        return [.. found.Select(mft => new DecoderMft(mft.Format, mft.Name, mft.IsHardware, mft.Clsid))];
    }

    private const MFT_ENUM_FLAG HardwareFlags = MFT_ENUM_FLAG.MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG.MFT_ENUM_FLAG_SORTANDFILTER;

    private const MFT_ENUM_FLAG SoftwareFlags =
        MFT_ENUM_FLAG.MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_ASYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_SORTANDFILTER;

    private static List<EncoderMft> Enumerate(VideoFormat format) =>
    [
        .. Enumerate(format, decoders: false, HardwareFlags, isHardware: true),
        .. Enumerate(format, decoders: false, SoftwareFlags, isHardware: false),
    ];

    // Encoders: video in this codec as output. Decoders: as input.
    private static unsafe List<EncoderMft> Enumerate(VideoFormat format, bool decoders, MFT_ENUM_FLAG flags, bool isHardware)
    {
        var type = new MFT_REGISTER_TYPE_INFO
        {
            guidMajorType = PInvoke.MFMediaType_Video,
            guidSubtype = format == VideoFormat.Mp4Hevc ? PInvoke.MFVideoFormat_HEVC : PInvoke.MFVideoFormat_H264,
        };
        IMFActivate_unmanaged** activates;
        uint count;
        if (decoders)
        {
            PInvoke.MFTEnumEx(PInvoke.MFT_CATEGORY_VIDEO_DECODER, flags, type, null, out activates, out count).ThrowOnFailure();
        }
        else
        {
            PInvoke.MFTEnumEx(PInvoke.MFT_CATEGORY_VIDEO_ENCODER, flags, null, type, out activates, out count).ThrowOnFailure();
        }

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
            // A Store codec's activate has none (MF_E_ATTRIBUTENOTFOUND); the key also has the name.
            return Guid.Empty;
        }
    }
}
