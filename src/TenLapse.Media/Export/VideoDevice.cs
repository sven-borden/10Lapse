using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.System.WinRT.Direct3D11;

namespace TenLapse.Media.Export;

/// <summary>
/// A Direct3D 11 device made for an export, and the Win2D device on top of it.
/// Drawing, colour conversion and the H.264 encoder all run on this one device,
/// so the GPU orders their work and nothing crosses devices.
/// </summary>
/// <remarks>
/// Created with video support (Media Foundation needs <c>ID3D11VideoDevice</c>
/// for the BGRA to NV12 conversion) and with multithread protection: Media
/// Foundation calls the immediate context from its own threads while Direct2D
/// draws on it from ours. Win2D's shared device has neither.
/// </remarks>
internal sealed class VideoDevice : IDisposable
{
    private VideoDevice(ID3D11Device device, CanvasDevice canvas)
    {
        Direct3D = device;
        Canvas = canvas;
    }

    /// <summary>The Direct3D 11 device, for <c>IMFDXGIDeviceManager.ResetDevice</c>.</summary>
    public ID3D11Device Direct3D { get; }

    /// <summary>The Win2D device every source bitmap and render target of the export is made on.</summary>
    public CanvasDevice Canvas { get; }

    /// <summary>Creates the device on the default adapter. Call it on an MTA thread (the export runs on the thread pool).</summary>
    /// <exception cref="VideoExportException">
    /// <see cref="VideoExportFailure.NoGraphicsDevice"/> when there is no hardware device with video support (only
    /// the Microsoft Basic Render Driver, for one) or creating it failed; <see cref="VideoExportFailure.OutOfMemory"/>.
    /// </exception>
    public static VideoDevice Create()
    {
        try
        {
            return CreateCore();
        }
        catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
        {
            // DXGI_ERROR_UNSUPPORTED on a PC with only WARP, or anything else: no device. Memory is the one exception.
            throw ExportFailures.Wrap(
                exception,
                ExportFailures.FromHResult(exception.HResult) == VideoExportFailure.OutOfMemory
                    ? VideoExportFailure.OutOfMemory
                    : VideoExportFailure.NoGraphicsDevice,
                VideoFormat.Mp4H264); // Neither summary names a codec.
        }
    }

    private static unsafe VideoDevice CreateCore()
    {
        ReadOnlySpan<D3D_FEATURE_LEVEL> levels = [D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0];
        PInvoke.D3D11CreateDevice(
                null,
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                default,
                D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
                levels,
                PInvoke.D3D11_SDK_VERSION,
                out var device,
                out _,
                out var context)
            .ThrowOnFailure();
        Marshal.ReleaseComObject(context);
        try
        {
            ((ID3D11Multithread)device).SetMultithreadProtected(true);

            PInvoke.CreateDirect3D11DeviceFromDXGIDevice((IDXGIDevice)device, out var inspectable).ThrowOnFailure();
            var pointer = Marshal.GetIUnknownForObject(inspectable);
            try
            {
                // Win2D keeps its own reference. Disposed here, or the wrapper holds the whole device (about 90 MB
                // private and 160 handles for a 640x360 export, 300 MB and 230 MB of video memory at 4K) until a GC.
                using var direct3DDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer);
                return new VideoDevice(device, CanvasDevice.CreateFromDirect3D11Device(direct3DDevice));
            }
            finally
            {
                Marshal.Release(pointer);
                Marshal.ReleaseComObject(inspectable);
            }
        }
        catch
        {
            Marshal.ReleaseComObject(device);
            throw;
        }
    }

    /// <summary>The texture under a Win2D bitmap or render target made on this device.</summary>
    public static unsafe ID3D11Texture2D TextureOf(CanvasBitmap bitmap)
    {
        var inspectable = WinRT.MarshalInspectable<object>.FromManaged(bitmap);
        try
        {
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(inspectable);
            try
            {
                var iid = typeof(ID3D11Texture2D).GUID;
                access.GetInterface(&iid, out var raw);
                try
                {
                    return (ID3D11Texture2D)Marshal.GetObjectForIUnknown((nint)raw);
                }
                finally
                {
                    Marshal.Release((nint)raw);
                }
            }
            finally
            {
                // The wrapper of the render target; left to the GC it keeps every frame's texture alive.
                Marshal.ReleaseComObject(access);
            }
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    /// <summary>
    /// Why the device was removed (DXGI_ERROR_DEVICE_HUNG, _REMOVED, _RESET, DXGI_ERROR_DRIVER_INTERNAL_ERROR),
    /// or 0 while it works: <c>ID3D11Device::GetDeviceRemovedReason</c>. Any thread (the device is multithread protected).
    /// </summary>
    public int RemovedReason()
    {
        try
        {
            // CsWin32 turns the HRESULT into an exception; S_OK returns.
            Direct3D.GetDeviceRemovedReason();
            return 0;
        }
        catch (Exception exception) when (((uint)exception.HResult >> 16) == 0x887A)
        {
            // A DXGI reason. Anything else (a released wrapper) says nothing about the device.
            return exception.HResult;
        }
        catch (Exception exception) when (exception is InvalidComObjectException or COMException)
        {
            return 0;
        }
    }

    public void Dispose()
    {
        Canvas.Dispose();
        Marshal.ReleaseComObject(Direct3D);
    }
}
