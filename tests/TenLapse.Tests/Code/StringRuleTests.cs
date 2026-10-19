using System.Text.RegularExpressions;
using System.Xml.Linq;
using TenLapse.Tests.Design;

namespace TenLapse.Tests.Code;

/// <summary>
/// Every user-visible string comes from the resource files (#39), so a translation is only new files. The XAML binds
/// its text to the text classes (<c>ProjectText</c>, <c>ExportText</c>, ...), which read <c>Strings/*.resx</c> through
/// <c>TextResources</c>. These tests fail on a literal user-visible string in a XAML text attribute, in the App's or
/// Presentation's code, and on a literal sentence in a text class whose summary does not start by saying it is for the
/// log. Allowed anywhere: text without letters once its <c>{0}</c> placeholders are left out (numbers, glyphs,
/// punctuation) and the brand name. The Debug-only token gallery and the Debugging folder are not checked.
/// <para>
/// Not scanned, by design: log lines and error contexts (<c>_log.Info("…")</c>, <c>_errors.Report(exception, "…")</c>)
/// and a <c>SkippedFile</c>'s message, which is for the log only (its constructor says so; the list shows the
/// reason's text from <c>SkippedFileText</c>). <c>ImportViewModel</c>'s "Only in OneDrive; not downloaded." is such
/// a message.
/// </para>
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
    public void NoAppOrPresentationCodeSetsALiteralUserVisibleString()
    {
        var offenders = CodeFiles()
            .SelectMany(file => CodeViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Take the text from a text class (ProjectText, ExportText, ...) that reads its .resx:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void ATextClassKeepsOnlyLogLinesAsLiterals()
    {
        var textClasses = SourceFiles("*.cs")
            .Where(file => IsTextClass(File.ReadAllText(file)))
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
    [InlineData("<TextBlock Text=\"{}{0} photos\" />")]
    [InlineData("<TextBlock Text=\"{Photos} left\" />")]
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
    [InlineData("<TextBlock Text=\"{Binding Name}\" />")]
    [InlineData("<TextBlock Text=\"{StaticResource AppName}\" />")]
    [InlineData("<Button ToolTipService.ToolTip=\"{ThemeResource Hint}\" />")]
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
    [InlineData("label.Text = \"{0} is ready.\";")]
    [InlineData("label.Text = $\"{count} photos\";")]
    [InlineData("var request = new InfoBarRequest(InfoBarSeverity.Warning, \"Export failed.\");")]
    [InlineData("var request = new InfoBarRequest(\n    InfoBarSeverity.Warning,\n    ProjectText.Format(\"{0} is ready.\", name));")]
    [InlineData("_infoBars.Show(InfoBarSeverity.Error, \"Saving failed.\");")]
    [InlineData("_infoBars.ShowWithDetails(InfoBarSeverity.Warning, ProjectText.Skipped, [new InfoBarDetail(name, \"Damaged\")]);")]
    [InlineData("var question = new Confirmation(\"Discard changes?\", ProjectText.DiscardMessage, ProjectText.Discard, ProjectText.Cancel);")]
    [InlineData("var question = new Choice(ProjectText.A, ProjectText.B, \"Save\", ProjectText.C, ProjectText.Cancel);")]
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
    [InlineData("_errors.Report(exception, \"Saving failed.\");")]
    [InlineData("label.Text = $\"{ProjectText.Done} {ProjectText.Saved}\";")]
    [InlineData("var request = new InfoBarRequest(InfoBarSeverity.Warning, ProjectText.ExportFailed) { Id = \"export\" };")]
    [InlineData("_infoBars.Show(InfoBarSeverity.Error, ProjectText.Format(ProjectText.SaveFailed, \"{0:N0}\"));")]
    public void TheCodeScannerAllowsTextClassesAndTheLog(string source) => Assert.Empty(CodeViolations(source));

    [Theory]
    [InlineData("/// <summary>The button.</summary>\npublic static string AddPhotos => \"Add photos\";")]
    [InlineData("/// <summary>The bar.</summary>\npublic const string Ready = \"Ready.\";")]
    [InlineData("/// <summary>The reason.</summary>\npublic static string Reason(string name) => $\"Exporting {name}\";")]
    [InlineData("/// <summary>The InfoBar when a video is done.</summary>\npublic static string Ready => \"{0} is ready.\";")]
    [InlineData("/// <summary>The count.</summary>\npublic static string Photos(int count) => $\"{count} photos\";")]
    [InlineData("/// <summary>The InfoBar when startup failed; it names the log folder.</summary>\npublic static string StartupFailed => \"10Lapse could not start. See the log folder.\";")]
    [InlineData("/// <summary>\n/// The Critical InfoBar, and the error context it is reported with.\n/// </summary>\npublic static string ActionFailed => \"That couldn't be done.\";")]
    public void TheTextClassScannerFlagsAVisibleLiteral(string source) => Assert.NotEmpty(TextClassViolations(source));

    [Theory]
    [InlineData("/// <summary>The button.</summary>\npublic static string AddPhotos => _strings.Get();")]
    [InlineData("/// <summary>Error context: the import failed.</summary>\npublic static string ImportFailed => \"Importing photos failed.\";")]
    [InlineData("/// <summary>Log line: the prompt was due.</summary>\npublic static string Postponed => \"Rating prompt: postponed.\";")]
    [InlineData("/// <summary>Log-only context: drawing failed.</summary>\npublic static string DrawFailed => \"Drawing failed.\";")]
    [InlineData("/// <summary>Diagnostics text: the first line (English, like the log it carries).</summary>\npublic static string Heading => \"{0} diagnostics\";")]
    [InlineData("/// <summary>\n/// Error context: an InfoBar's action threw.\n/// </summary>\npublic static string ActionFailedContext => \"An InfoBar's action failed.\";")]
    [InlineData("/// <summary>Log context: the import.</summary>\npublic static string Import => \"Import\";")]
    [InlineData("/// <summary>The log context of a save.</summary>\npublic static string Save => \"Save\";")]
    [InlineData("/// <summary>The glyphs between parts.</summary>\npublic static string Join => \"{0} \\u00B7 {1}\";")]
    public void TheTextClassScannerAllowsResourcesAndLogLines(string source) => Assert.Empty(TextClassViolations(source));

    private static IEnumerable<string> XamlViolations(string xaml)
    {
        var document = XDocument.Parse(xaml);
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes().Where(attribute => _textProperties.Contains(attribute.Name.LocalName) && attribute.Name.Namespace == XNamespace.None))
            {
                if (IsVisibleXamlLiteral(attribute.Value))
                {
                    yield return $"{element.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\"";
                }
            }

            if (element.Name.LocalName == "Setter"
                && _textProperties.Contains((string?)element.Attribute("Property") ?? string.Empty)
                && IsVisibleXamlLiteral((string?)element.Attribute("Value") ?? string.Empty))
            {
                yield return $"Setter {element.Attribute("Property")!.Value}=\"{element.Attribute("Value")!.Value}\"";
            }

            if (!_valueElements.Contains(element.Name.LocalName))
            {
                foreach (var text in element.Nodes().OfType<XText>().Where(text => IsVisibleXamlLiteral(text.Value.Trim())))
                {
                    yield return $"{element.Name.LocalName} text \"{text.Value.Trim()}\"";
                }
            }
        }
    }

    // The source without its comment lines, scanned whole so that a call split over several lines is still seen.
    private static IEnumerable<string> CodeViolations(string source)
    {
        var code = string.Join('\n', source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal)));
        foreach (var regex in new[] { PropertyLiteral(), AutomationLiteral(), ToolTipLiteral(), NotificationLiteral(), ToastLiteral(), FileTypeLiteral() })
        {
            foreach (Match match in regex.Matches(code))
            {
                if (IsVisibleCodeLiteral(Regex.Unescape(match.Groups["value"].Value)))
                {
                    yield return LineAt(code, match.Index);
                }
            }
        }

        // Every literal among the arguments of an InfoBar, a dialog or a detail line.
        foreach (Match call in MessageCall().Matches(code))
        {
            foreach (Match literal in StringLiteral().Matches(call.Groups["args"].Value))
            {
                if (IsVisibleCodeLiteral(Regex.Unescape(literal.Groups["value"].Value)))
                {
                    yield return LineAt(code, call.Index);
                }
            }
        }
    }

    private static string LineAt(string code, int index)
    {
        var start = index == 0 ? 0 : code.LastIndexOf('\n', index - 1) + 1;
        var end = code.IndexOf('\n', index);
        return code[start..(end < 0 ? code.Length : end)];
    }

    private static IEnumerable<string> TextClassViolations(string source)
    {
        foreach (Match match in TextClassLiteral().Matches(source.Replace("\r\n", "\n", StringComparison.Ordinal)))
        {
            if (IsVisibleCodeLiteral(Regex.Unescape(match.Groups["value"].Value)) && !LogSummary().IsMatch(SummaryText(match.Groups["doc"].Value)))
            {
                yield return match.Groups["name"].Value;
            }
        }
    }

    // Words: at least one letter besides the brand name. Only a real markup extension ({x:Bind ...}) is not a literal;
    // "{}{0} photos" (XAML's escape for a leading brace) is.
    private static bool IsVisibleXamlLiteral(string value) =>
        !MarkupExtension().IsMatch(value) && HasWords(value);

    // Words once the placeholders ({0}, {1:N0}, {count}) are left out: "{0} is ready." and $"{count} photos" are sentences.
    private static bool IsVisibleCodeLiteral(string value) => HasWords(Placeholder().Replace(value, string.Empty));

    private static bool HasWords(string value) => value.Replace(Brand, string.Empty, StringComparison.Ordinal).Any(char.IsLetter);

    // The summary's text, without the /// and the tags around it.
    private static string SummaryText(string doc)
    {
        var text = string.Join(' ', doc.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("///", StringComparison.Ordinal))
            .Select(line => line[3..].Trim()));
        var start = text.IndexOf("<summary>", StringComparison.Ordinal);
        return (start < 0 ? text : text[(start + "<summary>".Length)..]).Trim();
    }

    // Every App source except the Debug-only token gallery and the Debugging switches (removed from Release builds).
    private static IEnumerable<string> AppFiles(string pattern) =>
        ProjectFiles("TenLapse.App", pattern)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Gallery{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}Debugging{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    // The App's code and Presentation's, except the text classes, which the text class test checks.
    private static IEnumerable<string> CodeFiles() =>
        AppFiles("*.cs").Concat(ProjectFiles("TenLapse.Presentation", "*.cs").Where(file => !IsTextClass(File.ReadAllText(file))));

    private static IEnumerable<string> ProjectFiles(string project, string pattern) =>
        SourceFiles(pattern).Where(file => Relative(file).StartsWith(Path.Combine("src", project) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    private static bool IsTextClass(string source) =>
        source.Contains("TextResources _strings", StringComparison.Ordinal) || source.Contains("CoreText.Strings.Get()", StringComparison.Ordinal);

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

    // new InfoBarRequest(…), new Confirmation(…), new Choice(…), new InfoBarDetail(…), Show(InfoBarSeverity.…, …): the
    // argument list, with one level of nested calls.
    [GeneratedRegex(@"(?:\b(?:InfoBarRequest|Confirmation|Choice|InfoBarDetail)|\bShow(?:WithDetails)?(?=\(\s*InfoBarSeverity\.))\((?<args>(?:[^;()""]|""(?:[^""\\]|\\.)*""|\((?:[^;()""]|""(?:[^""\\]|\\.)*"")*\))*)\)")]
    private static partial Regex MessageCall();

    [GeneratedRegex(@"\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^\{(?:x:Bind|x:Null|Binding|StaticResource|ThemeResource|TemplateBinding|CustomResource)\b")]
    private static partial Regex MarkupExtension();

    [GeneratedRegex(@"\bFileTypeChoices\.Add\(\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex FileTypeLiteral();

    // A public string property, constant or one-line method of a text class whose body starts with a literal, with its doc comment.
    [GeneratedRegex(@"(?<doc>(?:^[ \t]*///.*\n)*)^[ \t]*public (?:static|const) string (?<name>\w+)(?:\([^)]*\))?\s*(?:=>|=)\s*(?:string\.Create\([^,]+,\s*)?\$?@?""(?<value>(?:[^""\\]|\\.)*)""", RegexOptions.Multiline)]
    private static partial Regex TextClassLiteral();

    // The summary's first words: "Error context: …", "Log line: …", "Log-only context: …", "Diagnostics text: …".
    [GeneratedRegex(@"^(?:Error context|Log(?:-only)?(?: line| context)?\b|The log context|Diagnostics text)")]
    private static partial Regex LogSummary();
}
