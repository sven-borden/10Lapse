using TenLapse.Core.Text;

namespace TenLapse.Tests.Localization;

/// <summary>
/// The choice of a shipped language for the app's language at start-up (#113): exact, parent, same language in another
/// region, else English.
/// </summary>
public sealed class ResourceLanguagesTests
{
    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("fr", "fr")]
    [InlineData("de", "de")]
    [InlineData("es", "es")]
    [InlineData("it", "it")]
    [InlineData("pt-BR", "pt-BR")]
    public void AShippedTagIsItself(string tag, string expected) => Assert.Equal(expected, ResourceLanguages.Match(tag));

    [Theory]
    [InlineData("FR", "fr")]
    [InlineData("pt-br", "pt-BR")]
    [InlineData("PT_BR", "pt-BR")]
    [InlineData(" de ", "de")]
    public void CaseSpacesAndUnderscoresDoNotMatter(string tag, string expected) => Assert.Equal(expected, ResourceLanguages.Match(tag));

    [Theory]
    [InlineData("fr-FR", "fr")]
    [InlineData("fr-CH", "fr")]
    [InlineData("fr-CA", "fr")]
    [InlineData("de-DE", "de")]
    [InlineData("de-CH", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("es-ES", "es")]
    [InlineData("es-419", "es")]
    [InlineData("es-MX", "es")]
    [InlineData("it-IT", "it")]
    [InlineData("it-CH", "it")]
    [InlineData("de-DE-u-co-phonebk", "de")]
    [InlineData("pt-BR-u-nu-latn", "pt-BR")]
    [InlineData("sr-Latn-RS", "en-US")]
    public void ARegionalTagFallsToItsParent(string tag, string expected) => Assert.Equal(expected, ResourceLanguages.Match(tag));

    [Theory]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("pt-AO", "pt-BR")]
    [InlineData("pt", "pt-BR")]
    [InlineData("en", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("en-CH", "en-US")]
    public void TheSameLanguageInAnotherRegionIsTheNearest(string tag, string expected) => Assert.Equal(expected, ResourceLanguages.Match(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    [InlineData("ja-JP")]
    [InlineData("nl")]
    [InlineData("zh-Hant-TW")]
    [InlineData("x-generate")]
    [InlineData("not a tag")]
    public void AnythingElseIsEnglish(string? tag) => Assert.Equal(ResourceLanguages.English, ResourceLanguages.Match(tag));

    [Fact]
    public void EnglishComesFirstAndEveryTagIsDistinct()
    {
        Assert.Equal(ResourceLanguages.English, ResourceLanguages.Shipped[0]);
        Assert.Equal(ResourceLanguages.Shipped.Count, ResourceLanguages.Shipped.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryShippedTagIsACultureDotNetKnows()
    {
        foreach (var tag in ResourceLanguages.Shipped)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(tag, predefinedOnly: true);
            Assert.Equal(tag, culture.Name);
        }
    }
}
