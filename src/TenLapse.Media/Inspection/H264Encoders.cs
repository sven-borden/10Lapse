using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;

namespace TenLapse.Media.Inspection;

/// <summary>An H.264 encoder Media Foundation can load.</summary>
/// <param name="Name">The MFT's friendly name, such as "NVIDIA H.264 Encoder MFT".</param>
/// <param name="IsHardware">True for a GPU encoder (NVENC, Quick Sync, AMF, a Qualcomm or Arm SoC block).</param>
public sealed record VideoEncoderInfo(string Name, bool IsHardware);

/// <summary>
/// Lists the H.264 encoders installed, in the order Media Foundation prefers
/// them: hardware first, then software. The sink writer with hardware
/// transforms on picks from the same list, so the first entry is the encoder it
/// is expected to use; <see cref="Export.VideoExportResult.Encoder"/> says which one ran.
/// </summary>
public static class H264Encoders
{
    /// <summary>Hardware encoders first, then the software ones (normally the Microsoft H.264 encoder).</summary>
    public static IReadOnlyList<VideoEncoderInfo> Find()
    {
        using var session = new MediaFoundationSession();
        var hardware = Enumerate(MFT_ENUM_FLAG.MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG.MFT_ENUM_FLAG_SORTANDFILTER, isHardware: true);
        var software = Enumerate(
            MFT_ENUM_FLAG.MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_ASYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_SORTANDFILTER,
            isHardware: false);
        return [.. hardware, .. software];
    }

    private static unsafe List<VideoEncoderInfo> Enumerate(MFT_ENUM_FLAG flags, bool isHardware)
    {
        var output = new MFT_REGISTER_TYPE_INFO
        {
            guidMajorType = PInvoke.MFMediaType_Video,
            guidSubtype = PInvoke.MFVideoFormat_H264,
        };
        PInvoke.MFTEnumEx(PInvoke.MFT_CATEGORY_VIDEO_ENCODER, flags, null, output, out var activates, out var count).ThrowOnFailure();

        var encoders = new List<VideoEncoderInfo>((int)count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                encoders.Add(new VideoEncoderInfo(FriendlyName(activates[i]), isHardware));
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
        catch (System.Runtime.InteropServices.COMException)
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
}
