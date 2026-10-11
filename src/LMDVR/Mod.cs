using LMDVR.Controls;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(LMDVR.Mod), "LMD VR", LMDVR.Mod.Version, "LMD VR")]
[assembly: MelonGame("Megagon Industries", "Lonely Mountains: Downhill")]

namespace LMDVR;

public class Mod : MelonMod
{
    public const string Version = "1.0.0";

    internal static MelonLogger.Instance Log { get; private set; }

    private VrManager _vr;
    private bool _vrInitialized;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Config.Init();
        _vr = new VrManager();
        Log.Msg("Gamepad: LB+RB + D-pad left/right = camera, up/down = adjust it; right stick = camera angle / screen size, " +
                "R3 = reset that, L3+R3 = reset view. Keyboard: F5 camera, +/- adjust, numpad angle, F10 reset view, " +
                "F9 VR on/off, F11 reload settings. Settings: UserData\\LMDVR.cfg");
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        Log.Msg($"Scene initialized: [{buildIndex}] {sceneName}");

        // The graphics device and coroutine runner are ready once the first scene is up.
        if (!_vrInitialized)
        {
            _vrInitialized = true;
            _vr.Initialize();
        }
    }

    public override void OnUpdate()
    {
        Gamepad.Poll();
        _vr.OnUpdate();
        HandleGamepad();
        HandleKeyboard();
        _vr.Look(Vector2.ClampMagnitude(Gamepad.RightStick + KeyboardLook(), 1f));
    }

    public override void OnApplicationQuit()
    {
        _vr.OnApplicationQuit();
    }

    /// <summary>
    /// The base game ignores LB/RB, the right stick and stick clicks while riding: LB+RB is the VR modifier,
    /// the right stick turns the camera, R3 resets that angle and clicking both sticks resets the whole view.
    /// </summary>
    private void HandleGamepad()
    {
        if (Gamepad.Held(Gamepad.LeftShoulder | Gamepad.RightShoulder))
        {
            if (Gamepad.Pressed(Gamepad.DpadRight))
            {
                _vr.CycleCamera(1);
            }
            else if (Gamepad.Pressed(Gamepad.DpadLeft))
            {
                _vr.CycleCamera(-1);
            }
            else if (Gamepad.Pressed(Gamepad.DpadUp))
            {
                _vr.Adjust(1);
            }
            else if (Gamepad.Pressed(Gamepad.DpadDown))
            {
                _vr.Adjust(-1);
            }
        }

        if (Gamepad.BecameHeld(Gamepad.LeftThumb | Gamepad.RightThumb))
        {
            _vr.Recenter();
        }
        else if (Gamepad.Pressed(Gamepad.RightThumb))
        {
            _vr.ResetLook();
        }
    }

    /// <summary>Numpad 4/6/8/2 turn the camera like the right stick.</summary>
    private static Vector2 KeyboardLook()
    {
        var look = Vector2.zero;
        if (Input.GetKey(KeyCode.Keypad4))
        {
            look.x -= 1f;
        }

        if (Input.GetKey(KeyCode.Keypad6))
        {
            look.x += 1f;
        }

        if (Input.GetKey(KeyCode.Keypad8))
        {
            look.y += 1f;
        }

        if (Input.GetKey(KeyCode.Keypad2))
        {
            look.y -= 1f;
        }

        return look;
    }

    private void HandleKeyboard()
    {
        if (Input.GetKeyDown(KeyCode.F9))
        {
            _vr.Toggle();
        }

        if (Input.GetKeyDown(KeyCode.F10))
        {
            _vr.Recenter();
        }

        if (Input.GetKeyDown(KeyCode.Keypad5))
        {
            _vr.ResetLook();
        }

        if (Input.GetKeyDown(KeyCode.F11))
        {
            Config.Reload();
            Log.Msg("VR: settings reloaded from LMDVR.cfg");
        }

        if (Input.GetKeyDown(KeyCode.F5))
        {
            _vr.CycleCamera(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
        }

        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            _vr.Adjust(1);
        }

        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            _vr.Adjust(-1);
        }

        if (Input.GetKeyDown(KeyCode.F6))
        {
            _vr.TogglePreview();
        }

        if (Input.GetKeyDown(KeyCode.PageDown))
        {
            _vr.TiltPreview(1);
        }

        if (Input.GetKeyDown(KeyCode.PageUp))
        {
            _vr.TiltPreview(-1);
        }

        if (Input.GetKeyDown(KeyCode.F7))
        {
            _vr.RequestDebugCapture();
        }
    }
}
