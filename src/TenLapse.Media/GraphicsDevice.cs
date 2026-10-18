using Microsoft.Graphics.Canvas;

namespace TenLapse.Media;

/// <summary>
/// The Win2D device that preview draws frames with. An export runs on its own
/// device instead (<c>VideoDevice</c>, with Direct3D video support and
/// multithread protection, which the shared device lacks).
/// </summary>
public static class GraphicsDevice
{
    /// <summary>
    /// Returns the process-wide shared Win2D device. Win2D owns its lifetime:
    /// callers must not dispose it.
    /// </summary>
    public static CanvasDevice Shared => CanvasDevice.GetSharedDevice();
}
