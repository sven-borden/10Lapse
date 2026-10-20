using System.Globalization;

namespace TenLapse.Core.Text;

/// <summary>A CLDR plural category: which form of a counted word a number takes in a language.</summary>
public enum PluralCategory
{
    /// <summary>The form for one, and in some languages for 0 or 1.5 too ("1 photo", French "0 photo").</summary>
    One,

    /// <summary>The form French, Spanish, Italian and Portuguese use for exact millions ("1 million de photos").</summary>
    Many,

    /// <summary>Every other number, and the form every plural string has (English "2 photos").</summary>
    Other,
}

/// <summary>
/// The plural category of a number in a language (#114), from the CLDR cardinal rules of the languages 10Lapse ships:
/// English, French, German, Spanish, Italian and Portuguese (Brazilian, with European Portuguese's own rule). Hand-written
/// because .NET has no plural rules; another language uses the English rule until its rule is added here.
/// <para>
/// The rules read the number as it is shown: <c>i</c> its whole part, <c>v</c> how many decimals it shows (trailing zeros
/// included), <c>n</c> its value. So 1 and 1.0 differ in English ("1 photo", "1.0 photos"), and the caller passes the
/// number with the decimals it prints: a <see cref="decimal"/> keeps them (<c>1.0m</c> has one).
/// </para>
/// </summary>
public static class Plural
{
    /// <summary>The category of the count <paramref name="count"/> (no decimals) in <paramref name="culture"/>'s language.</summary>
    public static PluralCategory Category(CultureInfo culture, long count) => Category(culture, (decimal)count);

    /// <summary>
    /// The category of <paramref name="number"/> as shown, its decimals included (<c>1.5m</c>, <c>1.0m</c>), in
    /// <paramref name="culture"/>'s language. The sign is ignored.
    /// </summary>
    public static PluralCategory Category(CultureInfo culture, decimal number)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var n = Math.Abs(number);
        var i = decimal.Truncate(n);
        var v = number.Scale;
        var whole = v == 0;

        // fr, es, it, pt: "many" for exact millions without decimals (e = 0 for numbers written out in full).
        var million = whole && i != 0 && i % 1_000_000 == 0;
        return culture.TwoLetterISOLanguageName switch
        {
            // one: i = 0,1
            "fr" => i <= 1 ? PluralCategory.One : million ? PluralCategory.Many : PluralCategory.Other,

            // one: n = 1
            "es" => n == 1 ? PluralCategory.One : million ? PluralCategory.Many : PluralCategory.Other,

            // one: i = 1 and v = 0
            "it" => i == 1 && whole ? PluralCategory.One : million ? PluralCategory.Many : PluralCategory.Other,

            // European Portuguese, one: i = 1 and v = 0; Brazilian (and plain "pt"), one: i = 0..1
            "pt" when IsEuropeanPortuguese(culture) => i == 1 && whole ? PluralCategory.One : million ? PluralCategory.Many : PluralCategory.Other,
            "pt" => i <= 1 ? PluralCategory.One : million ? PluralCategory.Many : PluralCategory.Other,

            // en, de and every language without its own rule here, one: i = 1 and v = 0
            _ => i == 1 && whole ? PluralCategory.One : PluralCategory.Other,
        };
    }

    /// <summary>
    /// The categories <paramref name="culture"/>'s language uses, <see cref="PluralCategory.Other"/> last: one and other
    /// for English, German and every language without its own rule here; one, many and other for French, Spanish,
    /// Italian and Portuguese. A translation's plural keys are among these (the parity tests check it).
    /// </summary>
    public static IReadOnlyList<PluralCategory> Categories(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName is "fr" or "es" or "it" or "pt"
            ? [PluralCategory.One, PluralCategory.Many, PluralCategory.Other]
            : [PluralCategory.One, PluralCategory.Other];
    }

    /// <summary>The key suffix of a category in the resource files: <c>_one</c>, <c>_many</c>, <c>_other</c>.</summary>
    public static string Suffix(PluralCategory category) => category switch
    {
        PluralCategory.One => "_one",
        PluralCategory.Many => "_many",
        PluralCategory.Other => "_other",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown plural category."),
    };

    // pt-PT and the other regions that follow it (every pt region but Brazil).
    private static bool IsEuropeanPortuguese(CultureInfo culture) =>
        culture.Name.Length > 2 && !culture.Name.Equals("pt-BR", StringComparison.OrdinalIgnoreCase);
}
