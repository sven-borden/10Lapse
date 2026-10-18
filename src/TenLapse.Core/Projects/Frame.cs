namespace TenLapse.Core.Projects;

/// <summary>
/// One photo in a project: where it lives on disk, the file facts that invalidate cached proxies and
/// thumbnails, and what import learned about it. Immutable; <c>with</c> keeps the <see cref="Id"/>, so
/// import can fill <see cref="PixelSize"/> and <see cref="CaptureTime"/> later without changing identity.
/// </summary>
public sealed record Frame
{
    /// <summary>Creates a frame with a new <see cref="Id"/>.</summary>
    /// <param name="sourcePath">The photo's full path. It is never written to or deleted.</param>
    /// <param name="fileSize">The file size in bytes when it was read.</param>
    /// <param name="lastWriteTimeUtc">The file's last write time; a local time is converted to UTC.</param>
    public Frame(string sourcePath, long fileSize, DateTime lastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);

        Id = Guid.NewGuid();
        SourcePath = sourcePath;
        FileSize = fileSize;
        LastWriteTimeUtc = ToUtc(lastWriteTimeUtc);
    }

    /// <summary>Stable identity for selection, undo and caches keyed per frame. Unique across projects.</summary>
    public Guid Id { get; }

    /// <summary>The photo's full path.</summary>
    public string SourcePath { get; }

    /// <summary>The file size in bytes when the frame was created; with <see cref="LastWriteTimeUtc"/> it invalidates caches.</summary>
    public long FileSize { get; }

    /// <summary>The file's last write time in UTC when the frame was created.</summary>
    public DateTime LastWriteTimeUtc { get; }

    /// <summary>The decoded size after EXIF orientation, when known (#13).</summary>
    public PixelSize? PixelSize { get; init; }

    /// <summary>
    /// When the photo was taken (EXIF <c>DateTimeOriginal</c> plus sub-seconds, #15), when known. EXIF stores
    /// a camera wall-clock time without a zone, so the kind is <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    public DateTime? CaptureTime { get; init; }

    /// <summary>The file name with its extension, for the name sort and the filmstrip.</summary>
    public string FileName => Path.GetFileName(SourcePath);

    /// <summary>True when the file on disk still has the size and write time this frame recorded.</summary>
    public bool Matches(long fileSize, DateTime lastWriteTimeUtc) =>
        FileSize == fileSize && LastWriteTimeUtc == ToUtc(lastWriteTimeUtc);

    // File.GetLastWriteTimeUtc returns Utc; an Unspecified time is taken as UTC too (not as local, which
    // ToUniversalTime would assume), so the same value always compares equal.
    private static DateTime ToUtc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Local => time.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        _ => time,
    };
}

/// <summary>A photo's size in pixels. Both sides are positive.</summary>
public readonly record struct PixelSize
{
    /// <summary>Creates a size; both sides must be positive.</summary>
    public PixelSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
    }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>True when the photo is taller than it is wide (#18 suggests 9:16 for a mostly portrait set).</summary>
    public bool IsPortrait => Height > Width;
}
