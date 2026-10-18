using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace TenLapse.Gallery;

/// <summary>One color token: its key, its value in the current theme and a brush to paint it.</summary>
internal sealed record Swatch(string Key, string Hex, Brush Fill);

/// <summary>One step of the type ramp.</summary>
internal sealed record TypeSample(string Label, string Text, Style Style);

/// <summary>One spacing step as a bar of that width.</summary>
internal sealed record SpacingSample(string Label, double Size);

/// <summary>One radius token as a rounded box.</summary>
internal sealed record RadiusSample(string Label, CornerRadius Radius);

/// <summary>
/// Debug-only window that shows every token: swatches, the type ramp, spacing, radius, elevation and the
/// brand-overridden controls. Open it with <c>TenLapse.App.exe --gallery [--theme=dark|light] [--scroll=PX]</c>.
/// </summary>
internal sealed partial class GalleryWindow : Window
{
    private static readonly string[] _spacingSteps = ["Xxs", "Xs", "Sm", "Md", "Lg", "Xl", "2xl", "3xl", "4xl"];
    private static readonly string[] _radiusNames = ["Thumb", "Control", "SegmentedTrack", "Card", "Overlay", "Round"];

    private static readonly (string Key, string Text)[] _ramp =
    [
        ("Caption", "742 photos, 30 frames per second, 0312 of 0742"),
        ("Body", "Running text, buttons and menu items at fourteen pixels"),
        ("BodyStrong", "Section headers and the selected segment"),
        ("Subtitle", "742 photos → 24.7 s"),
        ("Title", "Screen titles"),
        ("TitleLarge", "62%"),
        ("Display", "10Lapse"),
    ];

    public GalleryWindow(string? scroll)
    {
        InitializeComponent();
        Title = "10Lapse token gallery";
        AppWindow.Resize(new SizeInt32(1500, 1250));

        var resources = Application.Current.Resources;
        var colors = FindToken("Colors.xaml");
        var overrides = FindToken("ControlOverrides.xaml");

        var swatches = new List<Swatch>();
        var unresolved = new List<string>();
        foreach (var key in ColorKeys(colors).Order(StringComparer.Ordinal))
        {
            if (resources.TryGetValue(key, out var value) && value is Color color)
            {
                swatches.Add(new Swatch(key, Hex(color), new SolidColorBrush(color)));
            }
            else
            {
                unresolved.Add(key);
            }
        }

        Swatches.ItemsSource = swatches;
        TypeRamp.ItemsSource = _ramp
            .Select(step => new TypeSample($"{step.Key}TextStyle", step.Text, (Style)resources[$"{step.Key}TextStyle"]))
            .ToList();
        SpacingSamples.ItemsSource = _spacingSteps
            .Select(step => new SpacingSample($"Spacing{step}", (double)resources[$"Spacing{step}"]))
            .ToList();
        RadiusSamples.ItemsSource = _radiusNames
            .Select(name => new RadiusSample($"Radius{name}", (CornerRadius)resources[$"Radius{name}"]))
            .ToList();

        ElevationText.Text =
            $"Elevation, flyout only: blur {resources["ElevationFlyoutBlur"]} offset {resources["ElevationFlyoutOffsetY"]} " +
            $"in ShadowFlyoutColor, then blur {resources["ElevationFlyoutKeyBlur"]} offset {resources["ElevationFlyoutKeyOffsetY"]} " +
            $"in ShadowFlyoutKeyColor. Pill: blur {resources["ElevationPillBlur"]} offset {resources["ElevationPillOffsetY"]}.";

        SummaryText.Text = string.Join("  |  ",
            $"Theme: {Application.Current.RequestedTheme}",
            $"Colors: {swatches.Count} resolved, {unresolved.Count} not",
            ContrastReport(colors),
            OverrideReport(overrides),
            $"AnimationsEnabled: {Themes.MotionTokens.AnimationsEnabled}",
            MotionReport(resources));

        if (double.TryParse(scroll, out var offset))
        {
            Scroller.Loaded += (_, _) => Scroller.ChangeView(null, offset, null, disableAnimation: true);
        }
    }

    private static ResourceDictionary FindToken(string file) =>
        Application.Current.Resources.MergedDictionaries
            .First(dictionary => dictionary.Source?.OriginalString.EndsWith(file, StringComparison.Ordinal) == true);

    private static IEnumerable<string> ColorKeys(ResourceDictionary colors)
    {
        var themed = ((ResourceDictionary)colors.ThemeDictionaries["Light"]).Keys.OfType<string>();
        return themed.Concat(colors.Keys.OfType<string>()).Where(key => key.EndsWith("Color", StringComparison.Ordinal));
    }

    /// <summary>Every key of the Light dictionary must exist in HighContrast and must resolve there.</summary>
    private static string ContrastReport(ResourceDictionary colors)
    {
        var light = (ResourceDictionary)colors.ThemeDictionaries["Light"];
        var highContrast = (ResourceDictionary)colors.ThemeDictionaries["HighContrast"];
        var missing = light.Keys.Count(key => !highContrast.ContainsKey(key));
        var broken = 0;
        foreach (var key in highContrast.Keys.OfType<string>().Where(key => key.EndsWith("Color", StringComparison.Ordinal)))
        {
            try
            {
                _ = (Color)highContrast[key];
            }
            catch (Exception)
            {
                broken++;
            }
        }

        return $"HighContrast: {missing} missing, {broken} unresolved of {highContrast.Count}";
    }

    private static string OverrideReport(ResourceDictionary overrides)
    {
        var light = (ResourceDictionary)overrides.ThemeDictionaries["Light"];
        var highContrast = (ResourceDictionary)overrides.ThemeDictionaries["HighContrast"];
        var missing = light.Keys.Count(key => !highContrast.ContainsKey(key));
        var unresolved = 0;
        foreach (var key in highContrast.Keys.OfType<string>())
        {
            try
            {
                _ = ((SolidColorBrush)highContrast[key]).Color;
            }
            catch (Exception)
            {
                unresolved++;
            }
        }

        return $"Overrides: {light.Count} keys, {missing} missing in HighContrast, {unresolved} unresolved there";
    }

    /// <summary>Resolves every motion resource once, so a bad Duration or KeySpline fails here.</summary>
    private static string MotionReport(ResourceDictionary resources)
    {
        string[] keys =
        [
            "MotionTapDuration", "MotionControlDuration", "MotionRevealDuration", "MotionSceneDuration",
            "MotionCinemaDuration", "MotionEnterKeySpline", "MotionExitKeySpline", "MotionMoveKeySpline",
        ];
        var ok = keys.Count(key => resources.TryGetValue(key, out var value) && value is not null);
        return $"Motion: {ok} of {keys.Length}";
    }

    private static string Hex(Color color) =>
        color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
