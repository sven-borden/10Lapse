namespace TenLapse.Core.Projects;

/// <summary>
/// The music settings (#50): the audio file under the video, where in it the video starts, and how the music fades in
/// and out. Immutable; a <c>with</c> expression validates too. Project only, like the look and the crop: a new project
/// starts without music.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fitting the video.</b> The music starts with the video's first frame, <see cref="Start"/> into the song, and plays
/// until the video ends or the song does, whichever comes first (<see cref="PlayedLength"/>): it fades in over
/// <see cref="FadeIn"/> from the video's start and out over <see cref="FadeOut"/> up to that end, so a song longer than
/// the video ends with it, softly. A song that ends first leaves the rest of the video silent; it is never looped. Holds,
/// plays and a boomerang are part of the video, so the music runs on through them.
/// </para>
/// <para>
/// <b>Exported only to MP4.</b> A GIF has no sound; the preview plays the music only when the video it shows will have it.
/// </para>
/// </remarks>
public sealed record MusicSettings
{
    /// <summary>The longest fade, in and out: 10 s.</summary>
    public static readonly TimeSpan MaxFade = TimeSpan.FromSeconds(10);

    /// <summary>No music; a 1 s fade in and a 2 s fade out for the first song chosen. A project from before #50 reads as this.</summary>
    public static MusicSettings Default { get; } = new(null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

    /// <summary>Creates music settings.</summary>
    /// <param name="path">The audio file's full path, or null for no music.</param>
    /// <param name="length">The song's length as it was measured when it was chosen (for the panel; the export reads the file).</param>
    /// <param name="start">Where in the song the video starts.</param>
    /// <param name="fadeIn">How long the music takes to rise from silence at the video's start, 0 to <see cref="MaxFade"/>.</param>
    /// <param name="fadeOut">How long it takes to fall to silence where it ends, 0 to <see cref="MaxFade"/>.</param>
    public MusicSettings(string? path, TimeSpan length, TimeSpan start, TimeSpan fadeIn, TimeSpan fadeOut)
    {
        Path = path;
        Length = length;
        Start = start;
        FadeIn = fadeIn;
        FadeOut = fadeOut;
    }

    /// <summary>The audio file's full path, or null for no music.</summary>
    public string? Path
    {
        get;
        init => field = value is null || (value.Length > 0 && !value.Contains('\0') && System.IO.Path.IsPathFullyQualified(value))
            ? value
            : throw new ArgumentException("The music file must be a full path.", nameof(value));
    }

    /// <summary>The song's length when it was chosen; zero when unknown.</summary>
    public TimeSpan Length
    {
        get;
        init => field = value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The length cannot be negative.");
    }

    /// <summary>
    /// Where in the song the video starts. Not limited by <see cref="Length"/>, which may be stale; past the song's end the
    /// video is silent.
    /// </summary>
    public TimeSpan Start
    {
        get;
        init => field = value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The start cannot be negative.");
    }

    /// <summary>The fade in from the video's start, 0 to <see cref="MaxFade"/>.</summary>
    public TimeSpan FadeIn
    {
        get;
        init => field = IsFade(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A fade lasts 0 to 10 s.");
    }

    /// <summary>The fade out up to where the music ends, 0 to <see cref="MaxFade"/>.</summary>
    public TimeSpan FadeOut
    {
        get;
        init => field = IsFade(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A fade lasts 0 to 10 s.");
    }

    /// <summary>True when there is a song to play.</summary>
    public bool HasMusic => Path is not null;

    /// <summary>
    /// How long the music plays, in 100 ns ticks, under a video of <paramref name="videoDuration"/> ticks when the song is
    /// <paramref name="songLength"/> long: up to the video's end, or the song's, whichever comes first. Zero without music.
    /// </summary>
    public long PlayedLength(long videoDuration, long songLength) =>
        HasMusic ? Math.Clamp(songLength - Start.Ticks, 0, Math.Max(0, videoDuration)) : 0;

    /// <summary>
    /// The music's loudness, 0 to 1, at <paramref name="time"/> ticks into the video when it plays for
    /// <paramref name="played"/> ticks (<see cref="PlayedLength"/>): rising over <see cref="FadeIn"/> from the start,
    /// falling over <see cref="FadeOut"/> to the end, silent outside. Each fade is a square curve, which sounds even to
    /// the ear where a straight one seems to drop at its end; fades that overlap in a short video meet below full.
    /// </summary>
    public double GainAt(long time, long played)
    {
        if (time < 0 || time >= played)
        {
            return 0;
        }

        var level = 1.0;
        if (FadeIn.Ticks > 0)
        {
            level = Math.Min(level, (double)time / FadeIn.Ticks);
        }

        if (FadeOut.Ticks > 0)
        {
            level = Math.Min(level, (double)(played - time) / FadeOut.Ticks);
        }

        return level * level;
    }

    /// <summary>Where in the song the video is at <paramref name="time"/> ticks into it.</summary>
    public long SongTimeAt(long time) => Start.Ticks + time;

    private static bool IsFade(TimeSpan value) => value >= TimeSpan.Zero && value <= MaxFade;
}

/// <summary>What the music section reads from an audio file when it is chosen (#50).</summary>
/// <param name="Length">The song's length.</param>
public sealed record MusicFileInfo(TimeSpan Length);

/// <summary>Where a project file's music is found again (#50), as its photos are (<see cref="SavedFrames"/>).</summary>
public static class SavedMusic
{
    /// <summary>
    /// <paramref name="music"/> with its file looked up: the saved full path when a file is there, else the same path
    /// relative to <paramref name="projectFilePath"/>'s folder (a folder moved together with its project), else the saved
    /// full path unchanged (the music section then says the file is missing). Never throws on a missing file.
    /// </summary>
    public static MusicSettings Resolve(MusicSettings music, string? relativePath, string? projectFilePath, IProjectFileSystem disk)
    {
        ArgumentNullException.ThrowIfNull(music);
        ArgumentNullException.ThrowIfNull(disk);
        if (music.Path is { } full && disk.Probe(full) is not null)
        {
            return music;
        }

        var folder = string.IsNullOrEmpty(projectFilePath) ? null : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectFilePath));
        if (folder is not null && relativePath is not null)
        {
            var beside = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, relativePath));
            if (disk.Probe(beside) is not null)
            {
                return music with { Path = beside };
            }
        }

        return music;
    }

    /// <summary>The path relative to <paramref name="projectFolder"/> a project file stores beside the full one, or null on another drive.</summary>
    public static string? Relative(string? path, string? projectFolder)
    {
        if (path is null
            || string.IsNullOrEmpty(projectFolder)
            || !string.Equals(System.IO.Path.GetPathRoot(path), System.IO.Path.GetPathRoot(projectFolder), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = System.IO.Path.GetRelativePath(projectFolder, path);
        return System.IO.Path.IsPathRooted(relative) ? null : relative;
    }
}
