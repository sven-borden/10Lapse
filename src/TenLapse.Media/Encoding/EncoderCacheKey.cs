using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Win32;
using TenLapse.Media.Export;
using Windows.Win32;
using Windows.Win32.Graphics.Dxgi;

namespace TenLapse.Media.Encoding;

/// <summary>
/// What a cached probe result is valid for: the graphics adapter, its driver, the Windows build, the encoders and
/// decoders installed and their versions, and the build of 10Lapse that probed. Any change re-probes: a driver
/// update, a Windows update, a new GPU, the HEVC Video Extensions installed, removed or updated, an app update.
/// All strings, so two keys compare by value.
/// </summary>
/// <param name="Schema">The probe's own version (<see cref="CurrentSchema"/>): raise it when the probe or the file changes.</param>
/// <param name="Adapter">
/// The default adapter's PCI ids, as <c>VEN_10DE&amp;DEV_2803&amp;SUBSYS_171719DA&amp;REV_A1</c>. Not its LUID,
/// which Windows assigns anew at every boot and would re-probe every start.
/// </param>
/// <param name="AdapterName">The adapter's description, such as "NVIDIA GeForce RTX 4060 Ti".</param>
/// <param name="Driver">The user-mode driver version, such as "32.0.16.1714".</param>
/// <param name="OsBuild">The Windows version with its update revision, such as "10.0.26300.6899".</param>
/// <param name="Encoders">
/// Every listed encoder as <c>format:clsid:name</c>, sorted, separated by semicolons. The name counts because a
/// Store codec (the HEVC Video Extensions' "HEVCVideoExtensionEncoder") is listed without a class id.
/// </param>
/// <param name="Decoders">The listed HEVC decoders, the same way: installing or removing the HEVC Video Extensions re-probes.</param>
/// <param name="MediaVersion">
/// TenLapse.Media's informational version (with the commit in a developer build): an app update re-probes even when
/// nobody remembered to raise <see cref="CurrentSchema"/> after changing how the probe or the export encodes.
/// </param>
/// <param name="CodecVersions">
/// The file version of each listed encoder's and decoder's DLL as <c>clsid=version</c>, and the version of each
/// Store codec package (listed without a class id) as <c>package=version</c>, sorted, separated by semicolons:
/// a codec update re-probes even when the driver and Windows stay the same.
/// </param>
public sealed record EncoderCacheKey(
    int Schema,
    string Adapter,
    string AdapterName,
    string Driver,
    string OsBuild,
    string Encoders,
    string Decoders,
    string MediaVersion,
    string CodecVersions)
{
    // The Store packages that bring H.264 or HEVC codecs without a class id: the HEVC Video Extensions (the paid
    // and the PC maker's editions share this family name).
    private static readonly string[] _codecPackages = ["Microsoft.HEVCVideoExtension_8wekyb3d8bbwe"];

    /// <summary>The probe schema this build writes and accepts.</summary>
    public const int CurrentSchema = 2;

    /// <summary>
    /// The key for this PC now, from the encoders <see cref="EncoderCatalog.Find()"/> and the decoders
    /// <see cref="EncoderCatalog.FindDecoders"/> listed.
    /// </summary>
    public static EncoderCacheKey ForThisMachine(IReadOnlyList<EncoderMft> encoders, IReadOnlyList<DecoderMft> decoders)
    {
        ArgumentNullException.ThrowIfNull(encoders);
        ArgumentNullException.ThrowIfNull(decoders);
        var (adapter, name, driver) = DefaultAdapter();
        IEnumerable<Guid> classes = [.. encoders.Select(encoder => encoder.Clsid), .. decoders.Select(decoder => decoder.Clsid)];
        return new EncoderCacheKey(
            CurrentSchema,
            adapter,
            name,
            driver,
            WindowsBuild(),
            EncoderList(encoders),
            DecoderList(decoders),
            ThisMediaVersion,
            CodecVersionList(classes));
    }

    /// <summary>TenLapse.Media's informational version, the <see cref="MediaVersion"/> of a key made now.</summary>
    public static string ThisMediaVersion { get; } =
        typeof(EncoderCacheKey).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>
    /// The <see cref="CodecVersions"/> string for these class ids (an empty one stands for a Store codec, and adds
    /// the Store codec packages' versions). Never throws: what cannot be read is "unknown".
    /// </summary>
    internal static string CodecVersionList(IEnumerable<Guid> classes)
    {
        var entries = new List<string>();
        var storeCodec = false;
        foreach (var clsid in classes.Distinct())
        {
            if (clsid == Guid.Empty)
            {
                storeCodec = true;
                continue;
            }

            entries.Add($"{clsid:D}={DllVersion(clsid)}");
        }

        if (storeCodec)
        {
            entries.AddRange(_codecPackages.Select(family => $"{family}={PackageVersion(family)}"));
        }

        return Join(entries);
    }

    /// <summary>The <see cref="Encoders"/> string for a list of encoders: order does not matter.</summary>
    public static string EncoderList(IEnumerable<EncoderMft> encoders) =>
        Join(encoders.Select(encoder => $"{encoder.Format}:{encoder.Clsid:D}:{encoder.Name}"));

    /// <summary>The <see cref="Decoders"/> string for a list of decoders: order does not matter.</summary>
    public static string DecoderList(IEnumerable<DecoderMft> decoders) =>
        Join(decoders.Select(decoder => $"{decoder.Format}:{decoder.Clsid:D}:{decoder.Name}"));

    private static string Join(IEnumerable<string> entries) => string.Join(';', entries.Distinct().Order(StringComparer.Ordinal));

    // The COM server's file version: HKCR\CLSID\{clsid}\InprocServer32 names the DLL (a bare name lives in System32).
    private static string DllVersion(Guid clsid)
    {
        try
        {
            using var server = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid:B}\InprocServer32");
            if (server?.GetValue(null) is not string registered || registered.Length == 0)
            {
                return "unregistered";
            }

            var path = Environment.ExpandEnvironmentVariables(registered.Trim('"'));
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(Environment.SystemDirectory, path);
            }

            return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion ?? "no version" : "missing";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return "unknown";
        }
    }

    // The installed version of a Store package for this user, or "none".
    private static string PackageVersion(string familyName)
    {
        try
        {
            var package = new Windows.Management.Deployment.PackageManager().FindPackagesForUser(string.Empty, familyName).FirstOrDefault();
            if (package is null)
            {
                return "none";
            }

            var version = package.Id.Version;
            return string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
        }
        catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
        {
            // Package queries refused (a sandbox, a policy): the encoder names still tell an install from a removal.
            return "unknown";
        }
    }

    // The adapter D3D11CreateDevice(null, HARDWARE) uses: the first one DXGI lists (Windows orders them by the
    // app's GPU preference). If DXGI cannot say, the key says "unknown" and the probe itself finds out.
    private static (string Adapter, string Name, string Driver) DefaultAdapter()
    {
        IDXGIFactory1? factory = null;
        IDXGIAdapter1? adapter = null;
        try
        {
            PInvoke.CreateDXGIFactory1(out IDXGIFactory1 created).ThrowOnFailure();
            factory = created;
            factory.EnumAdapters1(0, out var first).ThrowOnFailure();
            adapter = first;
            var description = adapter.GetDesc1();
            var ids = string.Create(
                CultureInfo.InvariantCulture,
                $"VEN_{description.VendorId:X4}&DEV_{description.DeviceId:X4}&SUBSYS_{description.SubSysId:X8}&REV_{description.Revision:X2}");
            return (ids, description.Description.ToString(), DriverVersion(adapter));
        }
        catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
        {
            // DXGI_ERROR_NOT_FOUND: no adapter at all.
            return ("unknown", "unknown", "unknown");
        }
        finally
        {
            Release(adapter);
            Release(factory);
        }
    }

    // CheckInterfaceSupport with IDXGIDevice returns the user-mode driver version as four 16-bit parts, the
    // number Device Manager shows.
    private static string DriverVersion(IDXGIAdapter1 adapter)
    {
        try
        {
            adapter.CheckInterfaceSupport(typeof(IDXGIDevice).GUID, out var version);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(version >> 48) & 0xFFFF}.{(version >> 32) & 0xFFFF}.{(version >> 16) & 0xFFFF}.{version & 0xFFFF}");
        }
        catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
        {
            return "unknown";
        }
    }

    // Environment.OSVersion has no update revision; the monthly update can change Media Foundation, so it counts.
    private static string WindowsBuild()
    {
        var version = Environment.OSVersion.Version;
        var revision = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", null) is int ubr ? ubr : 0;
        return string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}.{revision}");
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(comObject))
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
        }
    }
}
