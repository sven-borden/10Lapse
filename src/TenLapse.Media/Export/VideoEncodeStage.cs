namespace TenLapse.Media.Export;

/// <summary>The Media Foundation API that encodes (#8 compared both).</summary>
public enum VideoEncodeStage
{
    /// <summary>
    /// <c>IMFSinkWriter</c> through CsWin32, on the export's own Direct3D device.
    /// Every frame arrives intact, GPU only, and it reports which encoder ran.
    /// </summary>
    SinkWriter,

    /// <summary>
    /// <c>MediaTranscoder</c> fed by a <c>MediaStreamSource</c>. Kept for the
    /// comparison; see <see cref="TranscoderInput"/> for why it is not the default.
    /// It also failed outright at 0.5 fps (a COMException with no message), cannot
    /// set low-latency mode (so the software encoder shifts every frame by one),
    /// and does not say which encoder ran.
    /// </summary>
    MediaTranscoder,
}

/// <summary>How frames reach <c>MediaTranscoder</c>.</summary>
public enum TranscoderInput
{
    /// <summary>
    /// <c>MediaStreamSample.CreateFromBuffer</c> with the pixels read back to the
    /// CPU: every frame arrives intact, but it is a third as fast at 4K and the
    /// 33 MB arrays per frame pile up until a GC (8 GB working set for 250 frames).
    /// </summary>
    CpuBuffer,

    /// <summary>
    /// <c>MediaStreamSample.CreateFromDirect3D11Surface</c>. Do not ship. Measured
    /// with NVENC (2026-10): on Win2D's shared device (with multithread protection
    /// turned on) it ran at 130 fps for 4K but about one export in three had a
    /// black frame; on the export's own video device (what the pipeline uses now)
    /// the transcoder never takes the first frame and the export fails after
    /// <see cref="VideoExporter.EncoderStallTimeout"/>.
    /// </summary>
    Direct3DSurface,
}
