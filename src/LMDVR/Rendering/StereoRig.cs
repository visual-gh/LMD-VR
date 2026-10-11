using LMDVR.OpenXR;
using UnityEngine;

namespace LMDVR.Rendering;

/// <summary>
/// Two eye cameras rendering into their own textures, plus vertically flipped copies that are
/// handed to the headset: Unity stores render textures bottom row first on D3D11, the
/// compositor reads them top row first.
/// </summary>
internal sealed class StereoRig
{
    private readonly Camera[] _eyes = new Camera[2];
    private readonly RenderTexture[] _render = new RenderTexture[2];
    private readonly RenderTexture[] _output = new RenderTexture[2];
    private readonly IntPtr[] _outputNative = new IntPtr[2];
    private Camera _source;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Camera Source => _source;
    public bool IsBuilt => _eyes[0] != null && _eyes[1] != null && _source != null;

    /// <summary>Keeps all of the game's post effects (3D screen) instead of the headset-friendly overrides.</summary>
    public bool GameLook { get; private set; }

    public RenderTexture EyeTexture(int eye) => _render[eye];

    public IntPtr OutputNative(int eye) => _outputNative[eye];

    /// <param name="cullingMask">What the game camera draws when VR hasn't taken it over.</param>
    public void Build(Camera source, int width, int height, bool gameLook, int cullingMask)
    {
        Destroy();
        _source = source;
        Width = width;
        Height = height;
        GameLook = gameLook;
        if (!gameLook)
        {
            PostOverrides.Ensure(Config.DisableVignette.Value);
        }

        for (var eye = 0; eye < 2; eye++)
        {
            _render[eye] = ScreenMirror.NewTexture(width, height, $"LMDVR_Eye{eye}", 24);
            _output[eye] = ScreenMirror.NewTexture(width, height, $"LMDVR_EyeOut{eye}");
            _outputNative[eye] = _output[eye].GetNativeTexturePtr();
            _eyes[eye] = EyeCameraFactory.Create(source, eye == 0 ? "LMDVR_LeftEye" : "LMDVR_RightEye", _render[eye],
                cullingMask, gameLook ? -1 : PostOverrides.Layer);
        }

        Mod.Log.Msg($"VR: stereo rig built from '{source.name}' at {width}x{height} per eye ({source.actualRenderingPath}, HDR {source.allowHDR}{(gameLook ? ", game look" : "")})");
    }

    /// <summary>Places one eye camera in the world with the headset's asymmetric frustum for that eye.</summary>
    public void PlaceEye(int eye, Vector3 worldPosition, Quaternion worldRotation, XrFovf fov, float near)
    {
        var cam = _eyes[eye];
        if (cam == null)
        {
            return;
        }

        var far = _source != null ? _source.farClipPlane : 5000f;
        cam.transform.SetPositionAndRotation(worldPosition, worldRotation);
        cam.nearClipPlane = near;
        cam.farClipPlane = far;
        cam.fieldOfView = XrMath.VerticalFovDegrees(fov);
        cam.projectionMatrix = XrMath.Projection(fov, near, far);
    }

    /// <summary>End of frame: produce the headset-ready (row-flipped) copies.</summary>
    public void FlipOutputs()
    {
        var flip = Config.FlipEyes.Value;
        for (var eye = 0; eye < 2; eye++)
        {
            Graphics.Blit(_render[eye], _output[eye], new Vector2(1f, flip ? -1f : 1f), new Vector2(0f, flip ? 1f : 0f));
        }
    }

    public void SetActive(bool active)
    {
        foreach (var cam in _eyes)
        {
            if (cam != null)
            {
                cam.enabled = active;
            }
        }
    }

    public void Destroy()
    {
        for (var eye = 0; eye < 2; eye++)
        {
            if (_eyes[eye] != null)
            {
                UnityEngine.Object.Destroy(_eyes[eye].gameObject);
                _eyes[eye] = null;
            }

            // Destroy, not just Release: these are never unloaded on their own.
            if (_render[eye] != null)
            {
                UnityEngine.Object.Destroy(_render[eye]);
                UnityEngine.Object.Destroy(_output[eye]);
            }

            _render[eye] = null;
            _output[eye] = null;
            _outputNative[eye] = IntPtr.Zero;
        }

        _source = null;
    }
}
