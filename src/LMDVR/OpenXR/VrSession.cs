using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LMDVR.OpenXR;

internal enum VrState
{
    Off,
    WaitingForRuntime,
    WaitingForHeadset,
    Idle,
    Running,
    Stopping,
    Failed,
}

internal struct EyeView
{
    public XrPosef Pose;
    public XrFovf Fov;
}

/// <summary>What the main thread wants composited for one frame.</summary>
internal struct FrameLayers
{
    public bool Projection;
    public IntPtr LeftEyeTexture;
    public IntPtr RightEyeTexture;

    /// <summary>A flat panel where each eye sees its own picture (the 3D screen). Drawn below <see cref="Quad"/>.</summary>
    public bool StereoQuad;
    public IntPtr StereoQuadLeftTexture;
    public IntPtr StereoQuadRightTexture;
    public XrPosef StereoQuadPose;
    public XrExtent2Df StereoQuadSize;

    public bool Quad;
    public IntPtr QuadTexture;
    public XrPosef QuadPose;
    public XrExtent2Df QuadSize;
    public bool QuadAlphaBlend;
}

/// <summary>
/// Owns the OpenXR instance/session and runs the frame loop.
/// Threading: xrWaitFrame/xrLocateViews run on Unity's main thread; xrBeginFrame, the swapchain
/// copies and xrEndFrame run on Unity's render thread through GL.IssuePluginEvent, so they are
/// ordered with the GPU work that produced the eye textures.
/// </summary>
internal sealed unsafe class VrSession
{
    private const int SlotCount = 4;
    private const int EvBegin = 1;
    private const int EvSubmit = 2;
    private const int EvEndSession = 3;
    private const string AppName = "Lonely Mountains Downhill VR";

    private struct FrameSlot
    {
        public long DisplayTime;
        public bool ShouldRender;
        public bool Began;
        public EyeView Left;
        public EyeView Right;
        public FrameLayers Layers;
    }

    private static readonly ConcurrentQueue<string> RenderThreadLog = new();
    private static readonly IntPtr RenderEventFunc;
    private static VrSession _current;

    private readonly FrameSlot[] _slots = new FrameSlot[SlotCount];
    private int _slot;
    private bool _frameWaited;
    private bool _beginIssued;
    private volatile bool _endSessionDone;
    private float _nextRetryTime;
    private bool _loggedRuntimeWait;
    private bool _failAfterStop;

    private ulong _instance;
    private ulong _systemId;
    private ulong _session;
    private ulong _localSpace;
    private readonly ulong[] _eyeSwapchains = new ulong[2];
    private readonly IntPtr[][] _eyeImages = new IntPtr[2][];
    private ulong _quadSwapchain;
    private IntPtr[] _quadImages;
    private readonly ulong[] _screenSwapchains = new ulong[2];
    private readonly IntPtr[][] _screenImages = new IntPtr[2][];
    private long _format;
    private IntPtr _device;
    private IntPtr _context;
    private XrSessionState _sessionState;
    private int _savedVSync;
    private int _savedTargetFrameRate;

    static VrSession()
    {
        delegate* unmanaged[Stdcall]<int, void> fn = &OnRenderEvent;
        RenderEventFunc = (IntPtr)fn;
    }

    public VrSession(string loaderPath, float resolutionScale, int quadWidth, int quadHeight)
    {
        LoaderPath = loaderPath;
        ResolutionScale = resolutionScale;
        QuadWidth = quadWidth;
        QuadHeight = quadHeight;
    }

    public string LoaderPath { get; }
    public float ResolutionScale { get; }
    public VrState State { get; private set; } = VrState.Off;
    public string RuntimeName { get; private set; } = "";
    public string SystemName { get; private set; } = "";
    public int EyeWidth { get; private set; }
    public int EyeHeight { get; private set; }
    public int QuadWidth { get; }
    public int QuadHeight { get; }

    /// <summary>True between xrWaitFrame and the submit for the current Unity frame.</summary>
    public bool FrameActive => _frameWaited;

    public bool ShouldRender => _frameWaited && _slots[_slot].ShouldRender;

    /// <summary>The runtime gave us the per-eye screen images the 3D screen needs.</summary>
    public bool StereoScreenAvailable => _screenSwapchains[0] != 0 && _screenSwapchains[1] != 0;

    /// <summary>Set by the render thread's first successful frame; used for logging only.</summary>
    public int SubmittedFrames { get; private set; }

    /// <summary>The user recentered through the headset's own menu; cleared by the reader.</summary>
    public bool RuntimeRecentered { get; set; }

    public void Start(IntPtr d3d11Device)
    {
        if (State != VrState.Off && State != VrState.Failed)
        {
            return;
        }

        _device = d3d11Device;
        _context = D3D11.GetImmediateContext(d3d11Device);
        _current = this;
        State = VrState.WaitingForRuntime;
        _nextRetryTime = 0;
        Mod.Log.Msg("VR: starting OpenXR");
    }

    /// <summary>Main thread, early every frame: events, lifecycle retries and xrWaitFrame.</summary>
    public void Tick()
    {
        DrainRenderThreadLog();

        try
        {
            switch (State)
            {
                case VrState.WaitingForRuntime:
                case VrState.WaitingForHeadset:
                    if (Time.unscaledTime >= _nextRetryTime)
                    {
                        _nextRetryTime = Time.unscaledTime + 3f;
                        TryCreate();
                    }
                    break;
                case VrState.Idle:
                case VrState.Running:
                case VrState.Stopping:
                    PollEvents();
                    break;
            }

            if (State == VrState.Running)
            {
                WaitFrame();
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    /// <summary>Main thread: head-relative eye poses for this frame, in the LOCAL reference space.</summary>
    public bool LocateViews(out EyeView left, out EyeView right)
    {
        left = default;
        right = default;
        if (!_frameWaited)
        {
            return false;
        }

        ref var slot = ref _slots[_slot];
        var info = new XrViewLocateInfo
        {
            Type = XrType.ViewLocateInfo,
            ViewConfigurationType = XrConst.ViewConfigurationPrimaryStereo,
            DisplayTime = slot.DisplayTime,
            Space = _localSpace,
        };
        var state = new XrViewState { Type = XrType.ViewState };
        var views = stackalloc XrView[2];
        views[0] = new XrView { Type = XrType.View };
        views[1] = new XrView { Type = XrType.View };
        uint count;
        var res = Xr.LocateViews(_session, &info, &state, 2, &count, views);
        if (res < 0 || count != 2 || (state.ViewStateFlags & XrConst.LocationOrientationValid) == 0)
        {
            return false;
        }

        left = new EyeView { Pose = views[0].Pose, Fov = views[0].Fov };
        right = new EyeView { Pose = views[1].Pose, Fov = views[1].Fov };
        slot.Left = left;
        slot.Right = right;
        return true;
    }

    /// <summary>Main thread, just before Unity renders: queue xrBeginFrame on the render thread.</summary>
    public void IssueBegin()
    {
        if (!_frameWaited || _beginIssued)
        {
            return;
        }

        _beginIssued = true;
        GL.IssuePluginEvent(RenderEventFunc, Encode(EvBegin, _slot));
    }

    /// <summary>Main thread, end of frame after all cameras rendered: queue copies + xrEndFrame.</summary>
    public void IssueSubmit(in FrameLayers layers)
    {
        if (!_frameWaited)
        {
            return;
        }

        IssueBegin();
        _slots[_slot].Layers = layers;
        Thread.MemoryBarrier();
        GL.IssuePluginEvent(RenderEventFunc, Encode(EvSubmit, _slot));
        _frameWaited = false;
    }

    /// <summary>Asks the runtime to end the session; teardown follows through the state machine.</summary>
    public void RequestStop()
    {
        if (_session != 0 && State == VrState.Running)
        {
            Check(Xr.RequestExitSession(_session), "xrRequestExitSession");
        }
        else if (State != VrState.Running && State != VrState.Stopping)
        {
            DestroyAll();
            State = VrState.Off;
        }
    }

    /// <summary>Hard teardown on game exit. Only call when no frames can be in flight.</summary>
    public void Shutdown()
    {
        try
        {
            if (_session != 0 && (State == VrState.Running || State == VrState.Stopping))
            {
                Xr.EndSession(_session);
            }
        }
        catch
        {
            // shutting down anyway
        }

        DestroyAll();
        State = VrState.Off;
    }

    private void TryCreate()
    {
        if (_instance == 0)
        {
            // The loader prints errors to the console on every failed attempt; check quietly first.
            if (!RuntimeInstalled())
            {
                LogWaitingForRuntime(-51);
                return;
            }

            if (!Xr.IsLoaderLoaded)
            {
                Environment.SetEnvironmentVariable("XR_LOADER_DEBUG", "none");
                Xr.LoadLoader(LoaderPath);
            }

            if (!CreateInstance())
            {
                return;
            }
        }

        var getInfo = new XrSystemGetInfo { Type = XrType.SystemGetInfo, FormFactor = XrConst.FormFactorHeadMountedDisplay };
        ulong systemId;
        var res = Xr.GetSystem(_instance, &getInfo, &systemId);
        if (res == XrResult.ErrorFormFactorUnavailable)
        {
            if (State != VrState.WaitingForHeadset)
            {
                Mod.Log.Msg("VR: runtime found, waiting for the headset to connect ...");
            }

            State = VrState.WaitingForHeadset;
            return;
        }

        Check(res, "xrGetSystem");
        _systemId = systemId;

        var props = new XrSystemProperties { Type = XrType.SystemProperties };
        Check(Xr.GetSystemProperties(_instance, _systemId, &props), "xrGetSystemProperties");
        SystemName = Xr.FixedString(props.SystemName, 256);
        Mod.Log.Msg($"VR: system '{SystemName}' (vendor 0x{props.VendorId:X}), max swapchain {props.MaxSwapchainImageWidth}x{props.MaxSwapchainImageHeight}, layers {props.MaxLayerCount}, position tracking {props.PositionTracking != 0}");

        var views = stackalloc XrViewConfigurationView[2];
        views[0] = new XrViewConfigurationView { Type = XrType.ViewConfigurationView };
        views[1] = new XrViewConfigurationView { Type = XrType.ViewConfigurationView };
        uint viewCount;
        Check(Xr.EnumerateViewConfigurationViews(_instance, _systemId, XrConst.ViewConfigurationPrimaryStereo, 2, &viewCount, views), "xrEnumerateViewConfigurationViews");
        EyeWidth = Math.Min((int)(views[0].RecommendedImageRectWidth * ResolutionScale), (int)views[0].MaxImageRectWidth) & ~1;
        EyeHeight = Math.Min((int)(views[0].RecommendedImageRectHeight * ResolutionScale), (int)views[0].MaxImageRectHeight) & ~1;
        Mod.Log.Msg($"VR: recommended eye size {views[0].RecommendedImageRectWidth}x{views[0].RecommendedImageRectHeight}, using {EyeWidth}x{EyeHeight} (scale {ResolutionScale:0.00})");

        var req = new XrGraphicsRequirementsD3D11KHR { Type = XrType.GraphicsRequirementsD3D11KHR };
        Check(Xr.GetD3D11GraphicsRequirementsKHR(_instance, _systemId, &req), "xrGetD3D11GraphicsRequirementsKHR");
        MelonLoader.MelonDebug.Msg($"VR: runtime wants adapter LUID {req.AdapterLuidHigh:X8}:{req.AdapterLuidLow:X8}, min feature level 0x{req.MinFeatureLevel:X}");

        CreateSession();
    }

    private bool CreateInstance()
    {
        uint extCount;
        var res = Xr.EnumerateInstanceExtensionProperties(null, 0, &extCount, null);
        if (res < 0)
        {
            LogWaitingForRuntime(res);
            return false;
        }

        var exts = new XrExtensionProperties[extCount];
        for (var i = 0; i < extCount; i++)
        {
            exts[i].Type = XrType.ExtensionProperties;
        }

        var hasD3D11 = false;
        fixed (XrExtensionProperties* p = exts)
        {
            Check(Xr.EnumerateInstanceExtensionProperties(null, extCount, &extCount, p), "xrEnumerateInstanceExtensionProperties");
            for (var i = 0; i < extCount; i++)
            {
                if (Xr.FixedString(p[i].ExtensionName, 128) == "XR_KHR_D3D11_enable")
                {
                    hasD3D11 = true;
                }
            }
        }

        if (!hasD3D11)
        {
            throw new InvalidOperationException("The active OpenXR runtime does not support Direct3D 11 (XR_KHR_D3D11_enable).");
        }

        var extName = Marshal.StringToHGlobalAnsi("XR_KHR_D3D11_enable");
        try
        {
            var names = stackalloc byte*[1];
            names[0] = (byte*)extName;
            var ci = new XrInstanceCreateInfo
            {
                Type = XrType.InstanceCreateInfo,
                EnabledExtensionCount = 1,
                EnabledExtensionNames = names,
            };
            Xr.WriteFixedString(ci.ApplicationInfo.ApplicationName, 128, AppName);
            Xr.WriteFixedString(ci.ApplicationInfo.EngineName, 128, "Unity");
            ci.ApplicationInfo.ApplicationVersion = 1;
            ci.ApplicationInfo.EngineVersion = 2020;
            ci.ApplicationInfo.ApiVersion = XrConst.ApiVersion10;

            ulong instance;
            res = Xr.CreateInstance(&ci, &instance);
            if (res < 0)
            {
                LogWaitingForRuntime(res);
                return false;
            }

            _instance = instance;
        }
        finally
        {
            Marshal.FreeHGlobal(extName);
        }

        Xr.LoadInstanceFunctions(_instance);
        var ip = new XrInstanceProperties { Type = XrType.InstanceProperties };
        Check(Xr.GetInstanceProperties(_instance, &ip), "xrGetInstanceProperties");
        var v = ip.RuntimeVersion;
        RuntimeName = $"{Xr.FixedString(ip.RuntimeName, 128)} {v >> 48}.{(v >> 32) & 0xffff}.{v & 0xffffffff}";
        Mod.Log.Msg($"VR: OpenXR runtime: {RuntimeName}");
        return true;
    }

    /// <summary>An OpenXR runtime (Meta Quest Link, SteamVR, Virtual Desktop, ...) registers itself here.</summary>
    private static bool RuntimeInstalled()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XR_RUNTIME_JSON")))
        {
            return true;
        }

        var active = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Khronos\OpenXR\1", "ActiveRuntime", null) as string;
        return !string.IsNullOrEmpty(active);
    }

    private void LogWaitingForRuntime(int res)
    {
        if (!_loggedRuntimeWait)
        {
            _loggedRuntimeWait = true;
            Mod.Log.Warning($"VR: no OpenXR runtime available yet ({res}). Install/start the Meta Quest Link app (or SteamVR / Virtual Desktop) and it will connect automatically. F6 previews first person on the monitor.");
        }

        State = VrState.WaitingForRuntime;
    }

    private void CreateSession()
    {
        var binding = new XrGraphicsBindingD3D11KHR { Type = XrType.GraphicsBindingD3D11KHR, Device = _device };
        var ci = new XrSessionCreateInfo { Type = XrType.SessionCreateInfo, Next = &binding, SystemId = _systemId };
        ulong session;
        Check(Xr.CreateSession(_instance, &ci, &session), "xrCreateSession");
        _session = session;

        _localSpace = CreateSpace(XrConst.ReferenceSpaceLocal);

        _format = ChooseFormat();
        for (var eye = 0; eye < 2; eye++)
        {
            _eyeSwapchains[eye] = CreateSwapchain(EyeWidth, EyeHeight, out _eyeImages[eye]);
        }

        _quadSwapchain = CreateSwapchain(QuadWidth, QuadHeight, out _quadImages);
        try
        {
            for (var eye = 0; eye < 2; eye++)
            {
                _screenSwapchains[eye] = CreateSwapchain(QuadWidth, QuadHeight, out _screenImages[eye]);
            }
        }
        catch (Exception ex)
        {
            // Only the 3D screen needs these; everything else works without them.
            Mod.Log.Warning($"VR: 3D screen unavailable: {ex.Message}");
        }

        var desc = D3D11.GetDesc(_eyeImages[0][0]);
        Mod.Log.Msg($"VR: session created. Swapchain format {_format}, image format {desc.Format}, {_eyeImages[0].Length} images per eye, quad {QuadWidth}x{QuadHeight}");
        State = VrState.Idle;
    }

    private ulong CreateSpace(int type)
    {
        var ci = new XrReferenceSpaceCreateInfo { Type = XrType.ReferenceSpaceCreateInfo, ReferenceSpaceType = type, PoseInReferenceSpace = XrPosef.Identity };
        ulong space;
        Check(Xr.CreateReferenceSpace(_session, &ci, &space), "xrCreateReferenceSpace");
        return space;
    }

    private long ChooseFormat()
    {
        uint count;
        Check(Xr.EnumerateSwapchainFormats(_session, 0, &count, null), "xrEnumerateSwapchainFormats");
        var formats = new long[count];
        fixed (long* p = formats)
        {
            Check(Xr.EnumerateSwapchainFormats(_session, count, &count, p), "xrEnumerateSwapchainFormats");
        }

        MelonLoader.MelonDebug.Msg($"VR: runtime swapchain formats: {string.Join(", ", formats)}");
        // Unity's linear-space ARGB32 render textures hold sRGB-encoded RGBA8, so an sRGB RGBA8
        // swapchain displays them correctly and stays CopyResource-compatible.
        foreach (var wanted in new long[] { D3D11.FormatR8G8B8A8UnormSrgb, D3D11.FormatR8G8B8A8Unorm })
        {
            if (Array.IndexOf(formats, wanted) >= 0)
            {
                return wanted;
            }
        }

        throw new InvalidOperationException("Runtime offers no RGBA8 swapchain format.");
    }

    private ulong CreateSwapchain(int width, int height, out IntPtr[] images)
    {
        var ci = new XrSwapchainCreateInfo
        {
            Type = XrType.SwapchainCreateInfo,
            UsageFlags = XrConst.SwapchainUsageColorAttachment | XrConst.SwapchainUsageTransferDst | XrConst.SwapchainUsageSampled,
            Format = _format,
            SampleCount = 1,
            Width = (uint)width,
            Height = (uint)height,
            FaceCount = 1,
            ArraySize = 1,
            MipCount = 1,
        };
        ulong swapchain;
        Check(Xr.CreateSwapchain(_session, &ci, &swapchain), "xrCreateSwapchain");

        uint count;
        Check(Xr.EnumerateSwapchainImages(swapchain, 0, &count, null), "xrEnumerateSwapchainImages");
        var buf = new XrSwapchainImageD3D11KHR[count];
        for (var i = 0; i < count; i++)
        {
            buf[i].Type = XrType.SwapchainImageD3D11KHR;
        }

        fixed (XrSwapchainImageD3D11KHR* p = buf)
        {
            Check(Xr.EnumerateSwapchainImages(swapchain, count, &count, p), "xrEnumerateSwapchainImages");
        }

        images = buf.Select(b => b.Texture).ToArray();
        return swapchain;
    }

    private void PollEvents()
    {
        var ev = new XrEventDataBuffer();
        while (true)
        {
            ev.Type = XrType.EventDataBuffer;
            ev.Next = null;
            var res = Xr.PollEvent(_instance, &ev);
            if (res == XrResult.EventUnavailable)
            {
                break;
            }

            Check(res, "xrPollEvent");
            switch (ev.Type)
            {
                case XrType.EventDataSessionStateChanged:
                    var sc = (XrEventDataSessionStateChanged*)&ev;
                    OnSessionState((XrSessionState)sc->State);
                    break;
                case XrType.EventDataInstanceLossPending:
                    Mod.Log.Warning("VR: runtime is going away (instance loss pending)");
                    OnSessionState(XrSessionState.LossPending);
                    break;
                case XrType.EventDataReferenceSpaceChangePending:
                    Mod.Log.Msg("VR: runtime recentered the play space");
                    RuntimeRecentered = true;
                    break;
            }
        }

        if (State == VrState.Stopping && _endSessionDone)
        {
            _endSessionDone = false;
            RestoreUnitySettings();
            Mod.Log.Msg("VR: session ended");
            State = VrState.Idle;
            if (_failAfterStop)
            {
                _failAfterStop = false;
                DestroyAll();
                State = VrState.Failed;
            }
            else if (_sessionState is XrSessionState.Exiting or XrSessionState.LossPending)
            {
                TearDownAfterExit();
            }
        }
    }

    private void OnSessionState(XrSessionState newState)
    {
        Mod.Log.Msg($"VR: session state {_sessionState} -> {newState}");
        _sessionState = newState;
        switch (newState)
        {
            case XrSessionState.Ready:
                var bi = new XrSessionBeginInfo { Type = XrType.SessionBeginInfo, PrimaryViewConfigurationType = XrConst.ViewConfigurationPrimaryStereo };
                Check(Xr.BeginSession(_session, &bi), "xrBeginSession");
                ApplyUnitySettings();
                State = VrState.Running;
                break;
            case XrSessionState.Stopping:
                BeginStop();
                break;
            case XrSessionState.Exiting:
            case XrSessionState.LossPending:
                if (State == VrState.Running)
                {
                    BeginStop();
                }
                else if (State != VrState.Stopping)
                {
                    TearDownAfterExit();
                }
                break;
        }
    }

    private void BeginStop()
    {
        if (State != VrState.Running)
        {
            return;
        }

        // Finish any frame this thread already waited, then end the session behind it on the render thread.
        if (_frameWaited)
        {
            IssueSubmit(default);
        }

        _endSessionDone = false;
        State = VrState.Stopping;
        GL.IssuePluginEvent(RenderEventFunc, Encode(EvEndSession, 0));
    }

    private void TearDownAfterExit()
    {
        var lost = _sessionState == XrSessionState.LossPending;
        DestroyAll();
        State = lost ? VrState.WaitingForRuntime : VrState.Off;
        _nextRetryTime = Time.unscaledTime + 3f;
        Mod.Log.Msg(lost ? "VR: runtime lost, will reconnect automatically" : "VR: stopped");
    }

    private void WaitFrame()
    {
        // A frame that was waited but never fully issued (no render this frame, e.g. a hitch or load)
        // must still be begun and ended. Its commands were only queued now, so skip waiting this
        // tick: xrWaitFrame blocks until the render thread has begun that frame.
        if (_frameWaited)
        {
            IssueSubmit(default);
            return;
        }

        var wi = new XrFrameWaitInfo { Type = XrType.FrameWaitInfo };
        var fs = new XrFrameState { Type = XrType.FrameState };
        var res = Xr.WaitFrame(_session, &wi, &fs);
        if (res < 0)
        {
            Check(res, "xrWaitFrame");
            return;
        }

        _slot = (_slot + 1) % SlotCount;
        _slots[_slot] = new FrameSlot { DisplayTime = fs.PredictedDisplayTime, ShouldRender = fs.ShouldRender != 0 };
        _frameWaited = true;
        _beginIssued = false;
    }

    private void ApplyUnitySettings()
    {
        _savedVSync = QualitySettings.vSyncCount;
        _savedTargetFrameRate = Application.targetFrameRate;
        QualitySettings.vSyncCount = 0; // the headset paces frames through xrWaitFrame
        Application.targetFrameRate = -1;
        Application.runInBackground = true; // the game window loses focus when the headset takes over

        // Exclusive fullscreen minimizes (and stops rendering) as soon as focus moves to the headset.
        if (Config.BorderlessInVr.Value && Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen)
        {
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Mod.Log.Msg("VR: switched the game from exclusive fullscreen to borderless window");
        }
    }

    private void RestoreUnitySettings()
    {
        QualitySettings.vSyncCount = _savedVSync;
        Application.targetFrameRate = _savedTargetFrameRate;
    }

    private void DestroyAll()
    {
        for (var eye = 0; eye < 2; eye++)
        {
            if (_eyeSwapchains[eye] != 0)
            {
                Xr.DestroySwapchain(_eyeSwapchains[eye]);
                _eyeSwapchains[eye] = 0;
            }
        }

        if (_quadSwapchain != 0)
        {
            Xr.DestroySwapchain(_quadSwapchain);
            _quadSwapchain = 0;
        }

        for (var eye = 0; eye < 2; eye++)
        {
            if (_screenSwapchains[eye] != 0)
            {
                Xr.DestroySwapchain(_screenSwapchains[eye]);
                _screenSwapchains[eye] = 0;
            }
        }

        if (_localSpace != 0)
        {
            Xr.DestroySpace(_localSpace);
            _localSpace = 0;
        }

        if (_session != 0)
        {
            Xr.DestroySession(_session);
            _session = 0;
        }

        if (_instance != 0)
        {
            Xr.DestroyInstance(_instance);
            _instance = 0;
        }

        _frameWaited = false;
        _sessionState = XrSessionState.Unknown;
        EyeWidth = EyeHeight = 0;
    }

    private void Fail(Exception ex)
    {
        Mod.Log.Error($"VR: {ex.Message}");
        if (ex is XrException xe && (xe.Result == XrResult.ErrorSessionLost || xe.Result == XrResult.ErrorInstanceLost))
        {
            // Runtime restarted or headset disconnected: frames can't be in flight on a lost session.
            DestroyAll();
            State = VrState.WaitingForRuntime;
            _nextRetryTime = Time.unscaledTime + 3f;
            return;
        }

        if (State == VrState.Running)
        {
            // Frames may be queued on the render thread: end the session behind them, then clean up.
            _failAfterStop = true;
            BeginStop();
            return;
        }

        if (State != VrState.Stopping)
        {
            DestroyAll();
            State = VrState.Failed;
        }
    }

    private void Check(int result, string call)
    {
        if (result < 0)
        {
            throw new XrException(call + " (" + Xr.ResultName(_instance, result) + ")", result);
        }
    }

    private static int Encode(int kind, int slot) => 0x4C4D0000 | (kind << 8) | slot;

    private static void DrainRenderThreadLog()
    {
        while (RenderThreadLog.TryDequeue(out var line))
        {
            if (line.StartsWith("VR: first"))
            {
                Mod.Log.Msg(line);
            }
            else
            {
                Mod.Log.Warning(line);
            }
        }
    }

    // ---------------- render thread ----------------

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void OnRenderEvent(int eventId)
    {
        try
        {
            var s = _current;
            if (s == null || (eventId & 0x7FFF0000) != 0x4C4D0000)
            {
                return;
            }

            var kind = (eventId >> 8) & 0xFF;
            var slot = eventId & 0xFF;
            switch (kind)
            {
                case EvBegin:
                    s.RenderThreadBegin(slot);
                    break;
                case EvSubmit:
                    s.RenderThreadSubmit(slot);
                    break;
                case EvEndSession:
                    s.RenderThreadEndSession();
                    break;
            }
        }
        catch (Exception ex)
        {
            RenderThreadLog.Enqueue("VR render thread: " + ex);
        }
    }

    private void RenderThreadBegin(int slot)
    {
        var bi = new XrFrameBeginInfo { Type = XrType.FrameBeginInfo };
        var res = Xr.BeginFrame(_session, &bi);
        _slots[slot].Began = res >= 0;
        if (res < 0)
        {
            RenderThreadLog.Enqueue($"VR: xrBeginFrame failed ({res})");
        }
    }

    private void RenderThreadSubmit(int slot)
    {
        Thread.MemoryBarrier();
        ref var s = ref _slots[slot];
        if (!s.Began)
        {
            return;
        }

        s.Began = false;
        var layers = stackalloc void*[4];
        uint layerCount = 0;
        var views = stackalloc XrCompositionLayerProjectionView[2];
        XrCompositionLayerProjection projection;
        XrCompositionLayerQuad screenLeft, screenRight, quad;
        ref var l = ref s.Layers;

        if (s.ShouldRender && l.Projection
            && CopyToSwapchain(_eyeSwapchains[0], _eyeImages[0], l.LeftEyeTexture)
            && CopyToSwapchain(_eyeSwapchains[1], _eyeImages[1], l.RightEyeTexture))
        {
            for (var eye = 0; eye < 2; eye++)
            {
                var ev = eye == 0 ? s.Left : s.Right;
                views[eye] = new XrCompositionLayerProjectionView
                {
                    Type = XrType.CompositionLayerProjectionView,
                    Pose = ev.Pose,
                    Fov = ev.Fov,
                    SubImage = new XrSwapchainSubImage
                    {
                        Swapchain = _eyeSwapchains[eye],
                        ImageRect = new XrRect2Di { Width = EyeWidth, Height = EyeHeight },
                    },
                };
            }

            projection = new XrCompositionLayerProjection
            {
                Type = XrType.CompositionLayerProjection,
                Space = _localSpace,
                ViewCount = 2,
                Views = views,
            };
            layers[layerCount++] = &projection;
        }

        if (s.ShouldRender && l.StereoQuad
            && CopyToSwapchain(_screenSwapchains[0], _screenImages[0], l.StereoQuadLeftTexture)
            && CopyToSwapchain(_screenSwapchains[1], _screenImages[1], l.StereoQuadRightTexture))
        {
            screenLeft = ScreenQuad(0, XrConst.EyeVisibilityLeft, ref l);
            screenRight = ScreenQuad(1, XrConst.EyeVisibilityRight, ref l);
            layers[layerCount++] = &screenLeft;
            layers[layerCount++] = &screenRight;
        }

        if (s.ShouldRender && l.Quad && CopyToSwapchain(_quadSwapchain, _quadImages, l.QuadTexture))
        {
            quad = new XrCompositionLayerQuad
            {
                Type = XrType.CompositionLayerQuad,
                LayerFlags = l.QuadAlphaBlend ? XrConst.LayerBlendTextureSourceAlpha : 0,
                Space = _localSpace,
                EyeVisibility = XrConst.EyeVisibilityBoth,
                SubImage = new XrSwapchainSubImage
                {
                    Swapchain = _quadSwapchain,
                    ImageRect = new XrRect2Di { Width = QuadWidth, Height = QuadHeight },
                },
                Pose = l.QuadPose,
                Size = l.QuadSize,
            };
            layers[layerCount++] = &quad;
        }

        var ei = new XrFrameEndInfo
        {
            Type = XrType.FrameEndInfo,
            DisplayTime = s.DisplayTime,
            EnvironmentBlendMode = XrConst.EnvironmentBlendModeOpaque,
            LayerCount = layerCount,
            Layers = layers,
        };
        var res = Xr.EndFrame(_session, &ei);
        if (res < 0)
        {
            RenderThreadLog.Enqueue($"VR: xrEndFrame failed ({res})");
        }
        else if (layerCount > 0 && SubmittedFrames++ == 0)
        {
            RenderThreadLog.Enqueue("VR: first frame submitted to the headset");
        }
    }

    /// <summary>One eye's half of the 3D screen: same panel for both eyes, each showing its own picture.</summary>
    private XrCompositionLayerQuad ScreenQuad(int eye, int visibility, ref FrameLayers l) => new()
    {
        Type = XrType.CompositionLayerQuad,
        Space = _localSpace,
        EyeVisibility = visibility,
        SubImage = new XrSwapchainSubImage
        {
            Swapchain = _screenSwapchains[eye],
            ImageRect = new XrRect2Di { Width = QuadWidth, Height = QuadHeight },
        },
        Pose = l.StereoQuadPose,
        Size = l.StereoQuadSize,
    };

    private bool CopyToSwapchain(ulong swapchain, IntPtr[] images, IntPtr source)
    {
        if (swapchain == 0 || source == IntPtr.Zero)
        {
            return false;
        }

        var ai = new XrSwapchainImageAcquireInfo { Type = XrType.SwapchainImageAcquireInfo };
        uint index;
        if (Xr.AcquireSwapchainImage(swapchain, &ai, &index) < 0)
        {
            return false;
        }

        var wi = new XrSwapchainImageWaitInfo { Type = XrType.SwapchainImageWaitInfo, Timeout = 1_000_000_000 };
        var res = Xr.WaitSwapchainImage(swapchain, &wi);
        if (res != XrResult.Success)
        {
            RenderThreadLog.Enqueue($"VR: xrWaitSwapchainImage returned {res}");
            return false;
        }

        D3D11.CopyResource(_context, images[index], source);
        var ri = new XrSwapchainImageReleaseInfo { Type = XrType.SwapchainImageReleaseInfo };
        Xr.ReleaseSwapchainImage(swapchain, &ri);
        return true;
    }

    private void RenderThreadEndSession()
    {
        var res = Xr.EndSession(_session);
        if (res < 0)
        {
            RenderThreadLog.Enqueue($"VR: xrEndSession failed ({res})");
        }

        _endSessionDone = true;
    }
}
