using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Media.Rendering;

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
    /// The pipeline calls this in index order from one thread pool (MTA) thread. At most
    /// <see cref="VideoExportSettings.DecodeThreads"/> loads run at once, and at most
    /// <see cref="VideoExportSettings.DecodeAhead"/> frames are loading or loaded and waiting to be drawn.
    /// It disposes every bitmap it gets back. The returned task must not complete on
    /// the caller's synchronization context (use <c>ConfigureAwait(false)</c>).
    /// A load still running when the export stops is awaited before the export returns,
    /// so honour <paramref name="cancellationToken"/> where you can.
    /// </summary>
    /// <param name="outputSize">
    /// The size the frame is drawn to (<see cref="FrameRenderer.Draw(CanvasDrawingSession, CanvasBitmap, FitMode, PixelSize)"/>).
    /// A source may return any size, but the one <see cref="FrameRenderer.DecodeOptions"/> asks for keeps memory
    /// down and lets the drawing copy the pixels one to one: a 24 MP photo decoded for 4K is a quarter of its full size.
    /// </param>
    /// <remarks>
    /// An exception the load throws (synchronously or through the task) fails the export as a
    /// <see cref="VideoExportException"/> with <see cref="VideoExportFailure.FrameUnreadable"/>, the frame index and
    /// <see cref="SourcePath"/>, the exception as its inner exception (as <see cref="VideoExportFailure.GraphicsDeviceLost"/>
    /// or <see cref="VideoExportFailure.OutOfMemory"/> when the device or memory is the cause); one that is a bug in our
    /// code (<see cref="ArgumentNullException"/>, our own <see cref="InvalidOperationException"/>) surfaces as itself.
    /// </remarks>
    Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken);

    /// <summary>The file frame <paramref name="index"/> comes from, for failure messages; null when it has none.</summary>
    string? SourcePath(int index) => null;

    /// <summary>
    /// The processing frame <paramref name="index"/> is drawn with (#45: its deflicker exposure), the same
    /// <see cref="FrameLook"/> the preview draws the photo with; none by default.
    /// </summary>
    FrameLook LookOf(int index) => FrameLook.None;
}
