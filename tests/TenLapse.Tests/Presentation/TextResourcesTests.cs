using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using TenLapse.Core.Projects;
using TenLapse.Core.Text;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Presentation;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// The resource files behind the text classes (#39): every property reads a string that exists, every string in a
/// file is read by a property, every sentence still formats (also pseudo-localized), and pseudo-localization keeps
/// the placeholders. The English words themselves are pinned by each panel's own tests.
/// </summary>
public sealed partial class TextResourcesTests
{
    public static TheoryData<string> Files() => [.. Holders().Select(holder => holder.Resources.BaseName)];

    [Fact]
    public void EveryTextClassIsFound() =>
        Assert.True(Holders().Count >= 9, "Found: " + string.Join(", ", Holders().Select(holder => holder.Resources.BaseName)));

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryPropertyReadsAString(string baseName)
    {
        var holder = Holder(baseName);
        foreach (var property in holder.Readers.SelectMany(StringProperties))
        {
            var value = (string?)property.GetValue(null);
            Assert.False(string.IsNullOrEmpty(value), $"{property.DeclaringType!.Name}.{property.Name} is empty.");
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryStringOfTheFileIsReadByTheProperty(string baseName)
    {
        var holder = Holder(baseName);
        var properties = holder.Readers.SelectMany(StringProperties).ToDictionary(property => property.Name, StringComparer.Ordinal);
        var keys = holder.Resources.Keys();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.True(properties.TryGetValue(key, out var property), $"{baseName}.resx has {key}, which no property reads.");
            Assert.Equal(holder.Resources.Get(key), (string?)property.GetValue(null));
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryStringFormatsAndKeepsItsPlaceholdersWhenPseudoLocalized(string baseName)
    {
        var holder = Holder(baseName);
        foreach (var key in holder.Resources.Keys())
        {
            var value = holder.Resources.Get(key);
            var arguments = Enumerable.Range(0, Placeholders(value).DefaultIfEmpty(-1).Max() + 1).Select(index => (object)(index + 1)).ToArray();
            var pseudo = PseudoLocalization.Apply(value);

            Assert.Equal(Placeholders(value).Order(), Placeholders(pseudo).Order());
            _ = string.Format(CultureInfo.InvariantCulture, value, arguments);
            _ = string.Format(CultureInfo.InvariantCulture, pseudo, arguments);
        }
    }

    [Fact]
    public void AMissingStringThrowsNamingTheFile()
    {
        var resources = new TextResources("TenLapse.Presentation.Strings.ProjectText", typeof(ProjectText).Assembly);
        var exception = Assert.Throws<System.Resources.MissingManifestResourceException>(() => resources.Get("NoSuchString"));
        Assert.Contains("ProjectText.resx", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PseudoLocalizationAccentsBracketsAndLengthens()
    {
        Assert.Equal("[Åðð þĥöţöš !]", PseudoLocalization.Apply("Add photos"));
        Assert.Equal(string.Empty, PseudoLocalization.Apply(string.Empty));

        var sentence = ProjectText.DiagnosticsCardCaption;
        var pseudo = PseudoLocalization.Apply(sentence);
        Assert.StartsWith("[", pseudo, StringComparison.Ordinal);
        Assert.EndsWith("]", pseudo, StringComparison.Ordinal);
        Assert.InRange(pseudo.Length, sentence.Length * 1.39, sentence.Length * 1.45);
        Assert.DoesNotContain(pseudo, char.IsAsciiLetter);
    }

    [Theory]
    [InlineData("{0} photos → {1} at {2}", "{0}", "{1}", "{2}")]
    [InlineData("Adding {0:N0} of {1} photos…", "{0:N0}", "{1}", null)]
    public void PseudoLocalizationKeepsPlaceholdersWhole(string text, string first, string second, string? third)
    {
        var pseudo = PseudoLocalization.Apply(text);
        Assert.Contains(first, pseudo, StringComparison.Ordinal);
        Assert.Contains(second, pseudo, StringComparison.Ordinal);
        if (third is not null)
        {
            Assert.Contains(third, pseudo, StringComparison.Ordinal);
        }

        Assert.Contains("→", PseudoLocalization.Apply("{0} → {1}"), StringComparison.Ordinal);
        Assert.Equal("[{{ţ}} !]", PseudoLocalization.Apply("{{t}}"));
    }

    private static IEnumerable<int> Placeholders(string format) =>
        Placeholder().Matches(format).Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));

    // {0}, {1:N0}, {2,5}; not an escaped {{0}}.
    [GeneratedRegex(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static IEnumerable<PropertyInfo> StringProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0);

    private static (TextResources Resources, Type[] Readers) Holder(string baseName) =>
        Holders().Single(holder => holder.Resources.BaseName == baseName);

    // Each text class holds its file in a private static field; Core's file is read by TimingWords and Project.
    private static List<(TextResources Resources, Type[] Readers)> Holders()
    {
        var assemblies = new[] { typeof(TextResources).Assembly, typeof(EncoderText).Assembly, typeof(ProjectText).Assembly };
        var holders = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(field => field.FieldType == typeof(TextResources) && !field.Name.StartsWith('<'))
                .Select(field => ((TextResources)field.GetValue(null)!, new[] { type })))
            .ToList();
        var core = (TextResources)typeof(TextResources).Assembly.GetType("TenLapse.Core.Text.CoreText", throwOnError: true)!
            .GetProperty("Strings", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        holders.Add((core, [typeof(TimingWords), typeof(Project)]));
        return holders;
    }
}
