using LMDVR.Cameras;
using LMDVR.OpenXR;
using UnityEngine;

namespace LMDVR;

/// <summary>What the player can change while playing: the camera, its setting and the right-stick angle.</summary>
internal sealed partial class VrManager
{
    private const float FovStep = 5f;
    private const float MaxFirstPersonFov = 120f; // the Alternative Camera mod's first-person limit
    private const float LookYawSpeed = 100f; // degrees per second, stick fully out
    private const float LookPitchSpeed = 70f;
    private const float ZoomStep = 1.25f;
    private const float DepthStep = 0.25f;

    private static readonly VrCameraMode[] CameraCycle =
        { VrCameraMode.Diorama, VrCameraMode.Chase, VrCameraMode.FirstPerson, VrCameraMode.Screen };

    private Vector2 _firstPersonLook; // right-stick angles in degrees: x = turned right, y = looking up
    private Vector2 _chaseLook;
    private bool _screenRide; // riding with the Screen camera (flat or 3D): the right stick resizes the screen
    private bool _screenResized;

    /// <summary>F9: VR on or off.</summary>
    public void Toggle()
    {
        if (_session == null || _session.State is VrState.Off or VrState.Failed)
        {
            StartVr();
        }
        else
        {
            Mod.Log.Msg("VR: stopping (F9)");
            _session.RequestStop();
        }
    }

    public void TogglePreview()
    {
        _preview = !_preview;
        Mod.Log.Msg($"VR: desktop stereo preview {(_preview ? "on (shows the left eye on the monitor, PageUp/PageDown tilt it)" : "off")}");
    }

    /// <summary>Desktop preview only: look up / down, for checking what a headset wearer sees there.</summary>
    public void TiltPreview(int direction)
    {
        if (_preview)
        {
            _previewPitch = Mathf.Clamp(_previewPitch + 15f * direction, -60f, 75f);
            Mod.Log.Msg($"VR: preview looks {Mathf.Abs(_previewPitch):0}° {(_previewPitch >= 0f ? "down" : "up")}");
        }
    }

    public void RequestDebugCapture() => _debugCaptureRequested = true;

    /// <summary>Straight ahead again: the headset's view and the right stick's camera angle.</summary>
    public void Recenter()
    {
        _recenterRequested = true;
        _firstPersonLook = Vector2.zero;
        _chaseLook = Vector2.zero;
    }

    /// <summary>R3: the right stick's camera angle (or, with the Screen camera, the screen size) back to default.</summary>
    public void ResetLook()
    {
        if (_screenRide)
        {
            if (Config.ScreenWidth.Value != Config.ScreenWidth.DefaultValue)
            {
                Config.ScreenWidth.ResetToDefault();
                Config.Save();
                Mod.Log.Msg($"VR: screen size reset ({Config.ScreenWidth.Value:0.0} m wide)");
            }
        }
        else if (_firstPersonLook != Vector2.zero || _chaseLook != Vector2.zero)
        {
            _firstPersonLook = Vector2.zero;
            _chaseLook = Vector2.zero;
            Mod.Log.Msg("VR: camera angle reset");
        }
    }

    /// <summary>
    /// Right stick, every frame: first person looks around, chase moves around the bike, the Screen
    /// camera grows or shrinks the screen. It all stays where it is left (R3 resets it).
    /// Stick right turns the view right, stick up looks up (or makes the screen bigger).
    /// </summary>
    public void Look(Vector2 input)
    {
        if (input == Vector2.zero || Time.timeScale <= 0f)
        {
            if (_screenResized)
            {
                _screenResized = false;
                Config.Save();
                Mod.Log.Msg($"VR: screen {Config.ScreenWidth.Value:0.0} m wide");
            }

            return;
        }

        // Gentle near the middle of the stick, fast at the edge.
        var dt = Time.unscaledDeltaTime;
        if (_screenRide)
        {
            // Up to twice / half the size per second, around the same center.
            if (input.y != 0f)
            {
                Config.ScreenWidth.Value = Mathf.Clamp(Config.ScreenWidth.Value * Mathf.Pow(2f, input.y * Mathf.Abs(input.y) * dt), 1f, 8f);
                _screenResized = true;
            }

            return;
        }

        if (_mode != Mode.Immersive)
        {
            return;
        }

        var yaw = input.x * Mathf.Abs(input.x) * LookYawSpeed * dt;
        var pitch = input.y * Mathf.Abs(input.y) * LookPitchSpeed * dt;
        switch (_activeCamera)
        {
            case VrCameraMode.FirstPerson:
                _firstPersonLook = new Vector2(WrapAngle(_firstPersonLook.x + yaw), Mathf.Clamp(_firstPersonLook.y + pitch, -80f, 50f));
                break;
            case VrCameraMode.Chase:
                _chaseLook = new Vector2(WrapAngle(_chaseLook.x + yaw), Mathf.Clamp(_chaseLook.y + pitch, ChaseCamera.MinLookPitch, ChaseCamera.MaxLookPitch));
                break;
        }
    }

    /// <summary>Next / previous camera while riding (diorama, chase, first person, screen).</summary>
    public void CycleCamera(int direction)
    {
        var index = Array.IndexOf(CameraCycle, Config.CameraMode.Value);
        var next = CameraCycle[(index + direction + CameraCycle.Length) % CameraCycle.Length];
        Config.CameraMode.Value = next;
        Config.Save();
        Mod.Log.Msg($"VR: camera -> {next}");
    }

    /// <summary>Tweaks the current camera: field of view (first person), chase distance, diorama zoom or screen depth.</summary>
    public void Adjust(int direction)
    {
        switch (Config.CameraMode.Value)
        {
            case VrCameraMode.FirstPerson:
                // Like the Alternative Camera mod's FoV, in 5° steps. Never narrower than the headset's own,
                // which would only magnify, so stepping down always ends at the true-to-life view.
                var current = Mathf.Max(Config.FirstPersonFov.Value, _headsetFov);
                var next = direction > 0
                    ? Mathf.Ceil((current + FovStep * 0.5f) / FovStep) * FovStep
                    : Mathf.Floor((current - FovStep * 0.5f) / FovStep) * FovStep;
                next = Mathf.Min(next, Mathf.Max(MaxFirstPersonFov, _headsetFov));
                Config.FirstPersonFov.Value = next < _headsetFov + FovStep * 0.5f ? 0f : next;
                Mod.Log.Msg(Config.FirstPersonFov.Value > 0f
                    ? $"VR: first person field of view {next:0}° (headset {_headsetFov:0}°)"
                    : $"VR: first person field of view: the headset's own ({_headsetFov:0}°)");
                break;
            case VrCameraMode.Chase:
                Config.ChaseDistance.Value = Mathf.Clamp(Config.ChaseDistance.Value - 0.5f * direction, 1f, 15f);
                Mod.Log.Msg($"VR: chase distance {Config.ChaseDistance.Value:0.0} m");
                break;
            case VrCameraMode.Diorama:
                // Steps of 1.25x, landing exactly on 1x (the game's framing) on the way through.
                var steps = Mathf.Round(Mathf.Log(Mathf.Max(0.01f, Config.DioramaZoom.Value)) / Mathf.Log(ZoomStep)) + direction;
                Config.DioramaZoom.Value = Mathf.Pow(ZoomStep, Mathf.Clamp(steps, -3f, 9f)); // 0.5x .. 7.5x
                Mod.Log.Msg($"VR: diorama zoom {Config.DioramaZoom.Value:0.##}x");
                break;
            case VrCameraMode.Screen:
                Config.ScreenDepth.Value = Mathf.Clamp01(Mathf.Round(Config.ScreenDepth.Value / DepthStep + direction) * DepthStep);
                Mod.Log.Msg(Config.ScreenDepth.Value > 0f ? $"VR: screen depth {Config.ScreenDepth.Value * 100f:0}% (3D)" : "VR: screen depth 0 (flat)");
                break;
        }

        Config.Save();
    }

    private static float WrapAngle(float degrees) => Mathf.Repeat(degrees + 180f, 360f) - 180f;
}
