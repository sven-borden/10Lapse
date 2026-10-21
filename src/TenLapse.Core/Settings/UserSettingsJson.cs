using System.Text.Json;
using System.Text.Json.Nodes;

namespace TenLapse.Core.Settings;

/// <summary>
/// The settings file's format (#21): one JSON object with a <c>version</c>. Enums are written by name (so reordering
/// the enum cannot change what a file means) and fractions as <c>[numerator, denominator]</c> (29.97 stays 30000/1001).
/// Reading is forgiving field by field: a value that is missing keeps its default quietly; one that is unknown or
/// out of range keeps its default and adds a line to <see cref="SettingsLoadResult.Problems"/>. A file that is not
/// JSON at all is the defaults with one problem. Nothing here throws on bad input. The timing, frame and output groups
/// are written and read by <see cref="JsonFields"/>, which the project file (#52) shares.
/// </summary>
public static class UserSettingsJson
{
    /// <summary>The version this build writes.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonDocumentOptions _readOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    // Step n upgrades a version n document to version n + 1, in place. Empty while version 1 is current; a step is
    // added when a later version changes what an existing field means (a rename or a new unit), never for a new field.
    private static readonly Action<JsonObject>[] _migrations = [];

    /// <summary>Writes the settings as the current version.</summary>
    public static string Write(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var root = new JsonObject
        {
            ["version"] = CurrentVersion,
            ["timing"] = JsonFields.WriteTiming(settings.Timing),
            ["frame"] = JsonFields.WriteFrame(settings.Frame),
            ["output"] = JsonFields.WriteOutput(settings.Output),
            ["importFolder"] = settings.ImportFolder,
            ["exportFolder"] = settings.ExportFolder,
            ["successfulExports"] = settings.SuccessfulExports,
            ["ratingAskedVersion"] = settings.RatingAskedVersion,
            ["recentProjects"] = new JsonArray([.. settings.RecentProjects.Select(path => (JsonNode?)JsonValue.Create(path))]),
        };
        return root.ToJsonString(_writeOptions);
    }

    /// <summary>Reads a settings file's text. Never throws.</summary>
    public static SettingsLoadResult Read(string? json)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json ?? string.Empty, documentOptions: _readOptions);
        }
        catch (JsonException exception)
        {
            return Fallback($"The settings file is not valid JSON ({exception.Message}).");
        }

        if (parsed is not JsonObject root)
        {
            return Fallback("The settings file does not hold a settings object.");
        }

        if (!JsonFields.TryVersion(root["version"], out var version))
        {
            return Fallback("The settings file has no usable version.");
        }

        var problems = new List<string>();
        if (version > CurrentVersion)
        {
            problems.Add($"The settings file is version {version}, newer than this app's {CurrentVersion}; reading the fields this version knows.");
        }
        else
        {
            for (var step = version; step < CurrentVersion; step++)
            {
                _migrations[step - 1](root);
            }
        }

        var defaults = UserSettings.Default;
        var settings = new UserSettings
        {
            Timing = JsonFields.ReadTiming(root, "timing", defaults.Timing, problems),
            Frame = JsonFields.ReadFrame(root, "frame", defaults.Frame, problems),
            Output = JsonFields.ReadOutput(root, "output", defaults.Output, problems),
            ImportFolder = JsonFields.Field(root, "importFolder", defaults.ImportFolder, ReadFolder, problems),
            ExportFolder = JsonFields.Field(root, "exportFolder", defaults.ExportFolder, ReadFolder, problems),
            SuccessfulExports = JsonFields.Field(root, "successfulExports", defaults.SuccessfulExports, ReadCount, problems),
            RatingAskedVersion = JsonFields.Field(root, "ratingAskedVersion", defaults.RatingAskedVersion, ReadVersionText, problems),
            RecentProjects = JsonFields.Field(root, "recentProjects", defaults.RecentProjects, node => ReadRecent(node, problems), problems),
        };
        return new SettingsLoadResult(settings, problems);

        static SettingsLoadResult Fallback(string problem) => new(UserSettings.Default, [problem]);
    }

    private static string? ReadFolder(JsonNode node)
    {
        var path = node.GetValue<string>();
        if (path.Length == 0)
        {
            return null;
        }

        return path.Contains('\0') || !Path.IsPathFullyQualified(path)
            ? throw new ArgumentException("not a full folder path")
            : path;
    }

    private static int ReadCount(JsonNode node)
    {
        var count = node.GetValue<int>();
        return count < 0 ? throw new ArgumentOutOfRangeException(nameof(node), "a count cannot be negative") : count;
    }

    private static string? ReadVersionText(JsonNode node)
    {
        var text = node.GetValue<string>();
        return text.Length == 0
            ? null
            : text.Length > 64 || text.Any(char.IsControl) ? throw new ArgumentException("not a version") : text;
    }

    // The recent project files (#52): full paths, the first MaxRecentProjects. An entry that is not one is dropped and
    // costs only itself.
    private static PathList ReadRecent(JsonNode node, List<string> problems)
    {
        if (node is not JsonArray entries)
        {
            throw new FormatException("expected a list of paths");
        }

        var paths = new List<string>();
        foreach (var entry in entries)
        {
            if (entry is JsonValue value && value.TryGetValue<string>(out var path) && path.Length > 0 && !path.Contains('\0') && Path.IsPathFullyQualified(path))
            {
                paths.Add(path);
            }
            else
            {
                problems.Add("Field recentProjects has an entry that is not a full file path; it was left out.");
            }
        }

        return PathList.Of(paths).Take(UserSettings.MaxRecentProjects);
    }
}
