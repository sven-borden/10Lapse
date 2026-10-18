using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TenLapse.Presentation.Services;
using Windows.ApplicationModel;

namespace TenLapse.Services;

/// <summary>The diagnostics section about the app and Windows: versions, architecture, identity, language.</summary>
internal sealed class AppDiagnostics : IDiagnosticsContributor
{
    /// <inheritdoc />
    public string Title => "App and Windows";

    /// <inheritdoc />
    public void Contribute(DiagnosticsReport report)
    {
        report.Add("App version", AppVersion());
        report.Add("Package identity", AppEnvironment.HasPackageIdentity ? "Yes" : "No (unpackaged)");
        report.Add("Windows", WindowsVersion());
        report.Add("Architecture", $"{RuntimeInformation.ProcessArchitecture} process on {RuntimeInformation.OSArchitecture} Windows");
        report.Add(".NET", RuntimeInformation.FrameworkDescription);
        report.Add("Windows App Runtime", Try(() => Microsoft.Windows.ApplicationModel.WindowsAppRuntime.RuntimeInfo.AsString));
        report.Add("Language", CultureInfo.CurrentUICulture.Name);
    }

    /// <summary>The package version when packaged; otherwise the assembly's informational version (with the commit).</summary>
    public static string AppVersion()
    {
        if (AppEnvironment.HasPackageIdentity)
        {
            var version = Try(() =>
            {
                var v = Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            });
            if (version != NotAvailable)
            {
                return version;
            }
        }

        return Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? NotAvailable;
    }

    private const string NotAvailable = "not available";

    // "10.0.26300.6899 (25H2)": the build with its update revision, which Environment.OSVersion leaves at 0.
    private static string WindowsVersion()
    {
        var version = Environment.OSVersion.Version;
        var text = $"{version.Major}.{version.Minor}.{version.Build}";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("UBR") is int revision)
            {
                text += "." + revision.ToString(CultureInfo.InvariantCulture);
            }

            if (key?.GetValue("DisplayVersion") is string display && display.Length > 0)
            {
                text += $" ({display})";
            }
        }
        catch (Exception)
        {
            // The build number alone still says which Windows it is.
        }

        return text;
    }

    private static string Try(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return NotAvailable;
        }
    }
}
