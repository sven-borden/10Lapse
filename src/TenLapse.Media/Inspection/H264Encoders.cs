using TenLapse.Core.Projects;

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
    public static IReadOnlyList<VideoEncoderInfo> Find() =>
        [.. Encoding.EncoderCatalog.Find(VideoFormat.Mp4H264).Select(encoder => new VideoEncoderInfo(encoder.Name, encoder.IsHardware))];
}
