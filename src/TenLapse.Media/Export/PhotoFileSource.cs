using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Media.Rendering;

namespace TenLapse.Media.Export;

/// <summary>
/// Frames from photo files on disk, decoded by <see cref="PhotoDecoder"/> (WIC) on the CPU at the size the frame
/// shows them (<see cref="FrameRenderer.DecodeOptions"/>: the cover size for Fill, the contain size for Fit), then
/// uploaded to the GPU. EXIF orientation is applied, colour converted to sRGB.
/// </summary>
/// <param name="paths">Absolute paths, one per frame, in frame order.</param>
/// <param name="fit">How the frames are drawn, which sets the decode size; the export's <see cref="VideoExportSettings.Fit"/> must agree.</param>
/// <remarks>
/// A photo that cannot be decoded throws a <see cref="PhotoDecodeException"/> naming the frame and the
/// <see cref="SkipReason"/>; the export fails with it (as the inner exception of a
/// <see cref="VideoExportFailure.FrameUnreadable"/> failure): it cannot leave a frame out without changing the clip's timing.
/// </remarks>
public sealed class PhotoFileSource(IReadOnlyList<string> paths, FitMode fit = FitMode.Fill) : IFrameSource
{
    private readonly IReadOnlyList<string> _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    /// <inheritdoc/>
    public int FrameCount => _paths.Count;

    /// <summary>How the frames are drawn.</summary>
    public FitMode Fit { get; } = Enum.IsDefined(fit) ? fit : throw new ArgumentOutOfRangeException(nameof(fit), fit, "Unknown fit mode.");

    /// <inheritdoc/>
    public string? SourcePath(int index) => _paths[index];

    /// <inheritdoc/>
    public Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken) =>
        // Task.Run: the first awaits complete synchronously often enough that the caller would decode inline.
        Task.Run(() => DecodeAsync(index, device, outputSize, cancellationToken), cancellationToken);

    private async Task<CanvasBitmap> DecodeAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken)
    {
        // The device's largest bitmap bounds a photo far narrower than the output's aspect (a 30,000 px panorama).
        var options = FrameRenderer.DecodeOptions(Fit, outputSize) with { MaxSide = device.MaximumBitmapSizeInPixels };
        var result = await PhotoDecoder.DecodeAsync(_paths[index], options, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new PhotoDecodeException(result.Failure, index);
        }

        using var photo = result.Value;
        return CanvasBitmap.CreateFromSoftwareBitmap(device, photo.Pixels);
    }
}
