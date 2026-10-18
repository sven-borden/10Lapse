using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
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

    public static unsafe VideoDevice Create()
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

        ((ID3D11Multithread)device).SetMultithreadProtected(true);

        PInvoke.CreateDirect3D11DeviceFromDXGIDevice((IDXGIDevice)device, out var inspectable).ThrowOnFailure();
        var pointer = Marshal.GetIUnknownForObject(inspectable);
        try
        {
            var direct3DDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer);
            return new VideoDevice(device, CanvasDevice.CreateFromDirect3D11Device(direct3DDevice));
        }
        finally
        {
            Marshal.Release(pointer);
            Marshal.ReleaseComObject(inspectable);
        }
    }

    /// <summary>The texture under a Win2D bitmap or render target made on this device.</summary>
    public static unsafe ID3D11Texture2D TextureOf(CanvasBitmap bitmap)
    {
        var inspectable = WinRT.MarshalInspectable<object>.FromManaged(bitmap);
        try
        {
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(inspectable);
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
            Marshal.Release(inspectable);
        }
    }

    public void Dispose()
    {
        Canvas.Dispose();
        Marshal.ReleaseComObject(Direct3D);
    }
}
