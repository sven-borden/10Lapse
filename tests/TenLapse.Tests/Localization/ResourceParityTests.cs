using System.Text.RegularExpressions;
using System.Xml.Linq;
using TenLapse.Core.Text;
using TenLapse.Tests.Design;

namespace TenLapse.Tests.Localization;

/// <summary>
/// The translations match the English files (#113). Found by glob, so they pass with no translation at all:
/// <list type="bullet">
/// <item>every <c>Strings/X.&lt;lang&gt;.resx</c> has the keys of <c>Strings/X.resx</c>, no other key, and the same
/// placeholder indices per key;</item>
/// <item>a language with one <c>.resx</c> translates every <c>.resx</c> of every project;</item>
/// <item>every <c>.resx</c> language has <c>src/TenLapse.App/Strings/&lt;lang&gt;/Resources.resw</c> (the manifest's
/// description, and the language's declaration to the package and the Store) and back, with the keys of the en-US one;</item>
/// <item>every language is one <see cref="ResourceLanguages.Shipped"/> names, spelled the same.</item>
/// </list>
/// Plurals (#114: <c>X_one</c>, <c>X_many</c>, <c>X_other</c>): a language has its own set of categories, so a key
/// <c>X_zero|one|two|few|many</c> whose <c>X_other</c> is in the English file may be missing or extra; <c>X_other</c> is
/// always required, and a category's placeholders are those of its English counterpart (<c>X_other</c> when English has
/// none).
/// </summary>
public sealed partial class ResourceParityTests
{
    private static readonly string[] _pluralCategories = ["zero", "one", "two", "few", "many"];

    [Fact]
    public void EveryTranslatedResxHasTheEnglishKeysAndPlaceholders()
    {
        var problems = new List<string>();
        foreach (var file in ResxFiles().Where(file => file.Language is not null))
        {
            var english = Path.Combine(Path.GetDirectoryName(file.Path)!, file.Name + ".resx");
            if (!File.Exists(english))
            {
                problems.Add($"{Relative(file.Path)} has no English {file.Name}.resx beside it.");
                continue;
            }

            problems.AddRange(Compare(Strings(english), Strings(file.Path), Relative(file.Path)));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryLanguageTranslatesEveryResx()
    {
        var files = ResxFiles();
        var english = files.Where(file => file.Language is null).ToList();
        var problems = new List<string>();
        foreach (var language in Languages(files))
        {
            foreach (var file in english)
            {
                var translated = Path.Combine(Path.GetDirectoryName(file.Path)!, $"{file.Name}.{language}.resx");
                if (!File.Exists(translated))
                {
                    problems.Add($"{language}: {Relative(translated)} is missing; its strings would show in English.");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryResxLanguageHasItsReswAndBack()
    {
        var resx = Languages(ResxFiles());
        var resw = ReswLanguages();
        var problems = new List<string>();
        foreach (var language in resx.Except(resw, StringComparer.Ordinal))
        {
            problems.Add($"{language} has .resx files but no src/TenLapse.App/Strings/{language}/Resources.resw: the package and the Store would not declare it.");
        }

        foreach (var language in resw.Except(resx, StringComparer.Ordinal))
        {
            problems.Add($"src/TenLapse.App/Strings/{language}/Resources.resw has no .resx files beside it: the Store would list a language the app does not speak.");
        }

        var english = Strings(ReswPath(ResourceLanguages.English));
        foreach (var language in resw)
        {
            problems.AddRange(Compare(english, Strings(ReswPath(language)), Relative(ReswPath(language))));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryLanguageIsAShippedOne()
    {
        var problems = Languages(ResxFiles()).Concat(ReswLanguages()).Distinct(StringComparer.Ordinal)
            .Where(language => !ResourceLanguages.Shipped.Skip(1).Contains(language, StringComparer.Ordinal))
            .Select(language => $"'{language}' is not in ResourceLanguages.Shipped (spelled exactly: {string.Join(", ", ResourceLanguages.Shipped.Skip(1))}).")
            .ToList();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheEnglishFilesAreFound()
    {
        // The glob must keep finding the English files, or every check above passes on nothing.
        Assert.True(ResxFiles().Count(file => file.Language is null) >= 9);
        Assert.True(File.Exists(ReswPath(ResourceLanguages.English)));
    }

    [Fact]
    public void TheComparisonCatchesAMissingKeyAnExtraKeyAndAPlaceholderMismatch()
    {
        var english = new Dictionary<string, string>
        {
            ["Kept"] = "{0} of {1}",
            ["Swapped"] = "{0} then {1}",
            ["Dropped"] = "Gone",
            ["Photos_one"] = "{0} photo",
            ["Photos_other"] = "{0} photos",
            ["Literal"] = "{{0}} stays text, {0} does not",
        };
        var translated = new Dictionary<string, string>
        {
            ["Kept"] = "{0} sur {2}",
            ["Swapped"] = "{1} puis {0}",
            ["Extra"] = "Nouveau",
            ["Photos_one"] = "une photo",
            ["Photos_many"] = "{0} de photos",
            ["Photos_other"] = "{0} photos",
            ["Literal"] = "{0} reste du texte, {{1}} aussi",
        };

        var problems = Compare(english, translated, "X.fr.resx");

        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("misses Dropped", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("has Extra", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Photos_one", StringComparison.Ordinal) && problem.Contains("{0}", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, problem => problem.Contains("Literal", StringComparison.Ordinal) || problem.Contains("Swapped", StringComparison.Ordinal) || problem.Contains("Photos_many", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Kept uses {0} {2}", StringComparison.Ordinal));
    }

    [Fact]
    public void APluralLanguageMayDropOrAddCategoriesButNotOther()
    {
        var english = new Dictionary<string, string> { ["Photos_one"] = "{0} photo", ["Photos_other"] = "{0} photos" };

        Assert.Empty(Compare(english, new Dictionary<string, string> { ["Photos_other"] = "{0} Fotos" }, "X.de.resx"));
        Assert.Empty(Compare(english, new Dictionary<string, string> { ["Photos_one"] = "{0} foto", ["Photos_many"] = "{0} de fotos", ["Photos_other"] = "{0} fotos" }, "X.es.resx"));
        var missingOther = Compare(english, new Dictionary<string, string> { ["Photos_one"] = "{0} photo" }, "X.fr.resx");
        Assert.Contains(missingOther, problem => problem.Contains("misses Photos_other", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyTranslationIsAProblem()
    {
        var problems = Compare(new Dictionary<string, string> { ["Save"] = "Save" }, new Dictionary<string, string> { ["Save"] = " " }, "X.it.resx");
        Assert.Contains(problems, problem => problem.Contains("Save is empty", StringComparison.Ordinal));
    }

    /// <summary>The problems of one translated file against its English one.</summary>
    internal static List<string> Compare(IReadOnlyDictionary<string, string> english, IReadOnlyDictionary<string, string> translated, string name)
    {
        var problems = new List<string>();
        foreach (var key in english.Keys.Where(key => !IsOptionalPlural(key, english.ContainsKey)).Order(StringComparer.Ordinal))
        {
            if (!translated.ContainsKey(key))
            {
                problems.Add($"{name} misses {key}.");
            }
        }

        foreach (var (key, value) in translated.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var counterpart = english.ContainsKey(key) ? key : IsOptionalPlural(key, english.ContainsKey) ? Stem(key) + "_other" : null;
            if (counterpart is null)
            {
                problems.Add($"{name} has {key}, which the English file does not have.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(english[counterpart]))
            {
                problems.Add($"{name}: {key} is empty.");
                continue;
            }

            var expected = Placeholders(english[counterpart]);
            var found = Placeholders(value);
            if (!expected.SetEquals(found))
            {
                problems.Add($"{name}: {key} uses {Describe(found)}, the English {counterpart} uses {Describe(expected)}.");
            }
        }

        return problems;
    }

    // X_one, X_many... when English has X_other: a language may have that category or not.
    private static bool IsOptionalPlural(string key, Func<string, bool> englishHas)
    {
        var underscore = key.LastIndexOf('_');
        return underscore > 0
            && _pluralCategories.Contains(key[(underscore + 1)..], StringComparer.Ordinal)
            && englishHas(key[..underscore] + "_other");
    }

    private static string Stem(string key) => key[..key.LastIndexOf('_')];

    private static HashSet<int> Placeholders(string text)
    {
        // {{ and }} are literal braces, not placeholders.
        var unescaped = text.Replace("{{", string.Empty, StringComparison.Ordinal).Replace("}}", string.Empty, StringComparison.Ordinal);
        return [.. PlaceholderPattern().Matches(unescaped).Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))];
    }

    private static string Describe(HashSet<int> indices) =>
        indices.Count == 0 ? "no placeholder" : string.Join(" ", indices.Order().Select(index => $"{{{index}}}"));

    [GeneratedRegex(@"\{(\d+)(?:,[^}:]*)?(?::[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    private sealed record ResxFile(string Path, string Name, string? Language);

    // Every Strings/*.resx under src, outside bin and obj: X.resx is English, X.<lang>.resx a translation.
    private static List<ResxFile> ResxFiles() =>
        [.. Directory.EnumerateFiles(Path.Combine(DesignFiles.Root, "src"), "*.resx", SearchOption.AllDirectories)
            .Where(path => !BuildFolder().IsMatch(path) && Path.GetFileName(Path.GetDirectoryName(path)) == "Strings")
            .Select(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var dot = name.LastIndexOf('.');
                return dot < 0 ? new ResxFile(path, name, null) : new ResxFile(path, name[..dot], name[(dot + 1)..]);
            })
            .OrderBy(file => file.Path, StringComparer.Ordinal)];

    private static List<string> Languages(IEnumerable<ResxFile> files) =>
        [.. files.Select(file => file.Language).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static string ReswFolder => Path.Combine(DesignFiles.Root, "src", "TenLapse.App", "Strings");

    private static string ReswPath(string language) => Path.Combine(ReswFolder, language, "Resources.resw");

    private static List<string> ReswLanguages() =>
        [.. Directory.EnumerateDirectories(ReswFolder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(language => language != ResourceLanguages.English && File.Exists(ReswPath(language)))
            .Order(StringComparer.Ordinal)];

    private static Dictionary<string, string> Strings(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .Where(data => data.Attribute("type") is null && data.Attribute("mimetype") is null)
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? string.Empty, StringComparer.Ordinal);

    private static string Relative(string path) => Path.GetRelativePath(DesignFiles.Root, path).Replace('\\', '/');

    [GeneratedRegex(@"[\\/](bin|obj)[\\/]")]
    private static partial Regex BuildFolder();
}
