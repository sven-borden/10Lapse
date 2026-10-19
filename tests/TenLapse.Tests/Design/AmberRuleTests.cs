using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TenLapse.Tests.Design;

/// <summary>
/// The amber rule (DESIGN.md Overview): Golden Hour Amber is the fill of the one accent button per screen
/// (DESIGN.md), carried by ExportButtonStyle and nothing else, and Deep Amber Indicator is the state indicator.
/// Scans every XAML and C# file under src/TenLapse.App, for the amber tokens and for the accent button styles.
/// </summary>
public sealed partial class AmberRuleTests
{
    private const string ExportStyleKey = "ExportButtonStyle";

    /// <summary>
    /// The views that may show a screen's one accent button (DESIGN.md, Colors: Export video, Choose folder on
    /// first launch, Play on the finished state) through ExportButtonStyle or AccentActionButtonStyle, and how many
    /// such buttons each may hold. Add the file here when a screen gets its accent button, so it is seen in review.
    /// </summary>
    private static readonly Dictionary<string, int> _accentButtonFiles = new(StringComparer.Ordinal)
    {
        // The Export video slot in the command bar (#24).
        ["src/TenLapse.App/Shell/ShellView.xaml"] = 1,

        // #29: Choose folder on the first-launch drop zone, which has no Export button.
        ["src/TenLapse.App/Shell/FirstLaunchView.xaml"] = 1,

        // The design gallery, which is not built into the app, shows the Export button at rest and disabled side by side.
        ["src/TenLapse.App/Gallery/GalleryWindow.xaml"] = 2,
    };

    /// <summary>
    /// Files other than the Themes dictionaries that may use DeepAmberIndicator*: a bespoke indicator such as
    /// the scrubber fill or the playhead ring. Add the file's path relative to the repository root when one needs
    /// the token, so the exception is visible in review.
    /// </summary>
    private static readonly string[] _bespokeIndicatorFiles =
    [
        // #29: the drop zone's drag-over outline (XAML) and the ring on Daybreak's current thumbnail (code).
        "src/TenLapse.App/Shell/FirstLaunchView.xaml",
        "src/TenLapse.App/Shell/FirstLaunchView.xaml.cs",

        // #19: the scrubber's played part and knob dot, with their hover and pressed tints.
        "src/TenLapse.App/Preview/Scrubber.xaml",

        // #20: the playhead ring on the filmstrip thumbnail the preview shows.
        "src/TenLapse.App/Filmstrip/FilmstripThumb.xaml",
    ];

    [Fact]
    public void AmberAppearsOnlyWhereTheRuleAllowsIt()
    {
        var files = DesignFiles.AppSources(".xaml", ".cs").ToList();

        Assert.NotEmpty(files);
        var violations = files
            .SelectMany(file => FindViolations(Path.GetRelativePath(DesignFiles.Root, file), File.ReadAllText(file)))
            .ToList();

        Assert.True(violations.Count == 0, "Amber used outside its rule:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ExportButtonStyleIsWhereTheAmberLives()
    {
        var text = File.ReadAllText(Path.Combine(DesignFiles.ThemesFolder, "ControlOverrides.xaml"));
        var document = XDocument.Parse(text);
        var export = ExportStyle(document);

        Assert.Contains("GoldenHourAmber", export.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("DeepAmberIndicator", export.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/TenLapse.App/MainWindow.xaml", "<Border Background=\"{ThemeResource GoldenHourAmberBrush}\" />", 1)]
    [InlineData("src/TenLapse.App/Pages/ExportPage.xaml", "<Button Foreground=\"{ThemeResource GoldenHourAmberHoverBrush}\" />", 1)]
    [InlineData("src/TenLapse.App/MainWindow.xaml.cs", "var brush = Resources[\"GoldenHourAmberBrush\"];", 1)]
    [InlineData("src/TenLapse.App/MainWindow.xaml", "<Rectangle Fill=\"{ThemeResource DeepAmberIndicatorBrush}\" />", 1)]
    [InlineData("src/TenLapse.App/Themes/Typography.xaml", "<SolidColorBrush Color=\"{ThemeResource DeepAmberIndicatorColor}\" />", 1)]
    [InlineData("src/TenLapse.App/Themes/ControlOverrides.xaml", ThemeEntry + "<SolidColorBrush x:Key=\"AccentButtonBackground\" Color=\"{ThemeResource GoldenHourAmberColor}\" />" + ThemeEntryEnd, 1)]
    [InlineData("src/TenLapse.App/Themes/ControlOverrides.xaml", ThemeEntry + "<SolidColorBrush x:Key=\"ButtonBackground\" Color=\"{ThemeResource DeepAmberIndicatorColor}\" />" + ThemeEntryEnd, 1)]
    [InlineData("src/TenLapse.App/Themes/ControlOverrides.xaml", ThemeEntry + "<SolidColorBrush x:Key=\"SliderThumbBackground\" Color=\"{ThemeResource DeepAmberIndicatorColor}\" />" + ThemeEntryEnd, 0)]
    [InlineData("src/TenLapse.App/Themes/ControlOverrides.xaml", "<Style x:Key=\"ExportButtonStyle\"><Setter Property=\"Background\" Value=\"{ThemeResource GoldenHourAmberBrush}\" /></Style>", 0)]
    [InlineData("src/TenLapse.App/Themes/ControlOverrides.xaml", "<Style x:Key=\"ExportButtonStyle\"><Setter Property=\"Background\" Value=\"{ThemeResource DeepAmberIndicatorBrush}\" /></Style>", 1)]
    [InlineData("src/TenLapse.App/Themes/Colors.xaml", "<SolidColorBrush x:Key=\"GoldenHourAmberBrush\" Color=\"{ThemeResource GoldenHourAmberColor}\" />", 0)]
    [InlineData("src/TenLapse.App/MainWindow.xaml", "<!-- GoldenHourAmber is only for Export --><Border />", 0)]
    [InlineData("src/TenLapse.App/MainWindow.xaml.cs", "// GoldenHourAmber is only for Export\nvar x = 1;", 0)]
    public void TheScannerFlagsAmberOutsideItsRule(string path, string content, int expected)
    {
        var text = path.EndsWith(".xaml", StringComparison.Ordinal)
            ? $"<ResourceDictionary xmlns=\"{DesignFiles.Presentation}\" xmlns:x=\"{DesignFiles.X}\">{content}</ResourceDictionary>"
            : content;

        Assert.Equal(expected, FindViolations(path, text).Count);
    }

    [Fact]
    public void TheAccentButtonStylesAppearOnlyInTheirViewsOncePerScreen()
    {
        var violations = DesignFiles.AppSources(".xaml", ".cs")
            .SelectMany(file => FindAccentButtonViolations(Path.GetRelativePath(DesignFiles.Root, file), File.ReadAllText(file)))
            .ToList();

        Assert.True(violations.Count == 0, "Accent button outside its rule:\n" + string.Join('\n', violations));
    }

    [Theory]
    [InlineData("src/TenLapse.App/Shell/FirstLaunchView.xaml", "<Button Style=\"{StaticResource AccentActionButtonStyle}\" />", 0)]
    [InlineData("src/TenLapse.App/Shell/FirstLaunchView.xaml", "<Button Style=\"{StaticResource AccentActionButtonStyle}\" /><Button Style=\"{StaticResource ExportButtonStyle}\" />", 1)]
    [InlineData("src/TenLapse.App/Pages/SettingsPage.xaml", "<Button Style=\"{StaticResource AccentActionButtonStyle}\" />", 1)]
    [InlineData("src/TenLapse.App/Pages/SettingsPage.xaml", "<Style x:Key=\"Mine\" TargetType=\"Button\" BasedOn=\"{StaticResource ExportButtonStyle}\" />", 1)]
    [InlineData("src/TenLapse.App/Pages/SettingsPage.xaml", "<!-- not ExportButtonStyle --><Button />", 0)]
    [InlineData("src/TenLapse.App/Shell/ShellView.xaml.cs", "button.Style = (Style)Resources[\"AccentActionButtonStyle\"];", 1)]
    [InlineData("src/TenLapse.App/Shell/ShellView.xaml.cs", "// AccentActionButtonStyle lives in ControlOverrides\nvar x = 1;", 0)]
    [InlineData("src/TenLapse.App/Themes/ControlOverrides.xaml", "<Style x:Key=\"AccentActionButtonStyle\" TargetType=\"Button\" BasedOn=\"{StaticResource ExportButtonStyle}\" />", 0)]
    public void TheAccentButtonScannerCountsEveryUse(string path, string content, int expected)
    {
        var text = path.EndsWith(".xaml", StringComparison.Ordinal)
            ? $"<ResourceDictionary xmlns=\"{DesignFiles.Presentation}\" xmlns:x=\"{DesignFiles.X}\">{content}</ResourceDictionary>"
            : content;

        Assert.Equal(expected, FindAccentButtonViolations(path, text).Count);
    }

    /// <summary>
    /// The file's uses of ExportButtonStyle and AccentActionButtonStyle beyond what <see cref="_accentButtonFiles"/>
    /// allows it (none for a file not listed). ControlOverrides.xaml, where the styles are defined, is not counted.
    /// </summary>
    internal static List<string> FindAccentButtonViolations(string relativePath, string text)
    {
        var path = relativePath.Replace('\\', '/');
        if (path.Equals("src/TenLapse.App/Themes/ControlOverrides.xaml", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        int uses;
        if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            var document = XDocument.Parse(text);
            document.DescendantNodes().OfType<XComment>().ToList().ForEach(comment => comment.Remove());
            uses = document.Descendants().SelectMany(element => element.Attributes()).Count(attribute => AccentStyle().IsMatch(attribute.Value));
        }
        else
        {
            uses = AccentStyle().Matches(BlockComment().Replace(LineComment().Replace(text, string.Empty), string.Empty)).Count;
        }

        var allowed = _accentButtonFiles.GetValueOrDefault(path);
        return uses <= allowed
            ? []
            : [$"{path}: {uses} use(s) of an accent button style where {allowed} may be (DESIGN.md: one accent button per screen)"];
    }

    private const string ThemeEntry ="<ResourceDictionary.ThemeDictionaries><ResourceDictionary x:Key=\"Light\">";
    private const string ThemeEntryEnd = "</ResourceDictionary></ResourceDictionary.ThemeDictionaries>";

    /// <summary>The offending lines of one file: its repository-relative path, then what is wrong.</summary>
    internal static List<string> FindViolations(string relativePath, string text)
    {
        var path = relativePath.Replace('\\', '/');
        var themes = "src/TenLapse.App/Themes/";
        if (path.Equals(themes + "Colors.xaml", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var violations = new List<string>();
        if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            var document = XDocument.Parse(text, LoadOptions.SetLineInfo);
            document.DescendantNodes().OfType<XComment>().ToList().ForEach(comment => comment.Remove());
            var isOverrides = path.Equals(themes + "ControlOverrides.xaml", StringComparison.OrdinalIgnoreCase);

            var export = document.Descendants(DesignFiles.Presentation + "Style")
                .FirstOrDefault(style => (string?)style.Attribute(DesignFiles.X + "Key") == ExportStyleKey);
            if (isOverrides && export is not null)
            {
                if (export.ToString().Contains("DeepAmberIndicator", StringComparison.Ordinal))
                {
                    violations.Add($"{path}: {ExportStyleKey} uses DeepAmberIndicator; the Export button is Golden Hour Amber only");
                }

                export.Remove();
            }

            foreach (var element in document.Descendants())
            {
                var key = (string?)element.Attribute(DesignFiles.X + "Key");
                foreach (var value in element.Attributes().Select(attribute => attribute.Value))
                {
                    if (value.Contains("GoldenHourAmber", StringComparison.Ordinal))
                    {
                        violations.Add($"{path}{Line(element)}: GoldenHourAmber outside {ExportStyleKey} ({key ?? element.Name.LocalName})");
                    }

                    if (value.Contains("DeepAmberIndicator", StringComparison.Ordinal) && !IndicatorMayUseIt(path, isOverrides, key))
                    {
                        violations.Add($"{path}{Line(element)}: DeepAmberIndicator outside the indicator override keys ({key ?? element.Name.LocalName})");
                    }
                }
            }
        }
        else
        {
            var code = BlockComment().Replace(LineComment().Replace(text, string.Empty), string.Empty);
            if (code.Contains("GoldenHourAmber", StringComparison.Ordinal))
            {
                violations.Add($"{path}: GoldenHourAmber in code");
            }

            if (code.Contains("DeepAmberIndicator", StringComparison.Ordinal) && !_bespokeIndicatorFiles.Contains(path))
            {
                violations.Add($"{path}: DeepAmberIndicator in code");
            }
        }

        return violations;
    }

    private static bool IndicatorMayUseIt(string path, bool isOverrides, string? key) =>
        _bespokeIndicatorFiles.Contains(path) || (isOverrides && key is not null && IndicatorKey().IsMatch(key));

    private static XElement ExportStyle(XDocument document) =>
        document.Root!.Elements(DesignFiles.Presentation + "Style").Single(style => (string?)style.Attribute(DesignFiles.X + "Key") == ExportStyleKey);

    private static string Line(XElement element) =>
        element is IXmlLineInfo info && info.HasLineInfo() ? $" (line {info.LineNumber})" : string.Empty;

    /// <summary>The override keys of the controls whose state is the indicator (a select's list marks its selected entry with the same pip as a list).</summary>
    [GeneratedRegex("^(ProgressBar|ProgressRing|Slider|ToggleSwitch(Fill|Stroke)On|ToggleSwitchKnobFillOn|CheckBox|RadioButton|ListViewItemSelectionIndicator|NavigationViewSelectionIndicator|PivotHeaderItemSelectedPipe|ComboBoxItemPillFill)")]
    private static partial Regex IndicatorKey();

    [GeneratedRegex(@"\b(ExportButtonStyle|AccentActionButtonStyle)\b")]
    private static partial Regex AccentStyle();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();
}
