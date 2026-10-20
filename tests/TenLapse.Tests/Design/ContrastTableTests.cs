namespace TenLapse.Tests.Design;

/// <summary>
/// The contrast table of #38, computed from the token dictionaries (<c>Themes/Colors.xaml</c>) in light and dark: every
/// text token on every surface it can sit on at least 4.5:1, and every part that carries a state or the focus at least
/// 3:1 against the surfaces around it. High contrast maps every token to a system color
/// (<see cref="ThemeDictionaryTests.HighContrastMapsToSystemColorsAndNeverToAmber"/>), so Windows' own theme sets its
/// contrast; the segmented pill's pair is also checked against Windows' four contrast themes. Parts below 3:1 on purpose are listed in <see cref="_belowThreeOnPurpose"/> with the reason (WCAG 1.4.11
/// asks 3:1 only of what is needed to see a control or its state).
/// </summary>
public sealed class ContrastTableTests
{
    private static readonly string[] _themes = ["Light", "Dark"];

    // Every surface text and controls sit on: the window ground, the content layer, cards and flyouts, control fills, the
    // segmented control's track and pill, and the unfilled track of bars.
    private static readonly string[] _surfaces = ["Mica", "Layer", "Card", "ControlFill", "SegmentTrack", "SegmentPill", "Track"];

    // Text: primary, secondary (there is no lighter grey), and the text actions.
    private static readonly string[] _text = ["Ink", "InkSecondary", "EmberLink"];

    // State and focus: the indicator in its three states (playhead ring, scrubber, progress, film gate, toggles on, slider
    // thumbs), Ink (the focus ring's outer line, icons, the checked segment's text) and Ink Secondary (the off toggle's outline
    // and knob, secondary icons).
    private static readonly string[] _stateParts = ["DeepAmberIndicator", "DeepAmberIndicatorHover", "DeepAmberIndicatorPressed", "Ink", "InkSecondary"];

    // Below 3:1 on purpose, and why that is allowed: none of them is the only way to see a control or its state.
    private static readonly Dictionary<string, string> _belowThreeOnPurpose = new(StringComparer.Ordinal)
    {
        ["ControlStroke"] = "the 1 px border of buttons and selects, which their Ink label and chevron already identify",
        ["Divider"] = "rules between sections, decoration",
        ["DropZoneStroke"] = "the first-launch drop zone's dashed edge; its heading and buttons say what it is",
        ["Track"] = "the unfilled part of bars and sliders; the indicator fill and thumb against it reach 3:1",
        ["SegmentPill"] = "the selected segment's pill (1.14:1 light, 1.49:1 dark). Sven 2026-10-20: kept as designed; the bold label and the UIA selection state carry it. A known WCAG 1.4.11 gap for #41; high contrast is fixed (a Highlight pill)",
    };

    public static TheoryData<string, string, string> TextPairs() => Pairs(_text);

    public static TheoryData<string, string, string> StatePairs() => Pairs(_stateParts);

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void TextIsAtLeastFourAndAHalfToOneOnEverySurface(string theme, string text, string surface)
    {
        var ratio = Ratio(theme, text, surface);

        Assert.True(ratio >= 4.5, $"{theme} {text} on {surface} is {ratio:0.00}:1, needs 4.5:1");
    }

    [Theory]
    [MemberData(nameof(StatePairs))]
    public void StatesAndFocusAreAtLeastThreeToOneOnEverySurface(string theme, string part, string surface)
    {
        var ratio = Ratio(theme, part, surface);

        Assert.True(ratio >= 3.0, $"{theme} {part} on {surface} is {ratio:0.00}:1, needs 3:1");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TheFocusRingsTwoLinesStandApart(string theme)
    {
        // The double ring (DESIGN.md, Elevation): an Ink line outside a ground line (FocusVisualPrimary/Secondary, Ink and
        // Layer), so it reads on any surface and on a photo.
        var ratio = Ratio(theme, "Ink", "Layer");

        Assert.True(ratio >= 4.5, $"{theme} focus ring Ink on Layer is {ratio:0.00}:1");
    }

    [Fact]
    public void TheTokensBelowThreeToOneAreTheOnesListedWithAReason()
    {
        var below = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var theme in _themes)
        {
            foreach (var part in new[] { "ControlStroke", "Divider", "DropZoneStroke", "Track" })
            {
                if (new[] { "Mica", "Layer", "Card", "ControlFill" }.Any(surface => Ratio(theme, part, surface) < 3.0))
                {
                    below.Add(part);
                }
            }

            if (Ratio(theme, "SegmentPill", "SegmentTrack") < 3.0)
            {
                below.Add("SegmentPill");
            }
        }

        Assert.Equal(new SortedSet<string>(_belowThreeOnPurpose.Keys, StringComparer.Ordinal), below);
    }

    // Windows 11's four contrast themes (the hcblack, hcwhite, hc1 and hc2 .theme files in Windows\Resources\Ease of Access
    // Themes), with the system colors the segmented control maps to: ButtonFace, Window, Highlight, HighlightText.
    public static TheoryData<string, string, string, string, string> ContrastThemes() => new()
    {
        { "Aquatic", "#202020", "#202020", "#8EE3F0", "#263B50" },
        { "Desert", "#FFFAEF", "#FFFAEF", "#903909", "#FFF5E3" },
        { "Dusk", "#2D3236", "#2D3236", "#A1BFDE", "#212D3B" },
        { "Night sky", "#000000", "#000000", "#D6B4FD", "#2B2B2B" },
    };

    [Theory]
    [MemberData(nameof(ContrastThemes))]
    public void InHighContrastTheSelectedPillStandsApartFromItsTrackAndItsLabelReadsOnIt(string name, string buttonFace, string window, string highlight, string highlightText)
    {
        // #38 review: the pill was Window on a ButtonFace track, the same color in every contrast theme, so the selection
        // vanished. It is Highlight now, with its label in HighlightText.
        var highContrast = DesignFiles.ThemeDictionary(DesignFiles.LoadTheme("Colors.xaml"), "HighContrast");
        var system = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SystemColorButtonFaceColor"] = buttonFace,
            ["SystemColorWindowColor"] = window,
            ["SystemColorHighlightColor"] = highlight,
            ["SystemColorHighlightTextColor"] = highlightText,
        };
        string Resolve(string token) => system[(string)highContrast[token + "Color"].Attribute("ResourceKey")!];

        var pill = DesignFiles.Contrast(Resolve("SegmentPill"), Resolve("SegmentTrack"));
        var label = DesignFiles.Contrast(Resolve("SegmentPillInk"), Resolve("SegmentPill"));

        Assert.True(pill >= 3.0, $"{name}: the pill on its track is {pill:0.00}:1, needs 3:1");
        Assert.True(label >= 4.5, $"{name}: the selected label on the pill is {label:0.00}:1, needs 4.5:1");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TheSelectedSegmentsLabelIsInkInLightAndDark(string theme)
    {
        // The high contrast fix leaves light and dark exactly as designed.
        var entry = DesignFiles.ThemeDictionary(DesignFiles.LoadTheme("Colors.xaml"), theme)["SegmentPillInkColor"];

        Assert.Equal("StaticResource", entry.Name.LocalName);
        Assert.Equal("InkColor", (string?)entry.Attribute("ResourceKey"));
    }

    private static TheoryData<string, string, string> Pairs(string[] foregrounds)
    {
        var data = new TheoryData<string, string, string>();
        foreach (var theme in _themes)
        {
            foreach (var foreground in foregrounds)
            {
                foreach (var surface in _surfaces)
                {
                    data.Add(theme, foreground, surface);
                }
            }
        }

        return data;
    }

    private static double Ratio(string theme, string foreground, string background)
    {
        var entries = DesignFiles.ThemeDictionary(DesignFiles.LoadTheme("Colors.xaml"), theme);
        return DesignFiles.Contrast(entries[foreground + "Color"].Value.Trim(), entries[background + "Color"].Value.Trim());
    }
}
