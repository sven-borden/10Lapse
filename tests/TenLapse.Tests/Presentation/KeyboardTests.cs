using TenLapse.Presentation.Export;
using TenLapse.Presentation.Keyboard;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// The shell's shortcuts and where the focus goes after a change (#38): which key is which shortcut, where each works
/// (exactly where its control is on screen), and the focus target after a screen change and after the export panel swaps.
/// </summary>
public sealed class KeyboardTests
{
    private static readonly ShortcutScreen _editing = new(SettingsOpen: false, DialogOpen: false, ShowsNewProject: true, ShowsExport: true);
    private static readonly ShortcutScreen _empty = new(SettingsOpen: false, DialogOpen: false, ShowsNewProject: false, ShowsExport: false);

    [Theory]
    [InlineData('O', false, ShellShortcut.ChooseFolder)]
    [InlineData('o', false, ShellShortcut.ChooseFolder)]
    [InlineData('O', true, ShellShortcut.ChoosePhotos)]
    [InlineData('N', false, ShellShortcut.NewProject)]
    [InlineData('E', false, ShellShortcut.Export)]
    public void CtrlWithTheLetterIsTheShortcut(char letter, bool shift, ShellShortcut expected) =>
        Assert.Equal(expected, ShellShortcuts.Resolve(letter, control: true, shift, other: false));

    [Theory]
    [InlineData('O', false, false, false)]
    [InlineData('O', true, false, false)]
    [InlineData('N', false, false, false)]
    [InlineData('N', true, true, false)]
    [InlineData('E', true, true, false)]
    [InlineData('E', false, true, true)]
    [InlineData('Z', false, true, false)]
    [InlineData('S', false, true, false)]
    public void AnyOtherCombinationIsNoShortcut(char letter, bool shift, bool control, bool other) =>
        Assert.Null(ShellShortcuts.Resolve(letter, control, shift, other));

    [Theory]
    [InlineData(ShellShortcut.ChooseFolder)]
    [InlineData(ShellShortcut.ChoosePhotos)]
    [InlineData(ShellShortcut.NewProject)]
    [InlineData(ShellShortcut.Export)]
    public void EveryShortcutWorksWhileEditing(ShellShortcut shortcut) =>
        Assert.True(ShellShortcuts.IsAvailable(shortcut, _editing));

    [Theory]
    [InlineData(ShellShortcut.ChooseFolder, true)]
    [InlineData(ShellShortcut.ChoosePhotos, true)]
    [InlineData(ShellShortcut.NewProject, false)]
    [InlineData(ShellShortcut.Export, false)]
    public void OnTheEmptyScreenOnlyAddingPhotosWorks(ShellShortcut shortcut, bool works) =>
        Assert.Equal(works, ShellShortcuts.IsAvailable(shortcut, _empty));

    [Theory]
    [InlineData(ShellShortcut.ChooseFolder)]
    [InlineData(ShellShortcut.ChoosePhotos)]
    [InlineData(ShellShortcut.NewProject)]
    [InlineData(ShellShortcut.Export)]
    public void NoShortcutWorksOnTheSettingsPageOrUnderADialog(ShellShortcut shortcut)
    {
        Assert.False(ShellShortcuts.IsAvailable(shortcut, _editing with { SettingsOpen = true }));
        Assert.False(ShellShortcuts.IsAvailable(shortcut, _editing with { DialogOpen = true }));
    }

    [Fact]
    public void ExportFollowsItsButton() =>
        Assert.False(ShellShortcuts.IsAvailable(ShellShortcut.Export, _editing with { ShowsExport = false }));

    [Theory]
    [InlineData(false, true, ShellScreen.Empty)]
    [InlineData(false, false, ShellScreen.Editing)]
    [InlineData(true, false, ShellScreen.Settings)]
    [InlineData(true, true, ShellScreen.Settings)]
    public void TheScreenComesFromTheShellsFlags(bool settingsOpen, bool showsEmpty, ShellScreen expected) =>
        Assert.Equal(expected, FocusTargets.ScreenOf(settingsOpen, showsEmpty));

    [Theory]
    [InlineData(ShellScreen.Empty, ShellScreen.Settings, false, FocusTarget.Back)]
    [InlineData(ShellScreen.Editing, ShellScreen.Settings, false, FocusTarget.Back)]
    [InlineData(ShellScreen.Settings, ShellScreen.Empty, false, FocusTarget.SettingsButton)]
    [InlineData(ShellScreen.Settings, ShellScreen.Editing, true, FocusTarget.SettingsButton)]
    [InlineData(ShellScreen.Empty, ShellScreen.Editing, true, FocusTarget.Play)]
    [InlineData(ShellScreen.Editing, ShellScreen.Empty, true, FocusTarget.ChooseFolder)]
    public void AScreenChangePutsTheFocusOnItsNextStep(ShellScreen from, ShellScreen to, bool focusLost, FocusTarget expected) =>
        Assert.Equal(expected, FocusTargets.AfterScreenChange(from, to, focusLost));

    [Theory]
    [InlineData(ShellScreen.Empty, ShellScreen.Editing)]
    [InlineData(ShellScreen.Editing, ShellScreen.Empty)]
    [InlineData(ShellScreen.Editing, ShellScreen.Editing)]
    public void AFocusTheChangeDidNotTakeAwayStays(ShellScreen from, ShellScreen to) =>
        Assert.Equal(FocusTarget.Keep, FocusTargets.AfterScreenChange(from, to, focusLost: false));

    [Theory]
    [InlineData(ExportPanelState.Exporting, true, FocusTarget.ExportProgress)]
    [InlineData(ExportPanelState.Finished, true, FocusTarget.PlayVideo)]
    [InlineData(ExportPanelState.Editing, true, FocusTarget.ExportButton)]
    [InlineData(ExportPanelState.Editing, false, FocusTarget.Play)]
    public void TheExportPanelsSwapPutsTheFocusWhereTheUserGoesOn(ExportPanelState state, bool exportButtonShown, FocusTarget expected) =>
        Assert.Equal(expected, FocusTargets.AfterExport(state, exportButtonShown));

    [Fact]
    public void ALiveProgressSaysTheFirstTextThenAtMostOncePerInterval()
    {
        var live = new LiveProgress(TimeSpan.FromSeconds(5));
        var said = new List<string>();
        for (var tenth = 0; tenth <= 120; tenth++)
        {
            // A new text every 100 ms for 12 s, as an import's batches.
            if (live.Next($"Adding {tenth} of 120 photos…", TimeSpan.FromMilliseconds(100 * tenth)) is { } text)
            {
                said.Add(text);
            }
        }

        Assert.Equal(["Adding 0 of 120 photos…", "Adding 50 of 120 photos…", "Adding 100 of 120 photos…"], said);
    }

    [Fact]
    public void ALiveProgressNeverRepeatsATextAndStartsAfreshAfterAReset()
    {
        var live = new LiveProgress(TimeSpan.FromSeconds(5));

        Assert.Equal("Looking for photos…", live.Next("Looking for photos…", TimeSpan.Zero));
        Assert.Null(live.Next("Looking for photos…", TimeSpan.FromSeconds(10)));
        Assert.Null(live.Next(string.Empty, TimeSpan.FromSeconds(20)));
        live.Reset();
        Assert.Equal("Looking for photos…", live.Next("Looking for photos…", TimeSpan.FromSeconds(21)));
    }

    [Fact]
    public void ATooltipJoinsTheNameAndTheShortcut()
    {
        Assert.Equal("New project (Ctrl+N)", ShortcutTips.NewProject);
        Assert.Equal("Export video (Ctrl+E)", ShortcutTips.Export);
        Assert.Equal("Control+O", KeyText.ChooseFolderKeys);
        Assert.Equal("Control+Shift+O", KeyText.ChoosePhotosKeys);
    }
}
