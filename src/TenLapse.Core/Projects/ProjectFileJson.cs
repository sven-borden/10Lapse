using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TenLapse.Core.Settings;

namespace TenLapse.Core.Projects;

/// <summary>Why a project file could not be read at all.</summary>
public enum ProjectFileFailure
{
    /// <summary>It was read.</summary>
    None,

    /// <summary>The text is not JSON.</summary>
    NotJson,

    /// <summary>It is JSON, but not a 10Lapse project (no <c>format</c> marker).</summary>
    NotAProject,

    /// <summary>It is a 10Lapse project without a usable <c>version</c>.</summary>
    NoUsableVersion,
}

/// <summary>What <see cref="ProjectFileJson.Read"/> found.</summary>
/// <param name="Content">The project, or null when <paramref name="Failure"/> says why it could not be read.</param>
/// <param name="Failure">Why nothing could be read, or <see cref="ProjectFileFailure.None"/>.</param>
/// <param name="Version">The file's version (0 when it has none).</param>
/// <param name="Problems">One English line per field or frame that could not be read and took its default or was left out; for the log.</param>
public sealed record ProjectFileReadResult(ProjectFileContent? Content, ProjectFileFailure Failure, int Version, IReadOnlyList<string> Problems)
{
    /// <summary>True when a newer 10Lapse wrote it: the fields this build knows were read, the others ignored.</summary>
    public bool IsNewerVersion => Version > ProjectFileJson.CurrentVersion;

    internal static ProjectFileReadResult Failed(ProjectFileFailure failure, string problem) => new(null, failure, 0, [problem]);
}

/// <summary>
/// The <c>.10lapse</c> project file's format (#52): UTF-8 JSON, one object with a <c>format</c> marker, a
/// <c>version</c>, one group per project setting (<c>timing</c>, <c>frame</c>, <c>output</c>, <c>sortMode</c>) and the
/// <c>frames</c> in project order. The groups share their writers and readers with the settings file
/// (<see cref="JsonFields"/>): enums by name, fractions as pairs. Reading never throws: a file that is not JSON, or not a
/// project, is a <see cref="ProjectFileFailure"/>; a newer version is read for the fields this build knows; a missing or
/// unusable field takes its default (a missing group means a file from before that setting existed), and an unknown enum
/// value (one a newer build added) takes the default and says so in the problems. Files are written atomically by
/// whoever saves them (<see cref="IO.AtomicFile"/>).
/// </summary>
/// <remarks>Adding a settings group: see <see cref="ProjectFileContent"/>.</remarks>
public static class ProjectFileJson
{
    /// <summary>The file name extension, with its dot.</summary>
    public const string Extension = ".10lapse";

    /// <summary>The version this build writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The value of the <c>format</c> marker that says a JSON file is a 10Lapse project.</summary>
    public const string FormatMarker = "10Lapse project";

    /// <summary>The largest project file that is read. 5,000 frames take about 2 MB; anything far bigger is not ours.</summary>
    public const long MaxFileBytes = 64L * 1024 * 1024;

    // A key twice in one object is damage, not a choice: allowed, the first look at that key would throw (JsonObject's
    // dictionary), so the parser refuses it and the file reads as not JSON.
    private static readonly JsonDocumentOptions _readOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16, AllowDuplicateProperties = false };
    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    // Step n upgrades a version n document to version n + 1, in place. Empty while version 1 is current; a step is added
    // only when a later version changes what an existing field means, never for a new field or group.
    private static readonly Action<JsonObject>[] _migrations = [];

    /// <summary>
    /// The <see cref="ProjectSnapshot"/> properties the file stores (as a group or as the frames), and the ones it leaves
    /// out on purpose. A test fails on a snapshot property that is in neither, so a new setting cannot be forgotten.
    /// </summary>
    internal static IReadOnlySet<string> StoredSnapshotProperties { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(ProjectSnapshot.Frames),
        nameof(ProjectSnapshot.Timing),
        nameof(ProjectSnapshot.FrameSettings),
        nameof(ProjectSnapshot.Output),
        nameof(ProjectSnapshot.SortMode),
        nameof(ProjectSnapshot.Sequence),
    };

    /// <inheritdoc cref="StoredSnapshotProperties"/>
    internal static IReadOnlySet<string> NotStoredSnapshotProperties { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // Session state, derived values, or the file itself: the name is the file's name (a recovery copy stores it apart).
        nameof(ProjectSnapshot.Version),
        nameof(ProjectSnapshot.SkippedFiles),
        nameof(ProjectSnapshot.Name),
        nameof(ProjectSnapshot.IsDirty),
        nameof(ProjectSnapshot.FilePath),
        nameof(ProjectSnapshot.EffectiveRate),
        nameof(ProjectSnapshot.VideoSize),
        nameof(ProjectSnapshot.EffectiveSequence),
        nameof(ProjectSnapshot.Playback),
        nameof(ProjectSnapshot.Duration),
        "EqualityContract",
    };

    /// <summary>Writes <paramref name="content"/> as the current version.</summary>
    public static string Write(ProjectFileContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var root = new JsonObject
        {
            ["format"] = FormatMarker,
            ["version"] = CurrentVersion,
            ["timing"] = JsonFields.WriteTiming(content.Timing),
            ["frame"] = JsonFields.WriteFrame(content.Frame),
            ["output"] = JsonFields.WriteOutput(content.Output),
            ["sortMode"] = content.SortMode.ToString(),
            ["sequence"] = JsonFields.WriteSequence(content.Sequence),
        };

        if (content.RecoveredName is not null || content.RecoveredFilePath is not null)
        {
            root["recovered"] = new JsonObject { ["name"] = content.RecoveredName, ["projectFile"] = content.RecoveredFilePath };
        }

        root["frames"] = new JsonArray([.. content.Frames.Select(WriteFrame)]);
        return root.ToJsonString(_writeOptions);
    }

    /// <summary>Reads a project file's text. Never throws.</summary>
    public static ProjectFileReadResult Read(string? json)
    {
        try
        {
            return ReadCore(json);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            // A shape no check below foresaw: a damaged file, never a throw.
            return ProjectFileReadResult.Failed(ProjectFileFailure.NotAProject, $"The project file could not be read as a project ({exception.Message}).");
        }
    }

    private static ProjectFileReadResult ReadCore(string? json)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json ?? string.Empty, documentOptions: _readOptions);
        }
        catch (JsonException exception)
        {
            return ProjectFileReadResult.Failed(ProjectFileFailure.NotJson, $"The project file is not valid JSON ({exception.Message}).");
        }

        if (parsed is not JsonObject root
            || root["format"] is not JsonValue marker
            || !marker.TryGetValue<string>(out var format)
            || !string.Equals(format, FormatMarker, StringComparison.Ordinal))
        {
            return ProjectFileReadResult.Failed(ProjectFileFailure.NotAProject, "The file is JSON but has no 10Lapse project format marker.");
        }

        if (!JsonFields.TryVersion(root["version"], out var version))
        {
            return ProjectFileReadResult.Failed(ProjectFileFailure.NoUsableVersion, "The project file has no usable version.");
        }

        var problems = new List<string>();
        if (version > CurrentVersion)
        {
            problems.Add($"The project file is version {version}, newer than this app's {CurrentVersion}; reading the fields this version knows.");
        }
        else
        {
            for (var step = version; step < CurrentVersion; step++)
            {
                _migrations[step - 1](root);
            }
        }

        var recovered = JsonFields.Section(root, "recovered", problems);
        var content = new ProjectFileContent
        {
            Timing = JsonFields.ReadTiming(root, "timing", TimingSettings.Default, problems),
            Frame = JsonFields.ReadFrame(root, "frame", FrameSettings.Default, problems),
            Output = JsonFields.ReadOutput(root, "output", OutputSettings.Default, problems),
            SortMode = JsonFields.Field(root, "sortMode", SortMode.CaptureTime, JsonFields.ReadEnum<SortMode>, problems),
            Sequence = JsonFields.ReadSequence(root, "sequence", SequenceSettings.Default, problems),
            RecoveredName = JsonFields.Field(recovered, "recovered.name", null, ReadText, problems),
            RecoveredFilePath = JsonFields.Field(recovered, "recovered.projectFile", null, ReadFullPath, problems),
            Frames = ReadFrames(root, problems),
        };
        return new ProjectFileReadResult(content, ProjectFileFailure.None, version, problems);
    }

    private static JsonObject WriteFrame(SavedFrame frame)
    {
        var entry = new JsonObject { ["path"] = frame.Path };
        if (frame.RelativePath is { } relative)
        {
            entry["relativePath"] = relative;
        }

        if (frame.FileSize is { } size)
        {
            entry["size"] = size;
        }

        if (frame.LastWriteTimeUtc is { } modified)
        {
            entry["modified"] = modified.ToString("O", CultureInfo.InvariantCulture);
        }

        if (frame.PixelSize is { } pixels)
        {
            entry["width"] = pixels.Width;
            entry["height"] = pixels.Height;
        }

        if (frame.CaptureTime is { } captured)
        {
            // Wall-clock time without a zone, as EXIF stores it (Unspecified).
            entry["captured"] = captured.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        }

        if (frame.CaptureUtcOffset is { } offset)
        {
            entry["utcOffsetMinutes"] = (int)offset.TotalMinutes;
        }

        return entry;
    }

    // Each entry costs only itself: one without a usable path is left out with a problem; a bad detail is simply unknown.
    private static ImmutableArray<SavedFrame> ReadFrames(JsonObject root, List<string> problems)
    {
        if (!root.TryGetPropertyValue("frames", out var node) || node is null)
        {
            return [];
        }

        if (node is not JsonArray entries)
        {
            problems.Add("Field frames is not a list; the project has no frames.");
            return [];
        }

        var frames = ImmutableArray.CreateBuilder<SavedFrame>(entries.Count);
        var unusable = 0;
        foreach (var entry in entries)
        {
            if (entry is JsonObject frame && ReadFrame(frame) is { } saved)
            {
                frames.Add(saved);
            }
            else
            {
                unusable++;
            }
        }

        if (unusable > 0)
        {
            problems.Add($"{unusable} frame entries have no usable path and were left out.");
        }

        return frames.ToImmutable();
    }

    private static SavedFrame? ReadFrame(JsonObject entry)
    {
        var path = Optional(entry, "path", ReadFullPath);
        var relative = Optional(entry, "relativePath", ReadRelativePath);
        if (path is null && relative is null)
        {
            return null;
        }

        var width = Optional<int?>(entry, "width", node => node.GetValue<int>());
        var height = Optional<int?>(entry, "height", node => node.GetValue<int>());
        return new SavedFrame(path)
        {
            RelativePath = relative,
            FileSize = Optional<long?>(entry, "size", node => node.GetValue<long>() is >= 0 and var size ? size : throw new FormatException()),
            LastWriteTimeUtc = Optional<DateTime?>(entry, "modified", node =>
                DateTime.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)),
            PixelSize = width is > 0 && height is > 0 ? new PixelSize(width.Value, height.Value) : null,
            CaptureTime = Optional<DateTime?>(entry, "captured", node =>
                DateTime.SpecifyKind(DateTime.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DateTimeKind.Unspecified)),
            CaptureUtcOffset = Optional<TimeSpan?>(entry, "utcOffsetMinutes", node =>
                node.GetValue<int>() is >= -18 * 60 and <= 18 * 60 and var minutes ? TimeSpan.FromMinutes(minutes) : throw new FormatException()),
        };
    }

    // A frame's detail that is missing or unusable is unknown, as it is for a photo whose file lacks it.
    private static T? Optional<T>(JsonObject entry, string name, Func<JsonNode, T> read)
    {
        if (!entry.TryGetPropertyValue(name, out var node) || node is null)
        {
            return default;
        }

        try
        {
            return read(node);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return default;
        }
    }

    private static string? ReadText(JsonNode node)
    {
        var text = node.GetValue<string>();
        return string.IsNullOrWhiteSpace(text) || text.Length > 260 || text.Any(char.IsControl) ? throw new FormatException("not a name") : text;
    }

    private static string? ReadFullPath(JsonNode node)
    {
        var path = node.GetValue<string>();
        return path.Length == 0 || path.Contains('\0') || !Path.IsPathFullyQualified(path) ? throw new FormatException("not a full path") : path;
    }

    private static string? ReadRelativePath(JsonNode node)
    {
        var path = node.GetValue<string>();
        return path.Length == 0 || path.Contains('\0') || Path.IsPathRooted(path) ? throw new FormatException("not a relative path") : path;
    }
}
