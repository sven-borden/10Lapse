namespace TenLapse.Presentation.Keyboard;

/// <summary>The window's shortcuts that are keyboard accelerators on the shell (#38; the canvas's keyboard map).</summary>
public enum ShellShortcut
{
    /// <summary>Ctrl+O: Add photos, Choose folder.</summary>
    ChooseFolder,

    /// <summary>Ctrl+Shift+O: Add photos, Choose photos.</summary>
    ChoosePhotos,

    /// <summary>Ctrl+N: New project.</summary>
    NewProject,

    /// <summary>Ctrl+E: Export video.</summary>
    Export,
}

/// <summary>What the shell shows when a shortcut arrives, as far as the shortcuts care.</summary>
/// <param name="SettingsOpen">The Settings page covers the window (its command bar holds only Back).</param>
/// <param name="DialogOpen">A dialog is open (the close-during-export question): it owns the keyboard.</param>
/// <param name="ShowsNewProject">The New project button is on screen (there is a project).</param>
/// <param name="ShowsExport">The Export button is on screen (frames and an output this PC can export).</param>
public readonly record struct ShortcutScreen(bool SettingsOpen, bool DialogOpen, bool ShowsNewProject, bool ShowsExport);

/// <summary>
/// The shell's shortcuts (#38): Ctrl+O and Ctrl+Shift+O add photos (a folder, or files), Ctrl+N starts a new project,
/// Ctrl+E exports. A shortcut works exactly where the control it stands for is on screen, and does nothing elsewhere
/// (unbuilt or unavailable actions are absent, so a shortcut for one is not advertised there either): none on the
/// Settings page or while a dialog is open, New project only with a project, Export only with the Export button. The
/// command then decides as a click would (an import or a new project during an export is refused with an InfoBar, an
/// export while one runs does nothing). Space, the arrows, Delete and Ctrl+Z are not here: they depend on the focused
/// control (<see cref="Preview.TransportKeys"/>, <see cref="Filmstrip.FilmstripKeys"/>).
/// </summary>
public static class ShellShortcuts
{
    /// <summary>The shortcut for a letter key with these modifiers, or null for any other combination.</summary>
    /// <param name="letter">The key's letter, upper case ('O', 'N', 'E').</param>
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
            ('O', false) => ShellShortcut.ChooseFolder,
            ('O', true) => ShellShortcut.ChoosePhotos,
            ('N', false) => ShellShortcut.NewProject,
            ('E', false) => ShellShortcut.Export,
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
            ShellShortcut.ChooseFolder or ShellShortcut.ChoosePhotos => true,
            ShellShortcut.NewProject => screen.ShowsNewProject,
            ShellShortcut.Export => screen.ShowsExport,
            _ => false,
        };
    }
}
