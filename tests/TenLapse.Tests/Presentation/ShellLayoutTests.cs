using System.ComponentModel;
using System.Globalization;
using TenLapse.Presentation;
using TenLapse.Tests.Design;

namespace TenLapse.Tests.Presentation;

/// <summary>The shell's narrow layout (#36): the breakpoint, the tab, the sections each layout shows and the panel's height.</summary>
public sealed class ShellLayoutTests
{
    // The Layout.xaml tokens the shell passes in (checked against the file below).
    private const double Dock = 1000;
    private const double Panel = 260;
    private const double PanelMin = 152;
    private const double PreviewMin = 96;

    // The stage's padding (20 + 20), the gaps (12 + 12), the 36 px transport and the 68 px filmstrip at its smallest thumbnails.
    private const double StageChrome = 40 + 24 + 36 + 68;

    private static ShellLayout Layout() => new(Dock, Panel, PanelMin, PreviewMin);

    [Theory]
    [InlineData(1280, false)]
    [InlineData(1000, false)]
    [InlineData(999.9, true)]
    [InlineData(999, true)]
    [InlineData(800, true)]
    [InlineData(640, true)]
    public void TheInspectorDocksFromExactlyOneThousandDips(double width, bool narrow) =>
        Assert.Equal(narrow, Layout().IsNarrowAt(width));

    // The window's width in DIPs is its pixels over the scale: the same physical window folds at a different pixel width
    // at 150 % and 200 %, and never at a fractional width that rounds the wrong way.
    [Theory]
    [InlineData(1.0, 1000, 1000)]
    [InlineData(1.25, 1250, 1000)]
    [InlineData(1.5, 1500, 1000)]
    [InlineData(1.75, 1750, 1000)]
    [InlineData(2.0, 2000, 1000)]
    public void TheBreakpointIsInDipsAtEveryScale(double scale, int dockingPixels, double dips)
    {
        var layout = Layout();

        Assert.Equal(dips, dockingPixels / scale);
        Assert.False(layout.IsNarrowAt(dockingPixels / scale));
        Assert.True(layout.IsNarrowAt((dockingPixels - 1) / scale));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void TheSmallestWindowIsNarrowAtEveryScale(double scale)
    {
        // MainWindow sizes the window to MinWindowWidth x MinWindowHeight DIPs, whatever the scale (rounded up to whole pixels).
        var pixels = Math.Ceiling(640 * scale);

        Assert.True(Layout().IsNarrowAt(pixels / scale));
        Assert.True(Layout().IsNarrowAt(640));
    }

    [Fact]
    public void ItStartsDockedWithEverySectionShownAndNoTabs()
    {
        var layout = Layout();

        Assert.False(layout.IsNarrow);
        Assert.False(layout.ShowsTabs);
        Assert.True(layout.ShowsTiming);
        Assert.True(layout.ShowsFrame);
        Assert.True(layout.ShowsOutput);
        Assert.True(layout.ShowsLook);
        Assert.True(layout.ShowsMusic);
    }

    [Fact]
    public void NarrowShowsTheTabsAndOnlyTheSelectedSection()
    {
        var layout = Layout();

        Assert.True(layout.Resize(800));

        Assert.True(layout.IsNarrow);
        Assert.True(layout.ShowsTabs);
        Assert.True(layout.ShowsTiming);
        Assert.False(layout.ShowsFrame);
        Assert.False(layout.ShowsOutput);

        layout.SelectedTabIndex = 2;

        Assert.Equal(InspectorTab.Output, layout.Tab);
        Assert.False(layout.ShowsTiming);
        Assert.False(layout.ShowsFrame);
        Assert.True(layout.ShowsOutput);
        Assert.False(layout.ShowsLook);

        // #45: Look, built, has the fourth tab.
        layout.SelectedTabIndex = 3;

        Assert.Equal(InspectorTab.Look, layout.Tab);
        Assert.False(layout.ShowsOutput);
        Assert.True(layout.ShowsLook);
        Assert.False(layout.ShowsMusic);

        // #50: Music has the fifth.
        layout.SelectedTabIndex = 4;

        Assert.Equal(InspectorTab.Music, layout.Tab);
        Assert.False(layout.ShowsLook);
        Assert.True(layout.ShowsMusic);
    }

    [Fact]
    public void DockedShowsEverySectionWhateverTheTab()
    {
        var layout = Layout();
        layout.SelectedTabIndex = 1;

        Assert.True(layout.ShowsTiming);
        Assert.True(layout.ShowsFrame);
        Assert.True(layout.ShowsOutput);
        Assert.True(layout.ShowsMusic);
    }

    [Fact]
    public void ResizeSaysWhetherTheLayoutChanged()
    {
        var layout = Layout();

        Assert.False(layout.Resize(1280));
        Assert.True(layout.Resize(999));
        Assert.False(layout.Resize(640));
        Assert.False(layout.Resize(998));
        Assert.True(layout.Resize(1000));
        Assert.False(layout.IsNarrow);
    }

    [Fact]
    public void FoldingKeepsTheSectionHoldingTheFocusOnScreen()
    {
        var layout = Layout();

        layout.Resize(900, tabToKeep: InspectorTab.Frame);

        Assert.Equal(InspectorTab.Frame, layout.Tab);
        Assert.True(layout.ShowsFrame);
        Assert.False(layout.ShowsTiming);
    }

    [Fact]
    public void FoldingWithTheFocusElsewhereKeepsTheTabTheUserLastPicked()
    {
        var layout = Layout();
        layout.Resize(900);
        layout.SelectedTabIndex = 2;
        layout.Resize(1200);

        layout.Resize(900);

        Assert.Equal(InspectorTab.Output, layout.Tab);
    }

    [Fact]
    public void AnUnfoldIgnoresTheSectionToKeep()
    {
        var layout = Layout();
        layout.Resize(900);
        layout.SelectedTabIndex = 1;

        layout.Resize(1200, tabToKeep: InspectorTab.Output);

        Assert.Equal(InspectorTab.Frame, layout.Tab);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void ATabIndexOutOfRangeIsIgnored(int index)
    {
        var layout = Layout();
        layout.SelectedTabIndex = 1;

        layout.SelectedTabIndex = index;

        Assert.Equal(InspectorTab.Frame, layout.Tab);
    }

    [Fact]
    public void ChangesAreAnnouncedForTheBindings()
    {
        var layout = Layout();
        var names = new List<string?>();
        ((INotifyPropertyChanged)layout).PropertyChanged += (_, e) => names.Add(e.PropertyName);

        layout.Resize(800);
        layout.SelectedTabIndex = 1;

        Assert.Contains(nameof(ShellLayout.IsNarrow), names);
        Assert.Contains(nameof(ShellLayout.ShowsTabs), names);
        Assert.Contains(nameof(ShellLayout.ShowsFrame), names);
        Assert.Contains(nameof(ShellLayout.Tab), names);
        Assert.Contains(nameof(ShellLayout.SelectedTabIndex), names);
    }

    [Fact]
    public void TheTabsAreTheSectionsInTheirOrder() =>
        Assert.Equal(new[] { "Timing", "Frame", "Output", "Look", "Music" }, ShellLayout.TabLabels);

    [Theory]
    [InlineData(800, 260)] // the default window: the full panel
    [InlineData(700, 260)]
    [InlineData(592, 260)] // an 800 x 600 window
    [InlineData(525, 260)] // 1 divider + 168 stage + 96 preview + 260 panel, to the pixel
    public void ATallWindowGetsTheFullPanel(double bodyHeight, double expected) =>
        Assert.Equal(expected, Layout().PanelHeight(bodyHeight, StageChrome));

    [Fact]
    public void TheFullPanelEndsExactlyWhereThePreviewReachesItsMinimum()
    {
        // 1 divider + stage chrome + the preview's minimum + the panel.
        var exactly = 1 + StageChrome + PreviewMin + Panel;

        Assert.Equal(Panel, Layout().PanelHeight(exactly, StageChrome));
        Assert.Equal(Panel - 1, Layout().PanelHeight(exactly - 1, StageChrome));
    }

    [Fact]
    public void AShortWindowTakesTheDifferenceFromThePanel() =>
        Assert.Equal(200, Layout().PanelHeight(1 + StageChrome + PreviewMin + 200, StageChrome));

    [Fact]
    public void ThePanelNeverShrinksBelowItsMinimumAndThePreviewGivesWay()
    {
        var layout = Layout();

        // 640 x 480 leaves 392 px under the title and command bars.
        var panel = layout.PanelHeight(392, StageChrome);

        Assert.Equal(PanelMin, panel);
        Assert.True(392 - 1 - StageChrome - panel > 0, "the preview still has some height");
        Assert.Equal(PanelMin, layout.PanelHeight(0, StageChrome));
    }

    [Fact]
    public void ABiggerFilmstripTakesItsHeightFromThePanelToo()
    {
        // Thumbnails at their largest (96 px) add 64 px to the stage.
        var small = Layout().PanelHeight(500, StageChrome);
        var large = Layout().PanelHeight(500, StageChrome + 64);

        Assert.Equal(235, small); // 500 - 1 - 168 - 96
        Assert.Equal(171, large);
    }

    [Theory]
    [InlineData(0, 260, 100, 96)]
    [InlineData(1000, 100, 120, 96)]
    [InlineData(1000, 260, 0, 96)]
    [InlineData(1000, 260, 100, -1)]
    public void NonsenseSizesAreRefused(double dock, double panel, double min, double preview) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShellLayout(dock, panel, min, preview));

    [Fact]
    public void TheShellPassesTheLayoutTokensOnAsTheyAreInTheFile()
    {
        var tokens = DesignFiles.RootEntries(DesignFiles.LoadTheme("Layout.xaml"));
        double Number(string key) => double.Parse(tokens[key].Value, CultureInfo.InvariantCulture);

        Assert.Equal(Dock, Number("InspectorDockMinWindowWidth"));
        Assert.Equal(Panel, Number("NarrowPanelHeight"));
        Assert.Equal(PanelMin, Number("NarrowPanelMinHeight"));
        Assert.Equal(PreviewMin, Number("NarrowPreviewMinHeight"));
        Assert.Equal(640, Number("MinWindowWidth"));
        Assert.Equal(480, Number("MinWindowHeight"));
        Assert.Equal(320, Number("InspectorWidth"));
    }

    [Fact]
    public void TheSmallestWindowsBodyHoldsThePanelsMinimumAndTheStage()
    {
        // The smallest window, minus the title bar and the command bar (both read from the tokens).
        var tokens = DesignFiles.RootEntries(DesignFiles.LoadTheme("Layout.xaml"));
        double Number(string key) => double.Parse(tokens[key].Value, CultureInfo.InvariantCulture);
        var body = Number("MinWindowHeight") - Number("TitleBarHeight") - Number("CommandBarHeight");
        var layout = Layout();

        var panel = layout.PanelHeight(body, StageChrome);

        Assert.True(panel + 1 + StageChrome <= body, "the panel and the stage's fixed parts fit under the bars");
    }
}
