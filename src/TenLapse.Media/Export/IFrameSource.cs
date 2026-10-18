using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;

namespace TenLapse.Media.Export;

/// <summary>The decode stage: turns frame n into a GPU bitmap.</summary>
public interface IFrameSource
{
    /// <summary>The number of frames, at least 1.</summary>
    int FrameCount { get; }

    /// <summary>
    /// Starts loading frame <paramref name="index"/> on <paramref name="device"/>,
    /// the export's own device: the bitmap must be made on it, not on
    /// <see cref="GraphicsDevice.Shared"/>.
    /// The pipeline calls this in index order from one thread pool (MTA) thread, keeps up to
    /// <see cref="VideoExportSettings.DecodeAhead"/> loads running at once, and
    /// disposes every bitmap it gets back. The returned task must not complete on
    /// the caller's synchronization context (use <c>ConfigureAwait(false)</c>).
    /// A load still running when the export stops is awaited before the device
    /// is disposed, so honour <paramref name="cancellationToken"/> where you can.
    /// </summary>
    /// <param name="outputSize">
    /// The size the frame is drawn to (<see cref="FrameFill.Draw"/>, a centred cover crop). A source may return
    /// any size, but one no larger than the cover size of this box (<see cref="Decoding.PhotoDecodeOptions.Cover"/>)
    /// keeps memory down: a 24 MP photo decoded for 4K is a quarter of its full size (#22).
    /// </param>
    Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken);
}
