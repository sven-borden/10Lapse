using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TenLapse.Tests.Design;

/// <summary>The token dictionaries match DESIGN.md and stay complete for Light, Dark and HighContrast.</summary>
public sealed partial class ThemeDictionaryTests
{
    private static readonly string[] _themes = ["Light", "Dark", "HighContrast"];

    private static readonly string[] _amberKeys = ["GoldenHourAmber", "GoldenHourAmberHover", "GoldenHourAmberPressed", "GoldenHourAmberStroke", "GoldenHourAmberTint", "DeepAmberIndicator"];

    public static TheoryData<string> ThemeFiles => ["Colors.xaml", "ControlOverrides.xaml"];

    [Theory]
    [MemberData(nameof(ThemeFiles))]
    public void HighContrastAndDarkHaveEveryKeyLightHas(string file)
    {
        var document = DesignFiles.LoadTheme(file);
        var light = DesignFiles.ThemeDictionary(document, "Light").Keys.ToHashSet();

        Assert.NotEmpty(light);
        foreach (var theme in new[] { "Dark", "HighContrast" })
        {
            var other = DesignFiles.ThemeDictionary(document, theme).Keys.ToHashSet();
            Assert.Empty(light.Except(other));
            Assert.Empty(other.Except(light));
        }
    }

    [Fact]
    public void EveryColorHasABrushAndEveryBrushHasAColor()
    {
        var document = DesignFiles.LoadTheme("Colors.xaml");
        foreach (var theme in _themes)
        {
            var keys = DesignFiles.ThemeDictionary(document, theme).Keys.ToList();
            var colors = keys.Where(key => key.EndsWith("Color", StringComparison.Ordinal)).Select(key => key[..^"Color".Length]).Order().ToList();
            var brushes = keys.Where(key => key.EndsWith("Brush", StringComparison.Ordinal)).Select(key => key[..^"Brush".Length]).Order().ToList();

            Assert.Equal(colors, brushes);
            Assert.Equal(keys.Count, colors.Count + brushes.Count);
        }
    }

    [Fact]
    public void EveryDesignColorIsInTheDictionaries()
    {
        var colors = DesignFiles.LoadTheme("Colors.xaml");
        var defined = _themes.Take(2)
            .SelectMany(theme => DesignFiles.ThemeDictionary(colors, theme).Values)
            .Concat(DesignFiles.RootEntries(colors).Values)
            .Select(element => element.Value.Trim().ToUpperInvariant())
            .ToHashSet();

        var designColors = DesignFiles.DesignTokens().Where(token => token.Key.StartsWith("colors.", StringComparison.Ordinal)).ToList();

        Assert.Equal(45, designColors.Count);
        var missing = designColors.Where(token => !defined.Contains(token.Value.ToUpperInvariant())).Select(token => $"{token.Key} {token.Value}").ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryDesignColorHasItsNamedKeyWithTheSameValue()
    {
        var colors = DesignFiles.LoadTheme("Colors.xaml");
        var light = DesignFiles.ThemeDictionary(colors, "Light");
        var dark = DesignFiles.ThemeDictionary(colors, "Dark");
        var root = DesignFiles.RootEntries(colors);

        foreach (var (name, value) in DesignFiles.DesignTokens().Where(token => token.Key.StartsWith("colors.", StringComparison.Ordinal)).Select(token => (token.Key["colors.".Length..], token.Value)))
        {
            var isDark = name.EndsWith("-dark", StringComparison.Ordinal);
            var isLight = name.EndsWith("-light", StringComparison.Ordinal);
            var key = DesignFiles.Pascal(isDark ? name[..^"-dark".Length] : isLight ? name[..^"-light".Length] : name) + "Color";
            var entries = isDark ? dark : light;
            var element = entries.TryGetValue(key, out var found) ? found : root[key];
            Assert.True(string.Equals(element.Value.Trim(), value, StringComparison.OrdinalIgnoreCase), $"{name}: expected {value} in {key}, found {element.Value.Trim()}");
        }
    }

    [Fact]
    public void HighContrastMapsToSystemColorsAndNeverToAmber()
    {
        var colors = DesignFiles.LoadTheme("Colors.xaml");
        var highContrast = DesignFiles.ThemeDictionary(colors, "HighContrast");

        foreach (var (key, element) in highContrast.Where(entry => entry.Key.EndsWith("Color", StringComparison.Ordinal)))
        {
            var isSystemColor = element.Name.LocalName == "StaticResource"
                && ((string?)element.Attribute("ResourceKey"))?.StartsWith("SystemColor", StringComparison.Ordinal) == true;
            var isTransparent = element.Name.LocalName == "Color" && element.Value.Trim() == "#00000000";
            Assert.True(isSystemColor || isTransparent, $"HighContrast {key} must be a system color or transparent");
        }

        foreach (var key in _amberKeys)
        {
            var element = highContrast[key + "Color"];
            Assert.DoesNotContain("E8962E", element.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheIndicatorIsDeepAmberInLightAndAmberInDark()
    {
        var colors = DesignFiles.LoadTheme("Colors.xaml");
        var light = DesignFiles.ThemeDictionary(colors, "Light");
        var dark = DesignFiles.ThemeDictionary(colors, "Dark");

        Assert.Equal("#B5620D", light["DeepAmberIndicatorColor"].Value.Trim());
        Assert.Equal(dark["GoldenHourAmberColor"].Value.Trim(), dark["DeepAmberIndicatorColor"].Value.Trim());
    }

    [Theory]
    [InlineData("Light", "Ink", "Layer", 4.5)]
    [InlineData("Light", "InkSecondary", "Layer", 4.5)]
    [InlineData("Light", "InkSecondary", "Card", 4.5)]
    [InlineData("Light", "InkSecondary", "Mica", 4.5)]
    [InlineData("Light", "InkSecondary", "Track", 4.5)]
    [InlineData("Light", "DeepAmberIndicator", "Layer", 3.0)]
    [InlineData("Light", "DeepAmberIndicatorHover", "Layer", 3.0)]
    [InlineData("Light", "DeepAmberIndicatorPressed", "Layer", 3.0)]
    [InlineData("Light", "DeepAmberIndicator", "Mica", 3.0)]
    [InlineData("Light", "DeepAmberIndicatorHover", "Mica", 3.0)]
    [InlineData("Light", "DeepAmberIndicatorPressed", "Mica", 3.0)]
    [InlineData("Light", "DeepAmberIndicator", "Track", 3.0)]
    [InlineData("Light", "DeepAmberIndicatorHover", "Track", 3.0)]
    [InlineData("Light", "DeepAmberIndicatorPressed", "Track", 3.0)]
    [InlineData("Light", "EmberLink", "Layer", 4.5)]
    [InlineData("Light", "EmberLink", "Mica", 4.5)]
    [InlineData("Light", "NightInkOnAmber", "GoldenHourAmber", 7.0)]
    [InlineData("Light", "OnIndicator", "DeepAmberIndicator", 3.0)]
    [InlineData("Light", "OnIndicator", "DeepAmberIndicatorHover", 3.0)]
    [InlineData("Light", "OnIndicator", "DeepAmberIndicatorPressed", 3.0)]
    [InlineData("Light", "Success", "SuccessFill", 4.5)]
    [InlineData("Light", "Caution", "CautionFill", 4.5)]
    [InlineData("Light", "Critical", "CriticalFill", 4.5)]
    [InlineData("Dark", "Ink", "Layer", 4.5)]
    [InlineData("Dark", "InkSecondary", "Layer", 4.5)]
    [InlineData("Dark", "InkSecondary", "Card", 4.5)]
    [InlineData("Dark", "InkSecondary", "Mica", 4.5)]
    [InlineData("Dark", "InkSecondary", "Track", 4.5)]
    [InlineData("Dark", "DeepAmberIndicator", "Layer", 3.0)]
    [InlineData("Dark", "DeepAmberIndicatorHover", "Layer", 3.0)]
    [InlineData("Dark", "DeepAmberIndicatorPressed", "Layer", 3.0)]
    [InlineData("Dark", "DeepAmberIndicator", "Mica", 3.0)]
    [InlineData("Dark", "DeepAmberIndicatorHover", "Mica", 3.0)]
    [InlineData("Dark", "DeepAmberIndicatorPressed", "Mica", 3.0)]
    [InlineData("Dark", "DeepAmberIndicator", "Track", 3.0)]
    [InlineData("Dark", "DeepAmberIndicatorHover", "Track", 3.0)]
    [InlineData("Dark", "DeepAmberIndicatorPressed", "Track", 3.0)]
    [InlineData("Dark", "EmberLink", "Layer", 4.5)]
    [InlineData("Dark", "EmberLink", "Mica", 4.5)]
    [InlineData("Dark", "NightInkOnAmber", "GoldenHourAmber", 7.0)]
    [InlineData("Dark", "OnIndicator", "DeepAmberIndicator", 3.0)]
    [InlineData("Dark", "OnIndicator", "DeepAmberIndicatorHover", 3.0)]
    [InlineData("Dark", "OnIndicator", "DeepAmberIndicatorPressed", 3.0)]
    [InlineData("Dark", "Success", "SuccessFill", 4.5)]
    [InlineData("Dark", "Caution", "CautionFill", 4.5)]
    [InlineData("Dark", "Critical", "CriticalFill", 4.5)]
    public void TextAndIndicatorsMeetTheirContrast(string theme, string foreground, string background, double minimum)
    {
        var entries = DesignFiles.ThemeDictionary(DesignFiles.LoadTheme("Colors.xaml"), theme);

        var ratio = DesignFiles.Contrast(entries[foreground + "Color"].Value.Trim(), entries[background + "Color"].Value.Trim());

        Assert.True(ratio >= minimum, $"{theme} {foreground} on {background} is {ratio:0.00}:1, needs {minimum}:1");
    }

    [Fact]
    public void GoldenHourAmberFailsAsALightIndicator()
    {
        var entries = DesignFiles.ThemeDictionary(DesignFiles.LoadTheme("Colors.xaml"), "Light");

        var ratio = DesignFiles.Contrast(entries["GoldenHourAmberColor"].Value.Trim(), entries["LayerColor"].Value.Trim());

        Assert.True(ratio < 3.0, $"Golden Hour Amber on the light layer is {ratio:0.00}:1; DESIGN.md says 2.26:1, which is why the indicator token exists");
    }

    [Fact]
    public void HighContrastOverridesAreSystemColorsOnlyWithGrayTextWhenDisabled()
    {
        var overrides = DesignFiles.ThemeDictionary(DesignFiles.LoadTheme("ControlOverrides.xaml"), "HighContrast");

        Assert.NotEmpty(overrides);
        foreach (var (key, element) in overrides)
        {
            Assert.Null(element.Attribute("Opacity"));
            var resolved = HighContrastColor(element);
            Assert.True(resolved.StartsWith("SystemColor", StringComparison.Ordinal), $"HighContrast {key} resolves to {resolved}, not a system color");

            if (DisabledKey().IsMatch(key))
            {
                // The knob of a disabled switch that is on sits on a GrayText fill, and a checked toggle button's text is GrayText,
                // so those two take the Window color to stay visible.
                var onGray = key.Contains("KnobFillOn", StringComparison.Ordinal) || key.StartsWith("ToggleButtonBackground", StringComparison.Ordinal);
                var expected = onGray ? "SystemColorWindowColor" : "SystemColorGrayTextColor";
                Assert.True(resolved == expected, $"HighContrast {key} is {resolved}, expected {expected}");
            }
        }
    }

    [Fact]
    public void HighContrastTracksAreWindowTextAndCheckedToggleButtonIsHighlight()
    {
        var overrides = DesignFiles.ThemeDictionary(DesignFiles.LoadTheme("ControlOverrides.xaml"), "HighContrast");

        foreach (var key in new[] { "ProgressBarBackground", "SliderTrackFill" })
        {
            Assert.Equal("SystemColorWindowTextColor", HighContrastColor(overrides[key]));
        }

        Assert.Equal("SystemColorHighlightColor", HighContrastColor(overrides["ToggleButtonBackgroundChecked"]));
        Assert.Equal("SystemColorHighlightTextColor", HighContrastColor(overrides["ToggleButtonForegroundChecked"]));
        Assert.Equal("SystemColorWindowTextColor", HighContrastColor(overrides["ToggleSwitchStrokeOff"]));
        Assert.Equal("SystemColorWindowTextColor", HighContrastColor(overrides["ToggleSwitchKnobFillOff"]));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public void IndicatorHoverAndPressDifferFromRest(string theme)
    {
        var overrides = DesignFiles.ThemeDictionary(DesignFiles.LoadTheme("ControlOverrides.xaml"), theme);
        var families = new[]
        {
            "SliderTrackValueFill", "SliderThumbBackground", "ToggleSwitchFillOn", "ToggleSwitchStrokeOn",
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundStrokeChecked",
            "CheckBoxCheckBackgroundFillIndeterminate", "CheckBoxCheckBackgroundStrokeIndeterminate",
            "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedStroke",
        };

        foreach (var family in families)
        {
            var rest = ColorName(overrides[family]);
            Assert.NotEqual(rest, ColorName(overrides[family + "PointerOver"]));
            Assert.NotEqual(rest, ColorName(overrides[family + "Pressed"]));
        }
    }

    [Fact]
    public void IndicatorTintsComeFromTheRampAndStepAwayFromTheRestColor()
    {
        var colors = DesignFiles.LoadTheme("Colors.xaml");
        var ramp = DesignFiles.IndicatorRamp();
        var light = DesignFiles.ThemeDictionary(colors, "Light");
        var dark = DesignFiles.ThemeDictionary(colors, "Dark");

        foreach (var entries in new[] { light, dark })
        {
            foreach (var token in new[] { "DeepAmberIndicatorHoverColor", "DeepAmberIndicatorPressedColor" })
            {
                Assert.Contains(entries[token].Value.Trim().ToUpperInvariant(), ramp);
            }
        }

        Assert.True(Luminance(light["DeepAmberIndicatorHoverColor"]) > Luminance(light["DeepAmberIndicatorColor"]), "Light hover is a lighter step");
        Assert.True(Luminance(light["DeepAmberIndicatorPressedColor"]) < Luminance(light["DeepAmberIndicatorColor"]), "Light press is a darker step");
        Assert.True(Luminance(dark["DeepAmberIndicatorHoverColor"]) < Luminance(dark["DeepAmberIndicatorColor"]), "Dark hover is the inverse: a darker step");
        Assert.True(Luminance(dark["DeepAmberIndicatorPressedColor"]) > Luminance(dark["DeepAmberIndicatorColor"]), "Dark press is the inverse: a lighter step");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ACheckedToggleButtonIsDistinctFromAnUncheckedOne(string theme)
    {
        var overrides = DesignFiles.ThemeDictionary(DesignFiles.LoadTheme("ControlOverrides.xaml"), theme);

        // Fluent's unchecked fill is in the Control Fill family; the checked fill must not be it, or the white pill (white on white in Light).
        Assert.DoesNotContain(ColorName(overrides["ToggleButtonBackgroundChecked"]), new[] { "ControlFill", "SegmentPill", "Card" });
        Assert.Equal("Ink", ColorName(overrides["ToggleButtonBorderBrushChecked"]));
    }

    [Fact]
    public void ExportButtonStyleDisabledIsNeutralNotAmber()
    {
        var export = DesignFiles.LoadTheme("ControlOverrides.xaml").Root!.Elements(DesignFiles.Presentation + "Style")
            .Single(style => (string?)style.Attribute(DesignFiles.X + "Key") == "ExportButtonStyle");
        var themes = export.Descendants(DesignFiles.Presentation + "ResourceDictionary").Where(dictionary => dictionary.Attribute(DesignFiles.X + "Key") is not null).ToList();

        Assert.Equal(2, themes.Count);
        foreach (var dictionary in themes)
        {
            var disabled = dictionary.Elements().Where(element => ((string?)element.Attribute(DesignFiles.X + "Key"))?.EndsWith("Disabled", StringComparison.Ordinal) == true).ToList();
            Assert.Equal(3, disabled.Count);
            Assert.DoesNotContain(disabled, element => element.ToString().Contains("Amber", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TypographyMatchesTheSpec()
    {
        var styles = DesignFiles.LoadTheme("Typography.xaml").Root!.Elements(DesignFiles.Presentation + "Style")
            .ToDictionary(style => (string)style.Attribute(DesignFiles.X + "Key")!);
        var tokens = DesignFiles.DesignTokens();
        var steps = new[] { "caption", "body", "body-strong", "subtitle", "title", "title-large", "display" };

        foreach (var step in steps)
        {
            var style = styles[DesignFiles.Pascal(step) + "TextStyle"];
            var setters = style.Elements(DesignFiles.Presentation + "Setter").ToDictionary(setter => (string)setter.Attribute("Property")!, setter => (string)setter.Attribute("Value")!);
            var inherited = (string?)style.Attribute("BasedOn") is { } basedOn ? Setters(styles[BasedOnKey(basedOn)]) : [];

            var spec = (string key) => tokens[$"typography.{step}.{key}"];
            Assert.Equal(spec("fontSize").Replace("px", string.Empty, StringComparison.Ordinal), Get("FontSize", setters, inherited));
            Assert.Equal(spec("lineHeight").Replace("px", string.Empty, StringComparison.Ordinal), Get("LineHeight", setters, inherited));
            Assert.Equal(spec("fontWeight") == "600" ? "SemiBold" : "Normal", Get("FontWeight", setters, inherited));
            var display = spec("fontFamily").StartsWith("Segoe UI Variable Display", StringComparison.Ordinal);
            Assert.Equal(display ? "{StaticResource DisplayFontFamily}" : "{StaticResource TextFontFamily}", Get("FontFamily", setters, inherited));

            var tabular = tokens.ContainsKey($"typography.{step}.fontFeature");
            Assert.Equal(tabular, Get("Typography.NumeralAlignment", setters, inherited) == "Tabular");
            Assert.True(styles.ContainsKey(DesignFiles.Pascal(step) + "TabularTextStyle"), $"{step} needs a tabular variant");
        }

        Assert.Equal("-20", Setters(styles["DisplayTextStyle"])["CharacterSpacing"]);
    }

    [Fact]
    public void SpacingAndRadiusMatchTheSpec()
    {
        var layer = DesignFiles.RootEntries(DesignFiles.LoadTheme("Layout.xaml"));
        var tokens = DesignFiles.DesignTokens();

        foreach (var (name, value) in Section(tokens, "spacing"))
        {
            Assert.Equal(Px(value), layer["Spacing" + DesignFiles.Pascal(name)].Value.Trim());
            Assert.Equal(Px(value), layer["Thickness" + DesignFiles.Pascal(name)].Value.Trim());
        }

        foreach (var (name, value) in Section(tokens, "rounded"))
        {
            Assert.Equal(Px(value), layer["Radius" + DesignFiles.Pascal(name)].Value.Trim());
        }

        Assert.Equal(Px(tokens["components.segmented-track.rounded"]), layer["RadiusSegmentedTrack"].Value.Trim());
        Assert.Equal("5", layer["RadiusSegmentedTrack"].Value.Trim());
        Assert.Equal("32", layer["ControlHeight"].Value.Trim());
    }

    [Fact]
    public void MotionMatchesTheSpec()
    {
        var motion = DesignFiles.RootEntries(DesignFiles.LoadTheme("Motion.xaml"));

        Assert.Equal(
            new[] { "0:0:0.083", "0:0:0.167", "0:0:0.25", "0:0:0.4", "0:0:0.7" },
            new[] { "Tap", "Control", "Reveal", "Scene", "Cinema" }.Select(name => motion[$"Motion{name}Duration"].Value.Trim()));
        Assert.Equal(("0.16,1", "0.3,1"), KeySpline(motion["MotionEnterKeySpline"]));
        Assert.Equal(("0.7,0", "0.84,0"), KeySpline(motion["MotionExitKeySpline"]));
        Assert.Equal(("0.65,0", "0.35,1"), KeySpline(motion["MotionMoveKeySpline"]));
    }

    private static (string, string) KeySpline(XElement element) =>
        ((string)element.Attribute("ControlPoint1")!, (string)element.Attribute("ControlPoint2")!);

    private static IEnumerable<(string Name, string Value)> Section(Dictionary<string, string> tokens, string section) =>
        tokens.Where(token => token.Key.StartsWith(section + ".", StringComparison.Ordinal)).Select(token => (token.Key[(section.Length + 1)..], token.Value));

    private static string Px(string value) => value.Replace("px", string.Empty, StringComparison.Ordinal);

    private static string BasedOnKey(string markup) => markup.Trim('{', '}').Split(' ')[1];

    private static Dictionary<string, string> Setters(XElement style) =>
        style.Elements(DesignFiles.Presentation + "Setter").ToDictionary(setter => (string)setter.Attribute("Property")!, setter => (string)setter.Attribute("Value")!);

    private static string Get(string property, Dictionary<string, string> own, Dictionary<string, string> inherited) =>
        own.TryGetValue(property, out var value) ? value : inherited.GetValueOrDefault(property, string.Empty);

    /// <summary>The token or system color a brush's Color points at: "DeepAmberIndicator" for a token, "SystemColorWindowTextColor" for a system color.</summary>
    private static string ColorName(XElement brush)
    {
        var name = ResourceName().Match((string?)brush.Attribute("Color") ?? string.Empty).Groups["name"].Value;
        Assert.NotEmpty(name);
        return name.StartsWith("SystemColor", StringComparison.Ordinal) ? name : name[..^"Color".Length];
    }

    /// <summary>The system color (SystemColor...Color) a HighContrast brush resolves to, through Colors.xaml for tokens.</summary>
    private static string HighContrastColor(XElement brush)
    {
        var name = ResourceName().Match((string?)brush.Attribute("Color") ?? string.Empty).Groups["name"].Value;
        Assert.NotEmpty(name);
        if (name.StartsWith("SystemColor", StringComparison.Ordinal))
        {
            return name;
        }

        var token = DesignFiles.ThemeDictionary(DesignFiles.LoadTheme("Colors.xaml"), "HighContrast")[name];
        return token.Name.LocalName == "StaticResource" ? (string)token.Attribute("ResourceKey")! : "literal " + token.Value.Trim();
    }

    private static double Luminance(XElement color)
    {
        var (r, g, b) = DesignFiles.ParseHex(color.Value.Trim());
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    [GeneratedRegex(@"\{ThemeResource (?<name>\w+)\}")]
    private static partial Regex ResourceName();

    [GeneratedRegex("Disabled(Brush)?$")]
    private static partial Regex DisabledKey();
}
