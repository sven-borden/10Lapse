using System.Collections.Immutable;

namespace TenLapse.Core.Projects;

/// <summary>
/// One photo as a project file (#52) remembers it: where it was (a full path, and the same path relative to the project
/// file, so a folder moved together with its project still opens), the file facts it had, and what import learned about it.
/// Only <see cref="Path"/> or <see cref="RelativePath"/> is required; everything else may be missing in a hand-made file.
/// </summary>
/// <param name="Path">The photo's full path when it was saved, or null when the file gave only a relative one.</param>
public sealed record SavedFrame(string? Path)
{
    /// <summary>The photo's path relative to the project file's folder, or null when it is on another drive (or unknown).</summary>
    public string? RelativePath { get; init; }

    /// <summary>The file size in bytes when it was saved.</summary>
    public long? FileSize { get; init; }

    /// <summary>The file's last write time (UTC) when it was saved.</summary>
    public DateTime? LastWriteTimeUtc { get; init; }

    /// <summary>The decoded size after EXIF orientation (<see cref="Frame.PixelSize"/>).</summary>
    public PixelSize? PixelSize { get; init; }

    /// <summary>The EXIF capture time, wall clock (<see cref="Frame.CaptureTime"/>).</summary>
    public DateTime? CaptureTime { get; init; }

    /// <summary>The camera's offset from UTC (<see cref="Frame.CaptureUtcOffset"/>).</summary>
    public TimeSpan? CaptureUtcOffset { get; init; }

    /// <summary>The exposure pass's measurement (<see cref="Frame.Exposure"/>, #45), kept so a reopened project is not measured again.</summary>
    public PhotoExposure? Exposure { get; init; }

    /// <summary>
    /// <paramref name="frame"/> as a project file in <paramref name="projectFolder"/> stores it: its full path, and the
    /// path relative to that folder when both are on the same drive (a share counts as a drive).
    /// </summary>
    public static SavedFrame From(Frame frame, string? projectFolder)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new SavedFrame(frame.SourcePath)
        {
            RelativePath = Relative(frame.SourcePath, projectFolder),
            FileSize = frame.FileSize,
            LastWriteTimeUtc = frame.LastWriteTimeUtc,
            PixelSize = frame.PixelSize,
            CaptureTime = frame.CaptureTime,
            CaptureUtcOffset = frame.CaptureUtcOffset,
            Exposure = frame.Exposure,
        };
    }

    /// <summary>This entry as a project file in <paramref name="projectFolder"/> stores it: its relative path taken again from its full one.</summary>
    internal SavedFrame RebasedTo(string? projectFolder) =>
        Path is { } full ? this with { RelativePath = Relative(full, projectFolder) } : this;

    private static string? Relative(string path, string? folder)
    {
        if (string.IsNullOrEmpty(folder)
            || !string.Equals(System.IO.Path.GetPathRoot(path), System.IO.Path.GetPathRoot(folder), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = System.IO.Path.GetRelativePath(folder, path);
        return System.IO.Path.IsPathRooted(relative) ? null : relative;
    }
}

/// <summary>
/// Everything a <c>.10lapse</c> file holds (#52): the frames in project order and every project setting. Removed frames
/// are simply absent (removing never touches the disk); the files import skipped are not kept. The project's name is the
/// file's name, so it is not stored, except in a recovery copy (<see cref="RecoveredName"/>), which has no file name of
/// its own. <see cref="ProjectFileJson"/> writes and reads it.
/// </summary>
/// <remarks>
/// <b>Adding a settings group</b> (every 3.1 feature): a property here with its type's default (what a file from before
/// the feature means: the feature off), a line in <see cref="From"/> and <see cref="CreateEmptyProject"/>, a writer and a
/// reader in <see cref="ProjectFileJson"/>, and a case in <c>ProjectFileJsonTests</c> (copy
/// <c>TheTimingGroupSurvivesAWriteAndARead</c>). <c>ProjectFileJsonTests.EverySnapshotSettingIsStored</c> fails until the
/// new <see cref="ProjectSnapshot"/> property is listed in <see cref="ProjectFileJson.StoredSnapshotProperties"/>.
/// </remarks>
public sealed record ProjectFileContent
{
    /// <summary>The frames in playback order.</summary>
    public ImmutableArray<SavedFrame> Frames { get; init; } = [];

    /// <summary>The timing group (<see cref="ProjectSnapshot.Timing"/>).</summary>
    public TimingSettings Timing { get; init; } = TimingSettings.Default;

    /// <summary>The frame group (<see cref="ProjectSnapshot.FrameSettings"/>).</summary>
    public FrameSettings Frame { get; init; } = FrameSettings.Default;

    /// <summary>The output group (<see cref="ProjectSnapshot.Output"/>): the preference, never the effective output.</summary>
    public OutputSettings Output { get; init; } = OutputSettings.Default;

    /// <summary>The order the user chose (<see cref="ProjectSnapshot.SortMode"/>). The frames are stored in that order already.</summary>
    public SortMode SortMode { get; init; } = SortMode.CaptureTime;

    /// <summary>The sequence group (<see cref="ProjectSnapshot.Sequence"/>, #49): a file from before it plays the photos once, forward, with no holds.</summary>
    public SequenceSettings Sequence { get; init; } = SequenceSettings.Default;
    /// <summary>The look group (<see cref="ProjectSnapshot.Look"/>, #45). A file from before #45 has none: deflicker off.</summary>
    public LookSettings Look { get; init; } = LookSettings.Default;

    /// <summary>A recovery copy only: the project's name, which a project that was never saved has no file to take it from.</summary>
    public string? RecoveredName { get; init; }

    /// <summary>A recovery copy only: the <c>.10lapse</c> file the project belonged to, or null when it was never saved.</summary>
    public string? RecoveredFilePath { get; init; }

    /// <summary>
    /// The content of <paramref name="snapshot"/>, with each frame's path relative to <paramref name="projectFilePath"/>'s
    /// folder as well (null for none, as in a recovery copy of a project that was never saved).
    /// </summary>
    public static ProjectFileContent From(ProjectSnapshot snapshot, string? projectFilePath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var folder = string.IsNullOrEmpty(projectFilePath) ? null : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectFilePath));
        return new ProjectFileContent
        {
            Frames = [.. snapshot.Frames.Select(frame => SavedFrame.From(frame, folder))],
            Timing = snapshot.Timing,
            Frame = snapshot.FrameSettings,
            Output = snapshot.Output,
            SortMode = snapshot.SortMode,
            Sequence = snapshot.Sequence,
            Look = snapshot.Look,
        };
    }

    /// <summary>
    /// A recovery copy of <paramref name="snapshot"/> (#52): its content, its name and its file (if it has one), with the
    /// relative paths taken against that file.
    /// </summary>
    public static ProjectFileContent Recovery(ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return From(snapshot, snapshot.FilePath) with { RecoveredName = snapshot.Name, RecoveredFilePath = snapshot.FilePath };
    }

    /// <summary>
    /// This content (made by <see cref="From"/> from a snapshot whose frames are <paramref name="frames"/>) with the frames
    /// an opened file could not find written back in (#52): each after the frame it followed in the file, or, when the user
    /// removed that frame, at its old place in the order (clamped). One whose photo is in the project again (imported
    /// since) is not written twice. Their relative paths are taken against <paramref name="projectFilePath"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="frames"/> are not this content's frames.</exception>
    public ProjectFileContent Carrying(IReadOnlyList<UnresolvedFrame> unresolved, IReadOnlyList<Frame> frames, string? projectFilePath)
    {
        ArgumentNullException.ThrowIfNull(unresolved);
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count != Frames.Length)
        {
            throw new ArgumentException("The frames are not this content's frames.", nameof(frames));
        }

        if (unresolved.Count == 0)
        {
            return this;
        }

        var folder = string.IsNullOrEmpty(projectFilePath) ? null : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectFilePath));
        var present = new HashSet<string>(frames.Select(frame => frame.SourcePath), StringComparer.OrdinalIgnoreCase);
        var ids = frames.Select(frame => frame.Id).ToHashSet();
        var carried = unresolved
            .Where(item => item.Entry.Path is not { } path || !present.Contains(path))
            .OrderBy(item => item.Index)
            .ToList();

        // Guid.Empty stands for "first": no frame has it.
        var after = carried.Where(item => item.After is not { } id || ids.Contains(id)).ToLookup(item => item.After ?? Guid.Empty);
        var written = new List<SavedFrame>(Frames.Length + carried.Count);
        written.AddRange(after[Guid.Empty].Select(item => item.Entry.RebasedTo(folder)));
        for (var i = 0; i < frames.Count; i++)
        {
            written.Add(Frames[i]);
            written.AddRange(after[frames[i].Id].Select(item => item.Entry.RebasedTo(folder)));
        }

        // The frame it followed was removed: never dropped, it goes back near its old place.
        foreach (var item in carried.Where(item => item.After is { } id && !ids.Contains(id)))
        {
            written.Insert(Math.Min(item.Index, written.Count), item.Entry.RebasedTo(folder));
        }

        return this with { Frames = [.. written] };
    }

    /// <summary>A new, empty, clean project with these settings, owned by the calling thread; the caller adds the frames.</summary>
    public Project CreateEmptyProject() => new(Timing, Frame, Output, SortMode, Sequence, Look);
}
