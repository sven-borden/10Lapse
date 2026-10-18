using System.Globalization;
using System.Runtime.InteropServices;
using TenLapse.Core;
using TenLapse.Presentation;

namespace TenLapse.Services;

/// <summary>
/// The last resort when the main window cannot be created at startup: there is no InfoBar to show, so a plain
/// Windows message box says what happened and where the log is. Never throws.
/// </summary>
internal static class StartupFailure
{
    /// <summary>The process exit code after a startup failure.</summary>
    public const int ExitCode = 1;

    private const uint MbOk = 0x0;
    private const uint MbIconError = 0x10;

    /// <summary>Shows the message box and waits for OK.</summary>
    /// <param name="logFolder">Where the log is, named in the message.</param>
    public static void Show(string logFolder)
    {
        try
        {
            var text = string.Format(CultureInfo.InvariantCulture, ProjectText.StartupFailed, logFolder);
            _ = MessageBoxW(IntPtr.Zero, text, Product.Name, MbOk | MbIconError);
        }
        catch (Exception)
        {
            // Nothing is left to show it with; the log has the failure.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
