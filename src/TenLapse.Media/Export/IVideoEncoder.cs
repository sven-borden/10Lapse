using Microsoft.Graphics.Canvas;
using TenLapse.Media.Inspection;

namespace TenLapse.Media.Export;

/// <summary>
/// The encode stage as the pipeline sees it: <see cref="SinkWriterEncoder"/> in
/// the product, a fake in tests that need the encoder to fail on cue.
/// </summary>
/// <remarks>
/// Created on a thread pool thread once the export's device exists, and then
/// called from one thread at a time. Creating it opens the output file.
/// </remarks>
internal interface IVideoEncoder : IDisposable
{
    /// <summary>The encoder that runs, if it can tell.</summary>
    VideoEncoderInfo? Encoder { get; }

    /// <summary>Queues one frame; blocks while the encoder is behind. The target is not drawn again afterwards.</summary>
    void Write(CanvasRenderTarget target, long time, long duration);

    /// <summary>Drains the encoder and completes the file.</summary>
    void Finish();
}

/// <summary>Opens the encoder for one export: the output path, the settings and the export's own device.</summary>
internal delegate IVideoEncoder VideoEncoderFactory(string path, VideoExportSettings settings, VideoDevice device);
