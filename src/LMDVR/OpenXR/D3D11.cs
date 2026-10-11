using System.Runtime.InteropServices;

namespace LMDVR.OpenXR;

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11Texture2DDesc
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public uint Format;
    public uint SampleCount;
    public uint SampleQuality;
    public uint Usage;
    public uint BindFlags;
    public uint CpuAccessFlags;
    public uint MiscFlags;
}

/// <summary>
/// The handful of Direct3D 11 COM calls we need, invoked straight through the vtables
/// (indices from d3d11.h). Unity owns the device; we only borrow it.
/// </summary>
internal static unsafe class D3D11
{
    // ID3D11DeviceChild
    private const int VtDeviceChildGetDevice = 3;
    // ID3D11Device
    private const int VtDeviceGetImmediateContext = 40;
    // ID3D11DeviceContext
    private const int VtContextCopyResource = 47;
    // ID3D11Texture2D
    private const int VtTexture2DGetDesc = 10;

    public const uint FormatR8G8B8A8Unorm = 28;
    public const uint FormatR8G8B8A8UnormSrgb = 29;

    private static void** VTable(IntPtr comObject) => *(void***)comObject;

    /// <summary>Returns the ID3D11Device that owns a resource (caller gets a reference).</summary>
    public static IntPtr GetDevice(IntPtr deviceChild)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)VTable(deviceChild)[VtDeviceChildGetDevice];
        IntPtr device;
        fn(deviceChild, &device);
        return device;
    }

    /// <summary>Returns the device's immediate context (caller gets a reference).</summary>
    public static IntPtr GetImmediateContext(IntPtr device)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)VTable(device)[VtDeviceGetImmediateContext];
        IntPtr context;
        fn(device, &context);
        return context;
    }

    /// <summary>GPU copy; only safe on Unity's render thread, which owns the immediate context.</summary>
    public static void CopyResource(IntPtr context, IntPtr dst, IntPtr src)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)VTable(context)[VtContextCopyResource];
        fn(context, dst, src);
    }

    public static D3D11Texture2DDesc GetDesc(IntPtr texture2D)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, D3D11Texture2DDesc*, void>)VTable(texture2D)[VtTexture2DGetDesc];
        D3D11Texture2DDesc desc;
        fn(texture2D, &desc);
        return desc;
    }
}
