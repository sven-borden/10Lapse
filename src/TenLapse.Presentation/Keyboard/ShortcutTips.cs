using TenLapse.Presentation.Export;

namespace TenLapse.Presentation.Keyboard;

/// <summary>
/// The tooltips that show a control's shortcut (#38), for x:Bind: its name and its keys ("New project (Ctrl+N)"), so a
/// sighted keyboard user learns them where a screen reader hears the accelerator key.
/// </summary>
public static class ShortcutTips
{
    /// <summary>The first-launch Choose folder.</summary>
    public static string ChooseFolder => KeyText.Tip(ProjectText.FirstLaunchChooseFolder, KeyText.ChooseFolderShortcut);

    /// <summary>The first-launch Choose photos.</summary>
    public static string ChoosePhotos => KeyText.Tip(ProjectText.FirstLaunchChoosePhotos, KeyText.ChoosePhotosShortcut);

    /// <summary>The command bar's New project.</summary>
    public static string NewProject => KeyText.Tip(ProjectText.NewProject, KeyText.NewProjectShortcut);

    /// <summary>The command bar's Export video.</summary>
    public static string Export => KeyText.Tip(ExportText.ExportVideo, KeyText.ExportShortcut);
}
