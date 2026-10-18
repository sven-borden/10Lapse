using Microsoft.Graphics.Canvas;

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
    /// The pipeline calls this in index order from one thread, keeps up to
    /// <see cref="VideoExportSettings.DecodeAhead"/> loads running at once, and
    /// disposes every bitmap it gets back. The returned task must not complete on
    /// the caller's synchronization context (use <c>ConfigureAwait(false)</c>).
    /// </summary>
    Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, CancellationToken cancellationToken);
}
