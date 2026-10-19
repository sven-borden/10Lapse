using TenLapse.Presentation.Export;

namespace TenLapse.Presentation.Keyboard;

/// <summary>The three screens of the shell (ShellViewModel's ShowsEmpty, ShowsEditing, IsSettingsOpen).</summary>
public enum ShellScreen
{
    /// <summary>First launch, no photos.</summary>
    Empty,

    /// <summary>The stage and the inspector.</summary>
    Editing,

    /// <summary>The Settings page.</summary>
    Settings,
}

/// <summary>Where the keyboard focus goes after a change.</summary>
public enum FocusTarget
{
    /// <summary>It stays where it is.</summary>
    Keep,

    /// <summary>The first-launch drop zone's Choose folder.</summary>
    ChooseFolder,

    /// <summary>The transport's Play button.</summary>
    Play,

    /// <summary>The Settings page's Back button.</summary>
    Back,

    /// <summary>The command bar's Settings button.</summary>
    SettingsButton,

    /// <summary>The export panel's progress bar, where a screen reader reads the progress.</summary>
    ExportProgress,

    /// <summary>The finished state's Play the video.</summary>
    PlayVideo,

    /// <summary>The command bar's Export button.</summary>
    ExportButton,
}

/// <summary>
/// Where the focus goes after a screen change (#38), so the keyboard and a screen reader stay where the user goes on: on
/// the next screen's first step, never on whatever control happens to come first in the window. A focus the change
/// did not take away (a control still on screen, such as the import's Cancel) is left alone.
/// </summary>
public static class FocusTargets
{
    /// <summary>The screen the shell shows.</summary>
    public static ShellScreen ScreenOf(bool settingsOpen, bool showsEmpty) =>
        settingsOpen ? ShellScreen.Settings : showsEmpty ? ShellScreen.Empty : ShellScreen.Editing;

    /// <summary>After the shell went from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">The screen before.</param>
    /// <param name="to">The screen now.</param>
    /// <param name="focusLost">The focused control left the screen with the change (or nothing had the focus).</param>
    public static FocusTarget AfterScreenChange(ShellScreen from, ShellScreen to, bool focusLost) => (from, to) switch
    {
        _ when from == to => FocusTarget.Keep,

        // Settings is a page of its own: in on Back, out on the button that opened it, wherever the focus was.
        (_, ShellScreen.Settings) => FocusTarget.Back,
        (ShellScreen.Settings, _) => FocusTarget.SettingsButton,

        // The first photos arrived: the preview is what to look at next. A new project: choose the next photos.
        (ShellScreen.Empty, ShellScreen.Editing) when focusLost => FocusTarget.Play,
        (ShellScreen.Editing, ShellScreen.Empty) when focusLost => FocusTarget.ChooseFolder,
        _ => FocusTarget.Keep,
    };

    /// <summary>After the inspector column swapped for <paramref name="state"/> (#24).</summary>
    /// <param name="state">What the column shows now.</param>
    /// <param name="exportButtonShown">The Export button is on screen and enabled (with frames and an output to export).</param>
    public static FocusTarget AfterExport(ExportPanelState state, bool exportButtonShown) => state switch
    {
        ExportPanelState.Exporting => FocusTarget.ExportProgress,
        ExportPanelState.Finished => FocusTarget.PlayVideo,

        // Cancelled, failed or back to editing: where the export began, or the preview when it cannot begin again.
        _ => exportButtonShown ? FocusTarget.ExportButton : FocusTarget.Play,
    };
}
