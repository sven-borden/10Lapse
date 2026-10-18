using System.Globalization;
using System.Runtime.InteropServices;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// The diagnostics section about the graphics adapters: name, vendor and device id, video memory and driver version
/// of each adapter DXGI lists, in its order (the first is the one Windows prefers). Plain DXGI through its vtable,
/// a few milliseconds, no device created.
/// </summary>
internal sealed unsafe class GraphicsDiagnostics : IDiagnosticsContributor
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 2;

    // Vtable slots: IUnknown (0-2), IDXGIObject (3-6), then IDXGIFactory (7-11) and IDXGIFactory1.EnumAdapters1 (12);
    // IDXGIAdapter.CheckInterfaceSupport (9) and IDXGIAdapter1.GetDesc1 (10).
    private const int ReleaseSlot = 2;
    private const int EnumAdapters1Slot = 12;
    private const int CheckInterfaceSupportSlot = 9;
    private const int GetDesc1Slot = 10;

    private static readonly Guid _factory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid _dxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    /// <inheritdoc />
    public string Title => "Graphics";

    /// <inheritdoc />
    public void Contribute(DiagnosticsReport report)
    {
        var iid = _factory1;
        void* factory;
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(&iid, &factory));
        try
        {
            for (uint index = 0; ; index++)
            {
                void* adapter;
                var hr = ((delegate* unmanaged<void*, uint, void**, int>)Slot(factory, EnumAdapters1Slot))(factory, index, &adapter);
                if (hr == DxgiErrorNotFound)
                {
                    if (index == 0)
                    {
                        report.Add("Adapters", "none");
                    }

                    return;
                }

                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    report.Add($"Adapter {index + 1}", Describe(adapter));
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
    }

    // "NVIDIA GeForce RTX 4060 Ti (VEN 10DE, DEV 2803), 8,188 MB video memory, driver 32.0.15.6094"
    private static string Describe(void* adapter)
    {
        AdapterDesc1 desc;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged<void*, AdapterDesc1*, int>)Slot(adapter, GetDesc1Slot))(adapter, &desc));

        var name = new string(desc.Description).TrimEnd('\0').Trim();
        var memory = ((ulong)desc.DedicatedVideoMemory / (1024 * 1024)).ToString("N0", CultureInfo.InvariantCulture);
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{name} (VEN {desc.VendorId:X4}, DEV {desc.DeviceId:X4}), {memory} MB video memory");
        if ((desc.Flags & DxgiAdapterFlagSoftware) != 0)
        {
            text += ", software";
        }

        // The user-mode driver version comes back from the IDXGIDevice support check, as in Device Manager.
        var device = _dxgiDevice;
        long umd;
        var hr = ((delegate* unmanaged<void*, Guid*, long*, int>)Slot(adapter, CheckInterfaceSupportSlot))(adapter, &device, &umd);
        text += hr >= 0
            ? string.Create(CultureInfo.InvariantCulture, $", driver {(umd >> 48) & 0xFFFF}.{(umd >> 32) & 0xFFFF}.{(umd >> 16) & 0xFFFF}.{umd & 0xFFFF}")
            : ", driver version not available";
        return text;
    }

    private static void* Slot(void* com, int slot) => (*(void***)com)[slot];

    private static void Release(void* com) => ((delegate* unmanaged<void*, uint>)Slot(com, ReleaseSlot))(com);

    [DllImport("dxgi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CreateDXGIFactory1(Guid* riid, void** factory);

    // DXGI_ADAPTER_DESC1.
    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }
}
