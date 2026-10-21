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
        "AutomationProperties.ItemStatus", "AutomationProperties.ItemType", "AutomationProperties.LocalizedLandmarkType",
        "Subtitle", "AccessKey",
    };

    // The display formatters that are not text classes but write the user's numbers and joins (#114).
    private static readonly string[] _formatterFiles =
    [
        Path.Combine("src", "TenLapse.Core", "Timing", "TimingFormat.cs"),
        Path.Combine("src", "TenLapse.Presentation", "Timing", "TimingInput.cs"),
        Path.Combine("src", "TenLapse.Presentation", "Timing", "TimingPanelViewModel.cs"),
        Path.Combine("src", "TenLapse.Presentation", "Output", "OutputPlan.cs"),
        Path.Combine("src", "TenLapse.Presentation", "Services", "SkippedFilesInfoBar.cs"),
    ];

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
    public void TextClassesNeitherGlueNorFormatInvariantlyNorChooseAPluralByOne()
    {
        var files = SourceFiles("*.cs")
            .Where(file => IsDisplayText(file, File.ReadAllText(file)))
            .ToList();
        Assert.True(files.Count >= 14, "The text classes and formatters were not found: " + string.Join(", ", files.Select(Relative)));

        var offenders = files
            .SelectMany(file => FormattingViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Join with a whole format string (ProjectText.SentenceJoin, TimingWords), write numbers in the user's culture, and choose a counted "
            + "form with a PluralText; mark an intended exception with an \"Invariant on purpose\" or \"Not a plural\" comment on or just above the line:\n"
            + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("/// <summary>The readout.</summary>\npublic string Readout => $\"{_headline} {_detail}\";")]
    [InlineData("/// <summary>The notes.</summary>\npublic static string Notes(string[] notes) => string.Join(' ', notes);")]
    [InlineData("/// <summary>The notes.</summary>\npublic static string Notes(string[] notes) => string.Join(\" \", notes);")]
    [InlineData("/// <summary>A count.</summary>\nprivate static string Count(int count) => count.ToString(\"N0\", CultureInfo.InvariantCulture);")]
    [InlineData("/// <summary>A sentence.</summary>\ninternal static string Format(string format, params object[] values) =>\n    string.Format(CultureInfo.InvariantCulture, format, values);")]
    [InlineData("/// <summary>The message.</summary>\npublic static string Removed(int count) => count == 1 ? RemovedOne : Format(RemovedMany, count);")]
    [InlineData("/// <summary>The message.</summary>\npublic static string Found(int found) => found switch\n{\n    1 => FoundOne,\n    _ => FoundMany,\n};")]
    [InlineData("/// <summary>The undo.</summary>\npublic static string Undo(FrameEdit next) => next switch\n{\n    { Count: 1 } => UndoOne,\n    _ => Undo,\n};")]
    [InlineData("/// <summary>The message.</summary>\npublic static string Removed(int count) => count is 1 ? RemovedOne : Format(RemovedMany, count);")]
    [InlineData("/// <summary>The message.</summary>\npublic static string Removed(int[] frames) => frames is { Length: 1 } ? RemovedOne : RemovedMany;")]
    [InlineData("/// <summary>The readout.</summary>\npublic string Readout => _headline + \" \" + _detail;")]
    [InlineData("/// <summary>The readout.</summary>\npublic string Readout => $\"{_headline}, {_detail}\";")]
    [InlineData("/// <summary>The readout.</summary>\npublic string Readout => @$\"{_headline} {_detail}\";")]
    [InlineData("/// <summary>The readout.</summary>\npublic string Readout => $@\"{_headline} {_detail}\";")]
    [InlineData("/// <summary>The names.</summary>\npublic static string Names(string[] names) => string.Join(\", \", names);")]
    [InlineData("/// <summary>A label.</summary>\npublic static string Label(CultureInfo? culture) => string.Format(culture ?? CultureInfo.InvariantCulture, LabelFormat, 1.5);")]
    public void TheFormattingScannerFlagsGluesInvariantNumbersAndOnePlurals(string source) => Assert.NotEmpty(FormattingViolations(source));

    [Theory]
    [InlineData("/// <summary>The readout.</summary>\npublic string Readout => ProjectText.Sentences(_headline, _detail);")]
    [InlineData("/// <summary>Log line: the problems.</summary>\npublic static string Problems(string[] problems) => \"Problems: \" + string.Join(\" \", problems);")]
    [InlineData("/// <summary>Log line: the count.</summary>\npublic static string Logged(int count) => string.Create(CultureInfo.InvariantCulture, $\"{count} {count}\");")]
    [InlineData("// Invariant on purpose: zero-padded counter digits.\nprivate static string Pad(int value) => value.ToString(CultureInfo.InvariantCulture);")]
    [InlineData("/// <summary>A pixel size. Invariant on purpose: no grouping.</summary>\npublic static string Size(int width) =>\n    string.Format(CultureInfo.InvariantCulture, SizeFormat, width);")]
    [InlineData("/// <summary>The move.</summary>\npublic static string Moved(int count) =>\n    // Not a plural: one frame is named by its number.\n    count == 1 ? Named() : Counted(count);")]
    [InlineData("/// <summary>A count.</summary>\nprivate static string Count(int count) => count.ToString(\"N0\", CultureInfo.CurrentCulture);")]
    [InlineData("/// <summary>A parse.</summary>\nprivate static bool Read(string text) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _);")]
    [InlineData("/// <summary>A bound.</summary>\nprivate static long Small(long value) => Math.Max(1, value);")]
    public void TheFormattingScannerAllowsMarkedExceptionsLogLinesAndParsing(string source) => Assert.Empty(FormattingViolations(source));

    [Fact]
    public void TheManifestKeepsTheStoreNameAndReadsItsDescriptionFromTheResw()
    {
        var root = Path.Combine(DesignFiles.Root, "src", "TenLapse.App");
        var manifest = XDocument.Load(Path.Combine(root, "Package.appxmanifest"));
        var visual = manifest.Descendants().Single(element => element.Name.LocalName == "VisualElements");
        var properties = manifest.Descendants().Single(element => element.Name.LocalName == "DisplayName" && element.Parent?.Name.LocalName == "Properties");
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

    [Fact]
    public void TheManifestAssociatesProjectFilesAndNamesTheTypeFromEveryResw()
    {
        // #52: .10lapse opens in 10Lapse; the type's name in File Explorer comes from each language's Resources.resw.
        var root = Path.Combine(DesignFiles.Root, "src", "TenLapse.App");
        var manifest = XDocument.Load(Path.Combine(root, "Package.appxmanifest"));
        var association = manifest.Descendants().Single(element => element.Name.LocalName == "FileTypeAssociation");
        Assert.Equal([".10lapse"], association.Descendants().Where(element => element.Name.LocalName == "FileType").Select(element => element.Value.Trim()));
        var name = association.Elements().Single(element => element.Name.LocalName == "DisplayName").Value.Trim();
        Assert.StartsWith("ms-resource:", name, StringComparison.Ordinal);
        foreach (var resw in Directory.EnumerateFiles(Path.Combine(root, "Strings"), "Resources.resw", SearchOption.AllDirectories))
        {
            var entry = XDocument.Load(resw).Root!.Elements("data").SingleOrDefault(data => (string?)data.Attribute("name") == name["ms-resource:".Length..]);
            Assert.False(string.IsNullOrWhiteSpace(entry?.Element("value")?.Value), $"{resw} does not name the project file type.");
        }
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
    [InlineData("<TeachingTip Subtitle=\"Drop photos here\" />")]
    [InlineData("<Button AccessKey=\"E\" />")]
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
    [InlineData("var picker = new FileSavePicker { SuggestedFileName = \"My video\" };")]
    [InlineData("_ = MessageBoxW(IntPtr.Zero, \"10Lapse could not start.\", Product.Name, MbOk);")]
    [InlineData("session.DrawText(\"Can't be read\", rect, color, format);")]
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
        foreach (var regex in new[] { PropertyLiteral(), AutomationLiteral(), ToolTipLiteral(), NotificationLiteral(), ToastLiteral(), FileTypeLiteral(), DrawTextLiteral() })
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

    // Line by line, each line read with the summary of the member it belongs to: a log line may glue and use the invariant
    // culture; anything else needs a marker comment on the line or in the four lines above it (or in its summary).
    private static IEnumerable<string> FormattingViolations(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var summary = string.Empty;
        var doc = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("///", StringComparison.Ordinal))
            {
                doc.Add(line);
                continue;
            }

            if (doc.Count > 0)
            {
                summary = SummaryText(string.Join('\n', doc));
                doc.Clear();
            }
            else if (Declaration().IsMatch(line))
            {
                summary = string.Empty;
            }

            if (line.StartsWith("//", StringComparison.Ordinal) || LogSummary().IsMatch(summary))
            {
                continue;
            }

            if (GluedPlaceholders().IsMatch(line))
            {
                yield return $"glued: {line}";
            }

            if (InvariantDisplay().IsMatch(line) && !Marked(lines, i, summary, "Invariant on purpose"))
            {
                yield return $"invariant: {line}";
            }

            if (PluralByOne().IsMatch(line) && !Marked(lines, i, summary, "Not a plural"))
            {
                yield return $"plural by one: {line}";
            }
        }
    }

    private static bool Marked(string[] lines, int index, string summary, string marker) =>
        summary.Contains(marker, StringComparison.Ordinal)
        || lines.Skip(Math.Max(0, index - 4)).Take(Math.Min(index, 4) + 1).Any(line => line.Contains("//", StringComparison.Ordinal) && line.Contains(marker, StringComparison.Ordinal));

    // A text class (ProjectText, TimingWords, ...), one of the display formatters beside them, or a Presentation view model
    // (FilmstripViewModel, ExportViewModel, ImportViewModel, ...), which fills the text classes' formats.
    private static bool IsDisplayText(string file, string source) =>
        (IsTextClass(source) && (file.EndsWith("Text.cs", StringComparison.Ordinal) || file.EndsWith("Words.cs", StringComparison.Ordinal)))
        || _formatterFiles.Any(formatter => Relative(file).Equals(formatter, StringComparison.OrdinalIgnoreCase))
        || (Relative(file).StartsWith(Path.Combine("src", "TenLapse.Presentation") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && file.EndsWith("ViewModel.cs", StringComparison.Ordinal));

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

    // The App's code and that of Presentation, Core and Media (#114), except the text classes, which the text class test checks.
    private static IEnumerable<string> CodeFiles() =>
        AppFiles("*.cs").Concat(
            new[] { "TenLapse.Presentation", "TenLapse.Core", "TenLapse.Media" }
                .SelectMany(project => ProjectFiles(project, "*.cs"))
                .Where(file => !IsTextClass(File.ReadAllText(file))));

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
    [GeneratedRegex(@"\b(?:Text|Content|Header|PlaceholderText|Title|Message|Label|Description|OnContent|OffContent|PrimaryButtonText|SecondaryButtonText|CloseButtonText|CommitButtonText|KeyboardAcceleratorTextOverride|SuggestedFileName|Subtitle)\s*=\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex PropertyLiteral();

    // Win2D's session.DrawText("…", …): the text is the first argument.
    [GeneratedRegex(@"\bDrawText\(\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex DrawTextLiteral();

    // Two placeholders glued with a space or a comma in an interpolated string ($"{a} {b}", $"{a}, {b}", @$"…"), two
    // values glued with a literal of spaces or commas (a + " " + b), or a list joined with one (string.Join(", ", …)).
    [GeneratedRegex(@"(?:\$@?|@\$)""[^""]*\}[,;]?\s+\{|\+\s*""[ ,;]+""\s*\+|\bstring\.Join\(\s*(?:'[ ,;]'|""[ ,;]+"")")]
    private static partial Regex GluedPlaceholders();

    // The invariant culture writing a number or a sentence (ToString, string.Format, string.Create), or standing in when no
    // culture is given (culture ?? CultureInfo.InvariantCulture); parsing is not display.
    [GeneratedRegex(@"(?:\.ToString\([^;]*|\bstring\.(?:Format|Create)\(\s*|\?\?\s*)CultureInfo\.InvariantCulture")]
    private static partial Regex InvariantDisplay();

    // A choice of words by "is it one": count == 1, != 1, count is 1, is not 1, a switch arm "1 =>", a property pattern
    // "Count: 1" or "Length: 1". A fraction's denominator compared with 1 (a whole number) is no such choice.
    [GeneratedRegex(@"(?<!Denominator\s*)(?:[!=]=|\bis(?:\s+not)?)\s*1\b(?![.\d])|(?:^|[\s(,])1\s*=>|\b(?:Count|Length):\s*1\b")]
    private static partial Regex PluralByOne();

    // A member declaration without a doc comment ends the previous member's summary.
    [GeneratedRegex(@"^(?:public|private|internal|protected)\b")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"\bAutomationProperties\.Set(?:Name|HelpText|FullDescription|AcceleratorKey|AccessKey|LocalizedControlType|ItemStatus|ItemType)\([^,]+,\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex AutomationLiteral();

    [GeneratedRegex(@"\bToolTipService\.SetToolTip\([^,]+,\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex ToolTipLiteral();

    // The message, the third argument; the fourth, the activity id, is not read out.
    [GeneratedRegex(@"\bRaiseNotificationEvent\([^,]+,[^,]+,\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex NotificationLiteral();

    [GeneratedRegex(@"\b(?:AddText|SetAttributionText|AppNotificationButton)\(\s*\$?@?""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex ToastLiteral();

    // new InfoBarRequest(…), new Confirmation(…), new Choice(…), new InfoBarDetail(…), MessageBoxW(…), Show(InfoBarSeverity.…, …):
    // the argument list, with one level of nested calls.
    [GeneratedRegex(@"(?:\b(?:InfoBarRequest|Confirmation|Choice|InfoBarDetail|MessageBoxW)|\bShow(?:WithDetails)?(?=\(\s*InfoBarSeverity\.))\((?<args>(?:[^;()""]|""(?:[^""\\]|\\.)*""|\((?:[^;()""]|""(?:[^""\\]|\\.)*"")*\))*)\)")]
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
