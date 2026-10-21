using System.Text.Json.Nodes;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Core.Settings;

/// <summary>
/// The pieces both of 10Lapse's JSON files are made of: the settings file (#21, <see cref="UserSettingsJson"/>) and the
/// project file (#52, <see cref="ProjectFileJson"/>). Enums are written by name (so reordering an enum cannot change what
/// a file means, and a value a newer build added reads as unknown) and fractions as <c>[numerator, denominator]</c> (29.97
/// stays 30000/1001). Reading is forgiving field by field: a missing field keeps its default quietly, an unusable one
/// keeps its default and adds a line to the problems. Nothing here throws on bad input.
/// </summary>
internal static class JsonFields
{
    /// <summary>The timing group: the mode and each mode's own input.</summary>
    public static JsonObject WriteTiming(TimingSettings timing) => new()
    {
        ["mode"] = timing.Mode.ToString(),
        ["framesPerSecond"] = Pair(timing.FramesPerSecond),
        ["lengthSeconds"] = Pair(timing.LengthSeconds),
        ["secondsPerPhoto"] = Pair(timing.SecondsPerPhoto),
    };

    /// <summary>Reads the timing group named <paramref name="name"/>; each field that is missing or unusable keeps <paramref name="fallback"/>'s value.</summary>
    public static TimingSettings ReadTiming(JsonObject root, string name, TimingSettings fallback, List<string> problems)
    {
        var section = Section(root, name, problems);
        return new TimingSettings(
            Field(section, $"{name}.mode", fallback.Mode, ReadEnum<SpeedMode>, problems),
            Field(section, $"{name}.framesPerSecond", fallback.FramesPerSecond, node => InRange(ReadFraction(node), t => TimingSettings.Default with { FramesPerSecond = t }), problems),
            Field(section, $"{name}.lengthSeconds", fallback.LengthSeconds, node => InRange(ReadFraction(node), t => TimingSettings.Default with { LengthSeconds = t }), problems),
            Field(section, $"{name}.secondsPerPhoto", fallback.SecondsPerPhoto, node => InRange(ReadFraction(node), t => TimingSettings.Default with { SecondsPerPhoto = t }), problems));
    }

    /// <summary>The frame group: the shape and fit.</summary>
    public static JsonObject WriteFrame(FrameSettings frame) => new()
    {
        ["aspect"] = frame.Aspect.ToString(),
        ["fit"] = frame.Fit.ToString(),
    };

    /// <summary>Reads the frame group named <paramref name="name"/>, field by field.</summary>
    public static FrameSettings ReadFrame(JsonObject root, string name, FrameSettings fallback, List<string> problems)
    {
        var section = Section(root, name, problems);
        return new FrameSettings(
            Field(section, $"{name}.aspect", fallback.Aspect, ReadEnum<FrameAspect>, problems),
            Field(section, $"{name}.fit", fallback.Fit, ReadEnum<FitMode>, problems));
    }

    /// <summary>The output group: the preference as the user picked it (never the effective output).</summary>
    public static JsonObject WriteOutput(OutputSettings output) => new()
    {
        ["resolution"] = output.Resolution.ToString(),
        ["format"] = output.Format.ToString(),
        ["quality"] = output.Quality.ToString(),
    };

    /// <summary>Reads the output group named <paramref name="name"/>, field by field.</summary>
    public static OutputSettings ReadOutput(JsonObject root, string name, OutputSettings fallback, List<string> problems)
    {
        var section = Section(root, name, problems);
        return new OutputSettings(
            Field(section, $"{name}.resolution", fallback.Resolution, ReadEnum<OutputResolution>, problems),
            Field(section, $"{name}.format", fallback.Format, ReadEnum<VideoFormat>, problems),
            Field(section, $"{name}.quality", fallback.Quality, ReadEnum<OutputQuality>, problems));
    }

    /// <summary>The object named <paramref name="name"/>, or null when it is missing (quietly) or not an object (a problem).</summary>
    public static JsonObject? Section(JsonObject root, string name, List<string> problems)
    {
        if (root[name] is JsonObject section)
        {
            return section;
        }

        if (root.TryGetPropertyValue(name, out var value) && value is not null)
        {
            problems.Add($"Section {name} is not an object; using the defaults for it.");
        }

        return null;
    }

    /// <summary>A fraction as <c>[numerator, denominator]</c>.</summary>
    public static JsonArray Pair(Fraction value) => new(value.Numerator, value.Denominator);

    /// <summary>A file's <c>version</c>: a whole number from 1.</summary>
    public static bool TryVersion(JsonNode? node, out int version)
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

    /// <summary>
    /// Reads the field at the end of <paramref name="path"/> ("timing.mode" reads "mode") from <paramref name="section"/>.
    /// A missing field (or section) keeps <paramref name="fallback"/> without a word: an older file does not know a newer
    /// field. One that <paramref name="read"/> rejects keeps it too, and says so in <paramref name="problems"/>.
    /// </summary>
    public static T Field<T>(JsonObject? section, string path, T fallback, Func<JsonNode, T> read, List<string> problems)
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
            problems.Add($"Field {path} is not usable ({exception.Message}); using the default.");
            return fallback;
        }
    }

    /// <summary>An enum by name, ignoring case. A name this build does not know (a value a newer build added) throws, so <see cref="Field"/> keeps the default.</summary>
    public static T ReadEnum<T>(JsonNode node)
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

    /// <summary>A fraction from <c>[numerator, denominator]</c>.</summary>
    public static Fraction ReadFraction(JsonNode node)
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
}
