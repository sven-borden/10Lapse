using Microsoft.Graphics.Canvas;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using Windows.UI;

namespace TenLapse.Tests.Media;

/// <summary>
/// Small synthetic frames drawn on the GPU: a colour that changes with the
/// index, and the index as a <see cref="FrameCode"/>.
/// </summary>
internal sealed class NumberedFrames(int count, int width, int height) : IFrameSource
{
    /// <summary>Runs before frame n is drawn; may throw or cancel, to test failure paths.</summary>
    public Action<int>? BeforeLoad { get; init; }

    public int FrameCount => count;

    public Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, CancellationToken cancellationToken) =>
        Task.Run<CanvasBitmap>(
            () =>
            {
                BeforeLoad?.Invoke(index);
                cancellationToken.ThrowIfCancellationRequested();
                var target = new CanvasRenderTarget(device, width, height, 96f);
                using var session = target.CreateDrawingSession();
                session.Clear(Color.FromArgb(255, (byte)(index * 4), 96, (byte)(255 - (index * 4))));
                FrameCode.Draw(session, index, width, height);
                return target;
            },
            cancellationToken);
}
