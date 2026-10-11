using System.Collections;
using LMDVR.Cameras;
using LMDVR.Game;
using LMDVR.OpenXR;
using LMDVR.Rendering;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace LMDVR;

/// <summary>
/// Glue between Unity's frame and the OpenXR session:
/// Update -> xrWaitFrame and mode changes, first camera cull -> head poses + xrBeginFrame,
/// end of frame -> layers + xrEndFrame.
/// Modes: Flat (no headset), Theater (menus and the flat Screen camera on a virtual screen),
/// Immersive (stereo while riding: first person, chase, diorama or the 3D screen).
/// </summary>
internal sealed partial class VrManager
{
    private const int ScreenTextureWidth = 1920;
    private const int ScreenTextureHeight = 1080;
    private const int PreviewEyeWidth = 960;
    private const int PreviewEyeHeight = 1080;

    private enum Mode
    {
        Flat,
        Theater,
        Immersive,
    }

    private readonly StereoRig _rig = new();
    private readonly RiderTracker _rider = new();
    private readonly GameCameras _cameras = new();
    private readonly ChaseCamera _chase = new();
    private readonly DioramaCamera _diorama = new();
    private readonly StereoScreenCamera _stereoScreen = new();
    private VrSession _session;
    private ScreenMirror _mirror;
    private Camera.CameraCallback _preCull;
    private int _lastPreCullFrame = -1;
    private IntPtr _device;
    private Mode _mode = Mode.Flat;
    private VrCameraMode _activeCamera;
    private bool _preview;
    private float _previewPitch;
    private bool _recenterRequested = true;
    private Vector3 _recenterPosition;
    private Quaternion _recenterRotation = Quaternion.identity;
    private float _headsetFov = 90f;
    private bool _loggedHeadsetFov;
    private bool _cameraModsPaused;
    private bool _debugCaptureRequested;
    private bool _loggedPreCullError;
    private bool _loggedEndOfFrameError;

    private bool HeadsetRunning => _session != null && _session.State == VrState.Running;

    public void Initialize()
    {
        // Application.onBeforeRender is stripped from this build, so hook the first camera cull of each
        // frame instead: it runs after all game logic, right before any rendering is queued.
        _preCull = new Action<Camera>(OnPreCull);
        Camera.onPreCull = Camera.onPreCull == null ? _preCull : Camera.onPreCull + _preCull;
        MelonCoroutines.Start(EndOfFrameLoop());

        if (Config.AutoStart.Value)
        {
            StartVr();
        }
    }

    public void OnUpdate()
    {
        _session?.Tick();
        if (_session != null && _session.RuntimeRecentered)
        {
            _session.RuntimeRecentered = false;
            _recenterRequested = true;
        }

        // Before any camera takeover: other camera mods go back to the game camera and pause while the headset is on.
        if (HeadsetRunning && !_cameraModsPaused && Config.PauseCameraMods.Value)
        {
            _cameraModsPaused = true;
            CameraModCompat.Pause();
        }
        else if (!HeadsetRunning && _cameraModsPaused)
        {
            _cameraModsPaused = false;
            CameraModCompat.Resume();
        }

        try
        {
            UpdateMode();
        }
        catch (Exception ex)
        {
            Mod.Log.Error($"VR: mode update failed: {ex}");
        }
    }

    public void OnApplicationQuit()
    {
        LeaveImmersive();
        _session?.Shutdown();
    }

    private void StartVr()
    {
        try
        {
            if (_device == IntPtr.Zero)
            {
                _device = GetUnityD3D11Device();
            }

            _mirror ??= new ScreenMirror(ScreenTextureWidth, ScreenTextureHeight);
            _mirror.FlipVertically = Config.FlipScreenCapture.Value;

            var loader = Path.Combine(MelonEnvironment.UserLibsDirectory, "openxr_loader.dll");
            _session = new VrSession(loader, Config.ResolutionScale.Value, ScreenTextureWidth, ScreenTextureHeight);
            _session.Start(_device);
            _recenterRequested = true;
        }
        catch (Exception ex)
        {
            Mod.Log.Error($"VR: could not start: {ex}");
        }
    }

    private void UpdateMode()
    {
        var active = HeadsetRunning || _preview;
        var camera = Config.CameraMode.Value;
        var inRide = active && _cameras.Refresh() && _rider.Refresh();
        _screenRide = inRide && camera == VrCameraMode.Screen;

        // The Screen camera shows the game's own image on the virtual screen (theater), unless it has 3D depth.
        var stereoScreen = camera == VrCameraMode.Screen && Config.ScreenDepth.Value > 0f && (!HeadsetRunning || _session.StereoScreenAvailable);
        var riding = inRide && (camera != VrCameraMode.Screen || stereoScreen);
        var target = riding ? Mode.Immersive : HeadsetRunning ? Mode.Theater : Mode.Flat;

        if (target == Mode.Immersive)
        {
            var width = stereoScreen ? ScreenTextureWidth : HeadsetRunning ? _session.EyeWidth : PreviewEyeWidth;
            var height = stereoScreen ? ScreenTextureHeight : HeadsetRunning ? _session.EyeHeight : PreviewEyeHeight;
            if (!_rig.IsBuilt || _rig.Source != _cameras.Gameplay || _rig.Width != width || _rig.Height != height || _rig.GameLook != stereoScreen)
            {
                _rig.Build(_cameras.Gameplay, width, height, stereoScreen, _cameras.GameplayCullingMask);
            }
        }

        if (target == _mode && (target != Mode.Immersive || camera == _activeCamera))
        {
            return;
        }

        var switchingCamera = target == Mode.Immersive && _mode == Mode.Immersive;
        if (_mode == Mode.Immersive)
        {
            LeaveImmersive();
        }

        if (target == Mode.Immersive)
        {
            _rig.SetActive(true);
            _cameras.Takeover(ScreenTextureWidth, ScreenTextureHeight, drive: camera is VrCameraMode.FirstPerson or VrCameraMode.Chase);
            _rider.SnapHeading();
            _chase.Reset();
            _diorama.Reset();
            _stereoScreen.Reset();
            _activeCamera = camera;
            if (!switchingCamera)
            {
                _recenterRequested = true;
            }
        }

        Mod.Log.Msg($"VR: mode {_mode} -> {target}{(target == Mode.Immersive ? $" ({camera})" : "")}");
        _mode = target;
    }

    private void LeaveImmersive()
    {
        _rig.SetActive(false);
        _cameras.Release();
    }

    private void OnPreCull(Camera camera)
    {
        try
        {
            var frame = Time.frameCount;
            if (frame == _lastPreCullFrame)
            {
                return;
            }

            _lastPreCullFrame = frame;
            var xrFrame = _session != null && _session.FrameActive;
            if (!xrFrame && !(_preview && _mode == Mode.Immersive))
            {
                return;
            }

            EyeView left = default, right = default;
            var haveViews = xrFrame ? _session.LocateViews(out left, out right) : PreviewViews(out left, out right);
            if (haveViews)
            {
                _headsetFov = XrMath.VerticalFovDegrees(left.Fov);
                if (xrFrame && !_loggedHeadsetFov)
                {
                    _loggedHeadsetFov = true;
                    Mod.Log.Msg($"VR: headset field of view per eye {_headsetFov:0}° vertical, {(left.Fov.AngleRight - left.Fov.AngleLeft) * Mathf.Rad2Deg:0}° horizontal");
                }
            }

            if (haveViews && _recenterRequested)
            {
                ApplyRecenter(left, right);
            }

            if (haveViews && _mode == Mode.Immersive)
            {
                PlaceEyes(left, right);
            }

            if (xrFrame)
            {
                _session.IssueBegin();
            }
        }
        catch (Exception ex)
        {
            if (!_loggedPreCullError)
            {
                _loggedPreCullError = true;
                Mod.Log.Error($"VR: pre-cull hook failed: {ex}");
            }
        }
    }

    private void ApplyRecenter(in EyeView left, in EyeView right)
    {
        _recenterRequested = false;
        _recenterPosition = (XrMath.ToUnity(left.Pose.Position) + XrMath.ToUnity(right.Pose.Position)) * 0.5f;
        _recenterRotation = XrMath.YawOnly(XrMath.ToUnity(left.Pose.Orientation));
        _rider.SnapHeading();
        Mod.Log.Msg("VR: view recentered");
    }

    private void PlaceEyes(in EyeView left, in EyeView right)
    {
        var dt = Time.unscaledDeltaTime;

        // After all game logic of this frame, so a respawn is seen in the same frame it happens:
        // the views cut straight to the new spot, like the game's camera does.
        _rider.Update();
        if (_rider.Teleported)
        {
            _rider.Teleported = false;
            _chase.Reset();
        }

        if (_activeCamera == VrCameraMode.Screen)
        {
            PlaceScreenEyes(dt);
            return;
        }

        RigPose rig;
        switch (_activeCamera)
        {
            case VrCameraMode.Chase:
                if (!_chase.TryGetPose(_rider, dt, _chaseLook, out rig))
                {
                    return;
                }

                break;
            case VrCameraMode.Diorama:
                if (!_diorama.TryGetPose(_cameras.Gameplay, _rider, dt, out rig))
                {
                    return;
                }

                break;
            default:
                if (!_rider.TryGetEyeAnchor(dt, out var position, out var rotation))
                {
                    return;
                }

                rotation *= Quaternion.Euler(-_firstPersonLook.y, _firstPersonLook.x, 0f);
                rig = new RigPose { Position = position, Rotation = rotation, Scale = Config.WorldScale.Value };
                break;
        }

        // Head pose relative to the recentered seat, scaled into game units, placed on the rig.
        var inverse = Quaternion.Inverse(_recenterRotation);
        var leftWorld = rig.Position + rig.Rotation * (inverse * (XrMath.ToUnity(left.Pose.Position) - _recenterPosition) * rig.Scale);
        var rightWorld = rig.Position + rig.Rotation * (inverse * (XrMath.ToUnity(right.Pose.Position) - _recenterPosition) * rig.Scale);
        var leftRotation = rig.Rotation * (inverse * XrMath.ToUnity(left.Pose.Orientation));
        var rightRotation = rig.Rotation * (inverse * XrMath.ToUnity(right.Pose.Orientation));

        // First person: the far near plane cuts the rider's body away; a wider FOV is still submitted
        // with the headset's own angles, so it gets squeezed into the lenses.
        var firstPerson = _activeCamera == VrCameraMode.FirstPerson;
        var near = (firstPerson ? Config.FirstPersonNearClip.Value : Config.NearClip.Value) * Mathf.Max(1f, rig.Scale);
        var leftFov = left.Fov;
        var rightFov = right.Fov;
        if (firstPerson && Config.FirstPersonFov.Value > 0f)
        {
            leftFov = XrMath.WidenFov(leftFov, Config.FirstPersonFov.Value);
            rightFov = XrMath.WidenFov(rightFov, Config.FirstPersonFov.Value);
        }

        _rig.PlaceEye(0, leftWorld, leftRotation, leftFov, near);
        _rig.PlaceEye(1, rightWorld, rightRotation, rightFov, near);
        _cameras.FollowHead((leftWorld + rightWorld) * 0.5f, leftRotation);
    }

    /// <summary>3D screen: the game camera's own view from two points a little apart; head movement doesn't change it.</summary>
    private void PlaceScreenEyes(float dt)
    {
        var game = _cameras.Gameplay;
        if (_stereoScreen.TryGetEyes(game, _rider, (float)_rig.Width / _rig.Height, dt,
                out var leftPosition, out var rightPosition, out var rotation, out var leftFov, out var rightFov))
        {
            _rig.PlaceEye(0, leftPosition, rotation, leftFov, game.nearClipPlane);
            _rig.PlaceEye(1, rightPosition, rotation, rightFov, game.nearClipPlane);
        }
    }

    /// <summary>Fixed eyes 64 mm apart looking ahead (tilted by PageUp/PageDown), for checking views without a headset.</summary>
    private bool PreviewViews(out EyeView left, out EyeView right)
    {
        var fov = XrMath.SymmetricFov(90f, 2f * Mathf.Atan(Mathf.Tan(45f * Mathf.Deg2Rad) * PreviewEyeHeight / PreviewEyeWidth) * Mathf.Rad2Deg);
        var tilt = Quaternion.Euler(_previewPitch, 0f, 0f);
        left = new EyeView { Pose = new XrPosef { Orientation = XrMath.ToXr(tilt), Position = XrMath.ToXr(tilt * new Vector3(-0.032f, 0f, 0f)) }, Fov = fov };
        right = new EyeView { Pose = new XrPosef { Orientation = XrMath.ToXr(tilt), Position = XrMath.ToXr(tilt * new Vector3(0.032f, 0f, 0f)) }, Fov = fov };
        return true;
    }

    private IEnumerator EndOfFrameLoop()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            OnEndOfFrame();
        }
    }

    private void OnEndOfFrame()
    {
        try
        {
            if (_debugCaptureRequested)
            {
                _debugCaptureRequested = false;
                SaveDebugCapture();
            }

            var xrFrame = _session != null && _session.FrameActive;
            var layers = new FrameLayers();

            if (_mode == Mode.Immersive && _rig.IsBuilt)
            {
                _rig.FlipOutputs();
                _cameras.FlipHud();
                DrawEyeOnMonitor();
                if (xrFrame && _session.ShouldRender && _activeCamera == VrCameraMode.Screen)
                {
                    // 3D screen: one panel where each eye sees its own picture, the HUD on top of it.
                    layers.StereoQuad = true;
                    layers.StereoQuadLeftTexture = _rig.OutputNative(0);
                    layers.StereoQuadRightTexture = _rig.OutputNative(1);
                    layers.StereoQuadPose = PanelPose(Config.ScreenDistance.Value, 0f);
                    layers.StereoQuadSize = PanelSize(Config.ScreenWidth.Value);
                    AddQuad(ref layers, _cameras.HudNative, Config.ScreenDistance.Value, 0f, Config.ScreenWidth.Value, true);
                }
                else if (xrFrame && _session.ShouldRender)
                {
                    layers.Projection = true;
                    layers.LeftEyeTexture = _rig.OutputNative(0);
                    layers.RightEyeTexture = _rig.OutputNative(1);
                    AddQuad(ref layers, _cameras.HudNative, Config.HudDistance.Value, Config.HudHeight.Value, Config.HudWidth.Value, true);
                }
            }
            else if (xrFrame && _session.ShouldRender)
            {
                AddQuad(ref layers, _mirror.Capture(), Config.ScreenDistance.Value, 0f, Config.ScreenWidth.Value, false);
            }

            if (xrFrame)
            {
                _session.IssueSubmit(layers);
            }
        }
        catch (Exception ex)
        {
            if (!_loggedEndOfFrameError)
            {
                _loggedEndOfFrameError = true;
                Mod.Log.Error($"VR: end of frame failed: {ex}");
            }
        }
    }

    /// <summary>A flat panel in front of the recentered head, fixed relative to the player.</summary>
    private void AddQuad(ref FrameLayers layers, IntPtr texture, float distance, float height, float width, bool alphaBlend)
    {
        if (texture == IntPtr.Zero)
        {
            return;
        }

        layers.Quad = true;
        layers.QuadTexture = texture;
        layers.QuadAlphaBlend = alphaBlend;
        layers.QuadPose = PanelPose(distance, height);
        layers.QuadSize = PanelSize(width);
    }

    private XrPosef PanelPose(float distance, float height)
    {
        var position = _recenterPosition + _recenterRotation * new Vector3(0f, height, distance);
        return new XrPosef { Position = XrMath.ToXr(position), Orientation = XrMath.ToXr(_recenterRotation) };
    }

    private static XrExtent2Df PanelSize(float width) => new() { Width = width, Height = width * ScreenTextureHeight / ScreenTextureWidth };

    /// <summary>Monitor shows the left eye, cropped to the window's aspect ratio.</summary>
    private void DrawEyeOnMonitor()
    {
        var eye = _rig.EyeTexture(0);
        var windowAspect = (float)Screen.width / Screen.height;
        var eyeAspect = (float)eye.width / eye.height;
        var visible = Mathf.Min(1f, eyeAspect / windowAspect);
        Graphics.Blit(eye, (RenderTexture)null, new Vector2(1f, visible), new Vector2(0f, (1f - visible) * 0.5f));
    }

    /// <summary>
    /// F7: writes test images. While riding: both eyes (upright). Otherwise the virtual screen, which
    /// is headset-ready and therefore looks upside down in the PNG (D3D stores rows top-first).
    /// </summary>
    private void SaveDebugCapture()
    {
        var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "LMDVR");
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("HHmmss");
        if (_mode == Mode.Immersive && _rig.IsBuilt)
        {
            SavePng(_rig.EyeTexture(0), Path.Combine(dir, $"eye_left_{stamp}.png"));
            SavePng(_rig.EyeTexture(1), Path.Combine(dir, $"eye_right_{stamp}.png"));
            Mod.Log.Msg($"VR: eye test images written to {dir}");
            return;
        }

        _mirror ??= new ScreenMirror(ScreenTextureWidth, ScreenTextureHeight) { FlipVertically = Config.FlipScreenCapture.Value };
        _mirror.Capture();
        var file = Path.Combine(dir, $"virtual_screen_{stamp}.png");
        SavePng(_mirror.Output, file);
        Mod.Log.Msg($"VR: virtual screen test image written to {file}");
    }

    private static void SavePng(RenderTexture rt, string file)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        byte[] png = ImageConversion.EncodeToPNG(tex);
        File.WriteAllBytes(file, png);
        UnityEngine.Object.Destroy(tex);
    }

    private static IntPtr GetUnityD3D11Device()
    {
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
        {
            throw new NotSupportedException($"VR needs Direct3D 11, the game runs {SystemInfo.graphicsDeviceType}.");
        }

        var probe = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
        probe.Create();
        var texture = probe.GetNativeTexturePtr();
        var device = D3D11.GetDevice(texture);
        probe.Release();
        UnityEngine.Object.Destroy(probe);
        Mod.Log.Msg($"VR: got Unity's D3D11 device 0x{device.ToInt64():X}");
        return device;
    }
}
