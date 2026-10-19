using System.Runtime.InteropServices;

namespace TenLapse.Services;

/// <summary>
/// The window's taskbar button as a progress bar (#24), through the shell's <c>ITaskbarList3</c>: normal while the export
/// runs, indeterminate while it gets ready, red when it failed, nothing otherwise. Called through the interface's vtable
/// (as <see cref="GraphicsDiagnostics"/> calls DXGI), so the app needs no COM interop generator for two methods. On the UI
/// thread only. Created lazily; a shell without a taskbar (some kiosk setups) makes it do nothing.
/// </summary>
internal sealed unsafe class TaskbarProgress(Func<nint> window) : IDisposable
{
    private const uint ClsctxInprocServer = 1;
    private const ulong Total = 10_000;

    // ITaskbarList3's vtable slots: IUnknown (0 to 2), ITaskbarList (3 to 7), ITaskbarList2 (8), then SetProgressValue and SetProgressState.
    private const int HrInitSlot = 3;
    private const int SetProgressValueSlot = 9;
    private const int SetProgressStateSlot = 10;
    private const int ReleaseSlot = 2;

    private static readonly Guid _clsidTaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private static readonly Guid _iidTaskbarList3 = new("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");

    private nint _taskbar;
    private bool _unavailable;

    // Set by Dispose: a late call (an export that ended after the window closed) must not create the COM object again.
    private bool _disposed;

    /// <summary>The TBPFLAG values.</summary>
    public enum State
    {
        /// <summary>No progress.</summary>
        None = 0,

        /// <summary>Busy, no amount.</summary>
        Indeterminate = 0x1,

        /// <summary>Progress.</summary>
        Normal = 0x2,

        /// <summary>Failed.</summary>
        Error = 0x4,
    }

    /// <summary>Shows <paramref name="fraction"/> (0 to 1).</summary>
    public void SetValue(double fraction)
    {
        if (Taskbar() is not 0 and var taskbar)
        {
            var done = (ulong)Math.Round(Math.Clamp(fraction, 0, 1) * Total);
            var call = (delegate* unmanaged[Stdcall]<nint, nint, ulong, ulong, int>)(*(void***)taskbar)[SetProgressValueSlot];
            Check(call(taskbar, window(), done, Total));
        }
    }

    /// <summary>Sets the kind of progress shown.</summary>
    public void SetState(State state)
    {
        if (Taskbar() is not 0 and var taskbar)
        {
            var call = (delegate* unmanaged[Stdcall]<nint, nint, int, int>)(*(void***)taskbar)[SetProgressStateSlot];
            Check(call(taskbar, window(), (int)state));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        if (_taskbar != 0)
        {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)_taskbar)[ReleaseSlot];
            release(_taskbar);
            _taskbar = 0;
        }
    }

    private nint Taskbar()
    {
        if (_taskbar != 0 || _unavailable || _disposed)
        {
            return _taskbar;
        }

        var hr = CoCreateInstance(in _clsidTaskbarList, 0, ClsctxInprocServer, in _iidTaskbarList3, out var taskbar);
        if (hr < 0 || taskbar == 0)
        {
            _unavailable = true;
            Marshal.ThrowExceptionForHR(hr);
            return 0;
        }

        var init = (delegate* unmanaged[Stdcall]<nint, int>)(*(void***)taskbar)[HrInitSlot];
        hr = init(taskbar);
        if (hr < 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)taskbar)[ReleaseSlot])(taskbar);
            _unavailable = true;
            Marshal.ThrowExceptionForHR(hr);
            return 0;
        }

        _taskbar = taskbar;
        return taskbar;
    }

    private static void Check(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
