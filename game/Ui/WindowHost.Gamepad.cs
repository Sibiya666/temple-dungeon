using Dialoid.Game.Input;
using Dialoid.Core.Explain;
using Godot;
using Range = Godot.Range;

namespace Dialoid.Game.Ui;

/// <summary>
/// The windows on the gamepad (plan 4 task 13, spec §4 «Геймпад»). The focus is either with the fight (<see cref="FocusedWindow"/>
/// null: no control holds the GUI focus, the gamepad's buttons go to the fight) or with one window or the top bar (every gamepad
/// button belongs to it; the fight's hero stands, the core keeps running).
/// <para>From the fight: a D-pad button opens its window (<see cref="GamepadLayout.DpadWindows"/>; with the focus, except the log and
/// the meter) or closes it when it is open without the focus; View gives the focus to the window focused last (else the top one);
/// Menu (Start) focuses the top bar. In a window: the D-pad and the left stick move the focus (repeat after 0.35 s, then every
/// 0.08 s; the nearest control that way, rows first, wrapping at the bottom), A presses (exactly as a click), B goes one step back
/// and at the top level closes the window (the focus goes back to the fight), LB/RB walk the ring (<see cref="Ring"/>), View gives
/// the focus back to the fight leaving the window open, the right stick scrolls, X/Y/LT/RT/R3 go to the focused control or the panel
/// (<see cref="IPadControl"/>, <see cref="ButtonPrompts.SetAction"/>, <see cref="IPadPanel"/>). Number fields: ←/→ step (faster when
/// held), LT/RT ±10; a field marked <see cref="ButtonPrompts.EditMeta"/> is edited between two A presses (B reverts).</para>
/// <para>The gamepad's events are taken here in <see cref="_Input"/>, before the room's viewport sees them; keyboard and mouse
/// handling is unchanged. On the switch to the mouse the focus is released (except a text field being typed into) and each window
/// keeps its last focused control itself; back on the gamepad the last window gets it again.</para>
/// </summary>
public partial class WindowHost
{
    /// <summary>The id <see cref="FocusedWindow"/> takes while the top bar has the focus.</summary>
    public const string TopBarId = "topbar";

    public const double RepeatDelay = 0.35;
    public const double RepeatEvery = 0.08;

    /// <summary>Repeats after which a held ←/→ on a number field steps by 10.</summary>
    public const int FastAfterRepeats = 10;

    /// <summary>Pixels per second the right stick scrolls at full tilt.</summary>
    public const float ScrollSpeed = 900f;

    public static readonly Color FocusedBorder = new(0.86f, 0.66f, 0.36f, 1f);

    private readonly Dictionary<string, (int[] Chain, Vector2 Center)> _remembered = new();
    private readonly Dictionary<PadCommand, bool> _stick = new();
    private string? _lastFocused;
    private string? _beforeMouse;
    private string? _beforeBar;
    private PadCommand? _held;
    private double _repeatLeft;
    private int _repeats;
    private bool _ltDown, _rtDown;
    private Vector2 _rightStick;
    private SpinBox? _editing;
    private bool _acceptPassed;
    private double _editValue, _editRevert;
    private SharedTooltip _tip = null!;
    private Label _barPrompt = null!;

    /// <summary>The top bar (Menu focuses it).</summary>
    public TopBar? Bar { get; set; }

    /// <summary>The window holding the gamepad focus, <see cref="TopBarId"/>, or null while the fight has it.</summary>
    public string? FocusedWindow { get; private set; }

    /// <summary>A window (or the top bar) has the gamepad focus: the fight takes no buttons.</summary>
    public bool WindowHasFocus => FocusedWindow != null;

    /// <summary>The field in edit mode, or null.</summary>
    public SpinBox? EditingField => _editing;

    /// <summary>The focus tooltip shown now, or null.</summary>
    public string? FocusTooltip => _tip.Shown;
    public Rect2 FocusTooltipRect => _tip.GetGlobalRect();

    /// <summary>The control under the mouse whose tooltip shows now (the same panel as the focus tooltip), or null.</summary>
    public Control? HoverTipControl => InputDevice.Gamepad ? null : _tip.HoverControl;

    /// <summary>The prompt row under the top bar while Menu gave it the focus, or null.</summary>
    public string? BarPromptText => _barPrompt.Visible ? _barPrompt.Text : null;

    /// <summary>The focus moved between the fight, a window and the top bar.</summary>
    public event Action? FocusChanged;

    /// <summary>The remembered focus target of each window (for the self-test).</summary>
    public bool Remembers(string windowId) => _remembered.ContainsKey(windowId);

    private void SetupPad()
    {
        _tip = new SharedTooltip();
        AddChild(_tip);
        _barPrompt = new Label { Name = "BarPrompts", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _barPrompt.AddThemeFontSizeOverride("font_size", 14);
        _barPrompt.AddThemeColorOverride("font_color", new Color(0.93f, 0.85f, 0.7f));
        var barStyle = new StyleBoxFlat { BgColor = TitleBackground, BorderColor = FocusedBorder, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 3, ContentMarginBottom = 4 };
        barStyle.SetBorderWidthAll(1);
        _barPrompt.AddThemeStyleboxOverride("normal", barStyle);
        AddChild(_barPrompt);
        GetViewport().GuiFocusChanged += OnGuiFocusChanged;
        GetTree().NodeAdded += OnNodeAdded;
        if (InputDevice.Instance is { } device) device.Changed += OnDeviceChanged;
    }

    private void TeardownPad()
    {
        if (InputDevice.Instance is { } device) device.Changed -= OnDeviceChanged;
        if (GetTree() is { } tree) tree.NodeAdded -= OnNodeAdded;
    }

    public override void _ExitTree() => TeardownPad();

    // ---------- the device ----------

    private void OnNodeAdded(Node node)
    {
        // A popup menu that has the window focus gets the gamepad events itself (the viewport forwards them before _Input) and does
        // not walk its items with them: its window_input hands them to PopupPad.
        if (node is PopupMenu popup && !popup.HasMeta("pad_hooked"))
        {
            popup.SetMeta("pad_hooked", true);
            popup.WindowInput += e => OnPopupInput(popup, e);
        }
        if (!InputDevice.Gamepad || node is not Control c) return;
        if (IsAncestorOf(c) || Bar?.IsAncestorOf(c) == true) ButtonPrompts.ApplyFocusModes(c, true);
    }

    private void OnDeviceChanged(InputKind kind)
    {
        var gamepad = kind == InputKind.Gamepad;
        ButtonPrompts.ApplyFocusModes(this, gamepad);
        if (Bar != null) ButtonPrompts.ApplyFocusModes(Bar, gamepad);
        foreach (var pad in ButtonPrompts.Walk(this).OfType<IPadControl>()) pad.PadModeChanged(gamepad);
        foreach (var w in _windows.Values) w.ShowDevice(gamepad);
        StopHold();
        if (gamepad)
        {
            var back = _beforeMouse;
            _beforeMouse = null;
            if (back != null && (back == TopBarId || IsOpen(back))) FocusWindow(back);
            else
            {
                SetFocused(null);
                GetViewport().GuiReleaseFocus(); // in the fight nothing holds the focus, a text field neither
            }
        }
        else
        {
            // A field the gamepad put the focus on (the seed, the skill level) is not being typed into: it lets go too, or 1–5 and
            // WASD would type into it (review, task 13 round 1). A field the player clicked into keeps the keys.
            var padOwned = FocusedWindow != null;
            _beforeMouse = FocusedWindow;
            CancelEdit();
            SetFocused(null, release: false);
            // Enter and Space must not press a control nobody sees; a text field being typed into keeps the keys.
            if (GetViewport().GuiGetFocusOwner() is { } focus && (padOwned || !IsTextField(focus))) focus.ReleaseFocus();
            _tip.Visible = false;
        }
    }

    private static bool IsTextField(Control c) => c is LineEdit or TextEdit or SpinBox;

    // ---------- focus between the fight, the windows and the top bar ----------

    private Control? FocusRoot => FocusedWindow == null ? null
        : FocusedWindow == TopBarId ? Bar
        : _windows.TryGetValue(FocusedWindow, out var w) ? w : null;

    private void SetFocused(string? id, bool release = true)
    {
        if (FocusedWindow == id) return;
        CancelEdit();
        FocusedWindow = id;
        if (id != null && id != TopBarId) _lastFocused = id;
        foreach (var (wid, w) in _windows) w.SetPadFocused(wid == id);
        if (id == null)
        {
            _tip.Visible = false;
            if (release && GetViewport().GuiGetFocusOwner() is { } f && (InputDevice.Gamepad || !IsTextField(f))) f.ReleaseFocus();
        }
        FocusChanged?.Invoke();
    }

    /// <summary>Gives the focus to the fight: no control keeps it, the windows stay open.</summary>
    public void FocusCombat() => SetFocused(null);

    /// <summary>Gives the gamepad focus to a window (opening it when closed) and to its remembered control, or to the top bar.</summary>
    public void FocusWindow(string id)
    {
        if (id == TopBarId)
        {
            FocusBar();
            return;
        }
        if (!IsOpen(id)) OpenWindow(id);
        Raise(id);
        SetFocused(id);
        RestoreFocus();
    }

    /// <summary>Opens a window from inside the interface (a meter bar opens «Разбор»): on the gamepad it takes the focus when a window had it.</summary>
    public void OpenFromUi(string id)
    {
        OpenWindow(id);
        if (InputDevice.Gamepad && FocusedWindow != null && FocusedWindow != TopBarId) FocusWindow(id);
    }

    /// <summary>A D-pad window from the fight: a closed one opens (with the focus unless <see cref="GamepadLayout.OpenWithoutFocus"/>), an open one closes.</summary>
    public void DpadWindow(string id)
    {
        if (!_windows.ContainsKey(id)) return;
        if (IsOpen(id))
        {
            CloseWindow(id);
            return;
        }
        OpenWindow(id);
        if (!GamepadLayout.OpenWithoutFocus.Contains(id)) FocusWindow(id);
    }

    /// <summary>View from the fight: the window focused last, else the top open one.</summary>
    public void ViewFromCombat()
    {
        var target = _lastFocused != null && IsOpen(_lastFocused) ? _lastFocused : TopWindow;
        if (target != null) FocusWindow(target);
    }

    /// <summary>The LB/RB ring: the D-pad windows, then the character and the ascendancy (open or not), then the other open windows.</summary>
    public IReadOnlyList<string> Ring()
    {
        var ring = GamepadLayout.FixedRing.Where(_windows.ContainsKey).ToList();
        ring.AddRange(Specs.Select(s => s.Id).Where(id => IsOpen(id) && !ring.Contains(id)));
        return ring;
    }

    /// <summary>LB (-1) or RB (+1) in a window: the neighbour in the ring opens if needed and takes the focus; the current one stays open.</summary>
    public void RingStep(int step)
    {
        if (FocusedWindow is not { } current || current == TopBarId) return;
        var ring = Ring().ToList();
        if (ring.Count == 0) return;
        var i = ring.IndexOf(current);
        var next = ring[((i < 0 ? 0 : i + step) % ring.Count + ring.Count) % ring.Count];
        FocusWindow(next);
    }

    /// <summary>Menu: the top bar takes the focus (its window buttons and the dummy list).</summary>
    public void FocusBar()
    {
        if (Bar == null) return;
        if (FocusedWindow != TopBarId) _beforeBar = FocusedWindow;
        SetFocused(TopBarId);
        RestoreFocus();
    }

    private void LeaveBar()
    {
        var back = _beforeBar;
        _beforeBar = null;
        if (back != null && IsOpen(back)) FocusWindow(back);
        else FocusCombat();
    }

    /// <summary>A on a window button of the top bar: an open window takes the focus, a closed one opens (with the focus unless the log or the meter).</summary>
    public void BarOpen(string id)
    {
        if (IsOpen(id))
        {
            FocusWindow(id);
            return;
        }
        OpenWindow(id);
        if (GamepadLayout.OpenWithoutFocus.Contains(id)) FocusCombat();
        else FocusWindow(id);
    }

    /// <summary>Puts the GUI focus on the focused window's remembered control (the nearest one when that one was rebuilt), else its first.</summary>
    public void RestoreFocus()
    {
        if (FocusRoot is not { } root || FocusedWindow is not { } id) return;
        var targets = ButtonPrompts.Targets(root);
        if (targets.Count == 0)
        {
            if (GetViewport().GuiGetFocusOwner() is { } f && !root.IsAncestorOf(f)) f.ReleaseFocus();
            return;
        }
        Control? pick = null;
        if (_remembered.TryGetValue(id, out var memo))
        {
            if (Resolve(root, memo.Chain) is { } exact && targets.Contains(exact)) pick = exact;
            else pick = targets.MinBy(t => t.GetGlobalRect().GetCenter().DistanceSquaredTo(memo.Center));
        }
        pick ??= targets.FirstOrDefault(t => t.HasMeta("pad_default")) ?? targets[0];
        pick.GrabFocus();
    }

    private void OnGuiFocusChanged(Control control)
    {
        if (FocusRoot is { } root && FocusedWindow is { } id && root.IsAncestorOf(control))
            _remembered[id] = (Chain(root, control), control.GetGlobalRect().GetCenter());
    }

    private static int[] Chain(Node root, Node node)
    {
        var chain = new List<int>();
        for (var n = node; n != null && n != root; n = n.GetParent()) chain.Add(n.GetIndex(true));
        chain.Reverse();
        return chain.ToArray();
    }

    private static Control? Resolve(Node root, int[] chain)
    {
        Node n = root;
        foreach (var i in chain)
        {
            if (i < 0 || i >= n.GetChildCount(true)) return null;
            n = n.GetChild(i, true);
        }
        return n as Control;
    }

    // ---------- gamepad events ----------

    /// <summary>One gamepad event; true when the windows took it (the room never sees it).</summary>
    public bool HandlePad(InputEvent e)
    {
        var commands = Track(e);
        // An open list or menu (a popup window) takes the gamepad: ↑/↓ walk its items, A picks, B closes it.
        if (FocusedWindow != null && OpenPopupMenu() is { } popup) return PopupPad(popup, e, commands);
        if (FocusedWindow == null) return HandlePadCombat(e);
        if (FocusRoot is not { } root || !root.IsVisibleInTree())
        {
            FocusCombat();
            return HandlePadCombat(e);
        }
        if (e is InputEventJoypadButton { Pressed: true } button) return HandleWindowButton(button);
        // A released: the GUI presses its button on the release (BaseButton), so A passes both ways when the press passed.
        if (e is InputEventJoypadButton { Pressed: false } up && up.IsAction("ui_accept")) return !_acceptPassed;
        foreach (var c in commands) Command(c);
        return true;
    }

    /// <summary>Directions from the left stick and edges of the triggers; the right stick is kept for <see cref="_Process"/>.</summary>
    private List<PadCommand> Track(InputEvent e)
    {
        var commands = new List<PadCommand>();
        switch (e)
        {
            case InputEventJoypadMotion m when m.Axis is JoyAxis.LeftX or JoyAxis.LeftY:
            {
                var (neg, pos) = m.Axis == JoyAxis.LeftX ? (PadCommand.Left, PadCommand.Right) : (PadCommand.Up, PadCommand.Down);
                foreach (var (dir, on) in new[] { (neg, m.AxisValue <= -InputActions.NavThreshold), (pos, m.AxisValue >= InputActions.NavThreshold) })
                {
                    var was = _stick.GetValueOrDefault(dir);
                    _stick[dir] = on;
                    if (on && !was) commands.Add(dir);
                    else if (!on && was && _held == dir) StopHold();
                }
                break;
            }
            case InputEventJoypadMotion m when m.Axis is JoyAxis.RightX or JoyAxis.RightY:
                _rightStick = m.Axis == JoyAxis.RightX ? _rightStick with { X = m.AxisValue } : _rightStick with { Y = m.AxisValue };
                break;
            case InputEventJoypadMotion m when m.Axis == JoyAxis.TriggerLeft:
                if (!_ltDown && m.AxisValue >= InputActions.TriggerPress) { _ltDown = true; commands.Add(PadCommand.TriggerLeft); }
                else if (_ltDown && m.AxisValue <= InputActions.TriggerRelease) _ltDown = false;
                break;
            case InputEventJoypadMotion m when m.Axis == JoyAxis.TriggerRight:
                if (!_rtDown && m.AxisValue >= InputActions.TriggerPress) { _rtDown = true; commands.Add(PadCommand.TriggerRight); }
                else if (_rtDown && m.AxisValue <= InputActions.TriggerRelease) _rtDown = false;
                break;
            case InputEventJoypadButton { Pressed: false } b:
                if (DirectionOf(b) is { } d && _held == d) StopHold();
                break;
        }
        return commands;
    }

    /// <summary>The open popup menu (an OptionButton list, the attribute menu, the log's kinds), or null.</summary>
    public PopupMenu? OpenPopupMenu() => GetViewport().GetEmbeddedSubwindows().OfType<PopupMenu>().LastOrDefault(w => w.Visible);

    /// <summary>
    /// The gamepad in an open popup menu: ↑/↓ (D-pad or stick) move its focused item past separators and disabled items, A picks
    /// the item exactly as a click does (id_pressed, index_pressed: an OptionButton selects it), B closes the menu with no change.
    /// </summary>
    private void OnPopupInput(PopupMenu popup, InputEvent e)
    {
        if (e is not (InputEventJoypadButton or InputEventJoypadMotion)) return;
        InputDevice.Instance?.Observe(e);
        if (!popup.Visible) return;
        PopupPad(popup, e, Track(e));
    }

    private bool PopupPad(PopupMenu popup, InputEvent e, List<PadCommand> commands)
    {
        // The release of the A that opened the list belongs to its button.
        if (e is InputEventJoypadButton { Pressed: false } up && up.IsAction("ui_accept") && _acceptPassed)
        {
            _acceptPassed = false;
            return false;
        }
        if (e is InputEventJoypadButton { Pressed: true } b)
        {
            if (b.IsActionPressed("ui_up")) commands.Add(PadCommand.Up);
            else if (b.IsActionPressed("ui_down")) commands.Add(PadCommand.Down);
            else if (b.IsActionPressed("ui_accept")) commands.Add(PadCommand.Accept);
            else if (b.IsActionPressed("ui_cancel"))
            {
                popup.Hide();
                return true;
            }
        }
        StopHold();
        foreach (var c in commands)
        {
            var count = popup.ItemCount;
            if (count == 0) break;
            var at = popup.GetFocusedItem();
            switch (c)
            {
                case PadCommand.Up or PadCommand.Down:
                {
                    var step = c == PadCommand.Down ? 1 : -1;
                    var next = at < 0 ? (step > 0 ? -1 : 0) : at;
                    for (int i = 0; i < count; i++)
                    {
                        next = (next + step + count) % count;
                        if (!popup.IsItemSeparator(next) && !popup.IsItemDisabled(next)) break;
                    }
                    popup.SetFocusedItem(next);
                    break;
                }
                case PadCommand.Accept when at >= 0 && at < count && !popup.IsItemDisabled(at) && !popup.IsItemSeparator(at):
                {
                    var checkable = popup.IsItemCheckable(at) || popup.IsItemRadioCheckable(at);
                    popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(at));
                    popup.EmitSignal(PopupMenu.SignalName.IndexPressed, at);
                    if (checkable ? popup.HideOnCheckableItemSelection : popup.HideOnItemSelection) popup.Hide();
                    break;
                }
            }
        }
        return true;
    }

    private static PadCommand? DirectionOf(InputEvent e) =>
        e.IsAction("ui_up") ? PadCommand.Up : e.IsAction("ui_down") ? PadCommand.Down
        : e.IsAction("ui_left") ? PadCommand.Left : e.IsAction("ui_right") ? PadCommand.Right : null;

    private bool HandlePadCombat(InputEvent e)
    {
        if (e is not InputEventJoypadButton { Pressed: true } b) return false;
        foreach (var spec in Specs)
            if (b.IsActionPressed(InputActions.Window(spec.Id)) && GamepadLayout.DpadOf(spec.Id) != null)
            {
                DpadWindow(spec.Id);
                return true;
            }
        if (b.IsActionPressed(InputActions.PadMenu))
        {
            FocusBar();
            return true;
        }
        if (b.IsActionPressed(InputActions.PadView))
        {
            ViewFromCombat();
            return true;
        }
        return false;
    }

    private bool HandleWindowButton(InputEventJoypadButton b)
    {
        if (b.IsActionPressed("ui_up") || b.IsActionPressed("ui_down") || b.IsActionPressed("ui_left") || b.IsActionPressed("ui_right"))
        {
            Command(DirectionOf(b)!.Value);
            return true;
        }
        if (b.IsActionPressed("ui_accept"))
        {
            var taken = Accept();
            _acceptPassed = !taken;
            return taken;
        }
        if (b.IsActionPressed("ui_cancel")) Back();
        else if (b.IsActionPressed(InputActions.PadAlt)) Command(PadCommand.Alt);
        else if (b.IsActionPressed(InputActions.PadExtra)) Command(PadCommand.Extra);
        else if (b.IsActionPressed(InputActions.PadCenter)) Command(PadCommand.Center);
        else if (b.IsActionPressed(InputActions.PadPrevWindow)) RingStep(-1);
        else if (b.IsActionPressed(InputActions.PadNextWindow)) RingStep(1);
        else if (b.IsActionPressed(InputActions.PadView))
        {
            if (FocusedWindow == TopBarId) LeaveBar();
            else FocusCombat();
        }
        else if (b.IsActionPressed(InputActions.PadMenu))
        {
            if (FocusedWindow == TopBarId) LeaveBar();
            else FocusBar();
        }
        return true;
    }

    private Control? FocusOwnerIn(Control root) => GetViewport().GuiGetFocusOwner() is { } f && root.IsAncestorOf(f) ? f : null;

    private IPadPanel? Panel => FocusedWindow != null && _windows.TryGetValue(FocusedWindow, out var w) ? w.Content as IPadPanel : null;

    /// <summary>A direction, X, Y, a trigger or R3 in the focused window (public for the self-test, which also pushes real events).</summary>
    public void Command(PadCommand command)
    {
        if (FocusRoot is not { } root) return;
        var focus = FocusOwnerIn(root);
        var direction = command is PadCommand.Up or PadCommand.Down or PadCommand.Left or PadCommand.Right;
        if (direction)
        {
            CommitHold();
            _held = command;
            _repeatLeft = RepeatDelay;
            _repeats = 0;
        }
        if (focus == null)
        {
            RestoreFocus();
            return;
        }
        Apply(command, focus, root, repeat: false);
    }

    private void Apply(PadCommand command, Control focus, Control root, bool repeat)
    {
        var horizontal = command is PadCommand.Left or PadCommand.Right;
        var sign = command is PadCommand.Left or PadCommand.TriggerLeft ? -1 : 1;
        if (_editing != null)
        {
            if (horizontal) StepEdit(sign * (_repeats >= FastAfterRepeats ? 10 : 1));
            else if (command is PadCommand.TriggerLeft or PadCommand.TriggerRight) StepEdit(sign * 10);
            return;
        }
        if (focus is IPadControl pad && pad.PadInput(command)) return;
        var field = FieldOf(focus);
        switch (command)
        {
            case PadCommand.Left or PadCommand.Right when field != null && !IsEditMode(field):
                Step(field, sign * (_repeats >= FastAfterRepeats ? 10 : 1), repeat);
                return;
            case PadCommand.Up or PadCommand.Down when focus is Tree tree && TreeStep(tree, command == PadCommand.Down ? 1 : -1):
                return;
            case PadCommand.Up or PadCommand.Down or PadCommand.Left or PadCommand.Right:
                Navigate(focus, command, root);
                return;
        }
        if (ButtonPrompts.TryAction(focus, command)) return;
        if (command is PadCommand.TriggerLeft or PadCommand.TriggerRight && field != null && !IsEditMode(field))
        {
            Step(field, sign * 10, repeat: false);
            return;
        }
        Panel?.PadButton(command, focus);
    }

    /// <summary>A: edit mode on and off, the pad control's own A, else (false) the GUI presses the focused control.</summary>
    private bool Accept()
    {
        if (FocusRoot is not { } root) return true;
        var focus = FocusOwnerIn(root);
        if (focus == null)
        {
            RestoreFocus();
            return true;
        }
        if (_editing != null)
        {
            var spin = _editing;
            _editing = null;
            spin.Value = _editValue; // the one change the panel hears
            return true;
        }
        var field = FieldOf(focus);
        if (field is SpinBox editable && IsEditMode(editable))
        {
            _editing = editable;
            _editValue = _editRevert = editable.Value;
            return true;
        }
        if (focus is IPadControl pad)
        {
            pad.PadInput(PadCommand.Accept);
            return true;
        }
        if (field != null) return true;
        if (FocusedWindow == TopBarId && Bar != null && Bar.WindowOf(focus) is { } id)
        {
            BarOpen(id);
            return true;
        }
        return false;
    }

    /// <summary>B: leave the edit mode, then the panel's own step back, then close the window (the focus goes to the fight).</summary>
    private void Back()
    {
        if (_editing != null)
        {
            CancelEdit();
            return;
        }
        if (FocusedWindow == TopBarId)
        {
            LeaveBar();
            return;
        }
        if (Panel?.PadBack() == true) return;
        if (FocusedWindow is { } id) CloseWindow(id);
    }

    private static Range? FieldOf(Control focus) => focus switch
    {
        Slider s => s,
        LineEdit { } edit when edit.GetParent() is SpinBox spin => spin,
        _ => null,
    };

    private static bool IsEditMode(Range field) => field.HasMeta(ButtonPrompts.EditMeta);

    private Range? _holdField;

    /// <summary>
    /// ←/→ on a number field: the first press applies at once; a held repeat only moves the shown value (the seed would restart
    /// the fight and the level rebuild the build on every repeat), and the release applies it once (<see cref="StopHold"/>).
    /// </summary>
    private void Step(Range field, int steps, bool repeat)
    {
        var step = field.Step > 0 ? field.Step : 1;
        var value = Mathf.Clamp(field.Value + steps * step, field.MinValue, field.MaxValue);
        if (!repeat)
        {
            CommitHold();
            field.Value = value;
            return;
        }
        _holdField = field;
        field.SetValueNoSignal(value);
        if (field is SpinBox spin) spin.GetLineEdit().Text = RuNumber.Format(value, "0.##");
        field.QueueRedraw();
    }

    /// <summary>A held direction ended (released, the stick back, another command): a held number change is applied once.</summary>
    private void StopHold()
    {
        _held = null;
        CommitHold();
    }

    private void CommitHold()
    {
        if (_holdField is not { } field) return;
        _holdField = null;
        if (IsInstanceValid(field)) field.EmitSignal(Godot.Range.SignalName.ValueChanged, field.Value);
    }

    /// <summary>The gamepad is gone: no stick, trigger or held direction is remembered.</summary>
    public void ClearPadState()
    {
        StopHold();
        _stick.Clear();
        _rightStick = Vector2.Zero;
        _ltDown = _rtDown = false;
    }

    private void StepEdit(int steps)
    {
        if (_editing is not { } spin) return;
        var step = spin.Step > 0 ? spin.Step : 1;
        _editValue = Mathf.Clamp(_editValue + steps * step, spin.MinValue, spin.MaxValue);
        spin.GetLineEdit().Text = RuNumber.Format(_editValue, "0.##");
    }

    private void CancelEdit()
    {
        if (_editing is not { } spin) return;
        _editing = null;
        if (IsInstanceValid(spin))
        {
            spin.SetValueNoSignal(_editRevert);
            spin.GetLineEdit().Text = RuNumber.Format(_editRevert, "0.##");
        }
    }

    private static bool TreeStep(Tree tree, int step)
    {
        var item = tree.GetSelected();
        var next = item == null ? tree.GetRoot()?.GetNextVisible() : step > 0 ? item.GetNextVisible() : item.GetPrevVisible();
        // Group rows cannot be selected: walk past them.
        while (next != null && !next.IsSelectable(0)) next = step > 0 ? next.GetNextVisible() : next.GetPrevVisible();
        if (next == null || next == tree.GetRoot()) return false;
        next.Select(0);
        tree.ScrollToItem(next);
        return true;
    }

    /// <summary>
    /// Moves the focus to the nearest control in the direction inside <paramref name="root"/>: the nearest row first, then the
    /// smallest sideways gap; left and right stay in the row. Down past the last row wraps to the next control in tree order (so
    /// repeated ↓ reaches every row), up past the first to the previous one.
    /// </summary>
    public Control? Navigate(Control from, PadCommand dir, Control root)
    {
        var targets = ButtonPrompts.Targets(root);
        var a = from.GetGlobalRect();
        Control? best = null;
        (float Row, float Side, float Centre) bestScore = (float.MaxValue, float.MaxValue, float.MaxValue);
        foreach (var c in targets)
        {
            if (c == from) continue;
            var b = c.GetGlobalRect();
            float along, side;
            switch (dir)
            {
                case PadCommand.Down when b.GetCenter().Y > a.GetCenter().Y + 2 && b.End.Y > a.End.Y:
                    (along, side) = (Mathf.Max(0, b.Position.Y - a.End.Y), Gap(a.Position.X, a.End.X, b.Position.X, b.End.X));
                    break;
                case PadCommand.Up when b.GetCenter().Y < a.GetCenter().Y - 2 && b.Position.Y < a.Position.Y:
                    (along, side) = (Mathf.Max(0, a.Position.Y - b.End.Y), Gap(a.Position.X, a.End.X, b.Position.X, b.End.X));
                    break;
                case PadCommand.Right when b.GetCenter().X > a.GetCenter().X + 2:
                    (along, side) = (Mathf.Max(0, b.Position.X - a.End.X), Gap(a.Position.Y, a.End.Y, b.Position.Y, b.End.Y));
                    if (side > 4) continue; // another row
                    break;
                case PadCommand.Left when b.GetCenter().X < a.GetCenter().X - 2:
                    (along, side) = (Mathf.Max(0, a.Position.X - b.End.X), Gap(a.Position.Y, a.End.Y, b.Position.Y, b.End.Y));
                    if (side > 4) continue;
                    break;
                default:
                    continue;
            }
            var score = (Mathf.Floor(along / 6), side, a.GetCenter().DistanceTo(b.GetCenter()));
            if (score.CompareTo(bestScore) < 0)
            {
                best = c;
                bestScore = score;
            }
        }
        if (best == null && targets.Count > 1 && dir is PadCommand.Down or PadCommand.Up)
        {
            var i = targets.IndexOf(from);
            best = dir == PadCommand.Down ? targets[(i + 1) % targets.Count] : targets[(i - 1 + targets.Count) % targets.Count];
        }
        best?.GrabFocus();
        return best;
    }

    private static float Gap(float a0, float a1, float b0, float b1) => Mathf.Max(0, Mathf.Max(b0 - a1, a0 - b1));

    public override void _Process(double delta)
    {
        ReplaceOpened();
        ProcessPad(delta);
    }

    private void ProcessPad(double delta)
    {
        if (!InputDevice.Gamepad)
        {
            _barPrompt.Visible = false;
            _tip.UpdateHover(delta, Size, TopInset());
            return;
        }
        if (FocusedWindow == null)
        {
            if (_tip.Visible) _tip.Visible = false;
            _barPrompt.Visible = false;
            return;
        }
        if (FocusRoot is not { } root || !root.IsVisibleInTree())
        {
            FocusCombat();
            return;
        }
        var focus = FocusOwnerIn(root);
        if (focus == null) RestoreFocus();
        if (_held is { } held && focus != null)
        {
            _repeatLeft -= delta;
            if (_repeatLeft <= 0)
            {
                _repeatLeft = RepeatEvery;
                _repeats++;
                Apply(held, focus, root, repeat: true);
            }
        }
        if (_rightStick.Length() > InputActions.StickDeadzone && focus != null)
        {
            if (focus is not IPadControl pad || !pad.PadStick(_rightStick, delta)) Scroll(root, focus, _rightStick.Y * ScrollSpeed * (float)delta);
        }
        UpdatePrompts(root, FocusOwnerIn(root));
    }

    /// <summary>The right stick: the scroll around the focused control, else the window's first scroll or list.</summary>
    private static void Scroll(Control root, Control focus, float dy)
    {
        for (Node? n = focus; n != null && n != root; n = n.GetParent())
            if (n is ScrollContainer sc && sc.GetVScrollBar().MaxValue > sc.Size.Y)
            {
                sc.ScrollVertical += (int)Mathf.Round(dy);
                return;
            }
        foreach (var node in ButtonPrompts.Walk(root))
        {
            if (node is ScrollContainer sc && sc.IsVisibleInTree() && sc.GetVScrollBar().MaxValue > sc.Size.Y)
            {
                sc.ScrollVertical += (int)Mathf.Round(dy);
                return;
            }
            if (node is Tree tree && tree.IsVisibleInTree() && tree.GetChildren(true).OfType<VScrollBar>().FirstOrDefault() is { } bar)
            {
                bar.Value += dy;
                return;
            }
        }
    }

    // ---------- prompts and the focus tooltip ----------

    /// <summary>What the buttons do now in the focused window (the prompt bar).</summary>
    public IReadOnlyList<(string Button, string Label)> PromptsFor(Control? focus)
    {
        var list = new List<(string, string)>();
        if (_editing != null)
        {
            list.Add(("[←/→]", "Значение"));
            list.Add(("[LT/RT]", "±10"));
            list.Add(("[A]", "Сохранить"));
            list.Add(("[B]", "Отменить"));
            return list;
        }
        var field = focus != null ? FieldOf(focus) : null;
        if (focus is IPadControl pad) list.AddRange(pad.PadPrompts());
        else if (field != null && IsEditMode(field)) list.Add(("[A]", "Изменить"));
        else if (field != null)
        {
            list.Add(("[←/→]", "Значение"));
            list.Add(("[LT/RT]", "±10"));
        }
        else list.Add(("[A]", ButtonPrompts.AcceptLabel(focus)));
        list.AddRange(ButtonPrompts.ActionsOf(focus).Select(a => (ButtonPrompts.Glyph(a.Command), a.Label)));
        if (Panel is { } panel) list.AddRange(panel.PadPrompts(focus));
        if (!list.Any(p => p.Item1 == "[B]")) list.Add(("[B]", "Закрыть"));
        list.Add(("[LB/RB]", "Окна"));
        list.Add(("[View]", "К бою"));
        return list;
    }

    private void UpdatePrompts(Control root, Control? focus)
    {
        if (root is GameWindow window)
        {
            var text = ButtonPrompts.Line(PromptsFor(focus));
            if (window.PromptBar.Text != text) window.PromptBar.Text = text;
        }
        // The top bar has no window of its own: its prompt row hangs under it (curator, task 13 round 1).
        _barPrompt.Visible = root == Bar;
        if (root == Bar && Bar != null)
        {
            _barPrompt.Text = ButtonPrompts.Line(Bar.WindowOf(focus!) != null || focus == null
                ? new[] { ("[A]", "Открыть"), ("[←/→]", "Выбор"), ("[B]", "Назад") }
                : new[] { ("[A]", "Список"), ("[←/→]", "Выбор"), ("[B]", "Назад") });
            _barPrompt.ResetSize();
            _barPrompt.Position = new Vector2(Margin, Bar.Size.Y + 2) - GlobalPosition;
            MoveChild(_barPrompt, GetChildCount() - 1);
        }
        UpdateTooltip(root, focus);
    }

    /// <summary>The focused control's text (<see cref="SharedTooltip.ShowForFocus"/>); the trees of «Пассивки» and «Асцендант» show
    /// their node's description in the window itself, so they give none.</summary>
    private void UpdateTooltip(Control root, Control? focus)
    {
        var inside = root is GameWindow w ? (Rect2?)w.Content.GetGlobalRect() : null;
        _tip.ShowForFocus(root, focus, root == Bar, HeroScreenX?.Invoke(), inside, Size, TopInset());
    }

    /// <summary>Where the hero stands on screen (x), so the focus tooltip keeps his sides free; null when unknown.</summary>
    public Func<float?>? HeroScreenX { get; set; }

    /// <summary>A mouse press hides the hover tooltip until the mouse rests again.</summary>
    private void HideHoverTip()
    {
        if (!InputDevice.Gamepad) _tip.ResetHover();
    }
}
