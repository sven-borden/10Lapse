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
    /// <c>MediaStreamSample.CreateFromDirect3D11Surface</c>: GPU only and fast,
    /// but with a hardware encoder about one export in three has a black frame
    /// (seen on NVIDIA, 2026-10). Do not ship.
    /// </summary>
    Direct3DSurface,
}
