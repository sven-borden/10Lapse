using System.Globalization;
using Microsoft.Win32;
using TenLapse.Media.Export;
using Windows.Win32;
using Windows.Win32.Graphics.Dxgi;

namespace TenLapse.Media.Encoding;

/// <summary>
/// What a cached probe result is valid for: the graphics adapter, its driver, the Windows build and the encoders
/// installed. Any change re-probes: a driver update, a Windows update, a new GPU, the HEVC Video Extensions
/// installed or removed. All strings, so two keys compare by value.
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
public sealed record EncoderCacheKey(int Schema, string Adapter, string AdapterName, string Driver, string OsBuild, string Encoders)
{
    /// <summary>The probe schema this build writes and accepts.</summary>
    public const int CurrentSchema = 1;

    /// <summary>The key for this PC now, from the encoders <see cref="EncoderCatalog.Find()"/> listed.</summary>
    public static EncoderCacheKey ForThisMachine(IReadOnlyList<EncoderMft> encoders)
    {
        ArgumentNullException.ThrowIfNull(encoders);
        var (adapter, name, driver) = DefaultAdapter();
        return new EncoderCacheKey(CurrentSchema, adapter, name, driver, WindowsBuild(), EncoderList(encoders));
    }

    /// <summary>The <see cref="Encoders"/> string for a list of encoders: order does not matter.</summary>
    public static string EncoderList(IEnumerable<EncoderMft> encoders) =>
        string.Join(';', encoders.Select(encoder => $"{encoder.Format}:{encoder.Clsid:D}:{encoder.Name}").Distinct().Order(StringComparer.Ordinal));

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
