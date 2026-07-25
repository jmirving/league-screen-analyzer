using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace LeagueScreenAnalyzer.Capture.Windows;

internal static partial class Direct3DDeviceFactory
{
    private const uint D3d11CreateDeviceBgraSupport = 0x20;
    private const uint D3d11SdkVersion = 7;
    private const int D3dDriverTypeHardware = 1;
    private static readonly Guid IdxgiDeviceGuid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    public static IDirect3DDevice Create()
    {
        int result = D3D11CreateDevice(
            0,
            D3dDriverTypeHardware,
            0,
            D3d11CreateDeviceBgraSupport,
            0,
            0,
            D3d11SdkVersion,
            out nint d3d11Device,
            out _,
            out nint deviceContext);
        Marshal.ThrowExceptionForHR(result);

        nint dxgiDevice = 0;
        nint inspectableDevice = 0;
        try
        {
            result = Marshal.QueryInterface(d3d11Device, in IdxgiDeviceGuid, out dxgiDevice);
            Marshal.ThrowExceptionForHR(result);
            result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectableDevice);
            Marshal.ThrowExceptionForHR(result);
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectableDevice);
        }
        finally
        {
            if (inspectableDevice != 0)
            {
                Marshal.Release(inspectableDevice);
            }

            if (dxgiDevice != 0)
            {
                Marshal.Release(dxgiDevice);
            }

            if (deviceContext != 0)
            {
                Marshal.Release(deviceContext);
            }

            if (d3d11Device != 0)
            {
                Marshal.Release(d3d11Device);
            }
        }
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(
        nint adapter,
        int driverType,
        nint software,
        uint flags,
        nint featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out nint device,
        out int featureLevel,
        out nint immediateContext);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);
}
