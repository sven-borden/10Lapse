using System.Diagnostics;
using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TenLapse.Core.Import;
using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;

namespace TenLapse.Media.Import;

/// <summary>
/// The import's file work (#12) on the Windows decoders: lists photos with <see cref="Directory"/>-level enumeration
/// (full trust is far faster than <c>StorageFile</c> for thousands of files), filtered by <see cref="PhotoCodecs"/>,
/// then reads each one's header with <see cref="PhotoDecoder.ReadInfoAsync"/> and its capture time with
/// <see cref="CaptureTimeReader"/>, several files at once. Thread-safe and stateless between calls; see
/// <see cref="IPhotoImportSource"/> for the two phases.
/// </summary>
public sealed class PhotoImporter : IPhotoImportSource
{
    /// <summary>How many files are read at once. The first open of a file costs milliseconds (the virus scanner), the parse microseconds (#15).</summary>
    public const int DefaultReaders = 8;

    // A camera folder nests a few levels at most; the cap only stops a junction that points back up from looping.
    private const int MaxFolderDepth = 32;

    // A read batch is handed out at this size, or sooner once this much time has passed since the last one.
    private static readonly TimeSpan _readBatchInterval = TimeSpan.FromMilliseconds(150);

    private readonly Func<string, bool> _isPhoto;
    private readonly int _readers;
    private readonly int _firstListBatch;
    private readonly int _listBatch;
    private readonly int _readBatch;

    /// <summary>
    /// An importer for the photo formats this PC reads (<see cref="PhotoCodecs.IsReadable"/>) and the known ones it
    /// cannot read yet (<see cref="KnownPhotoFormats"/>: HEIC, RAW without their Store extension), which come back from
    /// reading as <see cref="SkipReason.NeedsExtension"/> so #14 can offer the extension.
    /// </summary>
    public PhotoImporter()
        : this(IsImportable)
    {
    }

    /// <summary>True for a photo format this PC reads, or a known one an extension from the Store would read.</summary>
    public static bool IsImportable(string path) =>
        PhotoCodecs.IsReadable(path) || KnownPhotoFormats.ForPath(path) is not null;

    /// <summary>An importer with its own format filter and batch sizes (tests).</summary>
    /// <param name="isPhoto">True for the file paths to import.</param>
    /// <param name="readers">How many files are read at once.</param>
    /// <param name="firstListBatch">The size of the first listing batch: small, so the first frames show at once.</param>
    /// <param name="listBatch">The size of the later listing batches.</param>
    /// <param name="readBatch">The size of the reading batches (each is one project update on the UI thread).</param>
    public PhotoImporter(Func<string, bool> isPhoto, int readers = DefaultReaders, int firstListBatch = 100, int listBatch = 500, int readBatch = 100)
    {
        ArgumentNullException.ThrowIfNull(isPhoto);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(firstListBatch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(listBatch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readBatch);
        _isPhoto = isPhoto;
        _readers = readers;
        _firstListBatch = firstListBatch;
        _listBatch = listBatch;
        _readBatch = readBatch;
    }

    /// <summary>
    /// True for a file this PC decodes (<see cref="PhotoCodecs.IsReadable"/>; tests pass their own codec set). A RAW file
    /// is left out for its same-named partner only when this is true for the partner.
    /// </summary>
    internal Func<string, bool> IsReadable { get; init; } = PhotoCodecs.IsReadable;

    /// <inheritdoc />
    public bool IsPhotoFile(string path) => !string.IsNullOrEmpty(path) && _isPhoto(path);

    /// <inheritdoc />
    public async IAsyncEnumerable<ImportListBatch> ListAsync(ImportRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Off the caller's thread (the UI thread) from here on, whatever its synchronization context.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        var frames = new List<Frame>(_firstListBatch);
        var skipped = new List<SkippedFile>();
        var limit = _firstListBatch;
        foreach (var group in Groups(request, skipped, cancellationToken))
        {
            foreach (var file in group)
            {
                frames.Add(new Frame(file.Path, file.Length, file.LastWriteTimeUtc));
                if (frames.Count >= limit)
                {
                    yield return new ImportListBatch(frames, [.. skipped]);
                    frames = new List<Frame>(_listBatch);
                    skipped.Clear();
                    limit = _listBatch;
                }
            }
        }

        if (frames.Count > 0 || skipped.Count > 0)
        {
            yield return new ImportListBatch(frames, [.. skipped]);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IReadOnlyList<FrameReading>> ReadAsync(IReadOnlyList<Frame> frames, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            yield break;
        }

        // The readers stop when the caller stops listening, as well as when it cancels.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var results = Channel.CreateUnbounded<FrameReading>(new UnboundedChannelOptions { SingleReader = true });
        var readers = Task.Run(
            async () =>
            {
                try
                {
                    await Parallel.ForEachAsync(
                        frames,
                        new ParallelOptions { MaxDegreeOfParallelism = _readers, CancellationToken = stop.Token },
                        async (frame, token) => results.Writer.TryWrite(await ReadOneAsync(frame, token).ConfigureAwait(false))).ConfigureAwait(false);
                    results.Writer.TryComplete();
                }
                catch (Exception exception)
                {
                    results.Writer.TryComplete(exception);
                }
            },
            CancellationToken.None);

        try
        {
            var batch = new List<FrameReading>(_readBatch);
            var clock = Stopwatch.StartNew();
            while (await results.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (results.Reader.TryRead(out var reading))
                {
                    batch.Add(reading);
                    if (batch.Count >= _readBatch || clock.Elapsed >= _readBatchInterval)
                    {
                        yield return batch;
                        batch = new List<FrameReading>(_readBatch);
                        clock.Restart();
                    }
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await readers.ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Reads one listed frame: its header (size, orientation, a cut-short end), then its capture time.</summary>
    internal static async Task<FrameReading> ReadOneAsync(Frame frame, CancellationToken cancellationToken)
    {
        try
        {
            var info = await PhotoDecoder.ReadInfoAsync(frame.SourcePath, cancellationToken).ConfigureAwait(false);
            if (!info.Succeeded)
            {
                return FrameReading.Failed(frame, info.Failure!.ToSkippedFile());
            }

            var time = CaptureTimeReader.ReadFile(frame.SourcePath);
            return FrameReading.Read(frame with { PixelSize = info.Value.Size, CaptureTime = time?.Time, CaptureUtcOffset = time?.UtcOffset });
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The decoder classifies every file problem itself; anything that still escapes is listed, not thrown.
            return FrameReading.Failed(frame, new SkippedFile(frame.SourcePath, SkipReason.Other, exception.Message));
        }
    }

    /// <summary>
    /// Leaves out each camera RAW file that has a non-RAW photo this PC decodes (the camera's JPEG, a HEIF with its
    /// extension installed) with the same base name in the same folder, so a RAW+JPEG shot is imported once, as the JPEG
    /// (#13 review). A partner this PC cannot decode (a HEIF without its extension) keeps the RAW in. Then sorts by name
    /// the way Explorer does. <paramref name="files"/> all come from one folder.
    /// </summary>
    internal static List<ListedFile> PairAndSort(List<ListedFile> files, Func<string, bool> isReadable)
    {
        var withPhoto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!IsRaw(file.Path) && isReadable(file.Path))
            {
                withPhoto.Add(Path.GetFileNameWithoutExtension(file.Path));
            }
        }

        var kept = withPhoto.Count == 0
            ? files
            : files.FindAll(file => !IsRaw(file.Path) || !withPhoto.Contains(Path.GetFileNameWithoutExtension(file.Path)));
        kept.Sort((a, b) => NaturalNameComparer.Instance.Compare(Path.GetFileName(a.Path), Path.GetFileName(b.Path)));
        return kept;
    }

    private static bool IsRaw(string path) => KnownPhotoFormats.Raw.HasExtension(Path.GetExtension(path));

    // The request's photos, one group per folder: first the files picked or dropped one by one (grouped by their
    // folder, in the order the folders first appear), then each folder and, when asked, its subfolders.
    private IEnumerable<List<ListedFile>> Groups(ImportRequest request, List<SkippedFile> skipped, CancellationToken cancellationToken)
    {
        var looseFiles = new Dictionary<string, List<ListedFile>>(StringComparer.OrdinalIgnoreCase);
        var looseOrder = new List<string>();
        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in request.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(raw) || !Path.IsPathFullyQualified(raw))
            {
                continue;
            }

            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
            if (!seen.Add(path))
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                folders.Add(path);
            }
            else if (File.Exists(path))
            {
                if (!_isPhoto(path))
                {
                    continue;
                }

                if (Listed(path, skipped) is { } file)
                {
                    var folder = Path.GetDirectoryName(path) ?? string.Empty;
                    if (!looseFiles.TryGetValue(folder, out var list))
                    {
                        looseFiles[folder] = list = [];
                        looseOrder.Add(folder);
                    }

                    list.Add(file);
                }
            }
            else
            {
                skipped.Add(new SkippedFile(path, SkipReason.FileNotFound, "The file or folder is not there any more."));
            }
        }

        foreach (var folder in looseOrder)
        {
            yield return PairAndSort(looseFiles[folder], IsReadable);
        }

        foreach (var folder in folders)
        {
            foreach (var group in Folder(folder, request.IncludeSubfolders, skipped, cancellationToken))
            {
                yield return group;
            }
        }
    }

    // The photos of one folder and, when asked, of its subfolders, depth first in name order. The folder the user
    // chose must open (a refusal is listed as skipped); a subfolder that does not open is passed over.
    private IEnumerable<List<ListedFile>> Folder(string root, bool includeSubfolders, List<SkippedFile> skipped, CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (folder, depth) = pending.Pop();
            var isRoot = depth == 0;
            List<ListedFile> files = [];
            List<string> subfolders = [];
            var refused = false;
            try
            {
                files = [.. Files(folder, ignoreInaccessible: !isRoot)];
                if (includeSubfolders && depth < MaxFolderDepth)
                {
                    subfolders = [.. Subfolders(folder)];
                }
            }
            catch (Exception exception) when (isRoot && exception is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new SkippedFile(
                    root,
                    exception is UnauthorizedAccessException ? SkipReason.AccessDenied : SkipReason.FileNotFound,
                    exception.Message));
                refused = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A subfolder that went away or does not open while the walk runs: passed over, like the ones the
                // enumeration itself skips.
                continue;
            }

            if (refused)
            {
                yield break;
            }

            if (files.Count > 0)
            {
                yield return PairAndSort(files, IsReadable);
            }

            // Pushed in reverse so the first by name is walked first.
            subfolders.Sort((a, b) => NaturalNameComparer.Instance.Compare(Path.GetFileName(b), Path.GetFileName(a)));
            foreach (var subfolder in subfolders)
            {
                pending.Push((subfolder, depth + 1));
            }
        }
    }

    // Hidden and system entries are left out (the enumeration's default, kept explicit).
    private IEnumerable<ListedFile> Files(string folder, bool ignoreInaccessible)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = ignoreInaccessible,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            RecurseSubdirectories = false,
        };
        return new FileSystemEnumerable<ListedFile>(
            folder,
            (ref FileSystemEntry entry) => new ListedFile(entry.ToFullPath(), entry.Length, entry.LastWriteTimeUtc.UtcDateTime),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && _isPhoto(entry.FileName.ToString()),
        };
    }

    private static IEnumerable<string> Subfolders(string folder)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            RecurseSubdirectories = false,
        };
        return new FileSystemEnumerable<string>(folder, (ref FileSystemEntry entry) => entry.ToFullPath(), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => entry.IsDirectory,
        };
    }

    // A file picked or dropped by itself: its size and write time, or a skipped entry when it cannot be read.
    private static ListedFile? Listed(string path, List<SkippedFile> skipped)
    {
        try
        {
            var info = new FileInfo(path);
            return new ListedFile(info.FullName, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new SkippedFile(
                path,
                exception is UnauthorizedAccessException ? SkipReason.AccessDenied : SkipReason.FileNotFound,
                exception.Message));
            return null;
        }
    }

    /// <summary>A listed file's path and the facts the frame records.</summary>
    internal sealed record ListedFile(string Path, long Length, DateTime LastWriteTimeUtc);
}
