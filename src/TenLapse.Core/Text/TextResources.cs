using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;

namespace TenLapse.Core.Text;

/// <summary>
/// One resource file of user-visible text (#39): a <c>.resx</c> in a project's <c>Strings</c> folder, embedded in its
/// assembly and read in the user's UI language, English when there is no translation (English is the neutral language,
/// and the only one in 3.0). Each text class (<c>ProjectText</c>, <c>ExportText</c>, <see cref="Timing.TimingWords"/>, ...)
/// holds one and reads every sentence through it, named after the property that asks:
/// <code>public static string AddPhotos => _strings.Get();</code>
/// so call sites and tests read the same properties as before.
/// <para>
/// A translation adds files: an <c>X.&lt;lang&gt;.resx</c> beside each <c>Strings/X.resx</c> (<c>ProjectText.de.resx</c>),
/// and <c>src/TenLapse.App/Strings/&lt;lang&gt;/Resources.resw</c>, which carries the manifest's description and declares
/// the language to the package and the Store. The first translation also changes code, as <c>docs/STATUS.md</c> lists:
/// the UI culture taken from the app's language list (not the Windows display language), plural forms beyond
/// one and many, and numbers written in the user's culture where they are invariant now.
/// </para>
/// <para>
/// Log lines, error contexts, Copy diagnostics and exception messages stay in code, in English: the log is for the
/// developer and for bug reports, not for the user. Debug-only text stays in code too.
/// </para>
/// </summary>
public sealed class TextResources
{
    private readonly ResourceManager _manager;

    /// <param name="baseName">The resource's manifest name without <c>.resources</c> ("TenLapse.Presentation.Strings.ProjectText").</param>
    /// <param name="assembly">The assembly that embeds it.</param>
    public TextResources(string baseName, Assembly assembly)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseName);
        ArgumentNullException.ThrowIfNull(assembly);
        _manager = new ResourceManager(baseName, assembly);
        BaseName = baseName;
    }

    /// <summary>The resource's manifest name, for messages and tests.</summary>
    public string BaseName { get; }

    /// <summary>
    /// The string named <paramref name="key"/> (the calling property's name when left out), in the current UI language,
    /// pseudo-localized when <see cref="PseudoLocalization.IsEnabled"/>.
    /// </summary>
    /// <exception cref="MissingManifestResourceException">The file has no such string, a bug a test catches.</exception>
    public string Get([CallerMemberName] string key = "")
    {
        var value = _manager.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new MissingManifestResourceException($"{BaseName}.resx has no string named {key}.");
        return PseudoLocalization.IsEnabled ? PseudoLocalization.Apply(value) : value;
    }

    /// <summary>Every key of the English file, for the tests that check each one is used.</summary>
    public IReadOnlyList<string> Keys()
    {
        var set = _manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)
            ?? throw new MissingManifestResourceException($"{BaseName}.resources is not embedded.");
        return [.. set.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key).Order(StringComparer.Ordinal)];
    }
}
