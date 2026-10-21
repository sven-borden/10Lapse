using TenLapse.Core.Projects;

namespace TenLapse.Core.Settings;

/// <summary>
/// What 10Lapse remembers between launches (#21): the last-used timing, frame shape and output preference, and the
/// folders the pickers open in. A new project starts from these. Immutable.
/// </summary>
/// <remarks>
/// <see cref="Output"/> is the user's <b>preference</b>, never the effective output: a 4K choice stays 4K on a PC
/// that cannot encode it, and <c>OutputPlan</c> (Presentation) decides what is exported. Nothing here holds photo
/// content; the folders are local paths. To add a setting, add a property here, read and write it in
/// <see cref="UserSettingsJson"/> with its own fallback, and extend the round-trip test. The schema needs a new
/// version (and a migration step) only when an existing field changes meaning.
/// </remarks>
public sealed record UserSettings
{
    /// <summary>30 fps, 16:9 Fill, 1080p H.264 High, no folders: what a fresh install and <i>Reset to defaults</i> give.</summary>
    public static UserSettings Default { get; } = new();

    /// <summary>The timing mode and each mode's own input (#17).</summary>
    public TimingSettings Timing { get; init; } = TimingSettings.Default;

    /// <summary>The frame shape and fit (#18).</summary>
    public FrameSettings Frame { get; init; } = FrameSettings.Default;

    /// <summary>The output preference (#23): resolution, format and quality as the user picked them.</summary>
    public OutputSettings Output { get; init; } = OutputSettings.Default;

    /// <summary>The folder the import pickers open in (#12), or null for the Pictures library.</summary>
    public string? ImportFolder { get; init; }

    /// <summary>The folder the Save dialog opens in (#24 sets it after an export), or null for Videos.</summary>
    public string? ExportFolder { get; init; }

    /// <summary>
    /// How many exports have succeeded on this PC (#31), for the rating prompt. Not a preference: <i>Reset to defaults</i>
    /// keeps it, so a reset cannot bring the prompt back.
    /// </summary>
    public int SuccessfulExports { get; init; }

    /// <summary>The app version the rating prompt was last shown in (#31), or null when it never was. Kept by <i>Reset to defaults</i> too.</summary>
    public string? RatingAskedVersion { get; init; }

    /// <summary>
    /// The project files (#52) opened or saved last, the most recent first, at most <see cref="MaxRecentProjects"/>. Not a
    /// preference: <i>Reset to defaults</i> keeps it (the first-launch screen has its own <i>Clear list</i>).
    /// </summary>
    public PathList RecentProjects { get; init; } = PathList.Empty;

    /// <summary>How many recent projects are remembered and listed.</summary>
    public const int MaxRecentProjects = 5;
}

/// <summary>What <see cref="ISettingsStore.Load"/> found.</summary>
/// <param name="Settings">The settings: every field that read correctly, the default for every other.</param>
/// <param name="Problems">One line per field or file that could not be read; empty when everything was fine.</param>
public sealed record SettingsLoadResult(UserSettings Settings, IReadOnlyList<string> Problems)
{
    /// <summary>The defaults, with no problem (no file yet).</summary>
    public static SettingsLoadResult Fresh { get; } = new(UserSettings.Default, []);
}

/// <summary>Where the settings live between launches. A file in the app's data folder in the app; memory in tests.</summary>
public interface ISettingsStore
{
    /// <summary>Reads the settings. Never throws: a missing file is the defaults, an unreadable one is the defaults with a problem.</summary>
    SettingsLoadResult Load();

    /// <summary>Writes the settings, replacing what was stored. Throws when the write fails (a locked or full disk); the caller reports it.</summary>
    void Save(UserSettings settings);
}

/// <summary>Keeps the settings in memory, for tests and for runs that must leave the disk alone.</summary>
public sealed class MemorySettingsStore : ISettingsStore
{
    private readonly Lock _gate = new();
    private SettingsLoadResult _stored = SettingsLoadResult.Fresh;
    private int _saves;

    /// <summary>Starts with nothing stored.</summary>
    public MemorySettingsStore()
    {
    }

    /// <summary>Starts with <paramref name="result"/> as what <see cref="Load"/> finds.</summary>
    public MemorySettingsStore(SettingsLoadResult result) => _stored = result;

    /// <summary>When set, <see cref="Save"/> throws it (a failing disk).</summary>
    public Exception? FailWith { get; set; }

    /// <summary>The settings last stored.</summary>
    public UserSettings Stored
    {
        get
        {
            lock (_gate)
            {
                return _stored.Settings;
            }
        }
    }

    /// <summary>How many times <see cref="Save"/> succeeded.</summary>
    public int Saves
    {
        get
        {
            lock (_gate)
            {
                return _saves;
            }
        }
    }

    /// <inheritdoc />
    public SettingsLoadResult Load()
    {
        lock (_gate)
        {
            return _stored;
        }
    }

    /// <inheritdoc />
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            if (FailWith is { } failure)
            {
                throw failure;
            }

            _stored = new SettingsLoadResult(settings, []);
            _saves++;
        }
    }
}
