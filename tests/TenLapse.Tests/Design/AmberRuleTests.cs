using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TenLapse.Tests.Design;

/// <summary>
/// The amber rule (DESIGN.md Overview): Golden Hour Amber is the Export button's fill and nothing else, and
/// Deep Amber Indicator is the state indicator. Scans every XAML and C# file under src/TenLapse.App.
/// </summary>
public sealed partial class AmberRuleTests
{
    private const string ExportStyleKey = "ExportButtonStyle";

    /// <summary>
    /// Files other than the Themes dictionaries that may use DeepAmberIndicator*: a bespoke indicator such as
    /// the scrubber fill or the playhead ring. Empty today; add the file's path relative to the repository root
    /// when one needs the token, so the exception is visible in review.
    /// </summary>
    private static readonly string[] _bespokeIndicatorFiles = [];

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

    private const string ThemeEntry = "<ResourceDictionary.ThemeDictionaries><ResourceDictionary x:Key=\"Light\">";
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

    /// <summary>The override keys of the controls whose state is the indicator.</summary>
    [GeneratedRegex("^(ProgressBar|ProgressRing|Slider|ToggleSwitch(Fill|Stroke)On|ToggleSwitchKnobFillOn|CheckBox|RadioButton|ListViewItemSelectionIndicator|NavigationViewSelectionIndicator|PivotHeaderItemSelectedPipe)")]
    private static partial Regex IndicatorKey();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();
}
