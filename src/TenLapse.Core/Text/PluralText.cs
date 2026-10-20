using System.Globalization;

namespace TenLapse.Core.Text;

/// <summary>
/// A counted sentence (#114): one string per plural category in the resource file, <c>X_one</c>, <c>X_many</c> and
/// <c>X_other</c>, chosen by <see cref="Plural.Category(CultureInfo, decimal)"/> in the UI language. A text class
/// exposes it as a property named <c>X</c>:
/// <code>public static PluralText SkippedPhotos => _strings.Plural();</code>
/// <c>X_other</c> is always there; a category a language's file does not have falls back to <c>X_other</c>. English
/// has <c>_one</c> and <c>_other</c>; French, Spanish, Italian and Portuguese add <c>_many</c> (exact millions).
/// </summary>
public sealed class PluralText
{
    private readonly TextResources _resources;

    internal PluralText(TextResources resources, string key)
    {
        _resources = resources;
        Key = key;
    }

    /// <summary>The family's name, the key without its category suffix ("SkippedPhotos").</summary>
    public string Key { get; }

    /// <summary>The format string for <paramref name="number"/> as shown (its decimals included), in the current UI language.</summary>
    public string For(decimal number) => _resources.GetPlural(Key, number);

    /// <summary>
    /// The sentence for <paramref name="number"/>: the string of its category filled with <paramref name="arguments"/>
    /// in <paramref name="culture"/> (the current culture when null), which formats the numbers among them.
    /// </summary>
    public string Format(CultureInfo? culture, decimal number, params object?[] arguments) =>
        string.Format(culture ?? CultureInfo.CurrentCulture, For(number), arguments);
}
