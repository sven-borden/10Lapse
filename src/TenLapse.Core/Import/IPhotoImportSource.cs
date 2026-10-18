using TenLapse.Core.Projects;

namespace TenLapse.Core.Import;

/// <summary>What to import: files and folders as the user picked or dropped them.</summary>
/// <param name="Paths">
/// Fully qualified paths. A folder is searched for photos (its subfolders too when <paramref name="IncludeSubfolders"/>
/// is on); a file is taken as given when it is a photo format this PC reads, and left out otherwise.
/// </param>
/// <param name="IncludeSubfolders">Search the folders' subfolders too (Add photos, <c>Include subfolders</c>).</param>
public sealed record ImportRequest(IReadOnlyList<string> Paths, bool IncludeSubfolders);

/// <summary>One batch of the listing phase: new frames with their file facts only, and the paths that could not be listed.</summary>
/// <param name="Frames">The photos found, each a new <see cref="Frame"/> with its path, size and last write time.</param>
/// <param name="Skipped">Paths of the request that could not be read (gone, or access denied), with the reason.</param>
public sealed record ImportListBatch(IReadOnlyList<Frame> Frames, IReadOnlyList<SkippedFile> Skipped);

/// <summary>What the reading phase learned about one frame: the frame with its facts filled in, or why it was skipped.</summary>
public sealed record FrameReading
{
    private FrameReading(Frame frame, SkippedFile? skipped)
    {
        Frame = frame;
        Skipped = skipped;
    }

    /// <summary>
    /// The frame (same <see cref="Frame.Id"/> and path as the listed one) with <see cref="Frame.PixelSize"/>,
    /// <see cref="Frame.CaptureTime"/> and <see cref="Frame.CaptureUtcOffset"/> filled in where the file has them;
    /// when <see cref="Skipped"/> is set, the listed frame unchanged.
    /// </summary>
    public Frame Frame { get; }

    /// <summary>Why the file cannot be used (damaged, cut short, unsupported); null when it reads.</summary>
    public SkippedFile? Skipped { get; }

    /// <summary>True when the photo reads and stays in the project.</summary>
    public bool Succeeded => Skipped is null;

    /// <summary>A photo that reads: <paramref name="frame"/> carries what was learned.</summary>
    public static FrameReading Read(Frame frame) => new(frame ?? throw new ArgumentNullException(nameof(frame)), null);

    /// <summary>A photo that cannot be used: it leaves the project and joins the skipped list.</summary>
    public static FrameReading Failed(Frame frame, SkippedFile skipped) =>
        new(frame ?? throw new ArgumentNullException(nameof(frame)), skipped ?? throw new ArgumentNullException(nameof(skipped)));
}

/// <summary>
/// Finds and reads photos for an import (#12), in two phases so the filmstrip fills at once and the details follow:
/// <list type="number">
/// <item><see cref="ListAsync"/> walks the request and hands out frames with their file facts only, the first batch
/// within a fraction of a second.</item>
/// <item><see cref="ReadAsync"/> reads each listed frame's header (pixel size, orientation, a cut-short end) and
/// capture time with several readers in parallel, and hands the results out in batches.</item>
/// </list>
/// Both run off the calling thread and never touch a <see cref="Project"/>: the caller adds and updates the frames on
/// the project's own thread. <c>TenLapse.Media.Import.PhotoImporter</c> implements it with the Windows decoders; it
/// lives in Core so Media can implement it and Presentation can drive it without either knowing the other.
/// </summary>
public interface IPhotoImportSource
{
    /// <summary>
    /// Lists the photos of <paramref name="request"/> in batches, in a stable order (folder by folder, each folder's
    /// files in name order). Hidden and system files and folders are left out, folders Windows does not let 10Lapse
    /// open are passed over, and a camera RAW file with a JPEG (or another non-RAW photo) of the same name in the same
    /// folder is left out so each shot is imported once.
    /// </summary>
    IAsyncEnumerable<ImportListBatch> ListAsync(ImportRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reads <paramref name="frames"/> (listed by <see cref="ListAsync"/>) in parallel and hands each result out once,
    /// in batches, roughly in the order given. Never throws for a file: a file that cannot be used comes back as
    /// <see cref="FrameReading.Failed"/>. Only a cancellation ends it early.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<FrameReading>> ReadAsync(IReadOnlyList<Frame> frames, CancellationToken cancellationToken);

    /// <summary>True when this file's extension is a photo format this PC reads (cheap: the drag caption counts with it).</summary>
    bool IsPhotoFile(string path);
}
