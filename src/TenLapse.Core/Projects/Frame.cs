namespace TenLapse.Core.Projects;

/// <summary>
/// One photo in a project: where it lives on disk, the file facts that invalidate cached proxies and
/// thumbnails, and what import learned about it. Immutable; <c>with</c> keeps the <see cref="Id"/>, so
/// import can fill <see cref="PixelSize"/> and <see cref="CaptureTime"/> later without changing identity.
/// </summary>
public sealed record Frame
{
    private const string LongPathPrefix = @"\\?\";
    private const string LongUncPrefix = @"\\?\UNC\";

    /// <summary>Creates a frame with a new <see cref="Id"/>.</summary>
    /// <param name="sourcePath">
    /// The photo's fully qualified path (<see cref="Path.IsPathFullyQualified(string)"/>). It is stored in its
    /// canonical spelling (see <see cref="NormalizePath"/>) and is never written to or deleted.
    /// </param>
    /// <param name="fileSize">The file size in bytes when it was read.</param>
    /// <param name="lastWriteTimeUtc">The file's last write time; a local time is converted to UTC.</param>
    /// <exception cref="ArgumentException">The path is empty, relative, drive-relative or rooted without a drive.</exception>
    public Frame(string sourcePath, long fileSize, DateTime lastWriteTimeUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);

        Id = Guid.NewGuid();
        SourcePath = NormalizePath(sourcePath);
        FileSize = fileSize;
        LastWriteTimeUtc = ToUtc(lastWriteTimeUtc);
    }

    /// <summary>Stable identity for selection, undo and caches keyed per frame. Unique across projects.</summary>
    public Guid Id { get; }

    /// <summary>The photo's full path, canonical (see <see cref="NormalizePath"/>).</summary>
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

    /// <summary>
    /// The canonical spelling of a photo path, so one file has one spelling and the project's duplicate check
    /// (case-insensitive) catches every way of writing it. The path must be fully qualified. Then:
    /// <list type="bullet">
    /// <item>A <c>\\?\</c> (or <c>\\?\UNC\</c>) prefix is removed when it is redundant: when the path without
    /// it already normalizes to itself.</item>
    /// <item><see cref="Path.GetFullPath(string)"/> does the rest, as Windows itself resolves a path:
    /// <c>/</c> becomes <c>\</c>, <c>.</c> and <c>..</c> segments are resolved, repeated separators collapse,
    /// and trailing spaces and dots of the <em>last</em> segment are trimmed (Win32 trims them when it opens
    /// the file, so <c>IMG_0001.jpg </c> opens <c>IMG_0001.jpg</c>). Spaces inside or before a separator stay.</item>
    /// <item>A <c>\\?\</c> path that normalization would change (a name that ends in a space or a dot, which
    /// only the prefix can reach) is kept exactly as given: it names a different file.</item>
    /// </list>
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty, relative, drive-relative or rooted without a drive.</exception>
    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"A photo path must be fully qualified (\"C:\\Photos\\IMG_0001.jpg\"), not \"{path}\".", nameof(path));
        }

        var plain = path.StartsWith(LongUncPrefix, StringComparison.OrdinalIgnoreCase)
            ? @"\\" + path[LongUncPrefix.Length..]
            : path.StartsWith(LongPathPrefix, StringComparison.Ordinal) ? path[LongPathPrefix.Length..] : path;
        if (!ReferenceEquals(plain, path))
        {
            return Path.IsPathFullyQualified(plain) && string.Equals(Path.GetFullPath(plain), plain, StringComparison.Ordinal)
                ? plain
                : path;
        }

        return Path.GetFullPath(path);
    }

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
