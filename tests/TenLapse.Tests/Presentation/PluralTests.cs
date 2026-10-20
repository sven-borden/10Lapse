using System.Globalization;
using TenLapse.Core.Text;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// The CLDR cardinal plural rules of the six shipped languages (#114), at 0, 1, 1.5, 2, 1,000,000 and 1,000,001, and the
/// fallbacks: decimals shown count (1.0 is not 1 in English), and a language without its own rule uses English's.
/// </summary>
public sealed class PluralTests
{
    private const PluralCategory One = PluralCategory.One;
    private const PluralCategory Many = PluralCategory.Many;
    private const PluralCategory Other = PluralCategory.Other;

    [Theory]
    //                  0      1     1.5    2      1e6    1e6+1
    [InlineData("en-US", Other, One, Other, Other, Other, Other)]
    [InlineData("de-DE", Other, One, Other, Other, Other, Other)]
    [InlineData("de-CH", Other, One, Other, Other, Other, Other)]
    [InlineData("fr-FR", One, One, One, Other, Many, Other)]
    [InlineData("fr-CH", One, One, One, Other, Many, Other)]
    [InlineData("es-ES", Other, One, Other, Other, Many, Other)]
    [InlineData("es-MX", Other, One, Other, Other, Many, Other)]
    [InlineData("it-IT", Other, One, Other, Other, Many, Other)]
    [InlineData("pt-BR", One, One, One, Other, Many, Other)]
    [InlineData("pt", One, One, One, Other, Many, Other)]
    [InlineData("pt-PT", Other, One, Other, Other, Many, Other)]
    public void EachLanguageHasItsCldrCategories(string culture, PluralCategory zero, PluralCategory one, PluralCategory oneAndAHalf, PluralCategory two, PluralCategory million, PluralCategory millionAndOne)
    {
        var language = CultureInfo.GetCultureInfo(culture);

        Assert.Equal(zero, Plural.Category(language, 0));
        Assert.Equal(one, Plural.Category(language, 1));
        Assert.Equal(oneAndAHalf, Plural.Category(language, 1.5m));
        Assert.Equal(two, Plural.Category(language, 2));
        Assert.Equal(million, Plural.Category(language, 1_000_000));
        Assert.Equal(millionAndOne, Plural.Category(language, 1_000_001));
    }

    [Theory]
    [InlineData("en-US", Other)]
    [InlineData("de-DE", Other)]
    [InlineData("it-IT", Other)]
    [InlineData("pt-PT", Other)]
    [InlineData("es-ES", One)]
    [InlineData("fr-FR", One)]
    [InlineData("pt-BR", One)]
    public void ADecimalShownWithItsZeroIsNotOneWhereTheRuleCountsDecimals(string culture, PluralCategory expected) =>
        Assert.Equal(expected, Plural.Category(CultureInfo.GetCultureInfo(culture), 1.0m));

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("es-ES")]
    [InlineData("it-IT")]
    [InlineData("pt-BR")]
    public void ManyIsForExactMillionsWithoutDecimals(string culture)
    {
        var language = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(Many, Plural.Category(language, 2_000_000));
        Assert.Equal(Many, Plural.Category(language, 1_000_000_000));
        Assert.Equal(Other, Plural.Category(language, 1_000_000.0m));
        Assert.Equal(Other, Plural.Category(language, 1_500_000));
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("nl-NL")]
    [InlineData("")]
    public void ALanguageWithoutItsOwnRuleUsesTheEnglishOne(string culture)
    {
        var language = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(One, Plural.Category(language, 1));
        Assert.Equal(Other, Plural.Category(language, 0));
        Assert.Equal(Other, Plural.Category(language, 2));
    }

    [Fact]
    public void TheSignIsIgnoredAndTheSuffixesNameTheKeys()
    {
        var english = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal(One, Plural.Category(english, -1));
        Assert.Equal("_one", Plural.Suffix(One));
        Assert.Equal("_many", Plural.Suffix(Many));
        Assert.Equal("_other", Plural.Suffix(Other));
        Assert.Throws<ArgumentNullException>(() => Plural.Category(null!, 1));
    }
}
