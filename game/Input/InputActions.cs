using Dialoid.Game.Ui;
using Godot;

namespace Dialoid.Game.Input;

/// <summary>
/// The room's gamepad layout as data (plan 4 task 13, spec §4 «Геймпад»). The two choices still waiting for the owner live only
/// here: which windows sit on the D-pad (<see cref="DpadWindows"/>) and what the seed field takes from the gamepad
/// (<see cref="SeedFromGamepad"/>). Changing them changes the bindings, the ring order, the top-bar captions and the prompts.
/// </summary>
public static class GamepadLayout
{
    /// <summary>[Владельцу] The D-pad windows, in ring order: ← Пассивки, → Скиллы, ↑ Разбор, ↓ Измеритель.</summary>
    public static readonly IReadOnlyList<(JoyButton Button, string WindowId)> DpadWindows =
    [
        (JoyButton.DpadLeft, WindowIds.Passives),
        (JoyButton.DpadRight, WindowIds.Skills),
        (JoyButton.DpadUp, WindowIds.Breakdown),
        (JoyButton.DpadDown, WindowIds.Meter),
    ];

    /// <summary>The ring LB/RB walks in windows: the D-pad windows, then these (opened when closed), then any other open window.</summary>
    public static readonly IReadOnlyList<string> RingAfterDpad = [WindowIds.Character, WindowIds.Ascendancy];

    /// <summary>Windows opened from combat without taking the focus (the fight goes on, the buttons stay with it).</summary>
    public static readonly IReadOnlySet<string> OpenWithoutFocus = new HashSet<string> { WindowIds.Log, WindowIds.Meter };

    /// <summary>The slot buttons 1–5 (slot 5 is the right trigger).</summary>
    public static readonly IReadOnlyList<string> SlotGlyphs = ["X", "A", "B", "Y", "RT"];

    /// <summary>[Владельцу] What the seed field takes from the gamepad: ←/→ ±1 and X random (manual entry waits for an on-screen keyboard).</summary>
    public static readonly bool SeedFromGamepad = true;

    /// <summary>The D-pad button of a window, or null.</summary>
    public static JoyButton? DpadOf(string windowId)
    {
        foreach (var (button, id) in DpadWindows)
            if (id == windowId) return button;
        return null;
    }

    /// <summary>The fixed part of the ring: the D-pad windows then <see cref="RingAfterDpad"/>.</summary>
    public static IEnumerable<string> FixedRing => DpadWindows.Select(d => d.WindowId).Concat(RingAfterDpad);
}

/// <summary>
/// The InputMap actions of the room. Every game action has a keyboard (or mouse) binding and a gamepad binding; the code reads
/// actions, not keys. <see cref="Register"/> builds them at start (the D-pad windows come from <see cref="GamepadLayout"/>, the window
/// keys from <see cref="WindowHost.Specs"/>), and binds the built-in <c>ui_*</c> actions explicitly. Gamepad bindings answer any
/// device. Analog thresholds: the left stick has its own round dead zone (<see cref="StickDeadzone"/>) in the fight, the right
/// trigger presses at <see cref="TriggerPress"/> and lets go at <see cref="TriggerRelease"/>.
/// </summary>
public static class InputActions
{
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string MoveUp = "move_up";
    public const string MoveDown = "move_down";
    public const string ApproachTarget = "approach_target";
    public const string NextTarget = "next_target";
    public const string ZoomIn = "zoom_in";
    public const string ZoomOut = "zoom_out";
    public const string ZoomReset = "zoom_reset";
    public const string WindowClose = "window_close";

    /// <summary>Gamepad-only actions of the windows (the mouse has its own way: clicks, the top bar, the title's «×»).</summary>
    public const string PadMenu = "pad_menu";
    public const string PadView = "pad_view";
    public const string PadPrevWindow = "pad_prev_window";
    public const string PadNextWindow = "pad_next_window";
    public const string PadAlt = "pad_alt";
    public const string PadExtra = "pad_extra";
    public const string PadTriggerLeft = "pad_trigger_left";
    public const string PadTriggerRight = "pad_trigger_right";
    public const string PadCenter = "pad_center";

    public const float StickDeadzone = 0.2f;
    public const float StickRun = 0.6f;
    public const float TriggerPress = 0.5f;
    public const float TriggerRelease = 0.3f;
    public const float NavThreshold = 0.5f;

    public static string Slot(int slot) => "slot_" + slot;

    public static string Window(string windowId) => "window_" + windowId;

    /// <summary>The fight's actions (each has a key and a gamepad button).</summary>
    public static IEnumerable<string> GameActions =>
        new[] { MoveLeft, MoveRight, MoveUp, MoveDown, ApproachTarget, NextTarget, ZoomIn, ZoomOut, ZoomReset }
            .Concat(Enumerable.Range(1, 5).Select(Slot));

    public static bool Registered { get; private set; }

    /// <summary>Builds every action (idempotent: an action is rebuilt from scratch).</summary>
    public static void Register()
    {
        Add(MoveLeft, 0.05f, KeyEv(Key.A), Axis(JoyAxis.LeftX, -1));
        Add(MoveRight, 0.05f, KeyEv(Key.D), Axis(JoyAxis.LeftX, 1));
        Add(MoveUp, 0.05f, KeyEv(Key.W), Axis(JoyAxis.LeftY, -1));
        Add(MoveDown, 0.05f, KeyEv(Key.S), Axis(JoyAxis.LeftY, 1));
        for (int i = 1; i <= 4; i++) Add(Slot(i), 0.5f, KeyEv(Key.Key1 + (i - 1)), Joy(SlotButton(i)!.Value));
        Add(Slot(5), TriggerRelease, KeyEv(Key.Key5), Axis(JoyAxis.TriggerRight, 1));
        Add(ApproachTarget, 0.5f, KeyEv(Key.F), Joy(JoyButton.LeftShoulder));
        Add(NextTarget, 0.5f, KeyEv(Key.Tab), Joy(JoyButton.RightShoulder));
        Add(ZoomIn, StickDeadzone, Mouse(MouseButton.WheelUp), Axis(JoyAxis.RightY, -1));
        Add(ZoomOut, StickDeadzone, Mouse(MouseButton.WheelDown), Axis(JoyAxis.RightY, 1));
        Add(ZoomReset, 0.5f, Mouse(MouseButton.Middle), Joy(JoyButton.RightStick));
        foreach (var spec in WindowHost.Specs)
        {
            var events = new List<InputEvent> { KeyEv(spec.Key) };
            if (GamepadLayout.DpadOf(spec.Id) is { } button) events.Add(Joy(button));
            Add(Window(spec.Id), 0.5f, events.ToArray());
        }
        Add(WindowClose, 0.5f, KeyEv(Key.Escape), Joy(JoyButton.B));

        Add(PadMenu, 0.5f, Joy(JoyButton.Start));
        Add(PadView, 0.5f, Joy(JoyButton.Back));
        Add(PadPrevWindow, 0.5f, Joy(JoyButton.LeftShoulder));
        Add(PadNextWindow, 0.5f, Joy(JoyButton.RightShoulder));
        Add(PadAlt, 0.5f, Joy(JoyButton.X));
        Add(PadExtra, 0.5f, Joy(JoyButton.Y));
        Add(PadTriggerLeft, TriggerRelease, Axis(JoyAxis.TriggerLeft, 1));
        Add(PadTriggerRight, TriggerRelease, Axis(JoyAxis.TriggerRight, 1));
        Add(PadCenter, 0.5f, Joy(JoyButton.RightStick));

        // The built-in GUI actions, bound explicitly: arrows / D-pad / left stick move the focus, Enter / Space / A press, Esc / B go back.
        Add("ui_up", NavThreshold, Keycode(Key.Up), Joy(JoyButton.DpadUp), Axis(JoyAxis.LeftY, -1));
        Add("ui_down", NavThreshold, Keycode(Key.Down), Joy(JoyButton.DpadDown), Axis(JoyAxis.LeftY, 1));
        Add("ui_left", NavThreshold, Keycode(Key.Left), Joy(JoyButton.DpadLeft), Axis(JoyAxis.LeftX, -1));
        Add("ui_right", NavThreshold, Keycode(Key.Right), Joy(JoyButton.DpadRight), Axis(JoyAxis.LeftX, 1));
        Add("ui_accept", 0.5f, Keycode(Key.Enter), Keycode(Key.KpEnter), Keycode(Key.Space), Joy(JoyButton.A));
        Add("ui_cancel", 0.5f, Keycode(Key.Escape), Joy(JoyButton.B));
        Registered = true;
    }

    /// <summary>The face button of slots 1–4 (slot 5 is the right trigger, an axis).</summary>
    public static JoyButton? SlotButton(int slot) => slot switch
    {
        1 => JoyButton.X,
        2 => JoyButton.A,
        3 => JoyButton.B,
        4 => JoyButton.Y,
        _ => null,
    };

    /// <summary>The action has a keyboard or mouse binding.</summary>
    public static bool HasKeyboard(string action) =>
        InputMap.HasAction(action) && InputMap.ActionGetEvents(action).Any(e => e is InputEventKey or InputEventMouseButton);

    /// <summary>The action has a gamepad binding.</summary>
    public static bool HasGamepad(string action) =>
        InputMap.HasAction(action) && InputMap.ActionGetEvents(action).Any(e => e is InputEventJoypadButton or InputEventJoypadMotion);

    private static void Add(string action, float deadzone, params InputEvent[] events)
    {
        if (InputMap.HasAction(action)) InputMap.EraseAction(action);
        InputMap.AddAction(action, deadzone);
        foreach (var e in events) InputMap.ActionAddEvent(action, e);
    }

    private static InputEventKey KeyEv(Key key) => new() { PhysicalKeycode = key, Device = -1 };
    private static InputEventKey Keycode(Key key) => new() { Keycode = key, Device = -1 };
    private static InputEventJoypadButton Joy(JoyButton button) => new() { ButtonIndex = button, Device = -1 };
    private static InputEventJoypadMotion Axis(JoyAxis axis, float sign) => new() { Axis = axis, AxisValue = sign, Device = -1 };
    private static InputEventMouseButton Mouse(MouseButton button) => new() { ButtonIndex = button, Device = -1 };
}
