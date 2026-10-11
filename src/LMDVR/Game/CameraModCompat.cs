using System.Reflection;
using MelonLoader;

namespace LMDVR.Game;

/// <summary>
/// Pauses the Alternative Camera (+ Photo Mode) mod while the headset is on. LMD VR has its own VR
/// cameras, and that mod's gamepad buttons (L3 toggles its camera, R3 snaps it) would otherwise keep
/// switching the game camera the Diorama view looks through. Before pausing, it is put back on the
/// game's original camera, so VR shows the developers' view; it resumes when VR stops.
/// Everything goes through reflection and fails soft if that mod changes.
/// </summary>
internal static class CameraModCompat
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static object _state;
    private static object _camera;
    private static object _hud;
    private static bool _resolved;
    private static bool _paused;

    public static void Pause()
    {
        if (_paused || !Resolve())
        {
            return;
        }

        try
        {
            var suspended = (bool)_state.GetType().GetProperty("Suspended", Instance)!.GetValue(_state)!;
            if (suspended)
            {
                return; // it disabled itself (config error, conflicting mod): leave it alone
            }

            var cameraType = _camera.GetType();
            var mode = cameraType.GetProperty("Mode", Instance)?.GetValue(_camera);
            if (mode?.ToString() == "PhotoCam" && _hud != null)
            {
                // Leaving photo mode restores the time scale it froze.
                cameraType.GetMethod("TogglePhotoMode", Instance)?.Invoke(_camera, new[] { _hud, false, false });
            }

            var select = cameraType.GetMethod("SelectCameraMode", Instance);
            if (select != null)
            {
                var original = Enum.Parse(select.GetParameters()[0].ParameterType, "Original");
                select.Invoke(_camera, new[] { original });
            }

            _state.GetType().GetMethod("SuspendOperation", Instance)!.Invoke(_state, null);
            _paused = true;
            Mod.Log.Msg("VR: Alternative Camera mod paused while the headset is on (back on the game's camera; its L3/R3 buttons are inactive)");
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"VR: could not pause the Alternative Camera mod: {ex.GetBaseException().Message}");
        }
    }

    public static void Resume()
    {
        if (!_paused)
        {
            return;
        }

        _paused = false;
        try
        {
            _state.GetType().GetMethod("ResumeOperation", Instance)!.Invoke(_state, null);
            Mod.Log.Msg("VR: Alternative Camera mod resumed");
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"VR: could not resume the Alternative Camera mod: {ex.GetBaseException().Message}");
        }
    }

    private static bool Resolve()
    {
        if (_resolved)
        {
            return _state != null && _camera != null;
        }

        _resolved = true;
        var melon = MelonBase.RegisteredMelons.FirstOrDefault(m => m.Info.Name.StartsWith("Alternative Camera", StringComparison.OrdinalIgnoreCase));
        if (melon == null)
        {
            return false;
        }

        var type = melon.GetType();
        _state = type.GetField("_state", Instance)?.GetValue(melon);
        _camera = type.GetField("_camera", Instance)?.GetValue(melon);
        _hud = type.GetField("_hud", Instance)?.GetValue(melon);
        if (_state == null || _camera == null)
        {
            Mod.Log.Warning($"VR: found '{melon.Info.Name}' but not its camera state; it will keep running in VR (its L3/R3 buttons change the game camera)");
            return false;
        }

        return true;
    }
}
