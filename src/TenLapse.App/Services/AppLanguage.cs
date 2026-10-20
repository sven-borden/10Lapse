using System.Globalization;
using TenLapse.Core.Text;

namespace TenLapse.Services;

/// <summary>
/// The language the app speaks, chosen once at start-up (#113) so the resource files (.NET), WinUI's own strings (MRT)
/// and Win2D's text agree. Packaged, it is the first of <c>Windows.Globalization.ApplicationLanguages.Languages</c>:
/// Windows ranks the user's languages (or the app's own language in Settings, Apps) against the languages the package
/// declares. Unpackaged (Debug), it is the Windows display language .NET starts with. Either is mapped to a shipped
/// language by <see cref="ResourceLanguages.Match"/>.
/// <para>
/// Only the UI language is set. <see cref="CultureInfo.CurrentCulture"/> stays the user's regional format (numbers,
/// dates), which may differ from the language (English text, Swiss formats).
/// </para>
/// <para>
/// It is read once: a few lists of labels are read on first use and kept (ShellLayout, the Frame and Timing panels), so
/// a language change in Windows takes effect at the next start, as Windows itself says for an app's language.
/// </para>
/// </summary>
internal static class AppLanguage
{
    /// <summary>The language's culture: English until <see cref="Apply"/> runs.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(ResourceLanguages.English);

    /// <summary>The language's tag, for <c>FrameworkElement.Language</c> and Win2D's <c>LocaleName</c> ("fr", "pt-BR").</summary>
    public static string Tag => Culture.Name;

    /// <summary>
    /// Chooses the language and sets the .NET UI culture of this thread and of every thread started after it. Call it
    /// first, before anything reads text. Never throws: a language list Windows cannot give falls back to the display
    /// language, and the returned note says so for the log.
    /// </summary>
    /// <param name="requested">A tag that wins over Windows' choice (Debug <c>--lang</c>), or null.</param>
    /// <returns>A log line naming the language and where it came from.</returns>
    public static string Apply(string? requested)
    {
        var (source, origin) = requested is not null ? (requested, "the --lang switch") : WindowsLanguage();
        var culture = CultureInfo.GetCultureInfo(ResourceLanguages.Match(source));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Culture = culture;
        return $"Language {culture.Name}, from '{source}' ({origin}); regional format {CultureInfo.CurrentCulture.Name}.";
    }

    private static (string Tag, string Origin) WindowsLanguage()
    {
        if (AppEnvironment.HasPackageIdentity)
        {
            try
            {
                var languages = Windows.Globalization.ApplicationLanguages.Languages;
                if (languages.Count > 0)
                {
                    return (languages[0], "the app's languages");
                }
            }
            catch (Exception exception)
            {
                return (CultureInfo.CurrentUICulture.Name, $"the display language: the app's languages could not be read, {exception.GetType().Name} 0x{exception.HResult:X8}");
            }
        }

        return (CultureInfo.CurrentUICulture.Name, "the display language");
    }
}
