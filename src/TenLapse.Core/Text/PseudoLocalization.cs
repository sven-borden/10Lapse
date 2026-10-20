using System.Globalization;
using System.Text;

namespace TenLapse.Core.Text;

/// <summary>
/// Pseudo-localization (#39, #114), a Debug check, never on in a release: every string <see cref="TextResources"/> gives
/// out comes back longer, its letters accented and the whole of it in brackets ("Add photos" reads "[Åðð þĥöţöš !!!!!]"),
/// so a screen shows at a glance what would clip in a longer language and which text does not come from the resource
/// files at all (it stays plain). The growth follows real translation, where short labels grow most: German and French
/// run 35 to 70 % longer than English overall and a one-word label can double. The format placeholders ("{0}",
/// "{1:N0}") are kept as they are, so every sentence still formats.
/// <para>
/// Turning it on also sets the regional format to German (Germany), so a number written with the invariant culture
/// (a dot where German has a comma, "1,240" where German writes "1.240") stands out next to the localized ones.
/// </para>
/// </summary>
public static class PseudoLocalization
{
    private const string Lower = "åƀçðéƒĝĥîĵķļɱñöþǫŕšţûṽŵẋýž";
    private const string Upper = "ÅƁÇÐÉƑĜĤÎĴĶĻṀÑÖÞǪŔŠŢÛṼŴẊÝŽ";

    private static volatile bool _enabled;

    /// <summary>Whether <see cref="TextResources.Get"/> pseudo-localizes what it reads.</summary>
    public static bool IsEnabled => _enabled;

    /// <summary>
    /// Turns it on for the rest of the process and sets the regional format (<see cref="CultureInfo.CurrentCulture"/>,
    /// for this thread and every new one) to de-DE; the UI language stays as it is. The Debug app calls it for
    /// <c>--pseudo-loc</c>, first thing, before any text is read (a few lists are read once and kept).
    /// </summary>
    public static void Enable()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = german;
        CultureInfo.CurrentCulture = german;
        _enabled = true;
    }

    /// <summary>
    /// How much longer a text of <paramref name="visible"/> characters comes out, as a factor, the brackets included:
    /// 2 up to 5 characters, then falling to 1.4 above 70 ("Per second" grows 80 %, a long sentence 40 %).
    /// </summary>
    public static double Growth(int visible) => visible switch
    {
        <= 5 => 2.0,
        <= 10 => 1.8,
        <= 20 => 1.6,
        <= 70 => 1.5,
        _ => 1.4,
    };

    /// <summary>The pseudo-localized form of <paramref name="text"/>; an empty string stays empty.</summary>
    public static string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return text;
        }

        var result = new StringBuilder((text.Length * 3) + 4);
        result.Append('[');
        var visible = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
            {
                // An escaped brace of a format string.
                result.Append(c).Append(c);
                i++;
                visible++;
                continue;
            }

            if (c == '{' && text.IndexOf('}', i) is var end and > 0)
            {
                // A placeholder, kept whole.
                result.Append(text, i, end - i + 1);
                i = end;
                continue;
            }

            result.Append(c switch
            {
                >= 'a' and <= 'z' => Lower[c - 'a'],
                >= 'A' and <= 'Z' => Upper[c - 'A'],
                _ => c,
            });
            visible++;
        }

        // The growth for this length, the brackets and the space included.
        var padding = Math.Max(1, (int)Math.Ceiling(visible * (Growth(visible) - 1)) - 3);
        result.Append(' ').Append('!', padding).Append(']');
        return result.ToString();
    }
}
