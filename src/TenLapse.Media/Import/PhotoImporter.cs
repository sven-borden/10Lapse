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

    // A camera folder nests a few levels at most. Links are not followed and walked folders are not walked again; the
    // cap is the last stop against anything else that nests without end.
    private const int MaxFolderDepth = 32;

    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS, FILE_ATTRIBUTE_RECALL_ON_OPEN (no FileAttributes names) and Offline.
    private const FileAttributes CloudOnlyAttributes = (FileAttributes)0x400000 | (FileAttributes)0x40000 | FileAttributes.Offline;

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

    /// <summary>
    /// The attributes the listing sees for a file, given its path and what the file system reported. Tests mark files
    /// as OneDrive placeholders with it (real ones cannot be made without OneDrive); the importer takes them as they are.
    /// </summary>
    internal Func<string, FileAttributes, FileAttributes> Attributes { get; init; } = static (_, attributes) => attributes;

    /// <summary>
    /// True for an online-only cloud file (OneDrive files on demand): opening or reading it downloads it. Windows marks
    /// those as recall on data access (0x400000), recall on open (0x40000) or offline (0x1000). Reading only the listing's
    /// attributes downloads nothing.
    /// </summary>
    public static bool IsCloudOnly(FileAttributes attributes) => (attributes & CloudOnlyAttributes) != 0;

    /// <inheritdoc />
    public bool IsPhotoFile(string path) => !string.IsNullOrEmpty(path) && _isPhoto(path);

    /// <inheritdoc />
    public async IAsyncEnumerable<ImportListBatch> ListAsync(ImportRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Off the caller's thread (the UI thread) from here on, whatever its synchronization context.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        var frames = new List<Frame>(_firstListBatch);
        var listing = new Listing();
        var limit = _firstListBatch;
        foreach (var group in Groups(request, listing, cancellationToken))
        {
            foreach (var file in group)
            {
                if (file.CloudOnly)
                {
                    // Kept apart: reading it would download it, so the import asks first.
                    listing.CloudOnly.Add(new Frame(file.Path, file.Length, file.LastWriteTimeUtc));
                    continue;
                }

                frames.Add(new Frame(file.Path, file.Length, file.LastWriteTimeUtc));
                if (frames.Count >= limit)
                {
                    yield return listing.Take(frames);
                    frames = new List<Frame>(_listBatch);
                    limit = _listBatch;
                }
            }
        }

        if (frames.Count > 0 || listing.HasNews)
        {
            yield return listing.Take(frames);
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
    private IEnumerable<List<ListedFile>> Groups(ImportRequest request, Listing listing, CancellationToken cancellationToken)
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

                if (Listed(path, listing.Skipped) is { } file)
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
                listing.Unopened.Add(new UnopenedPath(path, "The file or folder is not there any more."));
            }
        }

        foreach (var folder in looseOrder)
        {
            yield return PairAndSort(looseFiles[folder], IsReadable);
        }

        // Every folder walked, by its final path, across the whole request: a second guard after the links are left
        // out, against two requested folders that overlap or a link that still slips through (Listing.Visited).
        foreach (var folder in folders)
        {
            foreach (var group in Folder(folder, request.IncludeSubfolders, listing, cancellationToken))
            {
                yield return group;
            }
        }
    }

    // The photos of one folder and, when asked, of its subfolders, depth first in name order. The folder the user
    // chose must open (a refusal is reported as unopened); a subfolder that does not open is passed over. Junctions and
    // symbolic links to folders are never followed: one pointing back up would list the same photos again and again.
    private IEnumerable<List<ListedFile>> Folder(string root, bool includeSubfolders, Listing listing, CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Path, string FinalPath, int Depth)>();
        pending.Push((root, FinalPath(root), 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (folder, finalPath, depth) = pending.Pop();
            if (!listing.Visited.Add(finalPath))
            {
                continue;
            }

            var isRoot = depth == 0;
            var walkSubfolders = includeSubfolders && depth < MaxFolderDepth;
            List<ListedFile> files = [];
            List<string> subfolders = [];
            var refused = false;
            try
            {
                foreach (var entry in Entries(folder, ignoreInaccessible: !isRoot, walkSubfolders))
                {
                    if (!entry.IsDirectory)
                    {
                        files.Add(new ListedFile(entry.Path, entry.Length, entry.LastWriteTimeUtc, IsCloudOnly(Attributes(entry.Path, entry.Attributes))));
                    }
                    else if (!IsLink(entry))
                    {
                        subfolders.Add(entry.Path);
                    }
                }
            }
            catch (Exception exception) when (isRoot && exception is IOException or UnauthorizedAccessException)
            {
                // Not a photo: the import names the folder in an InfoBar of its own.
                listing.Unopened.Add(new UnopenedPath(root, exception.Message));
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
                pending.Push((subfolder, Path.Join(finalPath, Path.GetFileName(subfolder)), depth + 1));
            }
        }
    }

    // One pass over a folder: its photo files and, when asked, its subfolders. Hidden and system entries are left out
    // (the enumeration's default, kept explicit).
    private IEnumerable<ListedEntry> Entries(string folder, bool ignoreInaccessible, bool includeSubfolders)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = ignoreInaccessible,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            RecurseSubdirectories = false,
        };
        return new FileSystemEnumerable<ListedEntry>(
            folder,
            (ref FileSystemEntry entry) => new ListedEntry(entry.ToFullPath(), entry.IsDirectory, entry.Length, entry.LastWriteTimeUtc.UtcDateTime, entry.Attributes),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => entry.IsDirectory ? includeSubfolders : _isPhoto(entry.FileName.ToString()),
        };
    }

    // A junction or a symbolic link to a folder. Other reparse points stay in the walk: a OneDrive folder can be one
    // too (its cloud tag) and holds real photos; .NET reports a link target only for the name-surrogate kinds.
    private static bool IsLink(ListedEntry entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        try
        {
            return new DirectoryInfo(entry.Path).LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A reparse point that cannot be asked about is not walked.
            return true;
        }
    }

    // The folder's path with a link at the folder itself resolved, for the visited set.
    private static string FinalPath(string folder)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Directory.ResolveLinkTarget(folder, returnFinalTarget: true)?.FullName ?? folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return folder;
        }
    }

    // A file picked or dropped by itself: its size and write time, or a skipped entry when it cannot be read.
    private ListedFile? Listed(string path, List<SkippedFile> skipped)
    {
        try
        {
            var info = new FileInfo(path);
            return new ListedFile(info.FullName, info.Length, info.LastWriteTimeUtc, IsCloudOnly(Attributes(info.FullName, info.Attributes)));
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
    internal sealed record ListedFile(string Path, long Length, DateTime LastWriteTimeUtc, bool CloudOnly = false);

    /// <summary>One entry of a folder's listing: a photo file or a subfolder, with its attributes.</summary>
    private readonly record struct ListedEntry(string Path, bool IsDirectory, long Length, DateTime LastWriteTimeUtc, FileAttributes Attributes);

    /// <summary>
    /// What one listing has gathered besides the frames: handed out with the next batch, then cleared (the walk keeps
    /// adding to the same lists, so a batch gets copies).
    /// </summary>
    private sealed class Listing
    {
        public List<SkippedFile> Skipped { get; } = [];

        public List<UnopenedPath> Unopened { get; } = [];

        /// <summary>Every folder walked, by its final path.</summary>
        public HashSet<string> Visited { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<Frame> CloudOnly { get; } = [];

        public bool HasNews => Skipped.Count > 0 || Unopened.Count > 0 || CloudOnly.Count > 0;

        public ImportListBatch Take(List<Frame> frames)
        {
            var batch = new ImportListBatch(frames, [.. Skipped]) { Unopened = [.. Unopened], CloudOnly = [.. CloudOnly] };
            Skipped.Clear();
            Unopened.Clear();
            CloudOnly.Clear();
            return batch;
        }
    }
}
