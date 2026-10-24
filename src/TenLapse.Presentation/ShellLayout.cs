using CommunityToolkit.Mvvm.ComponentModel;
using TenLapse.Presentation.Look;
using TenLapse.Presentation.Music;

namespace TenLapse.Presentation;

/// <summary>The inspector sections the narrow layout shows one at a time, in the canvas's tab order.</summary>
public enum InspectorTab
{
    /// <summary>Speed mode and value, and the length readout (#17).</summary>
    Timing,

    /// <summary>Shape and Fit or Fill (#18).</summary>
    Frame,

    /// <summary>Resolution, Format and Quality (#23).</summary>
    Output,

    /// <summary>Deflicker and its strength (#45).</summary>
    Look,

    /// <summary>The song under the video, its start and its fades (#50).</summary>
    Music,
}

/// <summary>
/// The shell's responsive layout (#36; DESIGN.md section 5, the canvas's narrow state). From
/// <see cref="DockMinWindowWidth"/> DIPs of window width the inspector docks right at a fixed width. Below it the
/// inspector becomes a panel under the stage, with a row of tabs (Timing, Frame, Output, Look, Music) in place of the stacked
/// sections, and the panel gives up height to the preview when the window is short. The sizes come from the
/// <c>Layout.xaml</c> tokens; this class holds the rules, so the view only applies them.
/// </summary>
public sealed partial class ShellLayout : ObservableObject
{
    /// <summary>The tab row's name for a screen reader (the canvas's "Panel" radio group).</summary>
    public static string TabsName => ProjectText.InspectorTabsName;

    /// <summary>The tabs' labels, in <see cref="InspectorTab"/> order.</summary>
    public static IReadOnlyList<string> TabLabels { get; } = [TimingText.Heading, FrameText.Heading, OutputText.Heading, LookText.Heading, MusicText.Heading];

    private readonly double _dockMinWindowWidth;
    private readonly double _panelHeight;
    private readonly double _panelMinHeight;
    private readonly double _previewMinHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTabs), nameof(ShowsTiming), nameof(ShowsFrame), nameof(ShowsOutput), nameof(ShowsLook), nameof(ShowsMusic))]
    private bool _isNarrow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTabIndex), nameof(ShowsTiming), nameof(ShowsFrame), nameof(ShowsOutput), nameof(ShowsLook), nameof(ShowsMusic))]
    private InspectorTab _tab;

    /// <param name="dockMinWindowWidth">The narrowest window that docks the inspector (token InspectorDockMinWindowWidth, 1000).</param>
    /// <param name="panelHeight">The narrow panel's height when there is room (token NarrowPanelHeight, 260).</param>
    /// <param name="panelMinHeight">The least the panel gives the tabs and a scrolling section (token NarrowPanelMinHeight).</param>
    /// <param name="previewMinHeight">The least height the preview keeps before the panel gives way (token NarrowPreviewMinHeight).</param>
    public ShellLayout(double dockMinWindowWidth, double panelHeight, double panelMinHeight, double previewMinHeight)
    {
        if (!(dockMinWindowWidth > 0) || !(panelMinHeight > 0) || panelHeight < panelMinHeight || !(previewMinHeight >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(dockMinWindowWidth), "The layout sizes must be positive, and the panel no lower than its minimum.");
        }

        _dockMinWindowWidth = dockMinWindowWidth;
        _panelHeight = panelHeight;
        _panelMinHeight = panelMinHeight;
        _previewMinHeight = previewMinHeight;
    }

    /// <summary>True when the tab row shows: only in the narrow layout.</summary>
    public bool ShowsTabs => IsNarrow;

    /// <summary>Whether the Timing section shows: always when docked, only on its tab when narrow.</summary>
    public bool ShowsTiming => !IsNarrow || Tab == InspectorTab.Timing;

    /// <summary>Whether the Frame section shows.</summary>
    public bool ShowsFrame => !IsNarrow || Tab == InspectorTab.Frame;

    /// <summary>Whether the Output section shows.</summary>
    public bool ShowsOutput => !IsNarrow || Tab == InspectorTab.Output;

    /// <summary>Whether the Look section shows (#45; the canvas shows it docked only, but built, it has its own tab).</summary>
    public bool ShowsLook => !IsNarrow || Tab == InspectorTab.Look;

    /// <summary>Whether the Music section shows (#50, a section and a tab the canvas does not have).</summary>
    public bool ShowsMusic => !IsNarrow || Tab == InspectorTab.Music;

    /// <summary>The selected tab as an index for the segmented control (two-way); an index out of range is ignored.</summary>
    public int SelectedTabIndex
    {
        get => (int)Tab;
        set
        {
            if (Enum.IsDefined((InspectorTab)value))
            {
                Tab = (InspectorTab)value;
            }
        }
    }

    /// <summary>Whether a window <paramref name="windowWidth"/> DIPs wide is narrow: below the dock width, exactly 1,000 is not.</summary>
    public bool IsNarrowAt(double windowWidth) => windowWidth < _dockMinWindowWidth;

    /// <summary>
    /// Takes the window's width. Returns true when the layout changed. <paramref name="tabToKeep"/> is the section
    /// the keyboard focus is in, when it is in one, so folding the inspector keeps that section on screen and focus
    /// where it is (a collapsed section would drop it).
    /// </summary>
    public bool Resize(double windowWidth, InspectorTab? tabToKeep = null)
    {
        var narrow = IsNarrowAt(windowWidth);
        if (narrow == IsNarrow)
        {
            return false;
        }

        if (narrow && tabToKeep is { } keep)
        {
            Tab = keep;
        }

        IsNarrow = narrow;
        return true;
    }

    /// <summary>
    /// The narrow panel's height in a body <paramref name="bodyHeight"/> DIPs tall (the area under the command bar).
    /// The panel keeps its full height while the stage still has <paramref name="stageChrome"/> (padding, transport,
    /// filmstrip, gaps) plus the preview's minimum; a shorter window takes the difference from the panel, down to its
    /// minimum, and then from the preview. The panel scrolls, so nothing in it is out of reach.
    /// </summary>
    /// <param name="bodyHeight">The body's height, including the 1 px divider above the stage.</param>
    /// <param name="stageChrome">The stage's height without the preview.</param>
    public double PanelHeight(double bodyHeight, double stageChrome)
    {
        const double divider = 1;
        var room = bodyHeight - divider - stageChrome - _previewMinHeight;
        return Math.Clamp(Math.Floor(room), _panelMinHeight, _panelHeight);
    }
}
