using System.Collections.Immutable;
using System.Text;

namespace TenLapse.Core.Projects;

/// <summary>A photo file's size and last write time, as the disk has them now.</summary>
/// <param name="Size">The size in bytes (for an online-only OneDrive file, the size it will download).</param>
/// <param name="LastWriteTimeUtc">The last write time, UTC.</param>
/// <param name="IsCloudOnly">True when OneDrive (or another cloud provider) keeps the file online only: reading it downloads it.</param>
public readonly record struct FileFacts(long Size, DateTime LastWriteTimeUtc, bool IsCloudOnly = false)
{
    /// <summary>
    /// The attributes of a file a cloud provider keeps online only: FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS and
    /// FILE_ATTRIBUTE_RECALL_ON_OPEN (no <see cref="FileAttributes"/> names), and <see cref="FileAttributes.Offline"/>. The
    /// import (Media's PhotoImporter) asks the same.
    /// </summary>
    public const FileAttributes CloudOnlyAttributes = (FileAttributes)0x400000 | (FileAttributes)0x40000 | FileAttributes.Offline;
}

/// <summary>The disk as opening and saving a project (#52) see it; a fake in tests.</summary>
public interface IProjectFileSystem
{
    /// <summary>
    /// Reads a project file's text. Throws when it cannot be read (gone, locked, no access: <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/>), and <see cref="InvalidDataException"/> when it is larger than
    /// <see cref="ProjectFileJson.MaxFileBytes"/>.
    /// </summary>
    string ReadText(string path);

    /// <summary>
    /// Writes a project file atomically (<see cref="IO.AtomicFile"/>). Throws when the write fails; the old file is then
    /// unchanged. A folder that is not there is never made: <see cref="DirectoryNotFoundException"/>.
    /// </summary>
    void WriteText(string path, string text);

    /// <summary>The file's facts, or null when there is no readable file at <paramref name="path"/>. Never throws.</summary>
    FileFacts? Probe(string path);
}

/// <summary>The real disk.</summary>
public sealed class ProjectFileSystem : IProjectFileSystem
{
    /// <summary>The one instance.</summary>
    public static ProjectFileSystem Instance { get; } = new();

    private ProjectFileSystem()
    {
    }

    /// <inheritdoc />
    public string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > ProjectFileJson.MaxFileBytes)
        {
            throw new InvalidDataException($"The file is {stream.Length / (1024 * 1024)} MB, more than a project file can be.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <inheritdoc />
    public void WriteText(string path, string text) => IO.AtomicFile.WriteAllText(path, text, createFolder: false);

    /// <inheritdoc />
    public FileFacts? Probe(string path)
    {
        try
        {
            var file = new FileInfo(path);
            // Reading the attributes downloads nothing.
            return file.Exists ? new FileFacts(file.Length, file.LastWriteTimeUtc, (file.Attributes & FileFacts.CloudOnlyAttributes) != 0) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }
}

/// <summary>What opening a project file's frames found on disk (#52).</summary>
/// <param name="Frames">The photos that are there, in project order, as new frames with the disk's current file facts.</param>
/// <param name="Missing">The photos that are not (their saved full path, else the path they were looked for at), in project order.</param>
/// <param name="Changed">How many photos are there but changed since the save (another size or write time).</param>
/// <param name="FoundBesideProject">How many were found by their path relative to the project file, not by their saved full path.</param>
public sealed record ResolvedFrames(ImmutableArray<Frame> Frames, ImmutableArray<string> Missing, int Changed, int FoundBesideProject)
{
    /// <summary>
    /// The saved frames whose photo was not found, each with its place in the order, so the next save writes them back
    /// (<see cref="ProjectFileContent.Carrying"/>): a photo on a drive that is not connected comes back once it is.
    /// </summary>
    public ImmutableArray<UnresolvedFrame> Unresolved { get; init; } = [];

    /// <summary>
    /// The frames whose size and capture time should be read again from the photo: the file has none, or the photo
    /// changed since the save. Online-only photos are left out: reading them would download them all at once.
    /// </summary>
    public ImmutableArray<Frame> ToRead { get; init; } = [];

    /// <summary>How many of <see cref="Frames"/> a cloud provider keeps online only (they download as they are read).</summary>
    public int CloudOnly { get; init; }

    /// <summary>The size of those online-only photos, in bytes.</summary>
    public long CloudOnlyBytes { get; init; }
}

/// <summary>
/// A frame of a project file whose photo was not found when it was opened (#52): kept so the next save writes it back.
/// </summary>
/// <param name="Entry">The frame as the file has it, with the full path it was looked for at.</param>
/// <param name="Index">Its place in the file's order.</param>
/// <param name="After">The frame found just before it (<see cref="Frame.Id"/>), or null when it came first: it is written back after that frame.</param>
public sealed record UnresolvedFrame(SavedFrame Entry, int Index, Guid? After);

/// <summary>Finds a project file's photos on disk (#52).</summary>
public static class SavedFrames
{
    /// <summary>
    /// Looks each saved frame up, in order: first by its saved full path (the photos the project was made with, even when
    /// a copy of the project file sits beside other photos of the same names), then by its path relative to
    /// <paramref name="projectFilePath"/>'s folder (so a folder moved together with its project still opens). A photo found
    /// is a new <see cref="Frame"/> with the disk's current size and write time; its saved pixel size and capture time are
    /// kept (<see cref="ResolvedFrames.ToRead"/> lists the frames to read again). A photo not found is listed in
    /// <see cref="ResolvedFrames.Missing"/> and kept in <see cref="ResolvedFrames.Unresolved"/>: missing photos never stop
    /// a project from opening, and never leave its file.
    /// </summary>
    /// <param name="content">The project file's content.</param>
    /// <param name="projectFilePath">The project file (or, for a recovery copy, the file it belonged to); null for none.</param>
    /// <param name="disk">Probes the files.</param>
    public static ResolvedFrames Resolve(ProjectFileContent content, string? projectFilePath, IProjectFileSystem disk)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(disk);
        var folder = string.IsNullOrEmpty(projectFilePath) ? null : Path.GetDirectoryName(Path.GetFullPath(projectFilePath));
        var frames = ImmutableArray.CreateBuilder<Frame>(content.Frames.Length);
        var missing = ImmutableArray.CreateBuilder<string>();
        var unresolved = ImmutableArray.CreateBuilder<UnresolvedFrame>();
        var toRead = ImmutableArray.CreateBuilder<Frame>();
        var changed = 0;
        var beside = 0;
        var cloudOnly = 0;
        var cloudOnlyBytes = 0L;
        Guid? previous = null;
        for (var index = 0; index < content.Frames.Length; index++)
        {
            var saved = content.Frames[index];
            var besideProject = folder is not null && saved.RelativePath is { } relative ? Full(Path.Combine(folder, relative)) : null;
            var (path, facts) = Find(saved.Path, disk);
            var foundBeside = false;
            if (facts is null && besideProject is not null && !string.Equals(besideProject, saved.Path, StringComparison.OrdinalIgnoreCase))
            {
                (path, facts) = Find(besideProject, disk);
                foundBeside = facts is not null;
            }

            if (facts is not { } found || path is null || Create(path, found, saved, Changed(saved, found)) is not { } frame)
            {
                var lookedFor = saved.Path ?? besideProject;
                missing.Add(lookedFor ?? saved.RelativePath!);
                unresolved.Add(new UnresolvedFrame(lookedFor is null ? saved : saved with { Path = lookedFor }, index, previous));
                continue;
            }

            var isChanged = Changed(saved, found);
            if (isChanged)
            {
                changed++;
            }

            if (foundBeside)
            {
                beside++;
            }

            if (found.IsCloudOnly)
            {
                cloudOnly++;
                cloudOnlyBytes += found.Size;
            }
            else if (isChanged || frame.PixelSize is null)
            {
                toRead.Add(frame);
            }

            frames.Add(frame);
            previous = frame.Id;
        }

        return new ResolvedFrames(frames.ToImmutable(), missing.ToImmutable(), changed, beside)
        {
            Unresolved = unresolved.ToImmutable(),
            ToRead = toRead.ToImmutable(),
            CloudOnly = cloudOnly,
            CloudOnlyBytes = cloudOnlyBytes,
        };
    }

    private static (string? Path, FileFacts? Facts) Find(string? path, IProjectFileSystem disk) =>
        path is null ? (null, null) : (path, disk.Probe(path));

    private static string? Full(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool Changed(SavedFrame saved, FileFacts found) =>
        saved.FileSize != found.Size || saved.LastWriteTimeUtc != found.LastWriteTimeUtc;

    // A photo changed since the save keeps its size and time until they are read again, but not its exposure (#45): that
    // is measured again from the new pixels.
    private static Frame? Create(string path, FileFacts facts, SavedFrame saved, bool changed)
    {
        try
        {
            return new Frame(path, facts.Size, facts.LastWriteTimeUtc)
            {
                PixelSize = saved.PixelSize,
                CaptureTime = saved.CaptureTime,
                CaptureUtcOffset = saved.CaptureUtcOffset,
                Exposure = changed ? null : saved.Exposure,
            };
        }
        catch (ArgumentException)
        {
            // Not a path a frame can have (relative, or not a file path at all): as good as missing.
            return null;
        }
    }
}
