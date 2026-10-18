using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;

namespace TenLapse.Media.Export;

/// <summary>
/// Frames from photo files on disk, decoded by <see cref="PhotoDecoder"/> (WIC) on the CPU at the cover size of the
/// output, then uploaded to the GPU. EXIF orientation is applied, colour converted to sRGB.
/// </summary>
/// <param name="paths">Absolute paths, one per frame, in frame order.</param>
/// <remarks>
/// A photo that cannot be decoded fails the export with a <see cref="PhotoDecodeException"/> naming the frame and
/// the <see cref="SkipReason"/>: an export cannot leave a frame out without changing the clip's timing.
/// </remarks>
public sealed class PhotoFileSource(IReadOnlyList<string> paths) : IFrameSource
{
    private readonly IReadOnlyList<string> _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    /// <inheritdoc/>
    public int FrameCount => _paths.Count;

    /// <inheritdoc/>
    public Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken) =>
        // Task.Run: the first awaits complete synchronously often enough that the caller would decode inline.
        Task.Run(() => DecodeAsync(index, device, outputSize, cancellationToken), cancellationToken);

    private async Task<CanvasBitmap> DecodeAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken)
    {
        // The device's largest bitmap bounds a photo far narrower than the output's aspect (a 30,000 px panorama).
        var options = PhotoDecodeOptions.Cover(outputSize) with { MaxSide = device.MaximumBitmapSizeInPixels };
        var result = await PhotoDecoder.DecodeAsync(_paths[index], options, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new PhotoDecodeException(result.Failure, index);
        }

        using var photo = result.Value;
        return CanvasBitmap.CreateFromSoftwareBitmap(device, photo.Pixels);
    }
}
