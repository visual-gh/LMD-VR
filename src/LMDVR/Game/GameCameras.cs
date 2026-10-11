using Il2CppInterop.Runtime;
using LMDVR.Rendering;
using UnityEngine;

namespace LMDVR.Game;

/// <summary>
/// The game's own cameras: the gameplay camera ("PlayCamera(Clone)") and the UI camera ("UICam").
/// While riding in VR the gameplay camera renders almost nothing (the eye cameras do the work) and the
/// UI camera renders the HUD and menus into a transparent texture shown on a panel.
/// Two flavours:
/// - drive: the game's camera controller is paused (like the Alternative Camera mod does) and the
///   camera follows the player's head, so FMOD audio and level-of-detail follow the headset;
/// - follow: the game (or another camera mod) keeps moving the camera and VR looks through it;
///   only camera shake and the "handheld" sway are paused, since they are sickening in a headset.
/// </summary>
internal sealed class GameCameras
{
    private readonly List<(Behaviour Behaviour, bool WasEnabled)> _paused = new();
    private Camera _gameplay;
    private Camera _ui;
    private Transform _listener;

    private bool _takenOver;
    private bool _drive;
    private RenderTexture _savedGameplayTarget;
    private int _savedCullingMask;
    private Vector3 _savedListenerLocalPosition;
    private RenderTexture _savedUiTarget;
    private CameraClearFlags _savedUiClear;
    private Color _savedUiBackground;
    private RenderTexture _sink;
    private RenderTexture _hudRender;
    private RenderTexture _hudOutput;
    private IntPtr _hudOutputNative;

    public Camera Gameplay => _gameplay;

    /// <summary>What the gameplay camera draws when VR hasn't taken it over (for cloning eye cameras).</summary>
    public int GameplayCullingMask => _takenOver ? _savedCullingMask : _gameplay != null ? _gameplay.cullingMask : -1;

    public IntPtr HudNative => _hudOutputNative;

    /// <summary>The live gameplay camera, or false when not riding (menus, loading, ...).</summary>
    public bool Refresh()
    {
        if (_gameplay == null)
        {
            var go = GameObject.Find("PlayCamera(Clone)");
            _gameplay = go != null ? go.GetComponent<Camera>() : null;
            // FMOD's listener sits on a child that the game keeps offset towards the bike.
            _listener = _gameplay != null ? go.transform.Find("AudioListener") : null;
        }

        if (_ui == null)
        {
            var uiGo = GameObject.Find("UICam");
            _ui = uiGo != null ? uiGo.GetComponent<Camera>() : null;
        }

        return _gameplay != null && _gameplay.isActiveAndEnabled;
    }

    public void Takeover(int hudWidth, int hudHeight, bool drive)
    {
        if (_takenOver && _drive == drive)
        {
            return;
        }

        Release();
        if (_gameplay == null)
        {
            return;
        }

        _takenOver = true;
        _drive = drive;
        _sink ??= ScreenMirror.NewTexture(16, 16, "LMDVR_Sink", 16);
        _savedGameplayTarget = _gameplay.targetTexture;
        _savedCullingMask = _gameplay.cullingMask;
        _gameplay.targetTexture = _sink;
        _gameplay.cullingMask = 0;

        var go = _gameplay.gameObject;
        if (drive)
        {
            Pause(go, "Cameras.PlayCamera");
            if (_listener != null)
            {
                _savedListenerLocalPosition = _listener.localPosition;
                Pause(_listener.gameObject, "DynamicAudioListenerDistance");
            }
        }
        else
        {
            Pause(go, "Cameras.CamShake");
            Pause(go, "RandomSinMovement");
        }

        if (_ui != null)
        {
            if (_hudRender == null)
            {
                _hudRender = ScreenMirror.NewTexture(hudWidth, hudHeight, "LMDVR_Hud", 16);
                _hudOutput = ScreenMirror.NewTexture(hudWidth, hudHeight, "LMDVR_HudOut");
                _hudOutputNative = _hudOutput.GetNativeTexturePtr();
            }

            _savedUiTarget = _ui.targetTexture;
            _savedUiClear = _ui.clearFlags;
            _savedUiBackground = _ui.backgroundColor;
            _ui.targetTexture = _hudRender;
            _ui.clearFlags = CameraClearFlags.SolidColor;
            _ui.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }

        Mod.Log.Msg(drive ? "VR: game camera paused, VR drives it (HUD on panel)" : "VR: looking through the game camera (HUD on panel)");
    }

    /// <summary>Drive mode: keeps the (invisible) gameplay camera at the player's head for audio and LOD.</summary>
    public void FollowHead(Vector3 position, Quaternion rotation)
    {
        if (!_takenOver || !_drive || _gameplay == null)
        {
            return;
        }

        _gameplay.transform.SetPositionAndRotation(position, rotation);
        if (_listener != null)
        {
            _listener.localPosition = Vector3.zero;
        }

        // Something (e.g. another camera mod) may switch paused behaviours back on.
        foreach (var (behaviour, _) in _paused)
        {
            if (behaviour != null && behaviour.enabled)
            {
                behaviour.enabled = false;
            }
        }
    }

    /// <summary>End of frame: headset-ready copy of the HUD.</summary>
    public void FlipHud()
    {
        if (!_takenOver || _hudRender == null)
        {
            return;
        }

        var flip = Config.FlipEyes.Value;
        Graphics.Blit(_hudRender, _hudOutput, new Vector2(1f, flip ? -1f : 1f), new Vector2(0f, flip ? 1f : 0f));
    }

    public void Release()
    {
        if (!_takenOver)
        {
            return;
        }

        _takenOver = false;
        if (_gameplay != null)
        {
            _gameplay.targetTexture = _savedGameplayTarget;
            _gameplay.cullingMask = _savedCullingMask;
        }

        foreach (var (behaviour, wasEnabled) in _paused)
        {
            if (behaviour != null)
            {
                behaviour.enabled = wasEnabled;
            }
        }

        _paused.Clear();
        if (_drive && _listener != null)
        {
            _listener.localPosition = _savedListenerLocalPosition;
        }

        if (_ui != null)
        {
            _ui.targetTexture = _savedUiTarget;
            _ui.clearFlags = _savedUiClear;
            _ui.backgroundColor = _savedUiBackground;
        }

        Mod.Log.Msg("VR: game cameras restored");
    }

    private void Pause(GameObject go, string typeNameSuffix)
    {
        foreach (var c in go.GetComponents(Il2CppType.Of<Behaviour>()))
        {
            if (c != null && c.GetIl2CppType().FullName.EndsWith(typeNameSuffix))
            {
                var behaviour = c.Cast<Behaviour>();
                _paused.Add((behaviour, behaviour.enabled));
                behaviour.enabled = false;
            }
        }
    }
}
