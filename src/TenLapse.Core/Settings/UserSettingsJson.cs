using System.Text.Json;
using System.Text.Json.Nodes;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Core.Settings;

/// <summary>
/// The settings file's format (#21): one JSON object with a <c>version</c>. Enums are written by name (so reordering
/// the enum cannot change what a file means) and fractions as <c>[numerator, denominator]</c> (29.97 stays 30000/1001).
/// Reading is forgiving field by field: a value that is missing keeps its default quietly; one that is unknown or
/// out of range keeps its default and adds a line to <see cref="SettingsLoadResult.Problems"/>. A file that is not
/// JSON at all is the defaults with one problem. Nothing here throws on bad input.
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
            ["timing"] = new JsonObject
            {
                ["mode"] = settings.Timing.Mode.ToString(),
                ["framesPerSecond"] = Pair(settings.Timing.FramesPerSecond),
                ["lengthSeconds"] = Pair(settings.Timing.LengthSeconds),
                ["secondsPerPhoto"] = Pair(settings.Timing.SecondsPerPhoto),
            },
            ["frame"] = new JsonObject
            {
                ["aspect"] = settings.Frame.Aspect.ToString(),
                ["fit"] = settings.Frame.Fit.ToString(),
            },
            ["output"] = new JsonObject
            {
                ["resolution"] = settings.Output.Resolution.ToString(),
                ["format"] = settings.Output.Format.ToString(),
                ["quality"] = settings.Output.Quality.ToString(),
            },
            ["importFolder"] = settings.ImportFolder,
            ["exportFolder"] = settings.ExportFolder,
            ["successfulExports"] = settings.SuccessfulExports,
            ["ratingAskedVersion"] = settings.RatingAskedVersion,
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

        if (!TryVersion(root["version"], out var version))
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

        var timing = Section(root, "timing", problems);
        var frame = Section(root, "frame", problems);
        var output = Section(root, "output", problems);
        var defaults = UserSettings.Default;
        var settings = new UserSettings
        {
            Timing = new TimingSettings(
                Field(timing, "timing.mode", defaults.Timing.Mode, ReadEnum<SpeedMode>, problems),
                Field(timing, "timing.framesPerSecond", defaults.Timing.FramesPerSecond, node => InRange(ReadFraction(node), t => TimingSettings.Default with { FramesPerSecond = t }), problems),
                Field(timing, "timing.lengthSeconds", defaults.Timing.LengthSeconds, node => InRange(ReadFraction(node), t => TimingSettings.Default with { LengthSeconds = t }), problems),
                Field(timing, "timing.secondsPerPhoto", defaults.Timing.SecondsPerPhoto, node => InRange(ReadFraction(node), t => TimingSettings.Default with { SecondsPerPhoto = t }), problems)),
            Frame = new FrameSettings(
                Field(frame, "frame.aspect", defaults.Frame.Aspect, ReadEnum<FrameAspect>, problems),
                Field(frame, "frame.fit", defaults.Frame.Fit, ReadEnum<FitMode>, problems)),
            Output = new OutputSettings(
                Field(output, "output.resolution", defaults.Output.Resolution, ReadEnum<OutputResolution>, problems),
                Field(output, "output.format", defaults.Output.Format, ReadEnum<VideoFormat>, problems),
                Field(output, "output.quality", defaults.Output.Quality, ReadEnum<OutputQuality>, problems)),
            ImportFolder = Field(root, "importFolder", defaults.ImportFolder, ReadFolder, problems),
            ExportFolder = Field(root, "exportFolder", defaults.ExportFolder, ReadFolder, problems),
            SuccessfulExports = Field(root, "successfulExports", defaults.SuccessfulExports, ReadCount, problems),
            RatingAskedVersion = Field(root, "ratingAskedVersion", defaults.RatingAskedVersion, ReadVersionText, problems),
        };
        return new SettingsLoadResult(settings, problems);

        static SettingsLoadResult Fallback(string problem) => new(UserSettings.Default, [problem]);
    }

    private static JsonObject? Section(JsonObject root, string name, List<string> problems)
    {
        if (root[name] is JsonObject section)
        {
            return section;
        }

        if (root.TryGetPropertyValue(name, out var value) && value is not null)
        {
            problems.Add($"Settings section {name} is not an object; using the defaults for it.");
        }

        return null;
    }

    private static JsonArray Pair(Fraction value) => new(value.Numerator, value.Denominator);

    private static bool TryVersion(JsonNode? node, out int version)
    {
        version = 0;
        try
        {
            return node is JsonValue value && value.TryGetValue(out version) && version >= 1;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    // A missing field (or section) keeps its default without a word: an older file does not know a newer field.
    private static T Field<T>(JsonObject? section, string path, T fallback, Func<JsonNode, T> read, List<string> problems)
    {
        var name = path[(path.LastIndexOf('.') + 1)..];
        if (section is null || !section.TryGetPropertyValue(name, out var node) || node is null)
        {
            return fallback;
        }

        try
        {
            return read(node);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Any non-fatal exception: a hand-edited file must never cost more than the field it spoils.
            problems.Add($"Setting {path} is not usable ({exception.Message}); using the default.");
            return fallback;
        }
    }

    private static T ReadEnum<T>(JsonNode node)
        where T : struct, Enum
    {
        var text = node.GetValue<string>();
        foreach (var name in Enum.GetNames<T>())
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<T>(name);
            }
        }

        throw new ArgumentException($"\"{text}\" is not a known {typeof(T).Name}.");
    }

    private static Fraction ReadFraction(JsonNode node)
    {
        if (node is not JsonArray { Count: 2 } parts)
        {
            throw new FormatException("expected [numerator, denominator]");
        }

        // A null or nested element is not a number: say so rather than dereference it.
        if (parts[0] is not JsonValue numerator || parts[1] is not JsonValue denominator)
        {
            throw new FormatException("expected two numbers");
        }

        return new Fraction(numerator.GetValue<int>(), denominator.GetValue<int>());
    }

    // The timing record checks each input against its range when it is set; borrowing that check keeps one source of truth.
    private static Fraction InRange(Fraction value, Func<Fraction, TimingSettings> check)
    {
        _ = check(value);
        return value;
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
}
