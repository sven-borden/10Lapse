using Microsoft.UI.Xaml;

namespace TenLapse.Gallery;

/// <summary>Reads the Debug-only command line: <c>--gallery</c>, <c>--theme=light|dark</c>, <c>--scroll=PX</c>.</summary>
internal static class GalleryLaunch
{
    private static readonly string[] _args = Environment.GetCommandLineArgs();

    public static bool Requested => _args.Contains("--gallery", StringComparer.OrdinalIgnoreCase);

    public static string? Scroll => Value("--scroll=");

    /// <summary>Forces the application theme; call from the App constructor, before any window exists.</summary>
    public static void ApplyTheme(Application application)
    {
        switch (Value("--theme="))
        {
            case "dark":
                application.RequestedTheme = ApplicationTheme.Dark;
                break;
            case "light":
                application.RequestedTheme = ApplicationTheme.Light;
                break;
        }
    }

    private static string? Value(string prefix) =>
        _args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..].ToLowerInvariant();
}
