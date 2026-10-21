using System.Globalization;
using System.Xml.Linq;

namespace TenLapse.Tests.Design;

/// <summary>
/// Reads the repository's design sources from disk: the XAML token dictionaries and the DESIGN.md front
/// matter. The tests project cannot reference the App exe, so everything here
/// works on files, found by walking up to TenLapse.slnx.
/// </summary>
internal static class DesignFiles
{
    public static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    public static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Lazy<string> _root = new(FindRoot);

    public static string Root => _root.Value;

    public static string ThemesFolder => Path.Combine(Root, "src", "TenLapse.App", "Themes");

    /// <summary>Every source file with one of the extensions under src/TenLapse.App, build output (bin, obj) excluded.</summary>
    public static IEnumerable<string> AppSources(params string[] extensions) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src", "TenLapse.App"), "*", SearchOption.AllDirectories)
            .Where(file => extensions.Any(extension => file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    public static XDocument LoadTheme(string file) => XDocument.Load(Path.Combine(ThemesFolder, file));

    /// <summary>The entries of a theme dictionary ("Light", "Dark" or "HighContrast"), by x:Key.</summary>
    public static Dictionary<string, XElement> ThemeDictionary(XDocument document, string theme)
    {
        var themeDictionaries = document.Root!.Element(Presentation + "ResourceDictionary.ThemeDictionaries")!;
        var dictionary = themeDictionaries.Elements(Presentation + "ResourceDictionary")
            .Single(element => (string?)element.Attribute(X + "Key") == theme);
        return Entries(dictionary);
    }

    /// <summary>The entries of the root dictionary itself (not the theme dictionaries), by x:Key.</summary>
    public static Dictionary<string, XElement> RootEntries(XDocument document) => Entries(document.Root!);

    private static Dictionary<string, XElement> Entries(XElement dictionary) =>
        dictionary.Elements()
            .Where(element => element.Attribute(X + "Key") is not null)
            .ToDictionary(element => (string)element.Attribute(X + "Key")!, element => element, StringComparer.Ordinal);

    /// <summary>The DESIGN.md front matter as flat keys: <c>colors.ink</c>, <c>typography.body.fontSize</c>, <c>spacing.xs</c>.</summary>
    public static Dictionary<string, string> DesignTokens()
    {
        var lines = File.ReadAllLines(Path.Combine(Root, "DESIGN.md"));
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        var section = string.Empty;
        var item = string.Empty;
        foreach (var line in lines.Skip(1).TakeWhile(line => line != "---"))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
            var key = trimmed[..colon];
            var value = trimmed[(colon + 1)..].Trim().Trim('"');
            if (indent == 0)
            {
                section = key;
            }
            else if (indent == 2)
            {
                if (value.Length == 0)
                {
                    item = key;
                }
                else
                {
                    tokens[$"{section}.{key}"] = value;
                }
            }
            else
            {
                tokens[$"{section}.{item}.{key}"] = value;
            }
        }

        return tokens;
    }

    /// <summary>The Deep Amber Indicator tonal ramp from .impeccable/design.json, as upper-case #RRGGBB.</summary>
    public static HashSet<string> IndicatorRamp()
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, ".impeccable", "design.json")));
        return document.RootElement.GetProperty("extensions").GetProperty("colorMeta").GetProperty("deep-amber-indicator").GetProperty("tonalRamp")
            .EnumerateArray().Select(step => step.GetString()!.ToUpperInvariant()).ToHashSet();
    }

    /// <summary>"golden-hour-amber" becomes "GoldenHourAmber"; "2xl" stays "2xl".</summary>
    public static string Pascal(string kebab) =>
        string.Concat(kebab.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    public static (byte R, byte G, byte B) ParseHex(string hex)
    {
        var digits = hex.TrimStart('#');
        return (
            byte.Parse(digits[^6..^4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(digits[^4..^2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(digits[^2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>WCAG contrast ratio between two opaque colors.</summary>
    public static double Contrast(string foreground, string background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }

    private static double Channel(byte value)
    {
        var c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TenLapse.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("TenLapse.slnx was not found above " + AppContext.BaseDirectory);
    }
}
