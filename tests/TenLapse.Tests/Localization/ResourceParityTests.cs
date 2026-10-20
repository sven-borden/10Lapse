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
/// Plurals (#114: <c>X_one</c>, <c>X_many</c>, <c>X_other</c>): every category the English file has is required, like
/// any key. A language may add a category English lacks only when its CLDR cardinal rule has it (<c>X_many</c> in
/// French, Spanish, Italian and Portuguese, optional there: it falls back to <c>X_other</c>); a category the language
/// never uses (<c>X_few</c>, or <c>X_many</c> in German) is a problem. An added category's placeholders are those of
/// <c>X_other</c>. Where English has placeholders, the translation must also parse as a composite format.
/// </summary>
public sealed partial class ResourceParityTests
{
    private static readonly string[] _pluralCategories = ["zero", "one", "two", "few", "many"];

    // The CLDR cardinal categories of each shipped language (other included). Switch this to #114's Plural rules once
    // that merges, so the test and the runtime share one table.
    private static readonly Dictionary<string, string[]> _languageCategories = new(StringComparer.Ordinal)
    {
        [ResourceLanguages.English] = ["one", "other"],
        ["de"] = ["one", "other"],
        ["fr"] = ["one", "many", "other"],
        ["es"] = ["one", "many", "other"],
        ["it"] = ["one", "many", "other"],
        ["pt-BR"] = ["one", "many", "other"],
    };

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

            problems.AddRange(Compare(Strings(english), Strings(file.Path), file.Language!, Relative(file.Path)));
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
            problems.AddRange(Compare(english, Strings(ReswPath(language)), language, Relative(ReswPath(language))));
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

        var problems = Compare(english, translated, "fr", "X.fr.resx");

        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("misses Dropped", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("has Extra", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Photos_one", StringComparison.Ordinal) && problem.Contains("{0}", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, problem => problem.Contains("Literal", StringComparison.Ordinal) || problem.Contains("Swapped", StringComparison.Ordinal) || problem.Contains("Photos_many", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Kept uses {0} {2}", StringComparison.Ordinal));
    }

    [Fact]
    public void APluralTranslationKeepsTheEnglishCategoriesAndAddsOnlyItsOwn()
    {
        var english = new Dictionary<string, string> { ["Photos_one"] = "{0} photo", ["Photos_other"] = "{0} photos" };

        // English's one and other are required, in every language.
        var missingOne = Compare(english, new Dictionary<string, string> { ["Photos_other"] = "{0} Fotos" }, "de", "X.de.resx");
        Assert.Contains(missingOne, problem => problem.Contains("misses Photos_one", StringComparison.Ordinal));
        var missingOther = Compare(english, new Dictionary<string, string> { ["Photos_one"] = "{0} photo" }, "fr", "X.fr.resx");
        Assert.Contains(missingOther, problem => problem.Contains("misses Photos_other", StringComparison.Ordinal));

        // many where the language has it, and optional there (it falls back to other).
        Assert.Empty(Compare(english, new Dictionary<string, string> { ["Photos_one"] = "{0} foto", ["Photos_many"] = "{0} de fotos", ["Photos_other"] = "{0} fotos" }, "es", "X.es.resx"));
        Assert.Empty(Compare(english, new Dictionary<string, string> { ["Photos_one"] = "{0} photo", ["Photos_other"] = "{0} photos" }, "fr", "X.fr.resx"));
        Assert.Empty(Compare(english, new Dictionary<string, string> { ["Photos_one"] = "{0} foto", ["Photos_many"] = "{0} de fotos", ["Photos_other"] = "{0} fotos" }, "pt-BR", "X.pt-BR.resx"));

        // A category the language never uses.
        var germanMany = Compare(english, new Dictionary<string, string> { ["Photos_one"] = "{0} Foto", ["Photos_many"] = "{0} Fotos", ["Photos_other"] = "{0} Fotos" }, "de", "X.de.resx");
        Assert.Contains(germanMany, problem => problem.Contains("Photos_many", StringComparison.Ordinal) && problem.Contains("de never uses", StringComparison.Ordinal));
        var englishMany = Compare(english, new Dictionary<string, string> { ["Photos_one"] = "{0} photo", ["Photos_many"] = "{0} photos", ["Photos_other"] = "{0} photos" }, ResourceLanguages.English, "Resources.resw");
        Assert.Single(englishMany);
        foreach (var category in new[] { "zero", "two", "few" })
        {
            var key = $"Photos_{category}";
            var problems = Compare(english, new Dictionary<string, string> { ["Photos_one"] = "{0} foto", [key] = "{0} foto", ["Photos_other"] = "{0} foto" }, "it", "X.it.resx");
            Assert.Single(problems);
            Assert.Contains($"{key}: the plural category {category}, which it never uses", problems[0], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ATranslationThatDoesNotParseAsAFormatIsAProblem()
    {
        var english = new Dictionary<string, string> { ["Count"] = "{0} photos", ["Plain"] = "No photo" };

        var problems = Compare(english, new Dictionary<string, string> { ["Count"] = "{0} photos }", ["Plain"] = "Aucune photo }" }, "fr", "X.fr.resx");

        // Plain has no placeholder in English, so it is shown as is and a brace is just text.
        Assert.Single(problems);
        Assert.Contains("Count is not a valid format string", problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyTranslationIsAProblem()
    {
        var problems = Compare(new Dictionary<string, string> { ["Save"] = "Save" }, new Dictionary<string, string> { ["Save"] = " " }, "it", "X.it.resx");
        Assert.Contains(problems, problem => problem.Contains("Save is empty", StringComparison.Ordinal));
    }

    /// <summary>The problems of one translated file, in <paramref name="language"/>, against its English one.</summary>
    internal static List<string> Compare(IReadOnlyDictionary<string, string> english, IReadOnlyDictionary<string, string> translated, string language, string name)
    {
        var problems = new List<string>();
        foreach (var key in english.Keys.Order(StringComparer.Ordinal))
        {
            if (!translated.ContainsKey(key))
            {
                problems.Add($"{name} misses {key}.");
            }
        }

        // A language missing from the table is reported by EveryLanguageIsAShippedOne; here it gets English's categories.
        var categories = _languageCategories.GetValueOrDefault(language, _languageCategories[ResourceLanguages.English]);
        foreach (var (key, value) in translated.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string counterpart;
            if (english.ContainsKey(key))
            {
                counterpart = key;
            }
            else if (PluralCategory(key, english.ContainsKey) is { } category)
            {
                if (!categories.Contains(category, StringComparer.Ordinal))
                {
                    problems.Add($"{name}: {key}: the plural category {category}, which {language} never uses (it has {string.Join(", ", categories)}).");
                    continue;
                }

                counterpart = Stem(key) + "_other";
            }
            else
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
            if (expected.Count > 0 && ParseError(value) is { } error)
            {
                problems.Add($"{name}: {key} is not a valid format string ({error}).");
                continue;
            }

            var found = Placeholders(value);
            if (!expected.SetEquals(found))
            {
                problems.Add($"{name}: {key} uses {Describe(found)}, the English {counterpart} uses {Describe(expected)}.");
            }
        }

        return problems;
    }

    // The category of X_zero|one|two|few|many when English has X_other, else null.
    private static string? PluralCategory(string key, Func<string, bool> englishHas)
    {
        var underscore = key.LastIndexOf('_');
        if (underscore <= 0 || !englishHas(key[..underscore] + "_other"))
        {
            return null;
        }

        var category = key[(underscore + 1)..];
        return _pluralCategories.Contains(category, StringComparer.Ordinal) ? category : null;
    }

    // What string.Format would throw on: a lone brace, an unclosed placeholder.
    private static string? ParseError(string text)
    {
        try
        {
            _ = System.Text.CompositeFormat.Parse(text);
            return null;
        }
        catch (FormatException exception)
        {
            return exception.Message;
        }
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
