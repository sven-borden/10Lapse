using System.Runtime.InteropServices;
using Windows.Storage;

namespace TenLapse.Services;

/// <summary>
/// Where the app runs: whether it has package identity, and where its log goes. Debug runs unpackaged, where
/// <c>ApplicationData.Current</c> and <c>Package.Current</c> throw (docs/STATUS.md, Decisions), so everything here
/// asks <see cref="HasPackageIdentity"/> first and has an unpackaged path.
/// </summary>
internal static class AppEnvironment
{
    private const int AppModelErrorNoPackage = 15700;

    private static readonly Lazy<bool> _hasPackageIdentity = new(ReadPackageIdentity);

    /// <summary>True when the process runs from its MSIX package (the Store build), false unpackaged (Debug).</summary>
    public static bool HasPackageIdentity => _hasPackageIdentity.Value;

    /// <summary>
    /// The log folder: the package's LocalState\Logs (removed with the app), or %LOCALAPPDATA%\10Lapse\Logs when
    /// unpackaged. Never throws: if LocalState cannot be read, the unpackaged folder is used.
    /// </summary>
    public static string LogFolder()
    {
        if (HasPackageIdentity)
        {
            try
            {
                return Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs");
            }
            catch (Exception)
            {
                // Fall through to the folder that needs no identity; the log says which one it uses.
            }
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(string.IsNullOrEmpty(local) ? Path.GetTempPath() : local, "10Lapse", "Logs");
    }

    private static bool ReadPackageIdentity()
    {
        try
        {
            uint length = 0;
            return GetCurrentPackageFullName(ref length, IntPtr.Zero) != AppModelErrorNoPackage;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
