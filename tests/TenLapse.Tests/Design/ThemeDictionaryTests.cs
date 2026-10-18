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
    public void AmberIsOnlyOnTheAccentButtonAndIndicators()
    {
        var overrides = DesignFiles.LoadTheme("ControlOverrides.xaml");
        foreach (var theme in _themes)
        {
            foreach (var (key, element) in DesignFiles.ThemeDictionary(overrides, theme))
            {
                var token = TokenName().Match((string?)element.Attribute("Color") ?? string.Empty).Groups["name"].Value;
                if (token.StartsWith("GoldenHourAmber", StringComparison.Ordinal))
                {
                    Assert.StartsWith("AccentButton", key, StringComparison.Ordinal);
                }

                Assert.DoesNotContain(token, new[] { "Daylight", "Golden", "Amber", "Rose", "Dusk", "BlueHour", "Night" });
            }
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
    [InlineData("Light", "InkSecondary", "Mica", 4.5)]
    [InlineData("Light", "DeepAmberIndicator", "Layer", 3.0)]
    [InlineData("Light", "EmberLink", "Layer", 4.5)]
    [InlineData("Light", "NightInkOnAmber", "GoldenHourAmber", 7.0)]
    [InlineData("Light", "OnIndicator", "DeepAmberIndicator", 3.0)]
    [InlineData("Dark", "Ink", "Layer", 4.5)]
    [InlineData("Dark", "InkSecondary", "Layer", 4.5)]
    [InlineData("Dark", "DeepAmberIndicator", "Layer", 3.0)]
    [InlineData("Dark", "EmberLink", "Layer", 4.5)]
    [InlineData("Dark", "NightInkOnAmber", "GoldenHourAmber", 7.0)]
    [InlineData("Dark", "OnIndicator", "DeepAmberIndicator", 3.0)]
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

    [GeneratedRegex(@"\{ThemeResource (?<name>\w+?)Color\}")]
    private static partial Regex TokenName();
}
