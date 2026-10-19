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

    /// <summary>
    /// The encoder capabilities cache (#9's <c>EncoderProbe.LoadOrProbeAsync</c> path): <c>encoder-capabilities.json</c>
    /// in the package's LocalState, or in %LOCALAPPDATA%\10Lapse unpackaged. Never throws.
    /// </summary>
    public static string EncoderCacheFile() => Path.Combine(Path.GetDirectoryName(LogFolder())!, "encoder-capabilities.json");

    /// <summary>
    /// The journal of unfinished export files (#22's <c>PartialFileJournal</c>): <c>unfinished-exports.txt</c> in the
    /// package's LocalState, or in %LOCALAPPDATA%\10Lapse unpackaged. Never throws.
    /// </summary>
    public static string UnfinishedExportsFile() => Path.Combine(Path.GetDirectoryName(LogFolder())!, "unfinished-exports.txt");

    /// <summary>
    /// The profile folder's 8.3 short form (<c>C:\Users\JANEDO~1</c>), which some paths in exception messages carry,
    /// so Copy diagnostics can redact it too. Null when Windows has none or cannot say. Never throws.
    /// </summary>
    public static string? ShortProfileFolder()
    {
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(profile))
            {
                return null;
            }

            var buffer = new char[1024];
            var length = GetShortPathNameW(profile, buffer, (uint)buffer.Length);
            return length is > 0 and < 1024 ? new string(buffer, 0, (int)length) : null;
        }
        catch (Exception)
        {
            return null;
        }
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);
}
