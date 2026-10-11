using System.Runtime.InteropServices;
using System.Text;

namespace LMDVR.OpenXR;

// Hand-written subset of the OpenXR 1.0 C API (layouts checked against openxr.h 1.1.63).
// Handles are 64-bit on x64; XrFlags64 are ulong; XrTime/XrDuration are long.

internal static class XrType
{
    public const int ExtensionProperties = 2;
    public const int InstanceCreateInfo = 3;
    public const int SystemGetInfo = 4;
    public const int SystemProperties = 5;
    public const int ViewLocateInfo = 6;
    public const int View = 7;
    public const int SessionCreateInfo = 8;
    public const int SwapchainCreateInfo = 9;
    public const int SessionBeginInfo = 10;
    public const int ViewState = 11;
    public const int FrameEndInfo = 12;
    public const int EventDataBuffer = 16;
    public const int EventDataInstanceLossPending = 17;
    public const int EventDataSessionStateChanged = 18;
    public const int InstanceProperties = 32;
    public const int FrameWaitInfo = 33;
    public const int CompositionLayerProjection = 35;
    public const int CompositionLayerQuad = 36;
    public const int ReferenceSpaceCreateInfo = 37;
    public const int EventDataReferenceSpaceChangePending = 40;
    public const int ViewConfigurationView = 41;
    public const int FrameState = 44;
    public const int FrameBeginInfo = 46;
    public const int CompositionLayerProjectionView = 48;
    public const int SwapchainImageAcquireInfo = 55;
    public const int SwapchainImageWaitInfo = 56;
    public const int SwapchainImageReleaseInfo = 57;
    public const int GraphicsBindingD3D11KHR = 1000027000;
    public const int SwapchainImageD3D11KHR = 1000027001;
    public const int GraphicsRequirementsD3D11KHR = 1000027002;
}

internal static class XrConst
{
    public const int FormFactorHeadMountedDisplay = 1;
    public const int ViewConfigurationPrimaryStereo = 2;
    public const int EnvironmentBlendModeOpaque = 1;
    public const int ReferenceSpaceLocal = 2;
    public const int EyeVisibilityBoth = 0;
    public const int EyeVisibilityLeft = 1;
    public const int EyeVisibilityRight = 2;

    public const ulong SwapchainUsageColorAttachment = 0x01;
    public const ulong SwapchainUsageTransferDst = 0x10;
    public const ulong SwapchainUsageSampled = 0x20;

    public const ulong LayerBlendTextureSourceAlpha = 0x02;

    public const ulong LocationOrientationValid = 0x01;

    public static ulong MakeVersion(ulong major, ulong minor, ulong patch) =>
        ((major & 0xffff) << 48) | ((minor & 0xffff) << 32) | (patch & 0xffffffff);

    // Request 1.0 so runtimes that have not moved to 1.1 still accept the instance.
    public static readonly ulong ApiVersion10 = MakeVersion(1, 0, 63);
}

internal enum XrSessionState
{
    Unknown = 0,
    Idle = 1,
    Ready = 2,
    Synchronized = 3,
    Visible = 4,
    Focused = 5,
    Stopping = 6,
    LossPending = 7,
    Exiting = 8,
}

internal static class XrResult
{
    public const int Success = 0;
    public const int EventUnavailable = 4;
    public const int ErrorFormFactorUnavailable = -35;
    public const int ErrorSessionLost = -17;
    public const int ErrorInstanceLost = -13;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XrVector3f
{
    public float X, Y, Z;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XrQuaternionf
{
    public float X, Y, Z, W;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XrPosef
{
    public XrQuaternionf Orientation;
    public XrVector3f Position;

    public static XrPosef Identity => new() { Orientation = new XrQuaternionf { W = 1f } };
}

[StructLayout(LayoutKind.Sequential)]
internal struct XrFovf
{
    public float AngleLeft, AngleRight, AngleUp, AngleDown;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XrRect2Di
{
    public int X, Y, Width, Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XrExtent2Df
{
    public float Width, Height;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrApplicationInfo
{
    public fixed byte ApplicationName[128];
    public uint ApplicationVersion;
    public fixed byte EngineName[128];
    public uint EngineVersion;
    public ulong ApiVersion;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrInstanceCreateInfo
{
    public int Type;
    public void* Next;
    public ulong CreateFlags;
    public XrApplicationInfo ApplicationInfo;
    public uint EnabledApiLayerCount;
    public byte** EnabledApiLayerNames;
    public uint EnabledExtensionCount;
    public byte** EnabledExtensionNames;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrExtensionProperties
{
    public int Type;
    public void* Next;
    public fixed byte ExtensionName[128];
    public uint ExtensionVersion;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrInstanceProperties
{
    public int Type;
    public void* Next;
    public ulong RuntimeVersion;
    public fixed byte RuntimeName[128];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSystemGetInfo
{
    public int Type;
    public void* Next;
    public int FormFactor;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSystemProperties
{
    public int Type;
    public void* Next;
    public ulong SystemId;
    public uint VendorId;
    public fixed byte SystemName[256];
    public uint MaxSwapchainImageHeight;
    public uint MaxSwapchainImageWidth;
    public uint MaxLayerCount;
    public uint OrientationTracking;
    public uint PositionTracking;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrGraphicsRequirementsD3D11KHR
{
    public int Type;
    public void* Next;
    public uint AdapterLuidLow;
    public int AdapterLuidHigh;
    public int MinFeatureLevel;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrGraphicsBindingD3D11KHR
{
    public int Type;
    public void* Next;
    public IntPtr Device;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSessionCreateInfo
{
    public int Type;
    public void* Next;
    public ulong CreateFlags;
    public ulong SystemId;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSessionBeginInfo
{
    public int Type;
    public void* Next;
    public int PrimaryViewConfigurationType;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrReferenceSpaceCreateInfo
{
    public int Type;
    public void* Next;
    public int ReferenceSpaceType;
    public XrPosef PoseInReferenceSpace;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrViewConfigurationView
{
    public int Type;
    public void* Next;
    public uint RecommendedImageRectWidth;
    public uint MaxImageRectWidth;
    public uint RecommendedImageRectHeight;
    public uint MaxImageRectHeight;
    public uint RecommendedSwapchainSampleCount;
    public uint MaxSwapchainSampleCount;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSwapchainCreateInfo
{
    public int Type;
    public void* Next;
    public ulong CreateFlags;
    public ulong UsageFlags;
    public long Format;
    public uint SampleCount;
    public uint Width;
    public uint Height;
    public uint FaceCount;
    public uint ArraySize;
    public uint MipCount;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSwapchainImageD3D11KHR
{
    public int Type;
    public void* Next;
    public IntPtr Texture;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSwapchainImageAcquireInfo
{
    public int Type;
    public void* Next;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSwapchainImageWaitInfo
{
    public int Type;
    public void* Next;
    public long Timeout;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSwapchainImageReleaseInfo
{
    public int Type;
    public void* Next;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrFrameWaitInfo
{
    public int Type;
    public void* Next;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrFrameState
{
    public int Type;
    public void* Next;
    public long PredictedDisplayTime;
    public long PredictedDisplayPeriod;
    public uint ShouldRender;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrFrameBeginInfo
{
    public int Type;
    public void* Next;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrFrameEndInfo
{
    public int Type;
    public void* Next;
    public long DisplayTime;
    public int EnvironmentBlendMode;
    public uint LayerCount;
    public void** Layers;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrViewLocateInfo
{
    public int Type;
    public void* Next;
    public int ViewConfigurationType;
    public long DisplayTime;
    public ulong Space;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrViewState
{
    public int Type;
    public void* Next;
    public ulong ViewStateFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrView
{
    public int Type;
    public void* Next;
    public XrPosef Pose;
    public XrFovf Fov;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XrSwapchainSubImage
{
    public ulong Swapchain;
    public XrRect2Di ImageRect;
    public uint ImageArrayIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrCompositionLayerProjectionView
{
    public int Type;
    public void* Next;
    public XrPosef Pose;
    public XrFovf Fov;
    public XrSwapchainSubImage SubImage;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrCompositionLayerProjection
{
    public int Type;
    public void* Next;
    public ulong LayerFlags;
    public ulong Space;
    public uint ViewCount;
    public XrCompositionLayerProjectionView* Views;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrCompositionLayerQuad
{
    public int Type;
    public void* Next;
    public ulong LayerFlags;
    public ulong Space;
    public int EyeVisibility;
    public XrSwapchainSubImage SubImage;
    public XrPosef Pose;
    public XrExtent2Df Size;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrEventDataBuffer
{
    public int Type;
    public void* Next;
    public fixed byte Varying[4000];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrEventDataSessionStateChanged
{
    public int Type;
    public void* Next;
    public ulong Session;
    public int State;
    public long Time;
}

/// <summary>
/// Function table resolved through xrGetInstanceProcAddr from the Khronos loader.
/// </summary>
internal static unsafe class Xr
{
    private static IntPtr _loader;
    private static delegate* unmanaged[Stdcall]<ulong, byte*, IntPtr*, int> _getInstanceProcAddr;

    public static delegate* unmanaged[Stdcall]<byte*, uint, uint*, XrExtensionProperties*, int> EnumerateInstanceExtensionProperties;
    public static delegate* unmanaged[Stdcall]<XrInstanceCreateInfo*, ulong*, int> CreateInstance;
    public static delegate* unmanaged[Stdcall]<ulong, int> DestroyInstance;
    public static delegate* unmanaged[Stdcall]<ulong, XrInstanceProperties*, int> GetInstanceProperties;
    public static delegate* unmanaged[Stdcall]<ulong, int, byte*, int> ResultToString;
    public static delegate* unmanaged[Stdcall]<ulong, XrEventDataBuffer*, int> PollEvent;
    public static delegate* unmanaged[Stdcall]<ulong, XrSystemGetInfo*, ulong*, int> GetSystem;
    public static delegate* unmanaged[Stdcall]<ulong, ulong, XrSystemProperties*, int> GetSystemProperties;
    public static delegate* unmanaged[Stdcall]<ulong, ulong, int, uint, uint*, XrViewConfigurationView*, int> EnumerateViewConfigurationViews;
    public static delegate* unmanaged[Stdcall]<ulong, ulong, XrGraphicsRequirementsD3D11KHR*, int> GetD3D11GraphicsRequirementsKHR;
    public static delegate* unmanaged[Stdcall]<ulong, XrSessionCreateInfo*, ulong*, int> CreateSession;
    public static delegate* unmanaged[Stdcall]<ulong, int> DestroySession;
    public static delegate* unmanaged[Stdcall]<ulong, XrSessionBeginInfo*, int> BeginSession;
    public static delegate* unmanaged[Stdcall]<ulong, int> EndSession;
    public static delegate* unmanaged[Stdcall]<ulong, int> RequestExitSession;
    public static delegate* unmanaged[Stdcall]<ulong, XrReferenceSpaceCreateInfo*, ulong*, int> CreateReferenceSpace;
    public static delegate* unmanaged[Stdcall]<ulong, int> DestroySpace;
    public static delegate* unmanaged[Stdcall]<ulong, uint, uint*, long*, int> EnumerateSwapchainFormats;
    public static delegate* unmanaged[Stdcall]<ulong, XrSwapchainCreateInfo*, ulong*, int> CreateSwapchain;
    public static delegate* unmanaged[Stdcall]<ulong, int> DestroySwapchain;
    public static delegate* unmanaged[Stdcall]<ulong, uint, uint*, XrSwapchainImageD3D11KHR*, int> EnumerateSwapchainImages;
    public static delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageAcquireInfo*, uint*, int> AcquireSwapchainImage;
    public static delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageWaitInfo*, int> WaitSwapchainImage;
    public static delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageReleaseInfo*, int> ReleaseSwapchainImage;
    public static delegate* unmanaged[Stdcall]<ulong, XrFrameWaitInfo*, XrFrameState*, int> WaitFrame;
    public static delegate* unmanaged[Stdcall]<ulong, XrFrameBeginInfo*, int> BeginFrame;
    public static delegate* unmanaged[Stdcall]<ulong, XrFrameEndInfo*, int> EndFrame;
    public static delegate* unmanaged[Stdcall]<ulong, XrViewLocateInfo*, XrViewState*, uint, uint*, XrView*, int> LocateViews;

    public static bool IsLoaderLoaded => _loader != IntPtr.Zero;

    /// <summary>Loads openxr_loader.dll and the functions callable without an instance.</summary>
    public static void LoadLoader(string path)
    {
        if (_loader != IntPtr.Zero)
        {
            return;
        }

        _loader = NativeLibrary.Load(path);
        _getInstanceProcAddr = (delegate* unmanaged[Stdcall]<ulong, byte*, IntPtr*, int>)NativeLibrary.GetExport(_loader, "xrGetInstanceProcAddr");
        EnumerateInstanceExtensionProperties = (delegate* unmanaged[Stdcall]<byte*, uint, uint*, XrExtensionProperties*, int>)Get(0, "xrEnumerateInstanceExtensionProperties");
        CreateInstance = (delegate* unmanaged[Stdcall]<XrInstanceCreateInfo*, ulong*, int>)Get(0, "xrCreateInstance");
    }

    /// <summary>Resolves every instance-level function we use.</summary>
    public static void LoadInstanceFunctions(ulong instance)
    {
        DestroyInstance = (delegate* unmanaged[Stdcall]<ulong, int>)Get(instance, "xrDestroyInstance");
        GetInstanceProperties = (delegate* unmanaged[Stdcall]<ulong, XrInstanceProperties*, int>)Get(instance, "xrGetInstanceProperties");
        ResultToString = (delegate* unmanaged[Stdcall]<ulong, int, byte*, int>)Get(instance, "xrResultToString");
        PollEvent = (delegate* unmanaged[Stdcall]<ulong, XrEventDataBuffer*, int>)Get(instance, "xrPollEvent");
        GetSystem = (delegate* unmanaged[Stdcall]<ulong, XrSystemGetInfo*, ulong*, int>)Get(instance, "xrGetSystem");
        GetSystemProperties = (delegate* unmanaged[Stdcall]<ulong, ulong, XrSystemProperties*, int>)Get(instance, "xrGetSystemProperties");
        EnumerateViewConfigurationViews = (delegate* unmanaged[Stdcall]<ulong, ulong, int, uint, uint*, XrViewConfigurationView*, int>)Get(instance, "xrEnumerateViewConfigurationViews");
        GetD3D11GraphicsRequirementsKHR = (delegate* unmanaged[Stdcall]<ulong, ulong, XrGraphicsRequirementsD3D11KHR*, int>)Get(instance, "xrGetD3D11GraphicsRequirementsKHR");
        CreateSession = (delegate* unmanaged[Stdcall]<ulong, XrSessionCreateInfo*, ulong*, int>)Get(instance, "xrCreateSession");
        DestroySession = (delegate* unmanaged[Stdcall]<ulong, int>)Get(instance, "xrDestroySession");
        BeginSession = (delegate* unmanaged[Stdcall]<ulong, XrSessionBeginInfo*, int>)Get(instance, "xrBeginSession");
        EndSession = (delegate* unmanaged[Stdcall]<ulong, int>)Get(instance, "xrEndSession");
        RequestExitSession = (delegate* unmanaged[Stdcall]<ulong, int>)Get(instance, "xrRequestExitSession");
        CreateReferenceSpace = (delegate* unmanaged[Stdcall]<ulong, XrReferenceSpaceCreateInfo*, ulong*, int>)Get(instance, "xrCreateReferenceSpace");
        DestroySpace = (delegate* unmanaged[Stdcall]<ulong, int>)Get(instance, "xrDestroySpace");
        EnumerateSwapchainFormats = (delegate* unmanaged[Stdcall]<ulong, uint, uint*, long*, int>)Get(instance, "xrEnumerateSwapchainFormats");
        CreateSwapchain = (delegate* unmanaged[Stdcall]<ulong, XrSwapchainCreateInfo*, ulong*, int>)Get(instance, "xrCreateSwapchain");
        DestroySwapchain = (delegate* unmanaged[Stdcall]<ulong, int>)Get(instance, "xrDestroySwapchain");
        EnumerateSwapchainImages = (delegate* unmanaged[Stdcall]<ulong, uint, uint*, XrSwapchainImageD3D11KHR*, int>)Get(instance, "xrEnumerateSwapchainImages");
        AcquireSwapchainImage = (delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageAcquireInfo*, uint*, int>)Get(instance, "xrAcquireSwapchainImage");
        WaitSwapchainImage = (delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageWaitInfo*, int>)Get(instance, "xrWaitSwapchainImage");
        ReleaseSwapchainImage = (delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageReleaseInfo*, int>)Get(instance, "xrReleaseSwapchainImage");
        WaitFrame = (delegate* unmanaged[Stdcall]<ulong, XrFrameWaitInfo*, XrFrameState*, int>)Get(instance, "xrWaitFrame");
        BeginFrame = (delegate* unmanaged[Stdcall]<ulong, XrFrameBeginInfo*, int>)Get(instance, "xrBeginFrame");
        EndFrame = (delegate* unmanaged[Stdcall]<ulong, XrFrameEndInfo*, int>)Get(instance, "xrEndFrame");
        LocateViews = (delegate* unmanaged[Stdcall]<ulong, XrViewLocateInfo*, XrViewState*, uint, uint*, XrView*, int>)Get(instance, "xrLocateViews");
    }

    private static IntPtr Get(ulong instance, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name + "\0");
        IntPtr fn;
        int res;
        fixed (byte* p = bytes)
        {
            res = _getInstanceProcAddr(instance, p, &fn);
        }

        if (res < 0 || fn == IntPtr.Zero)
        {
            throw new XrException($"xrGetInstanceProcAddr({name})", res);
        }

        return fn;
    }

    public static string ResultName(ulong instance, int result)
    {
        if (instance == 0 || ResultToString == null)
        {
            return result.ToString();
        }

        var buf = stackalloc byte[64];
        return ResultToString(instance, result, buf) >= 0 ? FixedString(buf, 64) : result.ToString();
    }

    public static string FixedString(byte* bytes, int capacity)
    {
        var len = 0;
        while (len < capacity && bytes[len] != 0)
        {
            len++;
        }

        return Encoding.UTF8.GetString(bytes, len);
    }

    public static void WriteFixedString(byte* dest, int capacity, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var n = Math.Min(bytes.Length, capacity - 1);
        for (var i = 0; i < n; i++)
        {
            dest[i] = bytes[i];
        }

        dest[n] = 0;
    }
}

internal sealed class XrException : Exception
{
    public int Result { get; }

    public XrException(string call, int result)
        : base($"{call} failed: {result}")
    {
        Result = result;
    }
}
