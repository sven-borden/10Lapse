using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TenLapse.Themes;

/// <summary>
/// Reads token values in code, for the places XAML cannot reach: Composition shadows and the system caption
/// buttons. Colors come from the theme dictionaries of Themes/Colors.xaml for the theme an element shows,
/// numbers from Themes/Layout.xaml. Read them again when <see cref="FrameworkElement.ActualThemeChanged"/> fires.
/// </summary>
public static class ThemeTokens
{
    private static readonly AccessibilitySettings _accessibility = new();

    /// <summary>True while Windows runs a high contrast theme, which maps every token to a system color.</summary>
    public static bool IsHighContrast => _accessibility.HighContrast;

    /// <summary>The color token <paramref name="key"/> (for example "InkColor") as <paramref name="element"/> shows it now.</summary>
    public static Color Color(string key, FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Color(key, element.ActualTheme);
    }

    /// <summary>
    /// The color token <paramref name="key"/> in <paramref name="theme"/> (Light unless Dark), or in HighContrast
    /// while Windows runs a high contrast theme. For what has no element to ask, such as the window's backdrop.
    /// </summary>
    public static Color Color(string key, ElementTheme theme)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var name = IsHighContrast ? "HighContrast" : theme == ElementTheme.Dark ? "Dark" : "Light";
        return Find(Application.Current.Resources, name, key) is Color color
            ? color
            : throw new KeyNotFoundException($"No {name} color token named {key}.");
    }

    /// <summary>
    /// A color token not tied to a theme (for example "DaybreakStarColor" or "NightColor"): the letterbox, the
    /// brand ramp and the Daybreak picture, which look the same in every theme.
    /// </summary>
    public static Color FixedColor(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Application.Current.Resources.TryGetValue(key, out var value) && value is Color color
            ? color
            : throw new KeyNotFoundException($"No color token named {key}.");
    }

    /// <summary>The number token <paramref name="key"/> (for example "ElevationFlyoutBlur").</summary>
    public static double Number(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Application.Current.Resources.TryGetValue(key, out var value) && value is double number
            ? number
            : throw new KeyNotFoundException($"No number token named {key}.");
    }

    // The tokens live in merged dictionaries, whose theme dictionaries the application's own lookup does not
    // expose by theme name, so walk them.
    private static object? Find(ResourceDictionary dictionary, string theme, string key)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed)
            && themed is ResourceDictionary themeDictionary
            && themeDictionary.TryGetValue(key, out var value))
        {
            return value;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (Find(merged, theme, key) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
