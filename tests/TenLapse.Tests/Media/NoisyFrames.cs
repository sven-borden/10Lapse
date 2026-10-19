using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Media.Export;
using Windows.Graphics.DirectX;

namespace TenLapse.Tests.Media;

/// <summary>
/// Frames of coloured noise, a new pattern each frame (seeded by the index): content no encoder can squeeze under
/// its target bitrate, so the file's bitrate shows the bitrate the encoder was really given.
/// </summary>
internal sealed class NoisyFrames(int count, int width, int height) : IFrameSource
{
    public int FrameCount => count;

    public Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                var pixels = new byte[width * height * 4];
                new Random(index).NextBytes(pixels);
                for (var i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }

                return CanvasBitmap.CreateFromBytes(device, pixels, width, height, DirectXPixelFormat.B8G8R8A8UIntNormalized);
            },
            cancellationToken);
}
