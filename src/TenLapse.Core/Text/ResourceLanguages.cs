namespace TenLapse.Core.Text;

/// <summary>
/// The languages 10Lapse's text ships in (#113, Sven 2026-10-20) and the choice of one of them for a Windows language
/// tag. The app reads its language once at start-up (the first of the app's languages when packaged), maps it here, and
/// sets the .NET UI culture, the window's <c>Language</c> and Win2D's locale to the answer, so the resource files, WinUI
/// and the preview's captions agree on one language.
/// <para>
/// English (<c>en-US</c>) is the neutral language of every <c>.resx</c> and the fallback. The others are neutral cultures
/// (<c>fr</c> serves France, Switzerland, Canada, Belgium...) except Brazilian Portuguese, which is regional: European
/// Portuguese differs too much to share one file, so <c>pt-PT</c> is mapped to the nearest shipped one, <c>pt-BR</c>.
/// </para>
/// <para>
/// A new language adds its tag to <see cref="Shipped"/>, an <c>X.&lt;tag&gt;.resx</c> beside each <c>Strings/X.resx</c> and
/// <c>src/TenLapse.App/Strings/&lt;tag&gt;/Resources.resw</c>; <c>ResourceParityTests</c> fails when the files and this list
/// disagree.
/// </para>
/// </summary>
public static class ResourceLanguages
{
    /// <summary>The fallback and the language of the neutral resource files.</summary>
    public const string English = "en-US";

    /// <summary>Every shipped language, English first. Tags as the resource files and the Strings folders name them.</summary>
    public static IReadOnlyList<string> Shipped { get; } = [English, "fr", "de", "es", "it", "pt-BR"];

    /// <summary>
    /// The shipped language for a BCP-47 tag (<c>fr-CH</c>, <c>pt-PT</c>, <c>de-DE-u-co-phonebk</c>), ignoring case and
    /// accepting <c>_</c> for <c>-</c>: the same tag, else the nearest parent (<c>fr-CH</c> to <c>fr</c>), else the same
    /// language in another region (<c>pt-PT</c> to <c>pt-BR</c>, <c>en-GB</c> to <c>en-US</c>), else English. Never throws;
    /// null, empty or unknown tags give English.
    /// </summary>
    public static string Match(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return English;
        }

        var candidate = tag.Trim().Replace('_', '-');

        // The tag itself, then its parents: fr-CH, fr; zh-Hant-TW, zh-Hant, zh.
        for (var parent = candidate; parent.Length > 0; parent = Parent(parent))
        {
            if (Find(parent) is { } exact)
            {
                return exact;
            }
        }

        // The same language in another region or script.
        var language = Language(candidate);
        foreach (var shipped in Shipped)
        {
            if (string.Equals(Language(shipped), language, StringComparison.OrdinalIgnoreCase))
            {
                return shipped;
            }
        }

        return English;
    }

    private static string? Find(string tag)
    {
        foreach (var shipped in Shipped)
        {
            if (string.Equals(shipped, tag, StringComparison.OrdinalIgnoreCase))
            {
                return shipped;
            }
        }

        return null;
    }

    private static string Parent(string tag)
    {
        var dash = tag.LastIndexOf('-');
        return dash < 0 ? string.Empty : tag[..dash];
    }

    private static string Language(string tag)
    {
        var dash = tag.IndexOf('-');
        return dash < 0 ? tag : tag[..dash];
    }
}
