using Microsoft.Graphics.Canvas;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TenLapse.Media.Export;

/// <summary>
/// Frames from photo files on disk, decoded with <see cref="BitmapDecoder"/>
/// (WIC) on the CPU, then uploaded to the GPU. EXIF orientation is applied.
/// </summary>
/// <param name="paths">Absolute paths, one per frame, in frame order.</param>
public sealed class PhotoFileSource(IReadOnlyList<string> paths) : IFrameSource
{
    private readonly IReadOnlyList<string> _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    /// <inheritdoc/>
    public int FrameCount => _paths.Count;

    /// <inheritdoc/>
    public Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, CancellationToken cancellationToken) =>
        // Task.Run: the first awaits complete synchronously often enough that the caller would decode inline.
        Task.Run(() => DecodeAsync(_paths[index], device, cancellationToken), cancellationToken);

    private static async Task<CanvasBitmap> DecodeAsync(string path, CanvasDevice device, CancellationToken cancellationToken)
    {
        using var stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read).AsTask(cancellationToken).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        using var pixels = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        return CanvasBitmap.CreateFromSoftwareBitmap(device, pixels);
    }
}
