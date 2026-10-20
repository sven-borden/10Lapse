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
/// The resource files behind the text classes (#39, #114): every property reads a string that exists, every string in a
/// file is read by a property (a plural family <c>X_one</c>/<c>X_other</c> by a <see cref="PluralText"/> property
/// <c>X</c>), every sentence still formats (in the invariant and in a German culture, also pseudo-localized), and
/// pseudo-localization keeps the placeholders. The English words themselves are pinned by each panel's own tests.
/// </summary>
public sealed partial class TextResourcesTests
{
    public static TheoryData<string> Files() => [.. Holders().Select(holder => holder.Resources.BaseName)];

    [Fact]
    public void EveryTextClassIsFound() =>
        Assert.True(Holders().Count >= 10, "Found: " + string.Join(", ", Holders().Select(holder => holder.Resources.BaseName)));

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

        foreach (var property in holder.Readers.SelectMany(PluralProperties))
        {
            var plural = (PluralText)property.GetValue(null)!;
            Assert.Equal(property.Name, plural.Key);
            Assert.False(string.IsNullOrEmpty(plural.For(1)), $"{property.Name} has no form for 1.");
            Assert.False(string.IsNullOrEmpty(plural.For(2)), $"{property.Name} has no form for 2.");
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryStringOfTheFileIsReadByTheProperty(string baseName)
    {
        var holder = Holder(baseName);
        var properties = holder.Readers.SelectMany(StringProperties).ToDictionary(property => property.Name, StringComparer.Ordinal);
        var plurals = holder.Readers.SelectMany(PluralProperties).ToDictionary(property => property.Name, StringComparer.Ordinal);
        var keys = holder.Resources.Keys();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            if (PluralKey().Match(key) is { Success: true } plural)
            {
                Assert.True(plurals.ContainsKey(plural.Groups["family"].Value), $"{baseName}.resx has {key}, which no PluralText property reads.");
                continue;
            }

            Assert.True(properties.TryGetValue(key, out var property), $"{baseName}.resx has {key}, which no property reads.");
            Assert.Equal(holder.Resources.Get(key), (string?)property.GetValue(null));
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryPluralFamilyHasItsOtherFormAndEnglishHasNoMany(string baseName)
    {
        var holder = Holder(baseName);
        var keys = holder.Resources.Keys().ToHashSet(StringComparer.Ordinal);
        foreach (var property in holder.Readers.SelectMany(PluralProperties))
        {
            Assert.Contains(property.Name + "_other", keys);
            Assert.DoesNotContain(property.Name + "_many", keys);

            // The one form says nothing the other form does not: its placeholders are among the other form's.
            if (keys.Contains(property.Name + "_one"))
            {
                var other = Placeholders(holder.Resources.Get(property.Name + "_other")).ToHashSet();
                Assert.All(Placeholders(holder.Resources.Get(property.Name + "_one")), index => Assert.Contains(index, other));
            }
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
            var arguments = Enumerable.Range(0, Placeholders(value).DefaultIfEmpty(-1).Max() + 1).Select(index => (object)(1234.5 + index)).ToArray();
            var pseudo = PseudoLocalization.Apply(value);

            Assert.Equal(Placeholders(value).Order(), Placeholders(pseudo).Order());
            foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.CurrentCulture, CultureInfo.GetCultureInfo("de-DE") })
            {
                _ = string.Format(culture, value, arguments);
                _ = string.Format(culture, pseudo, arguments);
            }
        }
    }

    [Fact]
    public void AMissingStringThrowsNamingTheFile()
    {
        var resources = new TextResources("TenLapse.Presentation.Strings.ProjectText", typeof(ProjectText).Assembly);
        var exception = Assert.Throws<System.Resources.MissingManifestResourceException>(() => resources.Get("NoSuchString"));
        Assert.Contains("ProjectText.resx", exception.Message, StringComparison.Ordinal);
        Assert.Throws<System.Resources.MissingManifestResourceException>(() => resources.Plural("NoSuchFamily").For(2));
    }

    [Fact]
    public void APluralFamilyFallsBackToItsOtherForm()
    {
        // A test-only file (Strings/PluralFallback.resx) with Counted_one and Counted_other and no _many, so the proof
        // reads no shipped translation: whichever forms a real language's file gains cannot break it.
        var resources = new TextResources("TenLapse.Tests.Presentation.Strings.PluralFallback", typeof(TextResourcesTests).Assembly);
        var counted = resources.Plural("Counted");
        var before = CultureInfo.CurrentUICulture;
        try
        {
            // French, Spanish, Italian and Portuguese have a "many" category (exact millions); the file has no such form.
            foreach (var language in new[] { "fr-FR", "es-ES", "it-IT", "pt-BR" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                Assert.Equal(PluralCategory.Many, Plural.Category(CultureInfo.CurrentUICulture, 1_000_000m));
                Assert.Equal("{0} items", counted.For(1_000_000m));
                Assert.Equal("{0} items", counted.For(2m));
            }

            // A form the file has is still chosen: French puts 0 in "one".
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("{0} item", counted.For(0m));
            Assert.Equal("{0} item", counted.For(1m));
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void EnglishPluralFamiliesChooseOneAndOther()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("{0} photo in the project.", ProjectText.ImportDone.For(1));
            Assert.Equal("{0} photos in the project.", ProjectText.ImportDone.For(0));
            Assert.Equal("{0} photos per second", TenLapse.Presentation.Export.ExportText.SpeedPhotos.For(1.0m));
            Assert.Equal("{0} photo per second", TenLapse.Presentation.Export.ExportText.SpeedPhotos.For(1m));
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void PseudoLocalizationAccentsBracketsAndLengthensShortTextMost()
    {
        Assert.Equal("[Åðð þĥöţöš !!!!!]", PseudoLocalization.Apply("Add photos"));
        Assert.Equal(string.Empty, PseudoLocalization.Apply(string.Empty));

        var sentence = ProjectText.DiagnosticsCardCaption;
        var pseudo = PseudoLocalization.Apply(sentence);
        Assert.StartsWith("[", pseudo, StringComparison.Ordinal);
        Assert.EndsWith("]", pseudo, StringComparison.Ordinal);
        Assert.InRange(pseudo.Length, sentence.Length * 1.39, sentence.Length * 1.45);
        Assert.DoesNotContain(pseudo, char.IsAsciiLetter);

        // A label doubles, a short phrase grows by more than half.
        Assert.InRange(PseudoLocalization.Apply("Fill").Length, 8, 10);
        Assert.InRange(PseudoLocalization.Apply("Per second").Length / 10.0, 1.75, 1.9);
        Assert.InRange(PseudoLocalization.Apply("Stop adding photos").Length / 18.0, 1.55, 1.7);
        Assert.True(PseudoLocalization.Growth(3) > PseudoLocalization.Growth(15));
        Assert.True(PseudoLocalization.Growth(15) > PseudoLocalization.Growth(200));
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
        Assert.StartsWith("[{{ţ}} !", PseudoLocalization.Apply("{{t}}"), StringComparison.Ordinal);
    }

    private static IEnumerable<int> Placeholders(string format) =>
        Placeholder().Matches(format).Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));

    // {0}, {1:N0}, {2,5}; not an escaped {{0}}.
    [GeneratedRegex(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    // A plural family's form: X_one, X_many, X_other.
    [GeneratedRegex(@"^(?<family>\w+?)_(?:one|many|other)$")]
    private static partial Regex PluralKey();

    private static IEnumerable<PropertyInfo> StringProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0);

    private static IEnumerable<PropertyInfo> PluralProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(property => property.PropertyType == typeof(PluralText));

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
