using System.Globalization;
using TenLapse.Core.Text;

namespace TenLapse.Presentation.Keyboard;

/// <summary>
/// The shortcuts' words (#38), read from <c>Strings/KeyText.resx</c> (#39): each shortcut twice, as a tooltip shows it
/// ("Ctrl+O") and as a screen reader names it (its accelerator key, "Control+O"), and the tooltip that joins a control's
/// name and its shortcut. The letters are fixed in code (<see cref="ShellShortcuts"/>); a translation changes only the
/// names Windows gives the modifier keys in that language.
/// </summary>
public static class KeyText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.KeyText", typeof(KeyText).Assembly);

    /// <summary>A tooltip with the shortcut: {0} the control's name ("New project"), {1} its shortcut ("Ctrl+N").</summary>
    public static string ShortcutTipFormat => _strings.Get();

    /// <summary>Choose folder's shortcut in a tooltip and beside the Add photos menu item.</summary>
    public static string ChooseFolderShortcut => _strings.Get();

    /// <summary>Choose folder's shortcut as a screen reader names it (its accelerator key).</summary>
    public static string ChooseFolderKeys => _strings.Get();

    /// <summary>Choose photos' shortcut in a tooltip and beside the Add photos menu item.</summary>
    public static string ChoosePhotosShortcut => _strings.Get();

    /// <summary>Choose photos' shortcut as a screen reader names it (its accelerator key).</summary>
    public static string ChoosePhotosKeys => _strings.Get();

    /// <summary>New project's shortcut in its tooltip.</summary>
    public static string NewProjectShortcut => _strings.Get();

    /// <summary>New project's shortcut as a screen reader names it (its accelerator key).</summary>
    public static string NewProjectKeys => _strings.Get();

    /// <summary>Export video's shortcut in its tooltip (its accelerator key is <c>ExportText.ExportKeys</c>).</summary>
    public static string ExportShortcut => _strings.Get();

    /// <summary>The play button's key, the space bar: in its tooltip and as a screen reader names it (its accelerator key).</summary>
    public static string PlayKeys => _strings.Get();

    /// <summary>Settings' Back button's key as a screen reader names it (its accelerator key; Alt+Left and the mouse's back button work too).</summary>
    public static string BackKeys => _strings.Get();

    /// <summary>"New project (Ctrl+N)": a control's name with its shortcut, for a tooltip.</summary>
    public static string Tip(string name, string shortcut) => string.Format(CultureInfo.CurrentCulture, ShortcutTipFormat, name, shortcut);
}
