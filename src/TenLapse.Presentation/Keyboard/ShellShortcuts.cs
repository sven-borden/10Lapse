namespace TenLapse.Presentation.Keyboard;

/// <summary>The window's shortcuts that are keyboard accelerators on the shell (#38; the canvas's keyboard map; #52).</summary>
public enum ShellShortcut
{
    /// <summary>Ctrl+I: Add photos, Choose folder (Ctrl+O until #52 gave it to Open).</summary>
    ChooseFolder,

    /// <summary>Ctrl+Shift+I: Add photos, Choose photos.</summary>
    ChoosePhotos,

    /// <summary>Ctrl+N: New project.</summary>
    NewProject,

    /// <summary>Ctrl+E: Export video.</summary>
    Export,

    /// <summary>Ctrl+O: File, Open (#52).</summary>
    Open,

    /// <summary>Ctrl+S: File, Save (#52).</summary>
    Save,

    /// <summary>Ctrl+Shift+S: File, Save as (#52).</summary>
    SaveAs,
}

/// <summary>What the shell shows when a shortcut arrives, as far as the shortcuts care.</summary>
/// <param name="SettingsOpen">The Settings page covers the window (its command bar holds only Back).</param>
/// <param name="DialogOpen">A dialog is open (the close-during-export question): it owns the keyboard.</param>
/// <param name="ShowsNewProject">The New project button is on screen (there is a project).</param>
/// <param name="ShowsExport">The Export button is on screen (frames and an output this PC can export).</param>
/// <param name="ShowsSave">The File menu has Save and Save as (the project has photos).</param>
public readonly record struct ShortcutScreen(bool SettingsOpen, bool DialogOpen, bool ShowsNewProject, bool ShowsExport, bool ShowsSave = false);

/// <summary>
/// The shell's shortcuts (#38, #52): Ctrl+I and Ctrl+Shift+I add photos (a folder, or files), Ctrl+N starts a new project,
/// Ctrl+E exports, Ctrl+O opens a project file, Ctrl+S saves it and Ctrl+Shift+S saves it as a new file. A shortcut works
/// exactly where the control it stands for is on screen, and does nothing elsewhere (unbuilt or unavailable actions are
/// absent, so a shortcut for one is not advertised there either): none on the Settings page or while a dialog is open, New
/// project only with a project, Export only with the Export button, Save and Save as only with photos. The command then
/// decides as a click would (an import or a new project during an export is refused with an InfoBar, an export while one
/// runs does nothing). Space, the arrows, Delete and Ctrl+Z are not here: they depend on the focused control
/// (<see cref="Preview.TransportKeys"/>, <see cref="Filmstrip.FilmstripKeys"/>).
/// </summary>
/// <remarks>
/// Ctrl+O was Choose folder in 3.0. #52 gave it to Open, the shortcut every Windows app with files has, and moved the
/// two Add photos shortcuts to Ctrl+I and Ctrl+Shift+I (I for import, as in video editors).
/// </remarks>
public static class ShellShortcuts
{
    /// <summary>The shortcut for a letter key with these modifiers, or null for any other combination.</summary>
    /// <param name="letter">The key's letter, upper case ('I', 'O', 'S', 'N', 'E').</param>
    /// <param name="control">Ctrl is down.</param>
    /// <param name="shift">Shift is down.</param>
    /// <param name="other">Alt or the Windows key is down.</param>
    public static ShellShortcut? Resolve(char letter, bool control, bool shift, bool other)
    {
        if (!control || other)
        {
            return null;
        }

        return (char.ToUpperInvariant(letter), shift) switch
        {
            ('I', false) => ShellShortcut.ChooseFolder,
            ('I', true) => ShellShortcut.ChoosePhotos,
            ('N', false) => ShellShortcut.NewProject,
            ('E', false) => ShellShortcut.Export,
            ('O', false) => ShellShortcut.Open,
            ('S', false) => ShellShortcut.Save,
            ('S', true) => ShellShortcut.SaveAs,
            _ => null,
        };
    }

    /// <summary>Whether <paramref name="shortcut"/> works on <paramref name="screen"/>.</summary>
    public static bool IsAvailable(ShellShortcut shortcut, ShortcutScreen screen)
    {
        if (screen.SettingsOpen || screen.DialogOpen)
        {
            return false;
        }

        return shortcut switch
        {
            ShellShortcut.ChooseFolder or ShellShortcut.ChoosePhotos or ShellShortcut.Open => true,
            ShellShortcut.NewProject => screen.ShowsNewProject,
            ShellShortcut.Export => screen.ShowsExport,
            ShellShortcut.Save or ShellShortcut.SaveAs => screen.ShowsSave,
            _ => false,
        };
    }
}
