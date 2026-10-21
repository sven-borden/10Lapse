using System.Collections.Immutable;
using System.Text;

namespace TenLapse.Core.Projects;

/// <summary>A photo file's size and last write time, as the disk has them now.</summary>
/// <param name="Size">The size in bytes.</param>
/// <param name="LastWriteTimeUtc">The last write time, UTC.</param>
public readonly record struct FileFacts(long Size, DateTime LastWriteTimeUtc);

/// <summary>The disk as opening and saving a project (#52) see it; a fake in tests.</summary>
public interface IProjectFileSystem
{
    /// <summary>
    /// Reads a project file's text. Throws when it cannot be read (gone, locked, no access: <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/>), and <see cref="InvalidDataException"/> when it is larger than
    /// <see cref="ProjectFileJson.MaxFileBytes"/>.
    /// </summary>
    string ReadText(string path);

    /// <summary>Writes a project file atomically (<see cref="IO.AtomicFile"/>). Throws when the write fails; the old file is then unchanged.</summary>
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
    public void WriteText(string path, string text) => IO.AtomicFile.WriteAllText(path, text);

    /// <inheritdoc />
    public FileFacts? Probe(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? new FileFacts(file.Length, file.LastWriteTimeUtc) : null;
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
public sealed record ResolvedFrames(ImmutableArray<Frame> Frames, ImmutableArray<string> Missing, int Changed, int FoundBesideProject);

/// <summary>Finds a project file's photos on disk (#52).</summary>
public static class SavedFrames
{
    /// <summary>
    /// Looks each saved frame up, in order: first by its path relative to <paramref name="projectFilePath"/>'s folder (so a
    /// copy or a move of the project folder together with its photos opens the photos beside it), then by its saved full
    /// path (so a project file moved away from its photos still finds them). A photo found is a new <see cref="Frame"/>
    /// with the disk's current size and write time; its saved pixel size and capture time are kept even when the file
    /// changed (an edit in place rarely changes either; the decoder reads the real size when it draws). A photo not found
    /// is listed in <see cref="ResolvedFrames.Missing"/>: missing photos never stop a project from opening.
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
        var changed = 0;
        var beside = 0;
        foreach (var saved in content.Frames)
        {
            var besideProject = folder is not null && saved.RelativePath is { } relative ? Full(Path.Combine(folder, relative)) : null;
            var (path, facts) = Find(besideProject, disk);
            var foundBeside = facts is not null && !string.Equals(path, saved.Path, StringComparison.OrdinalIgnoreCase);
            if (facts is null && saved.Path is not null)
            {
                (path, facts) = Find(saved.Path, disk);
                foundBeside = false;
            }

            if (facts is not { } found || path is null || Create(path, found, saved) is not { } frame)
            {
                missing.Add(saved.Path ?? besideProject ?? saved.RelativePath!);
                continue;
            }

            if (saved.FileSize != found.Size || saved.LastWriteTimeUtc != found.LastWriteTimeUtc)
            {
                changed++;
            }

            if (foundBeside)
            {
                beside++;
            }

            frames.Add(frame);
        }

        return new ResolvedFrames(frames.ToImmutable(), missing.ToImmutable(), changed, beside);
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

    private static Frame? Create(string path, FileFacts facts, SavedFrame saved)
    {
        try
        {
            return new Frame(path, facts.Size, facts.LastWriteTimeUtc)
            {
                PixelSize = saved.PixelSize,
                CaptureTime = saved.CaptureTime,
                CaptureUtcOffset = saved.CaptureUtcOffset,
            };
        }
        catch (ArgumentException)
        {
            // Not a path a frame can have (relative, or not a file path at all): as good as missing.
            return null;
        }
    }
}
