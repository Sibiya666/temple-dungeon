using System.Runtime.CompilerServices;
using Dialoid.Game.Input;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>A gamepad command a focused control or a window may take (directions, A, X, Y, triggers, R3).</summary>
public enum PadCommand { Up, Down, Left, Right, Accept, Alt, Extra, TriggerLeft, TriggerRight, Center }

/// <summary>
/// A control that handles the gamepad itself while it has the focus (the passive tree picks nodes, the meter's bars pick a row).
/// A direction it does not take (no node that way, the last row) moves the focus out as usual.
/// </summary>
public interface IPadControl
{
    /// <summary>One command; true when the control used it.</summary>
    bool PadInput(PadCommand command);

    /// <summary>The right stick, every frame while focused (scaled -1..1); true when the control used it (else the window scrolls).</summary>
    bool PadStick(Vector2 value, double delta);

    /// <summary>The hover text of the focused part (the tree's selected node, the bar's row).</summary>
    string PadTooltip();

    /// <summary>The prompts the control adds to the window's bar («[A] Взять», «[X] Атрибут»).</summary>
    IEnumerable<(string Button, string Label)> PadPrompts();

    /// <summary>The device switched: a pad selection is dropped on the mouse.</summary>
    void PadModeChanged(bool gamepad);
}

/// <summary>A window panel with its own gamepad commands: X and Y without a control action, B before the window closes.</summary>
public interface IPadPanel
{
    /// <summary>X or Y (or a trigger) the focused control did not take; true when used.</summary>
    bool PadButton(PadCommand command, Control? focus);

    /// <summary>B: one step back inside the window (a confirmation, the breakdown history); false closes the window.</summary>
    bool PadBack();

    /// <summary>The window's own prompts («[Y] Сбросить»).</summary>
    IEnumerable<(string Button, string Label)> PadPrompts(Control? focus);

    /// <summary>The window closed (a back trail is dropped).</summary>
    void PadClosed();
}

/// <summary>
/// Button prompts and the gamepad's small per-control hooks (plan 4 task 13). Glyphs are text («[A]») until the Kenney Input
/// Prompts set (CC0) is added. <see cref="SetAction"/> gives a control an X or Y action with its prompt (the skill tree's «−»,
/// the seed's «Случайный сид»); <c>pad_skip</c> keeps a control out of the gamepad's focus chain (its action is on another
/// button), <c>pad_info</c> puts a label with a tooltip into it, <c>pad_edit</c> gives a number field an edit mode (A enters,
/// A saves, B reverts) where ←/→ could not leave it sideways.
/// </summary>
public static class ButtonPrompts
{
    public const string SkipMeta = "pad_skip";
    public const string InfoMeta = "pad_info";
    public const string EditMeta = "pad_edit";
    public const string KeyboardFocusMeta = "pad_kb_focus";
    public const string AcceptMeta = "pad_accept";

    private static readonly ConditionalWeakTable<Control, Dictionary<PadCommand, (string Label, Action Run)>> Actions = new();

    /// <summary>A gamepad button as text: «[A]», «[RT]», «[←]».</summary>
    public static string Glyph(JoyButton button) => "[" + Name(button) + "]";

    public static string Name(JoyButton button) => button switch
    {
        JoyButton.A => "A",
        JoyButton.B => "B",
        JoyButton.X => "X",
        JoyButton.Y => "Y",
        JoyButton.Back => "View",
        JoyButton.Start => "Menu",
        JoyButton.LeftShoulder => "LB",
        JoyButton.RightShoulder => "RB",
        JoyButton.LeftStick => "L3",
        JoyButton.RightStick => "R3",
        JoyButton.DpadLeft => "←",
        JoyButton.DpadRight => "→",
        JoyButton.DpadUp => "↑",
        JoyButton.DpadDown => "↓",
        _ => button.ToString(),
    };

    /// <summary>The slot's button glyph: «[X]» … «[RT]».</summary>
    public static string SlotGlyph(int slot) => slot >= 1 && slot <= GamepadLayout.SlotGlyphs.Count ? "[" + GamepadLayout.SlotGlyphs[slot - 1] + "]" : "";

    public static string Glyph(PadCommand command) => command switch
    {
        PadCommand.Accept => "[A]",
        PadCommand.Alt => "[X]",
        PadCommand.Extra => "[Y]",
        PadCommand.TriggerLeft => "[LT]",
        PadCommand.TriggerRight => "[RT]",
        PadCommand.Center => "[R3]",
        PadCommand.Left or PadCommand.Right => "[←/→]",
        _ => "[↑/↓]",
    };

    /// <summary>Gives <paramref name="control"/> an action on X or Y (or a trigger) and its prompt label.</summary>
    public static void SetAction(Control control, PadCommand command, string label, Action run) =>
        Actions.GetOrCreateValue(control)[command] = (label, run);

    public static bool TryAction(Control? control, PadCommand command)
    {
        if (control == null || !Actions.TryGetValue(control, out var map) || !map.TryGetValue(command, out var action)) return false;
        action.Run();
        return true;
    }

    public static IEnumerable<(PadCommand Command, string Label)> ActionsOf(Control? control) =>
        control != null && Actions.TryGetValue(control, out var map) ? map.Select(kv => (kv.Key, kv.Value.Label)) : [];

    /// <summary>
    /// A seed field on the gamepad ([Владельцу] <see cref="GamepadLayout.SeedFromGamepad"/>): ←/→ ±1 (the window host steps every
    /// number field), X a random seed (Y stays the window's: «Сбросить» in the meter). Typing a long seed waits for an on-screen keyboard.
    /// </summary>
    public static void SeedPad(SpinBox seed)
    {
        if (!GamepadLayout.SeedFromGamepad) return;
        var edit = seed.GetLineEdit();
        SetAction(edit, PadCommand.Alt, "Случайный сид", () => seed.Value = Random.Shared.Next((int)Math.Max(1, seed.MinValue), (int)seed.MaxValue + 1));
    }

    /// <summary>Marks a control out of the gamepad's focus chain.</summary>
    public static T Skip<T>(T control) where T : Control
    {
        control.SetMeta(SkipMeta, true);
        return control;
    }

    /// <summary>Puts a label with a tooltip into the gamepad's focus chain (its tooltip shows on focus).</summary>
    public static T Info<T>(T control) where T : Control
    {
        control.SetMeta(InfoMeta, true);
        return control;
    }

    /// <summary>Gives a number field the edit mode (A enters, A saves, B reverts).</summary>
    public static SpinBox EditMode(SpinBox spin)
    {
        spin.SetMeta(EditMeta, true);
        return spin;
    }

    /// <summary>A control the gamepad can focus: buttons, sliders and number fields (their text field), trees, the pad controls and info labels.</summary>
    public static bool Focusable(Control c) =>
        !c.HasMeta(SkipMeta) &&
        (c is BaseButton or Slider or LineEdit or TextEdit or Tree or ItemList or IPadControl || c.HasMeta(InfoMeta));

    /// <summary>
    /// Sets the focus modes of everything under <paramref name="root"/> for the device: on the gamepad every focusable control
    /// takes the focus (its keyboard mode is kept in metadata), a skipped one never does, every scroll follows the focus; on the
    /// keyboard and mouse the kept modes come back (the log and the meter stay focus-free, as task 11 made them).
    /// </summary>
    public static void ApplyFocusModes(Node root, bool gamepad)
    {
        foreach (var node in Walk(root))
        {
            if (node is ScrollContainer scroll && gamepad) scroll.FollowFocus = true;
            if (node is not Control c) continue;
            Control target = c is SpinBox spin ? spin.GetLineEdit() : c;
            var wanted = c.HasMeta(SkipMeta) ? Control.FocusModeEnum.None
                : Focusable(c) || c is SpinBox ? Control.FocusModeEnum.All
                : (Control.FocusModeEnum?)null;
            if (wanted is not { } mode) continue;
            if (gamepad)
            {
                if (!target.HasMeta(KeyboardFocusMeta)) target.SetMeta(KeyboardFocusMeta, (int)target.FocusMode);
                target.FocusMode = mode;
            }
            else if (target.HasMeta(KeyboardFocusMeta))
            {
                target.FocusMode = (Control.FocusModeEnum)target.GetMeta(KeyboardFocusMeta).AsInt32();
                target.RemoveMeta(KeyboardFocusMeta);
            }
        }
    }

    /// <summary>The node and its descendants (not internal children).</summary>
    public static IEnumerable<Node> Walk(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            for (int i = n.GetChildCount() - 1; i >= 0; i--) stack.Push(n.GetChild(i));
        }
    }

    /// <summary>The focus targets under <paramref name="root"/> in tree order: visible, enabled, focusable now (a number field by its text field).</summary>
    public static List<Control> Targets(Node root)
    {
        var list = new List<Control>();
        foreach (var node in Walk(root))
        {
            if (node is not Control c || !c.IsVisibleInTree()) continue;
            if (c is SpinBox spin)
            {
                var edit = spin.GetLineEdit();
                if (edit.FocusMode != Control.FocusModeEnum.None && edit.Editable) list.Add(edit);
                continue;
            }
            if (!Focusable(c) || c.FocusMode == Control.FocusModeEnum.None) continue;
            if (c is BaseButton { Disabled: true }) continue;
            if (c.GetParent() is SpinBox) continue;
            list.Add(c);
        }
        return list;
    }

    /// <summary>The prompt line text: «[A] Взять  ·  [X] Атрибут  ·  [B] Назад».</summary>
    public static string Line(IEnumerable<(string Button, string Label)> prompts) =>
        string.Join("   ", prompts.Where(p => p.Label.Length > 0).Select(p => p.Button + " " + p.Label));

    /// <summary>What A does on a control (the first prompt of the bar).</summary>
    public static string AcceptLabel(Control? focus) => focus != null && focus.HasMeta(AcceptMeta) ? focus.GetMeta(AcceptMeta).AsString() : focus switch
    {
        null => "",
        NumberLabel => "Разбор",
        CheckBox or CheckButton => "Переключить",
        OptionButton or MenuButton => "Список",
        Tree => "Выбрать",
        BaseButton => "Нажать",
        _ => "",
    };
}
