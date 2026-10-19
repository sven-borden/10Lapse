using System.Collections.Immutable;
using TenLapse.Core.Projects;

namespace TenLapse.Core.Preview;

/// <summary>How good a frame's preview image is.</summary>
public enum FrameImageQuality
{
    /// <summary>No image.</summary>
    None = 0,

    /// <summary>A small, fast decode of the photo (480 px on the longer side), shown scaled up while its proxy is not built yet.</summary>
    Quick = 1,

    /// <summary>The proxy (about 1280 px): what the preview shows once the cache has it.</summary>
    Proxy = 2,
}

/// <summary>
/// Where the preview's frame images come from (#19). Media implements it with the proxy cache on disk, WIC and Win2D
/// (<c>PreviewImageSource</c>); <see cref="FrameImageCache{TImage}"/> schedules it; the tests fake it. Like
/// <see cref="Import.IPhotoImportSource"/>, it lives in Core so Media can implement it and the scheduling can be tested
/// without a GPU. Every member may be called from any thread, several at once.
/// </summary>
/// <typeparam name="TImage">The drawable image (a Win2D <c>CanvasBitmap</c> in the app).</typeparam>
public interface IFrameImageSource<TImage>
    where TImage : class, IDisposable
{
    /// <summary>
    /// True when the frame's proxy is known to be on disk, so a <see cref="FrameImageQuality.Proxy"/> load is a fast
    /// decode of a small file. A hint for scheduling: it must answer at once (no file access), and may be wrong.
    /// </summary>
    bool HasProxy(Frame frame);

    /// <summary>
    /// Loads <paramref name="frame"/>'s image. <see cref="FrameImageQuality.Proxy"/> decodes the proxy, building it
    /// from the photo first when it is missing or stale (the photo's size or time changed);
    /// <see cref="FrameImageQuality.Quick"/> decodes the photo small (or the proxy, when it already exists). Never
    /// throws for a photo that cannot be read: that is <see cref="FrameImageResult{TImage}.Failure"/>. A proxy that cannot
    /// be written still gives the image, with <see cref="FrameImageResult{TImage}.CacheFailure"/> set; a damaged proxy that
    /// is built again and saved is no failure at all.
    /// </summary>
    Task<FrameImageResult<TImage>> LoadAsync(Frame frame, FrameImageQuality quality, CancellationToken cancellationToken);

    /// <summary>
    /// Builds <paramref name="frame"/>'s proxy when it is missing or stale, without keeping an image (the background
    /// pass). Never throws for a photo that cannot be read or a proxy that cannot be written; says so in the result.
    /// </summary>
    Task<FrameProxyResult> EnsureProxyAsync(Frame frame, CancellationToken cancellationToken);

    /// <summary>
    /// The frames of the project being previewed changed; the source may protect their proxies from eviction and clean
    /// up the rest. Called by the cache on its caller's thread; must return quickly.
    /// </summary>
    void FramesChanged(ImmutableArray<Frame> frames);

    /// <summary>
    /// True when the photo on disk is no longer the one the image of <paramref name="version"/>
    /// (<see cref="FrameImageResult{TImage}.Version"/>) was loaded from: edited or replaced since, or gone. One cheap
    /// stat of the photo; the cache asks it for the frame on screen while paused, never under its lock.
    /// </summary>
    bool IsStale(Frame frame, string version);
}

/// <summary>A frame that cannot be shown: the photo as the skipped-files list would name it, and what was thrown, for the log.</summary>
/// <param name="File">The photo, why it failed (<see cref="SkippedFile.Reason"/>) and the detail for the log.</param>
/// <param name="Exception">The exception behind it, if any.</param>
public sealed record FrameLoadFailure(SkippedFile File, Exception? Exception = null);

/// <summary>What <see cref="IFrameImageSource{TImage}.LoadAsync"/> gave.</summary>
public sealed class FrameImageResult<TImage>
    where TImage : class, IDisposable
{
    private FrameImageResult(TImage? image, FrameImageQuality quality, long bytes, FrameLoadFailure? failure, Exception? cacheFailure, string? version)
    {
        Image = image;
        Quality = quality;
        Bytes = bytes;
        Failure = failure;
        CacheFailure = cacheFailure;
        Version = version;
    }

    /// <summary>The image; null when the frame failed. The receiver owns it.</summary>
    public TImage? Image { get; }

    /// <summary>How good <see cref="Image"/> is.</summary>
    public FrameImageQuality Quality { get; }

    /// <summary>The memory the image holds, for the cache's budget.</summary>
    public long Bytes { get; }

    /// <summary>Why the frame cannot be shown; null when it loaded.</summary>
    public FrameLoadFailure? Failure { get; }

    /// <summary>The proxy could not be written (the image came from the photo instead); null when all was well.</summary>
    public Exception? CacheFailure { get; }

    /// <summary>
    /// Which state of the photo the image shows (the app uses the proxy key: path, size and last write time), for
    /// <see cref="IFrameImageSource{TImage}.IsStale"/>; null when the source cannot tell.
    /// </summary>
    public string? Version { get; }

    /// <summary>A loaded image.</summary>
    public static FrameImageResult<TImage> Loaded(TImage image, FrameImageQuality quality, long bytes, Exception? cacheFailure = null, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (quality == FrameImageQuality.None || !Enum.IsDefined(quality))
        {
            throw new ArgumentOutOfRangeException(nameof(quality), quality, "A loaded image has a quality.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        return new FrameImageResult<TImage>(image, quality, bytes, null, cacheFailure, version);
    }

    /// <summary>A frame that cannot be shown.</summary>
    public static FrameImageResult<TImage> Failed(FrameLoadFailure failure) =>
        new(null, FrameImageQuality.None, 0, failure ?? throw new ArgumentNullException(nameof(failure)), null, null);
}

/// <summary>What <see cref="IFrameImageSource{TImage}.EnsureProxyAsync"/> gave.</summary>
/// <param name="Failure">The photo cannot be read; null otherwise.</param>
/// <param name="CacheFailure">The proxy could not be written; null otherwise.</param>
public sealed record FrameProxyResult(FrameLoadFailure? Failure = null, Exception? CacheFailure = null)
{
    /// <summary>The proxy is there.</summary>
    public static FrameProxyResult Ready { get; } = new();

    /// <summary>True when the proxy is on disk now.</summary>
    public bool Succeeded => Failure is null && CacheFailure is null;
}
