using Microsoft.Graphics.Canvas;

namespace TenLapse.Media;

/// <summary>The Win2D device that preview and export draw frames with.</summary>
public static class GraphicsDevice
{
    /// <summary>
    /// Returns the process-wide shared Win2D device. Win2D owns its lifetime:
    /// callers must not dispose it.
    /// </summary>
    public static CanvasDevice Shared => CanvasDevice.GetSharedDevice();
}
