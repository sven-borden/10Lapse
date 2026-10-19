using System.ComponentModel;
using System.Runtime.InteropServices;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// <see cref="IKeepAwake"/> as a Windows power request (#25): <c>PowerCreateRequest</c> and <c>PowerSetRequest</c> with
/// <c>PowerRequestSystemRequired</c>, let go with <c>PowerClearRequest</c> and <c>CloseHandle</c>. The reason string is
/// what <c>powercfg /requests</c> shows. It does not ask for the display (<c>PowerRequestDisplayRequired</c>): the screen
/// may turn off while the video is written. A full-trust process is not suspended, so this is all an export needs to
/// finish with the window minimized and the display off.
/// </summary>
/// <remarks>
/// A refusal is logged once per launch and the export goes on without a request. The lease may be disposed from any thread.
/// </remarks>
internal sealed class PowerRequestKeepAwake : IKeepAwake
{
    private readonly IPowerRequests _api;
    private readonly IErrorSink _log;
    private int _reported;

    /// <summary>The service over the Win32 calls <paramref name="api"/> makes (a fake in tests).</summary>
    /// <param name="api">The calls.</param>
    /// <param name="log">Where a failure goes (log only).</param>
    public PowerRequestKeepAwake(IPowerRequests api, IErrorSink log)
    {
        _api = api;
        _log = log;
    }

    /// <summary>The service over the real Windows calls.</summary>
    /// <param name="log">Where a failure goes (log only).</param>
    public PowerRequestKeepAwake(IErrorSink log)
        : this(new Win32PowerRequests(), log)
    {
    }

    /// <inheritdoc />
    public IDisposable Hold(string reason)
    {
        try
        {
            var handle = _api.Create(reason);
            try
            {
                _api.SetSystemRequired(handle);
            }
            catch
            {
                _api.Close(handle);
                throw;
            }

            return new Lease(this, handle);
        }
        catch (Exception exception)
        {
            ReportOnce(exception, ExportText.KeepAwakeFailed);
            return NoLease.Instance;
        }
    }

    // One line in the log, however many exports fail the same way.
    private void ReportOnce(Exception exception, string context)
    {
        if (Interlocked.Exchange(ref _reported, 1) == 0)
        {
            _log.Report(exception, context);
        }
    }

    private sealed class Lease(PowerRequestKeepAwake owner, nint handle) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            try
            {
                try
                {
                    owner._api.ClearSystemRequired(handle);
                }
                finally
                {
                    owner._api.Close(handle);
                }
            }
            catch (Exception exception)
            {
                owner.ReportOnce(exception, ExportText.KeepAwakeReleaseFailed);
            }
        }
    }

    private sealed class NoLease : IDisposable
    {
        public static readonly NoLease Instance = new();

        public void Dispose()
        {
            // Nothing was taken.
        }
    }
}

/// <summary>The four power request calls <see cref="PowerRequestKeepAwake"/> makes. Each throws a <see cref="Win32Exception"/> when Windows refuses.</summary>
internal interface IPowerRequests
{
    /// <summary>Creates a request that shows <paramref name="reason"/> in <c>powercfg /requests</c>; returns its handle.</summary>
    nint Create(string reason);

    /// <summary>Turns on <c>PowerRequestSystemRequired</c> for <paramref name="handle"/>.</summary>
    void SetSystemRequired(nint handle);

    /// <summary>Turns it off again.</summary>
    void ClearSystemRequired(nint handle);

    /// <summary>Closes the handle (and with it the request, whatever is set).</summary>
    void Close(nint handle);
}

/// <summary>The Win32 power request API (kernel32).</summary>
internal sealed class Win32PowerRequests : IPowerRequests
{
    // POWER_REQUEST_TYPE.PowerRequestSystemRequired.
    private const int SystemRequired = 1;

    // REASON_CONTEXT.Flags: the reason is a plain string.
    private const uint SimpleString = 0x1;

    private static readonly nint _invalid = -1;

    // The reason strings, kept alive until their request is closed (the context points at them).
    private readonly Dictionary<nint, nint> _reasons = [];
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public nint Create(string reason)
    {
        var text = Marshal.StringToHGlobalUni(reason);
        var context = new ReasonContext { Version = 0, Flags = SimpleString, SimpleReasonString = text };
        var handle = PowerCreateRequest(ref context);
        if (handle == 0 || handle == _invalid)
        {
            var error = Marshal.GetLastPInvokeError();
            Marshal.FreeHGlobal(text);
            throw new Win32Exception(error, "PowerCreateRequest failed.");
        }

        lock (_gate)
        {
            _reasons[handle] = text;
        }

        return handle;
    }

    /// <inheritdoc />
    public void SetSystemRequired(nint handle)
    {
        if (!PowerSetRequest(handle, SystemRequired))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "PowerSetRequest failed.");
        }
    }

    /// <inheritdoc />
    public void ClearSystemRequired(nint handle)
    {
        if (!PowerClearRequest(handle, SystemRequired))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "PowerClearRequest failed.");
        }
    }

    /// <inheritdoc />
    public void Close(nint handle)
    {
        // The string is forgotten before the handle is closed, so a new request that gets the same handle value cannot
        // have its string freed by this call. It is freed after: the request may read it until then.
        nint text;
        lock (_gate)
        {
            _reasons.Remove(handle, out text);
        }

        var closed = CloseHandle(handle);
        var error = Marshal.GetLastPInvokeError();
        if (text != 0)
        {
            Marshal.FreeHGlobal(text);
        }

        if (!closed)
        {
            throw new Win32Exception(error, "CloseHandle failed.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(nint request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(nint request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    // REASON_CONTEXT: Version, Flags, then a union whose Detailed member is the largest (a module, an id, a count and a
    // string array: 24 bytes on x64); SimpleReasonString sits at its start.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct ReasonContext
    {
        [FieldOffset(0)]
        public uint Version;

        [FieldOffset(4)]
        public uint Flags;

        [FieldOffset(8)]
        public nint SimpleReasonString;
    }
}
