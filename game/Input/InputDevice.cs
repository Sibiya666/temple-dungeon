using Godot;

namespace Dialoid.Game.Input;

/// <summary>The device the owner used last.</summary>
public enum InputKind { KeyboardMouse, Gamepad }

/// <summary>
/// Autoload (plan 4 task 13, spec §4 «Переключение устройства на лету»): which device is active now and the switch between them.
/// It watches every event in <see cref="_Input"/> and takes none. A gamepad button press or a stick or trigger past
/// <see cref="StickSwitch"/> makes the gamepad active; a key press, a mouse button or the mouse moved more than
/// <see cref="MouseSwitchPixels"/> within <see cref="MouseWindowSec"/> makes the keyboard and mouse active. Stick drift and a
/// touched mouse do not switch. In gamepad mode the mouse cursor is hidden. A gamepad disconnecting switches back to the
/// keyboard and mouse and raises <see cref="GamepadLost"/> (the fight lets go of its slots, channel and movement) with the
/// message «Геймпад отключен»; connecting says «Геймпад подключен» and switches on the first press. The device number does not matter.
/// </summary>
public partial class InputDevice : Node
{
    public const float StickSwitch = 0.35f;
    public const float MouseSwitchPixels = 4f;
    public const double MouseWindowSec = 0.25;
    public const string DisconnectedText = "Геймпад отключен";
    public const string ConnectedText = "Геймпад подключен";

    private float _mouseTravel;
    private ulong _mouseTravelStart;

    /// <summary>The autoload (null before it entered the tree, e.g. in a plain C# test).</summary>
    public static InputDevice? Instance { get; private set; }

    /// <summary>The active device (keyboard and mouse at start).</summary>
    public InputKind Active { get; private set; } = InputKind.KeyboardMouse;

    /// <summary>The active device is the gamepad (false without the autoload).</summary>
    public static bool Gamepad => Instance?.Active == InputKind.Gamepad;

    /// <summary>The active device changed.</summary>
    public event Action<InputKind>? Changed;

    /// <summary>The gamepad disconnected: everything it held must be let go.</summary>
    public event Action? GamepadLost;

    /// <summary>A short message for the owner («Геймпад отключен»).</summary>
    public event Action<string>? Message;

    /// <summary>The last message shown, or null.</summary>
    public string? LastMessage { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
        InputActions.Register();
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready() => Godot.Input.JoyConnectionChanged += OnJoyConnectionChanged;

    public override void _Input(InputEvent e) => Observe(e);

    /// <summary>Looks at one event and switches the device when it says so; takes nothing. Called again harmlessly by the window
    /// host, which sees events before the room does (so the switch happens before the event is used).</summary>
    public void Observe(InputEvent e)
    {
        switch (e)
        {
            case InputEventJoypadButton { Pressed: true }:
                Set(InputKind.Gamepad);
                break;
            case InputEventJoypadMotion m when Mathf.Abs(m.AxisValue) > StickSwitch:
                Set(InputKind.Gamepad);
                break;
            case InputEventKey { Pressed: true }:
            case InputEventMouseButton { Pressed: true }:
                Set(InputKind.KeyboardMouse);
                break;
            case InputEventMouseMotion motion when Active == InputKind.Gamepad:
            {
                var now = Time.GetTicksMsec();
                if (now - _mouseTravelStart > (ulong)(MouseWindowSec * 1000))
                {
                    _mouseTravelStart = now;
                    _mouseTravel = 0;
                }
                _mouseTravel += motion.Relative.Length();
                if (_mouseTravel > MouseSwitchPixels) Set(InputKind.KeyboardMouse);
                break;
            }
        }
    }

    /// <summary>Makes <paramref name="kind"/> active (no-op when it already is).</summary>
    public void Set(InputKind kind)
    {
        if (Active == kind) return;
        Active = kind;
        _mouseTravel = 0;
        _mouseTravelStart = Time.GetTicksMsec();
        Godot.Input.MouseMode = kind == InputKind.Gamepad ? Godot.Input.MouseModeEnum.Hidden : Godot.Input.MouseModeEnum.Visible;
        Changed?.Invoke(kind);
    }

    /// <summary>Input.JoyConnectionChanged (public for the self-test).</summary>
    public void OnJoyConnectionChanged(int device, bool connected) => OnJoyConnectionChanged((long)device, connected);

    public void OnJoyConnectionChanged(long device, bool connected)
    {
        if (connected)
        {
            Say(ConnectedText);
            return;
        }
        GamepadLost?.Invoke();
        Set(InputKind.KeyboardMouse);
        Say(DisconnectedText);
    }

    private void Say(string text)
    {
        LastMessage = text;
        Message?.Invoke(text);
    }
}
