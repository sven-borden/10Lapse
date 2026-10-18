using Microsoft.Graphics.Canvas;
using TenLapse.Media.Export;
using Windows.UI;

namespace TenLapse.Tests.Media;

/// <summary>Frames filled with one colour, for checking what the colour conversion and the tags do to it.</summary>
internal sealed class SolidFrames(int count, int width, int height, Color color) : IFrameSource
{
    public int FrameCount => count;

    public Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, CancellationToken cancellationToken) =>
        Task.Run<CanvasBitmap>(
            () =>
            {
                var target = new CanvasRenderTarget(device, width, height, 96f);
                using var session = target.CreateDrawingSession();
                session.Clear(color);
                return target;
            },
            cancellationToken);
}
