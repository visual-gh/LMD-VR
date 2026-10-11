using System.Runtime.InteropServices;
using UnityEngine;

namespace LMDVR.Controls;

/// <summary>
/// Reads Xbox-style controllers straight from XInput: works whether or not the game window has
/// focus (it usually doesn't while the headset is on) and sees buttons the game ignores.
/// </summary>
internal static class Gamepad
{
    public const ushort DpadUp = 0x0001;
    public const ushort DpadDown = 0x0002;
    public const ushort DpadLeft = 0x0004;
    public const ushort DpadRight = 0x0008;
    public const ushort LeftThumb = 0x0040;
    public const ushort RightThumb = 0x0080;
    public const ushort LeftShoulder = 0x0100;
    public const ushort RightShoulder = 0x0200;

    private const float StickDeadzone = 0.24f;

    private static ushort _previous;
    private static ushort _current;
    private static Vector2 _rightStick;
    private static bool _unavailable;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    /// <summary>Right stick, -1..1 per axis (right and up positive), with a round dead zone.</summary>
    public static Vector2 RightStick => _rightStick;

    /// <summary>Call once per frame. Merges all connected controllers.</summary>
    public static void Poll()
    {
        _previous = _current;
        _current = 0;
        _rightStick = Vector2.zero;
        if (_unavailable)
        {
            return;
        }

        var stick = Vector2.zero;
        try
        {
            for (uint i = 0; i < 4; i++)
            {
                if (XInputGetState(i, out var state) == 0)
                {
                    _current |= state.Buttons;
                    var s = new Vector2(state.ThumbRX, state.ThumbRY) / 32767f;
                    if (s.sqrMagnitude > stick.sqrMagnitude)
                    {
                        stick = s;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
            Mod.Log.Warning("VR: XInput not available, gamepad shortcuts disabled");
        }

        var magnitude = Mathf.Min(1f, stick.magnitude);
        if (magnitude > StickDeadzone)
        {
            _rightStick = stick.normalized * ((magnitude - StickDeadzone) / (1f - StickDeadzone));
        }
    }

    public static bool Held(ushort buttons) => (_current & buttons) == buttons;

    /// <summary>True on the frame a button combination becomes complete.</summary>
    public static bool BecameHeld(ushort buttons) => Held(buttons) && (_previous & buttons) != buttons;

    public static bool Pressed(ushort button) => (_current & button) != 0 && (_previous & button) == 0;
}
