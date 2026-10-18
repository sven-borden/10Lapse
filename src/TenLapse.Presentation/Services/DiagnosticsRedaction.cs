using System.Text.RegularExpressions;

namespace TenLapse.Presentation.Services;

/// <summary>
/// Takes the account out of the text Copy diagnostics puts on the clipboard, so a pasted report does not name the
/// person: the profile folder in every form a log line may carry it, the user name as a folder name, and the
/// organization in a work OneDrive folder's name. File and folder names below them stay: they are what makes a
/// failure readable, and PRIVACY.md says the log may hold them.
/// </summary>
/// <remarks>
/// What is replaced:
/// <list type="bullet">
/// <item>The profile folder (<c>C:\Users\Jane</c>) and its 8.3 short form (<c>C:\Users\JANEDO~1</c>), with back
/// slashes, doubled back slashes (as escaped in JSON), forward slashes (<c>C:/Users/Jane</c>, which also covers
/// <c>file:///C:/Users/Jane</c>) and URL-escaped spaces (<c>file:///C:/Users/Jane%20Doe</c>): <c>%USERPROFILE%</c>.</item>
/// <item>The user name as a whole folder name anywhere (<c>D:\Backup\Jane\</c>): <c>%USERNAME%</c>.</item>
/// <item><c>OneDrive - Contoso</c>, the folder of a work or school OneDrive: <c>OneDrive - &lt;organization&gt;</c>.</item>
/// </list>
/// Case is ignored throughout, as Windows paths ignore it.
/// </remarks>
public sealed partial class DiagnosticsRedaction
{
    /// <summary>What the profile folder reads as.</summary>
    public const string ProfilePlaceholder = "%USERPROFILE%";

    /// <summary>What the user name reads as where it is a folder name.</summary>
    public const string UserNamePlaceholder = "%USERNAME%";

    /// <summary>What a work OneDrive folder's organization reads as.</summary>
    public const string OrganizationPlaceholder = "<organization>";

    // Shorter than this is not a profile path (a drive root, a typo): replacing it would mangle the text.
    private const int MinimumFolderLength = 4;

    private readonly string[] _profileForms;
    private readonly Regex? _userName;

    /// <param name="profileFolder">The user's profile folder (<c>C:\Users\Jane</c>); null or empty for none.</param>
    /// <param name="userName">The account's user name; null or empty for none.</param>
    /// <param name="shortProfileFolder">The profile folder's 8.3 short form, when Windows has one.</param>
    public DiagnosticsRedaction(string? profileFolder, string? userName = null, string? shortProfileFolder = null)
    {
        var forms = new List<string>();
        foreach (var folder in new[] { profileFolder, shortProfileFolder })
        {
            var trimmed = folder?.Trim().TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(trimmed) || trimmed.Length < MinimumFolderLength)
            {
                continue;
            }

            var forward = trimmed.Replace('\\', '/');
            forms.AddRange([trimmed, trimmed.Replace("\\", "\\\\", StringComparison.Ordinal), forward, forward.Replace(" ", "%20", StringComparison.Ordinal)]);
        }

        // Longest first, so the doubled form is replaced before the single one inside it.
        _profileForms = [.. forms.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(form => form.Length)];

        var name = userName?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            var names = new[] { name, name.Replace(" ", "%20", StringComparison.Ordinal) }.Distinct().Select(Regex.Escape);
            _userName = new Regex(
                $@"(?<=[\\/])(?:{string.Join('|', names)})(?=[\\/""'\s,;)]|$)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>The redaction for the account this process runs as.</summary>
    /// <param name="shortProfileFolder">The profile folder's 8.3 short form (the App asks Windows for it).</param>
    public static DiagnosticsRedaction ForCurrentUser(string? shortProfileFolder = null) =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, shortProfileFolder);

    /// <summary><paramref name="text"/> with the account replaced (see the remarks).</summary>
    public string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var form in _profileForms)
        {
            text = text.Replace(form, ProfilePlaceholder, StringComparison.OrdinalIgnoreCase);
        }

        if (_userName is not null)
        {
            text = _userName.Replace(text, UserNamePlaceholder);
        }

        return OneDriveOrganization().Replace(text, "OneDrive - " + OrganizationPlaceholder);
    }

    // "OneDrive - Contoso Ltd" up to the end of the folder name: characters a file name cannot hold end it.
    [GeneratedRegex(@"OneDrive - [^\\/\r\n""<>|:*?]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OneDriveOrganization();
}
