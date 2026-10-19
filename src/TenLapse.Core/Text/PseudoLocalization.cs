using System.Text;

namespace TenLapse.Core.Text;

/// <summary>
/// Pseudo-localization (#39), a Debug check, never on in a release: every string <see cref="TextResources"/> gives out
/// comes back about 40 % longer, its letters accented and the whole of it in brackets ("Add photos" reads
/// "[Åðð þĥöţöš !]"), so a screen shows at a glance what would clip in a longer language (German runs about a third
/// longer than English) and which text does not come from the resource files at all (it stays plain). The format
/// placeholders ("{0}", "{1:N0}") are kept as they are, so every sentence still formats.
/// </summary>
public static class PseudoLocalization
{
    private const string Lower = "åƀçðéƒĝĥîĵķļɱñöþǫŕšţûṽŵẋýž";
    private const string Upper = "ÅƁÇÐÉƑĜĤÎĴĶĻṀÑÖÞǪŔŠŢÛṼŴẊÝŽ";

    private static volatile bool _enabled;

    /// <summary>Whether <see cref="TextResources.Get"/> pseudo-localizes what it reads.</summary>
    public static bool IsEnabled => _enabled;

    /// <summary>
    /// Turns it on for the rest of the process. The Debug app calls it for <c>--pseudo-loc</c>, first thing, before any
    /// text is read (a few lists are read once and kept).
    /// </summary>
    public static void Enable() => _enabled = true;

    /// <summary>The pseudo-localized form of <paramref name="text"/>; an empty string stays empty.</summary>
    public static string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return text;
        }

        var result = new StringBuilder((text.Length * 2) + 4);
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

        // About 40 % longer than the text itself, the brackets included.
        var padding = Math.Max(1, (int)Math.Ceiling(visible * 0.4) - 3);
        result.Append(' ').Append('!', padding).Append(']');
        return result.ToString();
    }
}
