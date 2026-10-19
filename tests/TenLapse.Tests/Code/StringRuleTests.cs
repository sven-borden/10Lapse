using System.Text.RegularExpressions;
using System.Xml.Linq;
using TenLapse.Tests.Design;

namespace TenLapse.Tests.Code;

/// <summary>
/// Every user-visible string comes from the resource files (#39), so a translation is only new files. The XAML binds
/// its text to the text classes (<c>ProjectText</c>, <c>ExportText</c>, ...), which read <c>Strings/*.resx</c> through
/// <c>TextResources</c>. These tests fail on a literal user-visible string in a XAML text attribute or in the App's
/// code, and on a literal sentence in a text class whose summary does not say it is for the log. Allowed anywhere:
/// text without letters (numbers, glyphs, punctuation) and the brand name. The Debug-only token gallery and the
/// Debugging folder are not checked.
/// </summary>
public sealed partial class StringRuleTests
{
    private const string Brand = "10Lapse";

    // The XAML properties a user reads or a screen reader speaks.
    private static readonly HashSet<string> _textProperties = new(StringComparer.Ordinal)
    {
        "Text", "Content", "Header", "PlaceholderText", "Title", "Message", "Label", "Description", "OnContent", "OffContent",
        "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText", "KeyboardAcceleratorTextOverride",
        "ToolTipService.ToolTip", "AutomationProperties.Name", "AutomationProperties.HelpText", "AutomationProperties.FullDescription",
        "AutomationProperties.AcceleratorKey", "AutomationProperties.AccessKey", "AutomationProperties.LocalizedControlType",
        "AutomationProperties.ItemStatus", "AutomationProperties.ItemType",
    };

    // The value types whose text in XAML is never words (a number, a color, a length).
    private static readonly HashSet<string> _valueElements = new(StringComparer.Ordinal)
    {
        "Double", "Int32", "Boolean", "Color", "Thickness", "CornerRadius", "GridLength", "FontFamily", "FontWeight", "Duration", "KeySpline", "TimeSpan",
    };

    [Fact]
    public void NoXamlHasALiteralUserVisibleString()
    {
        var offenders = AppFiles("*.xaml")
            .SelectMany(file => XamlViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Bind the text to a property of a text class (ProjectText, ExportText, ...) that reads its .resx:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void NoAppCodeSetsALiteralUserVisibleString()
    {
        var offenders = AppFiles("*.cs")
            .SelectMany(file => CodeViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Take the text from a text class (ProjectText, ExportText, ...) that reads its .resx:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void ATextClassKeepsOnlyLogLinesAsLiterals()
    {
        var textClasses = SourceFiles("*.cs")
            .Where(file => File.ReadAllText(file) is var source && (source.Contains("TextResources _strings", StringComparison.Ordinal) || source.Contains("CoreText.Strings.Get()", StringComparison.Ordinal)))
            .ToList();
        Assert.True(textClasses.Count >= 9, "The text classes were not found: " + string.Join(", ", textClasses.Select(Relative)));

        var offenders = textClasses
            .SelectMany(file => TextClassViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Move the sentence to the class's .resx and read it with _strings.Get(), or say in its summary that it is a log line or an error context:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void TheManifestKeepsTheStoreNameAndReadsItsDescriptionFromTheResw()
    {
        var root = Path.Combine(DesignFiles.Root, "src", "TenLapse.App");
        var manifest = XDocument.Load(Path.Combine(root, "Package.appxmanifest"));
        var visual = manifest.Descendants().Single(element => element.Name.LocalName == "VisualElements");
        var properties = manifest.Descendants().Single(element => element.Name.LocalName == "DisplayName");
        Assert.Equal(Brand, properties.Value);
        Assert.Equal(Brand, (string?)visual.Attribute("DisplayName"));

        var description = (string?)visual.Attribute("Description");
        Assert.NotNull(description);
        Assert.StartsWith("ms-resource:", description, StringComparison.Ordinal);
        var resw = XDocument.Load(Path.Combine(root, "Strings", "en-US", "Resources.resw"));
        var entry = resw.Root!.Elements("data").SingleOrDefault(data => (string?)data.Attribute("name") == description["ms-resource:".Length..]);
        Assert.NotNull(entry);
        Assert.False(string.IsNullOrWhiteSpace(entry.Element("value")?.Value));
    }

    [Theory]
    [InlineData("<TextBlock Text=\"Add photos\" />")]
    [InlineData("<Button Content=\"Export video\" />")]
    [InlineData("<Ellipse AutomationProperties.Name=\"Unsaved changes\" />")]
    [InlineData("<Button ToolTipService.ToolTip=\"Settings\" />")]
    [InlineData("<TextBox PlaceholderText=\"Type a rate\" Header=\"Rate\" />")]
    [InlineData("<Button AutomationProperties.AcceleratorKey=\"Control+E\" />")]
    [InlineData("<TextBlock>Drop photos here</TextBlock>")]
    [InlineData("<Button><Button.Content>Play</Button.Content></Button>")]
    [InlineData("<Style TargetType=\"TextBlock\"><Setter Property=\"Text\" Value=\"Loading\" /></Style>")]
    public void TheXamlScannerFlagsALiteral(string element) => Assert.NotEmpty(XamlViolations(Wrap(element)));

    [Theory]
    [InlineData("<TextBlock Text=\"{x:Bind presentation:ProjectText.AddPhotos}\" />")]
    [InlineData("<TextBlock Text=\"{x:Bind ViewModel.Title, Mode=OneWay}\" />")]
    [InlineData("<FontIcon Glyph=\"&#xE710;\" />")]
    [InlineData("<TextBlock Text=\"&#xF136;\" />")]
    [InlineData("<TextBlock Text=\"0312 / 0742\" />")]
    [InlineData("<TextBlock Text=\"10Lapse\" />")]
    [InlineData("<Grid x:Name=\"Content\"><x:Double x:Key=\"Size\">16</x:Double></Grid>")]
    [InlineData("<Button Content=\"{x:Null}\" />")]
    public void TheXamlScannerAllowsBindingsGlyphsAndNumbers(string element) => Assert.Empty(XamlViolations(Wrap(element)));

    [Theory]
    [InlineData("label.Text = \"Add photos\";")]
    [InlineData("var block = new TextBlock { Text = \"Drop to start\" };")]
    [InlineData("button.Content = $\"Export {name}\";")]
    [InlineData("dialog.PrimaryButtonText = \"OK\";")]
    [InlineData("AutomationProperties.SetName(this, \"Filmstrip\");")]
    [InlineData("AutomationProperties.SetHelpText(thumb, \"A gap\");")]
    [InlineData("ToolTipService.SetToolTip(button, \"Settings\");")]
    [InlineData("peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, \"Export done\", \"Export\");")]
    [InlineData("new AppNotificationBuilder().AddText(\"Your video is ready\");")]
    [InlineData("picker.FileTypeChoices.Add(\"MP4 video\", [\".mp4\"]);")]
    public void TheCodeScannerFlagsALiteral(string source) => Assert.NotEmpty(CodeViolations(source));

    [Theory]
    [InlineData("label.Text = ProjectText.AddPhotos;")]
    [InlineData("AutomationProperties.SetName(this, ProjectText.PreviewScrubberName);")]
    [InlineData("peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, message, \"Export\");")]
    [InlineData("Title = \"10Lapse\";")]
    [InlineData("glyph.Text = \"\\uE710\";")]
    [InlineData("counter.Text = \"0000\";")]
    [InlineData("// label.Text = \"Add photos\"; in a comment")]
    [InlineData("report.Add(\"App version\", AppVersion());")]
    [InlineData("_log.Info(\"Started.\");")]
    public void TheCodeScannerAllowsTextClassesAndTheLog(string source) => Assert.Empty(CodeViolations(source));

    [Theory]
    [InlineData("/// <summary>The button.</summary>\npublic static string AddPhotos => \"Add photos\";")]
    [InlineData("/// <summary>The bar.</summary>\npublic const string Ready = \"Ready.\";")]
    [InlineData("/// <summary>The reason.</summary>\npublic static string Reason(string name) => $\"Exporting {name}\";")]
    public void TheTextClassScannerFlagsAVisibleLiteral(string source) => Assert.NotEmpty(TextClassViolations(source));

    [Theory]
    [InlineData("/// <summary>The button.</summary>\npublic static string AddPhotos => _strings.Get();")]
    [InlineData("/// <summary>Error context: the import failed.</summary>\npublic static string ImportFailed => \"Importing photos failed.\";")]
    [InlineData("/// <summary>Log line: the prompt was due.</summary>\npublic static string Postponed => \"Rating prompt: postponed.\";")]
    [InlineData("/// <summary>Log-only context: drawing failed.</summary>\npublic static string DrawFailed => \"Drawing failed.\";")]
    [InlineData("/// <summary>The first line of the diagnostics text (English, like the log it carries).</summary>\npublic static string Heading => \"{0} diagnostics\";")]
    public void TheTextClassScannerAllowsResourcesAndLogLines(string source) => Assert.Empty(TextClassViolations(source));

    private static IEnumerable<string> XamlViolations(string xaml)
    {
        var document = XDocument.Parse(xaml);
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes().Where(attribute => _textProperties.Contains(attribute.Name.LocalName) && attribute.Name.Namespace == XNamespace.None))
            {
                if (IsVisibleLiteral(attribute.Value))
                {
                    yield return $"{element.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\"";
                }
            }

            if (element.Name.LocalName == "Setter"
                && _textProperties.Contains((string?)element.Attribute("Property") ?? string.Empty)
                && IsVisibleLiteral((string?)element.Attribute("Value") ?? string.Empty))
            {
                yield return $"Setter {element.Attribute("Property")!.Value}=\"{element.Attribute("Value")!.Value}\"";
            }

            if (!_valueElements.Contains(element.Name.LocalName))
            {
                foreach (var text in element.Nodes().OfType<XText>().Where(text => IsVisibleLiteral(text.Value.Trim())))
                {
                    yield return $"{element.Name.LocalName} text \"{text.Value.Trim()}\"";
                }
            }
        }
    }

    private static IEnumerable<string> CodeViolations(string source)
    {
        foreach (var line in source.Split('\n').Select(line => line.Trim()).Where(line => !line.StartsWith("//", StringComparison.Ordinal)))
        {
            foreach (var regex in new[] { PropertyLiteral(), AutomationLiteral(), ToolTipLiteral(), NotificationLiteral(), ToastLiteral(), FileTypeLiteral() })
            {
                foreach (Match match in regex.Matches(line))
                {
                    if (IsVisibleLiteral(Regex.Unescape(match.Groups["value"].Value)))
                    {
                        yield return line;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> TextClassViolations(string source)
    {
        foreach (Match match in TextClassLiteral().Matches(source.Replace("\r\n", "\n", StringComparison.Ordinal)))
        {
            var summary = match.Groups["doc"].Value;
            if (IsVisibleLiteral(match.Groups["value"].Value) && !LogSummary().IsMatch(summary))
            {
                yield return match.Groups["name"].Value;
            }
        }
    }

    // Words: at least one letter, and not the brand name. A markup extension ({x:Bind ...}) is not a literal.
    private static bool IsVisibleLiteral(string value) =>
        value.Length > 0 && !value.StartsWith('{') && value != Brand && value.Any(char.IsLetter);

    // Every App source except the Debug-only token gallery and the Debugging switches (removed from Release builds).
    private static IEnumerable<string> AppFiles(string pattern) =>
        SourceFiles(pattern)
            .Where(file => Relative(file).StartsWith(Path.Combine("src", "TenLapse.App") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Gallery{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}Debugging{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(DesignFiles.Root, "src"), pattern, SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string Relative(string file) => Path.GetRelativePath(DesignFiles.Root, file);

    private static string Wrap(string element) =>
        $"<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">{element}</Grid>";

    // label.Text = "…", new TextBlock { Text = "…" }, dialog.Title = $"…".
    [GeneratedRegex(@"\b(?:Text|Content|Header|PlaceholderText|Title|Message|Label|Description|OnContent|OffContent|PrimaryButtonText|SecondaryButtonText|CloseButtonText|CommitButtonText|KeyboardAcceleratorTextOverride)\s*=\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex PropertyLiteral();

    [GeneratedRegex(@"\bAutomationProperties\.Set(?:Name|HelpText|FullDescription|AcceleratorKey|AccessKey|LocalizedControlType|ItemStatus|ItemType)\([^,]+,\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex AutomationLiteral();

    [GeneratedRegex(@"\bToolTipService\.SetToolTip\([^,]+,\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex ToolTipLiteral();

    // The message, the third argument; the fourth, the activity id, is not read out.
    [GeneratedRegex(@"\bRaiseNotificationEvent\([^,]+,[^,]+,\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex NotificationLiteral();

    [GeneratedRegex(@"\b(?:AddText|SetAttributionText|AppNotificationButton)\(\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex ToastLiteral();

    [GeneratedRegex(@"\bFileTypeChoices\.Add\(\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex FileTypeLiteral();

    // A public string property, constant or one-line method of a text class whose body starts with a literal, with its doc comment.
    [GeneratedRegex(@"(?<doc>(?:^[ \t]*///.*\n)*)^[ \t]*public (?:static|const) string (?<name>\w+)(?:\([^)]*\))?\s*(?:=>|=)\s*(?:string\.Create\([^,]+,\s*)?\$?@?""(?<value>(?:[^""\\]|\\.)*)""", RegexOptions.Multiline)]
    private static partial Regex TextClassLiteral();

    [GeneratedRegex(@"(?i)error context|log[- ]only|\blog\b|log line|log context|diagnostics text")]
    private static partial Regex LogSummary();
}
