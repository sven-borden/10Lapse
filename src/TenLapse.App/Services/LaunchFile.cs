using Microsoft.Windows.AppLifecycle;

namespace TenLapse.Services;

/// <summary>
/// The project file 10Lapse was started to open (#52): a double click on a <c>.10lapse</c> file (packaged, the file type
/// association activates the app with it), or a path on the command line (unpackaged Debug, a shortcut, a script).
/// </summary>
/// <remarks>
/// Each start is a process of its own: opening a project while 10Lapse runs starts a second window with it, and the
/// running one keeps its project (#52's choice; the app already supports two running copies, see
/// <c>JsonFileSettingsStore</c> and <c>RecoveryStore</c>).
/// </remarks>
internal static class LaunchFile
{
    /// <summary>The project file to open, made full, or null when 10Lapse was started without one.</summary>
    public static string? Find()
    {
        try
        {
            if (AppInstance.GetCurrent().GetActivatedEventArgs() is { Kind: ExtendedActivationKind.File, Data: Windows.ApplicationModel.Activation.IFileActivatedEventArgs file }
                && file.Files.Count > 0
                && file.Files[0].Path is { Length: > 0 } activated)
            {
                return Path.GetFullPath(activated);
            }
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // No activation arguments (an unusual host): the command line below still tells.
        }

        return Presentation.ProjectFiles.LaunchArguments.ProjectFile(Environment.GetCommandLineArgs());
    }
}
