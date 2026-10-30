using Dialoid.Game.Input;
using Dialoid.Game.Room;
using Dialoid.Game.Ui;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Self-test steps of the gamepad (plan 4 task 13, appendix §5, spec §10 п.5): synthetic <see cref="InputEventJoypadButton"/> and
/// <see cref="InputEventJoypadMotion"/> go through the real input path (<see cref="Viewport.PushInput"/> on the main viewport: the
/// device switch, the windows, the GUI, then the room's viewport). Every step starts in gamepad mode and the last one goes back
/// to the keyboard and mouse, so the later steps see the old room.
/// </summary>
public sealed partial class SelfTest
{
    private InputDevice Device => InputDevice.Instance ?? throw new Failure("нет автозагрузки InputDevice");
    private WindowHost Host => _main.Windows;

    private IEnumerable<Step> GamepadSteps()
    {
        yield return new("gamepad.bindings", GamepadBindings);
        yield return new("gamepad.combat-buttons", GamepadCombatButtons);
        yield return new("gamepad.stick-move", GamepadStickMove);
        yield return new("gamepad.lb-rb-target", GamepadTargets);
        yield return new("gamepad.window-focus", GamepadWindowFocus);
        yield return new("gamepad.view-and-ring", GamepadViewAndRing);
        yield return new("gamepad.reachability", GamepadReachability);
        yield return new("gamepad.tooltip-on-focus", GamepadTooltip);
        yield return new("gamepad.passive-tree", GamepadTree);
        yield return new("gamepad.lists-slider-seed", GamepadFields);
        yield return new("gamepad.meter-reset", GamepadMeterReset);
        yield return new("gamepad.device-switch", GamepadDeviceSwitch);
        yield return new("gamepad.disconnect", GamepadDisconnect);
        yield return new("layout.log-clear-of-hud-1366", LogClearOfHud);
        yield return new("log.short-types", LogShortTypes);
        yield return new("gamepad.restore", async () =>
        {
            Host.FocusCombat();
            _main.CloseAllWindows();
            Device.Set(InputKind.KeyboardMouse);
            _main.Hud!.HideToast();
            await CombatRestore();
        });
    }

    // ---------- helpers ----------

    private void Pad(JoyButton button, bool pressed) =>
        _main.GetViewport().PushInput(new InputEventJoypadButton { ButtonIndex = button, Pressed = pressed, Device = 0 });

    /// <summary>A gamepad button pressed and released.</summary>
    private void PadTap(JoyButton button)
    {
        Pad(button, true);
        Pad(button, false);
    }

    private void Axis(JoyAxis axis, float value) =>
        _main.GetViewport().PushInput(new InputEventJoypadMotion { Axis = axis, AxisValue = value, Device = 0 });

    /// <summary>Gamepad mode, the fight has the focus, no window open.</summary>
    private async Task PadMode()
    {
        _main.CloseAllWindows();
        Host.FocusCombat();
        Device.Set(InputKind.Gamepad);
        await NextFrame();
    }

    private Control? FocusOwner => _main.GetViewport().GuiGetFocusOwner();

    private int UseLines(string skillId) => Training.Log.Count(l => l.Kind == LogKind.Use && l.SkillId == skillId);

    // ---------- steps ----------

    /// <summary>Every fight action has a key and a gamepad button; every window has a key and a gamepad path (D-pad or the top bar).</summary>
    private Task GamepadBindings()
    {
        foreach (var action in InputActions.GameActions)
        {
            Check(InputActions.HasKeyboard(action), $"у действия {action} нет клавиши");
            Check(InputActions.HasGamepad(action), $"у действия {action} нет кнопки геймпада");
        }
        var bar = _main.Bar ?? throw new Failure("нет верхней панели");
        foreach (var spec in WindowHost.Specs)
        {
            var action = InputActions.Window(spec.Id);
            Check(InputActions.HasKeyboard(action), $"у окна «{spec.Title}» нет клавиши");
            Check(InputActions.HasGamepad(action) || bar.Buttons.ContainsKey(spec.Id), $"у окна «{spec.Title}» нет пути с геймпада");
        }
        foreach (var (button, id) in GamepadLayout.DpadWindows)
            Check(InputMap.ActionGetEvents(InputActions.Window(id)).OfType<InputEventJoypadButton>().Any(e => e.ButtonIndex == button), $"окно {id} не на крестовине");
        foreach (var ui in new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_cancel" })
            Check(InputActions.HasGamepad(ui) && InputActions.HasKeyboard(ui), $"{ui} без явной привязки");
        foreach (var pad in new[] { InputActions.PadMenu, InputActions.PadView, InputActions.PadPrevWindow, InputActions.PadNextWindow })
            Check(InputActions.HasGamepad(pad), $"{pad} без кнопки");
        return Task.CompletedTask;
    }

    /// <summary>X presses slot 1; RT 1.0 starts the channel, 0.4 does not let it go (no bounce), 0.0 stops it.</summary>
    private async Task GamepadCombatButtons()
    {
        await LoadForCombat(WarriorMelee);
        await PadMode();
        PlaceNearDummy();
        var first = Training.Slots[0]!.SkillId;
        var uses = UseLines(first);
        PadTap(JoyButton.X);
        await Run(0.5);
        Check(UseLines(first) > uses, $"X не применил слот 1 ({first})");

        // The right stick zooms, R3 restores the default zoom.
        var rig = Room.CameraRig;
        rig.ResetZoom();
        Axis(JoyAxis.RightY, -1f);
        rig.StickZoom(0.2);
        Check(rig.Zoom < CameraRig.DefaultZoom, $"правый стик вверх не приблизил: {rig.Zoom}");
        Axis(JoyAxis.RightY, 0f);
        PadTap(JoyButton.RightStick);
        Check(Mathf.IsEqualApprox(rig.Zoom, CameraRig.DefaultZoom), $"R3 не вернул зум: {rig.Zoom}");

        // The channel on the trigger: Whirlwind into slot 5 for this step.
        var skills = _main.Skills!;
        var ww = SlotOf("whirlwind");
        Check(skills.SelectSkill(ww - 1, null).Ok && skills.SelectSkill(4, "whirlwind").Ok, "Вихрь не встал в слот 5");
        await Settle();
        Driver.ResetVisuals();
        Check(Training.Slots[4] is { IsChannel: true }, "в слоте 5 не канал");
        PlaceNearDummy();
        await Run(1.2); // the rebuilt fight settles
        Axis(JoyAxis.TriggerRight, 1f);
        await Run(0.4);
        Check(Training.State!.HeldSlot == 5, "RT 1,0 не начал канал");
        Axis(JoyAxis.TriggerRight, 0.4f);
        await Run(0.3);
        Check(Training.State!.HeldSlot == 5, "RT 0,4 отпустил канал (дребезг)");
        Axis(JoyAxis.TriggerRight, 0.6f);
        await Run(0.1);
        Check(Training.State!.HeldSlot == 5, "канал прервался при 0,6");
        Axis(JoyAxis.TriggerRight, 0f);
        await Run(0.3);
        Check(Training.State!.HeldSlot == null, "RT 0,0 не прекратил канал");

        // Held on both devices: let go only when both let go.
        Axis(JoyAxis.TriggerRight, 1f);
        Driver.HandleInput(new InputEventKey { PhysicalKeycode = Key.Key5, Pressed = true });
        await Run(0.3);
        Axis(JoyAxis.TriggerRight, 0f);
        await Run(0.2);
        Check(Training.State!.HeldSlot == 5, "канал отпущен, хотя клавиша 5 еще зажата");
        Driver.HandleInput(new InputEventKey { PhysicalKeycode = Key.Key5, Pressed = false });
        await Run(0.2);
        Check(Training.State!.HeldSlot == null, "канал не отпущен, когда отпущены оба");
        await LoadForCombat(WarriorMelee);
    }

    /// <summary>The stick (0.8, 0) moves as D does (the same direction, running); 0.1 does not move; released, the hero stands.</summary>
    private async Task GamepadStickMove()
    {
        await LoadForCombat(WarriorMelee);
        await PadMode();
        var hero = Room.Hero;
        Room.PlaceHero(Room.HeroSpawn);
        Driver.HandleInput(new InputEventKey { PhysicalKeycode = Key.D, Pressed = true });
        var byKey = (hero.MoveInput, hero.MoveInputRuns);
        Driver.HandleInput(new InputEventKey { PhysicalKeycode = Key.D, Pressed = false });
        Check(hero.MoveInput == Vector2.Zero, "D отпущена, а движение осталось");
        Device.Set(InputKind.Gamepad);

        Axis(JoyAxis.LeftX, 0.8f);
        Check(hero.MoveInput.IsEqualApprox(byKey.MoveInput) && hero.MoveInputRuns == byKey.MoveInputRuns,
            $"стик (0,8; 0): {hero.MoveInput} бег {hero.MoveInputRuns}, клавиша D: {byKey.MoveInput} бег {byKey.MoveInputRuns}");
        var start = hero.Position;
        for (int i = 0; i < 15; i++) await _main.ToSignal(_main.GetTree(), SceneTree.SignalName.PhysicsFrame);
        Check(hero.Position.DistanceTo(start) > 0.3f, "стик не сдвинул героя");
        Axis(JoyAxis.LeftX, 0.4f);
        Check(hero.MoveInput != Vector2.Zero && !hero.MoveInputRuns, "стик 0,4 не шаг");
        Axis(JoyAxis.LeftX, 0.1f);
        Check(hero.MoveInput == Vector2.Zero, "стик 0,1 двигает героя (мертвая зона)");
        Axis(JoyAxis.LeftX, 0.8f);
        Axis(JoyAxis.LeftX, 0f);
        Check(hero.MoveInput == Vector2.Zero, "после отпускания стика движение осталось");
        await _main.ToSignal(_main.GetTree(), SceneTree.SignalName.PhysicsFrame);
        var stop = hero.Position;
        for (int i = 0; i < 5; i++) await _main.ToSignal(_main.GetTree(), SceneTree.SignalName.PhysicsFrame);
        Check(hero.Position.DistanceTo(stop) < 0.01f, "после отпускания стика герой не стоит");
        Room.PlaceHero(Room.HeroSpawn);
    }

    /// <summary>RB moves the ring to the next figure of «Рой»; LB walks into melee reach of it.</summary>
    private async Task GamepadTargets()
    {
        await LoadForCombat(WarriorMelee);
        await PadMode();
        Driver.SetDummy("swarm");
        var dummy = Room.Dummy;
        Room.PlaceHero(Room.HeroSpawn);
        PadTap(JoyButton.RightShoulder);
        var first = Driver.SelectedTarget;
        Check(first != null && dummy.RingTarget == first, $"RB не выбрал цель: {first}, кольцо {dummy.RingTarget}");
        PadTap(JoyButton.RightShoulder);
        var second = Driver.SelectedTarget;
        Check(second != null && second != first && dummy.RingTarget == second, $"RB не сменил цель: {first} → {second}, кольцо {dummy.RingTarget}");
        PadTap(JoyButton.LeftShoulder);
        Check(Room.Hero.MoveTarget != null, "LB не повел героя");
        for (int i = 0; i < 240 && Room.Hero.MoveTarget != null; i++) await _main.ToSignal(_main.GetTree(), SceneTree.SignalName.PhysicsFrame);
        var feet = dummy.TargetPosition(second!.Value);
        var edge = new Vector2(Room.Hero.Position.X - feet.X, Room.Hero.Position.Z - feet.Z).Length() - dummy.CollisionRadius(second.Value);
        Check(edge <= Dialoid.Sandbox.ReachRules.MeleeReachMeters, $"LB не подвел к цели {second}: край в {edge:0.00} м");
        Driver.SetDummy(TrainingRoom.DefaultDummyId);
        Room.PlaceHero(Room.HeroSpawn);
    }

    /// <summary>
    /// D-pad ← opens «Пассивки» with the focus inside; while the window has it, X uses no skill and a held channel is let go; B closes
    /// the window and the focus goes back to the fight (no control holds it); D-pad ↓ opens the meter without the focus and closes it.
    /// </summary>
    private async Task GamepadWindowFocus()
    {
        await LoadForCombat(WarriorMelee);
        await PadMode();
        PlaceNearDummy();
        var ww = SlotOf("whirlwind");
        var wwButton = InputActions.SlotButton(ww) ?? throw new Failure("Вихрь на триггере");
        Pad(wwButton, true);
        await Run(0.4);
        Check(Training.State!.HeldSlot == ww, "Вихрь не зажат кнопкой");

        PadTap(JoyButton.DpadLeft);
        await Settle();
        Check(Host.IsOpen(WindowIds.Passives) && Host.FocusedWindow == WindowIds.Passives, $"← не открыл «Пассивки» с фокусом: {Host.FocusedWindow}");
        Check(FocusOwner != null && Host.Window(WindowIds.Passives).IsAncestorOf(FocusOwner), $"фокус не в окне: {FocusOwner?.Name}");
        await Run(0.3);
        Check(Training.State!.HeldSlot == null, "канал не отпущен, когда фокус ушел в окно");
        Pad(wwButton, false);
        var first = Training.Slots[0]!.SkillId;
        var uses = UseLines(first);
        PadTap(JoyButton.X);
        await Run(0.5);
        Check(UseLines(first) == uses, "X при фокусе в окне применил скилл");
        Check(Room.Hero.MoveInput == Vector2.Zero, "герой идет, пока фокус в окне");

        PadTap(JoyButton.B);
        await Settle();
        Check(!Host.IsOpen(WindowIds.Passives) && Host.FocusedWindow == null, "B не закрыл окно");
        Check(FocusOwner == null, $"после B фокус остался на {FocusOwner?.Name}");
        PadTap(JoyButton.X);
        await Run(0.5);
        Check(UseLines(first) > uses, "после B кнопка X не дошла до боя");

        PadTap(JoyButton.DpadDown);
        await Settle();
        Check(Host.IsOpen(WindowIds.Meter) && Host.FocusedWindow == null, $"↓ открыл измеритель не так: открыт {Host.IsOpen(WindowIds.Meter)}, фокус {Host.FocusedWindow}");
        PadTap(JoyButton.DpadDown);
        await Settle();
        Check(!Host.IsOpen(WindowIds.Meter), "↓ не закрыл измеритель без фокуса");
    }

    /// <summary>View gives the fight the focus with the window open, View gives it back (the same control); LB/RB walk the ring from «Пассивки» to «Скиллы».</summary>
    private async Task GamepadViewAndRing()
    {
        await PadMode();
        PadTap(JoyButton.DpadLeft);
        await Settle();
        Check(Host.FocusedWindow == WindowIds.Passives, "← не дал фокус «Пассивкам»");
        PadTap(JoyButton.DpadDown); // a move inside the window: the remembered control is not the first
        await Settle();
        var remembered = FocusOwner;
        PadTap(JoyButton.Back);
        await Settle();
        Check(Host.FocusedWindow == null && Host.IsOpen(WindowIds.Passives) && FocusOwner == null, $"View не отдал фокус бою: {Host.FocusedWindow}, {FocusOwner?.Name}");
        PadTap(JoyButton.Back);
        await Settle();
        Check(Host.FocusedWindow == WindowIds.Passives && FocusOwner == remembered, $"View не вернул фокус на {remembered?.Name}: {FocusOwner?.Name}");

        PadTap(JoyButton.RightShoulder);
        await Settle();
        Check(Host.FocusedWindow == WindowIds.Skills && Host.IsOpen(WindowIds.Skills) && Host.IsOpen(WindowIds.Passives), $"RB из «Пассивок» не открыл «Скиллы»: {Host.FocusedWindow}");
        PadTap(JoyButton.LeftShoulder);
        await Settle();
        Check(Host.FocusedWindow == WindowIds.Passives, $"LB не вернул «Пассивки»: {Host.FocusedWindow}");
        var ring = Host.Ring();
        Check(ring.Take(GamepadLayout.DpadWindows.Count + 2).SequenceEqual(GamepadLayout.FixedRing), $"кольцо {string.Join(", ", ring)}");
        PadTap(JoyButton.LeftShoulder); // backwards past the start: the last of the ring
        await Settle();
        Check(Host.FocusedWindow == ring[^1], $"LB с начала кольца: {Host.FocusedWindow}, а не {ring[^1]}");
        // The meter reached through the ring takes the focus (unlike the D-pad).
        Host.FocusWindow(WindowIds.Skills);
        PadTap(JoyButton.RightShoulder);
        PadTap(JoyButton.RightShoulder);
        await Settle();
        Check(Host.FocusedWindow == WindowIds.Meter, $"RB по кольцу не дал фокус измерителю: {Host.FocusedWindow}");
        await PadMode();
    }

    /// <summary>
    /// Main check «нет действия только для мыши»: in every window, from its first control, the gamepad's directions (pushed as
    /// D-pad presses) reach every visible enabled button, list, slider and field. A number field gets only ↑/↓ (←/→ change it).
    /// </summary>
    private async Task GamepadReachability()
    {
        await LoadForCombat(WarriorMelee);
        await PadMode();
        _main.Breakdown!.ShowLine(_main.Session!.CharacterLines[0]); // history buttons in «Разбор»
        _main.Skills!.ShowTree(SlotOf("cleave") - 1); // the skill tree's rows in «Скиллы»
        PlaceNearDummy();
        await Fight(2.0, close: false); // bars in the meter
        var report = new List<string>();
        foreach (var spec in WindowHost.Specs)
        {
            await PadMode();
            Host.FocusWindow(spec.Id);
            await Settle();
            var window = Host.Window(spec.Id);
            var wanted = ButtonPrompts.Targets(window).Where(c => c is BaseButton or Godot.Range or LineEdit or IPadControl or Tree or ItemList).ToList();
            var reached = new HashSet<Control>();
            var queue = new Queue<Control>();
            if (FocusOwner is { } start && window.IsAncestorOf(start))
            {
                reached.Add(start);
                queue.Enqueue(start);
            }
            while (queue.Count > 0)
            {
                var from = queue.Dequeue();
                var field = from is Slider || from is LineEdit { } le && le.GetParent() is SpinBox;
                var dirs = field ? new[] { JoyButton.DpadDown, JoyButton.DpadUp } : new[] { JoyButton.DpadDown, JoyButton.DpadRight, JoyButton.DpadUp, JoyButton.DpadLeft };
                foreach (var dir in dirs)
                {
                    if (!GodotObject.IsInstanceValid(from) || !from.IsVisibleInTree()) break;
                    from.GrabFocus();
                    PadTap(dir);
                    // A tree or the bars take the direction while they have a node or a row that way: repeated presses leave them.
                    for (int i = 0; i < 60 && from is IPadControl && FocusOwner == from; i++) PadTap(dir);
                    if (FocusOwner is { } to && to != from && window.IsAncestorOf(to) && reached.Add(to)) queue.Enqueue(to);
                    Check(FocusOwner == null || window.IsAncestorOf(FocusOwner), $"«{spec.Title}»: фокус ушел из окна на {FocusOwner?.Name}");
                }
            }
            var missed = wanted.Where(c => !reached.Contains(c)).Select(c => $"{c.GetType().Name}«{(c as Button)?.Text ?? c.Name}»").ToList();
            report.Add($"{spec.Title} {reached.Count}/{wanted.Count}");
            Check(missed.Count == 0, $"«{spec.Title}»: недостижимы с геймпада {string.Join(", ", missed.Take(8))}{(missed.Count > 8 ? $" и еще {missed.Count - 8}" : "")}");
        }
        GD.Print("SELFTEST NOTE достижимость с геймпада: " + string.Join(", ", report));
        await PadMode();
    }

    /// <summary>The focus shows the same text the mouse shows on hover: a passive node, a «Разбор» history row.</summary>
    private async Task GamepadTooltip()
    {
        await PadMode();
        Host.FocusWindow(WindowIds.Passives);
        await Settle();
        var view = _main.Passives!.View;
        view.GrabFocus();
        var node = view.Nodes.First(n => n.Caption != null);
        view.PadSelect(node.Id);
        await Settle();
        // «Пассивки» shows the node's description in its own line: no focus tooltip repeats it over the room (curator round 1).
        var hover = view.NodeLocalPosition(node.Id) is { } at ? view._GetTooltip(at) : "";
        var panelLine = _main.Passives.HoverText;
        Check(Host.FocusTooltip == null && hover == node.Tooltip && panelLine.Contains(node.Tooltip.Split('\n')[0]),
            $"узел по фокусу: подсказка «{Host.FocusTooltip}», строка окна «{panelLine}», по наведению «{hover}»");
        var help = ButtonPrompts.Walk(_main.Passives).OfType<Label>().FirstOrDefault(l => l.HasMeta(ButtonPrompts.HelpMeta));
        Check(help != null && help.Text.StartsWith("A —") && !help.Text.Contains("ЛКМ"), $"строка помощи в режиме геймпада: «{help?.Text}»");
        // The gamepad's captions: the focused node and where the stick goes, on plates inside the view.
        var plates = view.PadCaptions();
        Check(plates.Any(p => p.Id == node.Id) && plates.Count >= 2 && plates.All(p => new Rect2(Vector2.Zero, view.Size).Encloses(p.Rect)),
            $"подписи геймпада: {string.Join(", ", plates.Select(p => p.Id))}");

        _main.Breakdown!.ShowLine(_main.Session!.CharacterLines[0]);
        Host.FocusWindow(WindowIds.Breakdown);
        await Settle();
        var row = ButtonPrompts.Walk(_main.Breakdown).OfType<NumberLabel>().FirstOrDefault() ?? throw new Failure("в «Разборе» нет строки истории");
        row.GrabFocus();
        await Settle();
        Check(Host.FocusTooltip == row.GetTooltip(row.Size / 2) && row.TooltipText.Length > 0, $"подсказка строки по фокусу «{Host.FocusTooltip}», по наведению «{row.TooltipText}»");
        var tipRect = Host.FocusTooltipRect;
        var bdWindow = Host.Window(WindowIds.Breakdown).GetGlobalRect();
        var heroX = Host.HeroScreenX?.Invoke();
        var clearOfHero = heroX is not { } hx || tipRect.End.X < hx - SharedTooltip.HeroClearance + 1 || tipRect.Position.X > hx + SharedTooltip.HeroClearance - 1;
        Check((clearOfHero || bdWindow.Encloses(tipRect)) && !tipRect.Intersects(row.GetGlobalRect()),
            $"подсказка {tipRect} у героя x={heroX} или на строке {row.GetGlobalRect()} (окно {bdWindow})");
        // The mouse shows the same text through the same component (Godot's own tooltips are off).
        Device.Set(InputKind.KeyboardMouse);
        var centre = row.GetGlobalRect().GetCenter();
        _main.GetViewport().PushInput(new InputEventMouseMotion { Position = centre, GlobalPosition = centre });
        var waitFrom = Time.GetTicksMsec();
        while (Host.HoverTipControl != row && Time.GetTicksMsec() - waitFrom < 3000) await NextFrame();
        Check(Host.HoverTipControl == row && Host.FocusTooltip == row.TooltipText, $"по наведению показано «{Host.FocusTooltip}» над {Host.HoverTipControl?.Name}");
        _main.GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(2, _main.Size.Y - 2), GlobalPosition = new Vector2(2, _main.Size.Y - 2) });
        await PadMode();
    }

    /// <summary>
    /// The passive tree on the gamepad: → picks the nearest node to the right; A changes it as a click does (and back); X on an
    /// attribute node opens the attribute menu on the current choice; LT/RT zoom within limits; the selected node stays in view.
    /// </summary>
    private async Task GamepadTree()
    {
        await LoadForCombat(WarriorMelee);
        await PadMode();
        PadTap(JoyButton.DpadLeft);
        await Settle();
        var view = _main.Passives!.View;
        if (view.HasFocus()) view.ReleaseFocus();
        view.FitToView();
        var fitZoom = view.Zoom;
        view.GrabFocus();
        Check(view.Zoom >= fitZoom * 1.5f - 1e-3f || Mathf.IsEqualApprox(view.Zoom, TreeView.MaxZoom), $"фокус в дереве не приблизил: {view.Zoom} при «Вписать» {fitZoom}");
        // A node with a neighbour to the right.
        var from = view.Nodes.FirstOrDefault(n => view.NeighbourOf(n.Id, Vector2.Right) != null && view.NodeLocalPosition(n.Id) is { } p && new Rect2(Vector2.Zero, view.Size).HasPoint(p))
                   ?? throw new Failure("нет узла с соседом справа");
        view.PadSelect(from.Id);
        var expected = view.NeighbourOf(from.Id, Vector2.Right)!;
        PadTap(JoyButton.DpadRight);
        Check(view.HoveredId == expected, $"→ выбрал {view.HoveredId}, а не {expected}");
        var a = view.Nodes.First(n => n.Id == from.Id).Position;
        var b = view.Nodes.First(n => n.Id == expected).Position;
        Check(b.X > a.X, $"узел {expected} не правее {from.Id}");
        Check(view.NodeLocalPosition(expected) is { } shown && new Rect2(Vector2.Zero, view.Size).HasPoint(shown), "выбранный узел вне вида");

        // A = click: take an available node, then the mouse path drops it, then A takes it again.
        var available = view.Nodes.FirstOrDefault(n => view.StateOf(n.Id) == TreeNodeState.Available) ?? throw new Failure("нет доступного узла");
        view.PadSelect(available.Id);
        PadTap(JoyButton.A);
        await Settle();
        var afterA = view.StateOf(available.Id);
        Check(afterA == TreeNodeState.Taken, $"A не взял узел {available.Id}: {afterA}");
        Check(view.ClickAt(view.NodeLocalPosition(available.Id)!.Value) == available.Id, "клик не попал в узел");
        await Settle();
        Check(view.StateOf(available.Id) != TreeNodeState.Taken, "клик не снял узел, взятый A");
        PadTap(JoyButton.A);
        await Settle();
        Check(view.StateOf(available.Id) == afterA, "A после клика дал другое состояние");
        PadTap(JoyButton.A); // back as it was
        await Settle();
        Check(view.StateOf(available.Id) != TreeNodeState.Taken, "A на взятом узле не снял его");

        // X on a taken attribute node: the menu on the current choice.
        var attr = view.Nodes.FirstOrDefault(n => view.StateOf(n.Id) == TreeNodeState.Taken &&
            _main.Session!.Data.Passives.Nodes.TryGetValue(n.Id, out var def) && def.Kind == Dialoid.Core.Passives.PassiveKind.Attribute);
        if (attr != null)
        {
            view.PadSelect(attr.Id);
            PadTap(JoyButton.X);
            await NextFrame();
            var menu = _main.Passives.AttributeMenu;
            Check(menu.Visible, "X не открыл выбор атрибута");
            var current = _main.Session!.Build.AttributeChoices.TryGetValue(attr.Id, out var c) ? PassivePanel.AttributeOrder.ToList().IndexOf(c) : 0;
            Check(menu.GetFocusedItem() == Math.Max(0, current), $"фокус меню на {menu.GetFocusedItem()}, а не на текущем {current}");
            menu.Hide();
            await NextFrame();
        }
        else GD.Print("SELFTEST NOTE дерево: нет взятого узла атрибута для X");

        view.PadSelect(expected);
        var zoom = view.Zoom;
        Axis(JoyAxis.TriggerRight, 1f);
        Axis(JoyAxis.TriggerRight, 0f);
        Check(view.Zoom > zoom, "RT не приблизил дерево");
        for (int i = 0; i < 40; i++)
        {
            Axis(JoyAxis.TriggerRight, 1f);
            Axis(JoyAxis.TriggerRight, 0f);
        }
        Check(view.Zoom <= TreeView.MaxZoom + 1e-4f, $"масштаб {view.Zoom} за пределом");
        Check(view.NodeLocalPosition(expected) is { } kept && new Rect2(Vector2.Zero, view.Size).HasPoint(kept), "после масштаба выбранный узел вне вида");
        for (int i = 0; i < 80; i++)
        {
            Axis(JoyAxis.TriggerLeft, 1f);
            Axis(JoyAxis.TriggerLeft, 0f);
        }
        Check(view.Zoom >= TreeView.MinZoom - 1e-4f, $"масштаб {view.Zoom} ниже предела");
        view.FitToView();
        await PadMode();
    }

    /// <summary>
    /// A, ↓, A in the top bar's dummy list changes the preset as the mouse does; ←/→ on the level slider ±1; X on the seed a new seed;
    /// the skill level field edits between two A presses (B reverts).
    /// </summary>
    private async Task GamepadFields()
    {
        await LoadForCombat(WarriorMelee);
        await PadMode();
        var bar = _main.Bar!;
        PadTap(JoyButton.Start);
        await NextFrame();
        Check(Host.FocusedWindow == WindowHost.TopBarId && FocusOwner != null && bar.IsAncestorOf(FocusOwner), $"Menu не дал фокус верхней панели: {FocusOwner?.Name}");
        Check(Host.BarPromptText?.Contains("[A]") == true && Host.BarPromptText.Contains("[B]"), $"под верхней панелью нет строки кнопок: «{Host.BarPromptText}»");
        bar.DummyOptions.GrabFocus();
        var before = bar.DummyOptions.Selected;
        PadTap(JoyButton.A);
        await NextFrame();
        var popup = bar.DummyOptions.GetPopup();
        Check(popup.Visible, "A не открыл список манекенов");
        Check(popup.GetFocusedItem() == before, $"фокус списка на {popup.GetFocusedItem()}, а не на текущем {before}");
        PadTap(JoyButton.DpadDown);
        PadTap(JoyButton.A);
        await Settle();
        Check(bar.DummyOptions.Selected == before + 1 && Training.DummyId == bar.DummyIds[before + 1], $"A, ↓, A: манекен {Training.DummyId}, выбран {bar.DummyOptions.Selected}");
        PadTap(JoyButton.B); // back to the fight
        await NextFrame();
        Check(Host.FocusedWindow == null, "B не вернул фокус бою из верхней панели");
        bar.SelectDummy(TrainingRoom.DefaultDummyId);

        // The level slider.
        Host.FocusWindow(WindowIds.Character);
        await Settle();
        var slider = ButtonPrompts.Walk(_main.Character!).OfType<HSlider>().FirstOrDefault() ?? throw new Failure("нет ползунка уровня");
        slider.GrabFocus();
        var level = _main.Session!.Build.Level;
        var down = level > slider.MinValue ? JoyButton.DpadLeft : JoyButton.DpadRight;
        PadTap(down);
        await Settle();
        var moved = _main.Session.Build.Level;
        Check(Math.Abs(moved - level) == 1, $"← / → на ползунке: уровень {level} → {moved}");
        var slider2 = ButtonPrompts.Walk(_main.Character!).OfType<HSlider>().First();
        slider2.GrabFocus();
        PadTap(down == JoyButton.DpadLeft ? JoyButton.DpadRight : JoyButton.DpadLeft);
        await Settle();
        Check(_main.Session.Build.Level == level, $"уровень не вернулся: {_main.Session.Build.Level}");

        // The seed of the meter.
        Host.FocusWindow(WindowIds.Meter);
        await Settle();
        var seed = Meter.Seed;
        seed.GetLineEdit().GrabFocus();
        var seedBefore = (int)seed.Value;
        PadTap(JoyButton.DpadRight);
        await NextFrame();
        Check((int)seed.Value == seedBefore + 1, $"→ на сиде: {seed.Value}, а не {seedBefore + 1}");
        var tries = 0;
        do
        {
            PadTap(JoyButton.X);
            await NextFrame();
        } while ((int)seed.Value == seedBefore + 1 && ++tries < 5);
        Check((int)seed.Value != seedBefore + 1, "X не сменил сид");

        // Held → on the seed: the shown value runs, the fight restarts once on the release, not on every repeat.
        var changes = 0;
        void Count(double _) => changes++;
        seed.ValueChanged += Count;
        try
        {
            var heldFrom = (int)seed.Value;
            Pad(JoyButton.DpadRight, true);
            var holdStart = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - holdStart < 900) await NextFrame();
            var shown = (int)seed.Value;
            Check(shown > heldFrom + 2 && changes == 1, $"удержание →: сид {heldFrom} → {shown}, применений {changes}");
            Pad(JoyButton.DpadRight, false);
            await NextFrame();
            Check(changes == 2 && Training.Seed == (int)seed.Value, $"после отпускания: применений {changes}, сид боя {Training.Seed}, поле {seed.Value}");
        }
        finally
        {
            seed.ValueChanged -= Count;
        }
        PadTap(JoyButton.Y);
        await NextFrame();
        Check(Meter.ConfirmingReset, "Y на поле сида не спросил «Сбросить» (Y — действие окна)");
        PadTap(JoyButton.B);
        await NextFrame();
        Check(!Meter.ConfirmingReset && Host.FocusedWindow == WindowIds.Meter, "B не закрыл подтверждение");
        Meter.SetSimMode(Dialoid.Core.Simulation.SimMode.Roll, 1);

        // The skill level field: edit mode.
        Host.FocusWindow(WindowIds.Skills);
        await Settle();
        var spin = ButtonPrompts.Walk(_main.Skills!).OfType<SpinBox>().FirstOrDefault(s => s.HasMeta(ButtonPrompts.EditMeta)) ?? throw new Failure("нет поля уровня скилла");
        var slotLevel = (int)spin.Value;
        spin.GetLineEdit().GrabFocus();
        var dir = slotLevel > spin.MinValue ? JoyButton.DpadLeft : JoyButton.DpadRight;
        PadTap(dir);
        await Settle();
        Check((int)spin.Value == slotLevel && FocusOwner != spin.GetLineEdit(), $"←/→ без правки сменило уровень или не ушло вбок: {spin.Value}, фокус {FocusOwner?.Name}");
        spin.GetLineEdit().GrabFocus();
        PadTap(JoyButton.A);
        Check(Host.EditingField == spin, "A не включил правку поля");
        PadTap(dir);
        Check((int)spin.Value == slotLevel, "в правке значение применилось до A");
        PadTap(JoyButton.B);
        Check(Host.EditingField == null && (int)spin.Value == slotLevel, "B не откатил правку");
        PadTap(JoyButton.A);
        PadTap(dir);
        PadTap(JoyButton.A);
        await Settle();
        var after = _main.Session.GetSlot(0)?.Level;
        var changed = Enumerable.Range(0, SandboxSession.SlotCount).Select(i => _main.Session.GetSlot(i)?.Level).ToList();
        Check(Host.EditingField == null && changed.Contains(slotLevel + (dir == JoyButton.DpadLeft ? -1 : 1)), $"A, ←/→, A не сменили уровень скилла: {string.Join(",", changed)}");
        GD.Print($"SELFTEST NOTE поле уровня: {slotLevel} → {string.Join(",", changed)} (слот 1 {after})");
        await LoadForCombat(WarriorMelee);
        await PadMode();
    }

    /// <summary>Y in the meter asks «Сбросить?» with the focus on «Отмена»; A there resets nothing and the focus goes back to «Сбросить».</summary>
    private async Task GamepadMeterReset()
    {
        await LoadForCombat(WarriorMelee);
        await PadMode();
        PlaceNearDummy();
        await Fight(2.0, close: false);
        var segments = Training.Segments.Count;
        Check(segments > 0, "нет сегментов");
        Host.FocusWindow(WindowIds.Meter);
        await Settle();
        PadTap(JoyButton.Y);
        await NextFrame();
        Check(Meter.ConfirmingReset && Meter.CancelButton.HasFocus(), $"Y: подтверждение {Meter.ConfirmingReset}, фокус на {FocusOwner?.Name}");
        PadTap(JoyButton.A);
        await Settle();
        Check(!Meter.ConfirmingReset && Training.Segments.Count == segments, $"A на «Отмена» сбросил измеритель: {Training.Segments.Count}");
        Check(Meter.ResetButton.HasFocus(), $"после «Отмена» фокус на {FocusOwner?.Name}");
        // A on a bar opens the skill's breakdown (and «Разбор» takes the focus).
        Meter.Bars.GrabFocus();
        PadTap(JoyButton.A);
        await Settle();
        Check(Host.IsOpen(WindowIds.Breakdown) && Host.FocusedWindow == WindowIds.Breakdown, $"A на полосе: разбор {Host.IsOpen(WindowIds.Breakdown)}, фокус {Host.FocusedWindow}");
        Check(_main.Breakdown!.Trail.Count > 0, "у разбора нет шага назад");
        var shown = _main.Breakdown.Current;
        PadTap(JoyButton.B);
        await Settle();
        Check(Host.IsOpen(WindowIds.Breakdown) && _main.Breakdown.Current != shown, "B в разборе не шагнул назад по истории");
        while (Host.IsOpen(WindowIds.Breakdown) && Host.FocusedWindow == WindowIds.Breakdown)
        {
            PadTap(JoyButton.B);
            await Settle();
        }
        Check(!Host.IsOpen(WindowIds.Breakdown) && Host.FocusedWindow == null, "B на верхнем уровне разбора не закрыл окно");
        await PadMode();
    }

    /// <summary>
    /// A gamepad button switches to the gamepad: the cursor hides, the top bar has no key letters, the combat screen shows button
    /// glyphs; a 10 px mouse move switches back (no control keeps the focus); a 1 px move and a stick at 0.1 do not switch.
    /// </summary>
    private async Task GamepadDeviceSwitch()
    {
        await LoadForCombat(WarriorMelee);
        Device.Set(InputKind.KeyboardMouse);
        Host.FocusCombat();
        PadTap(JoyButton.DpadLeft); // a gamepad press switches, and opens «Пассивки» with the focus
        await Settle();
        Check(Device.Active == InputKind.Gamepad, "кнопка геймпада не переключила устройство");
        if (DisplayServer.GetName() != "headless") Check(Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Hidden, "курсор виден в режиме геймпада");
        var bar = _main.Bar!;
        foreach (var spec in WindowHost.Specs)
        {
            var key = bar.KeyLabel(spec.Id).Text;
            Check(key != GameWindow.KeyName(spec.Key), $"на кнопке «{spec.Title}» буква клавиши {key}");
        }
        var hud = _main.Hud!;
        Check(hud.Slots.All(s => s.KeyText == ButtonPrompts.SlotGlyph(s.Number)), $"на панели боя {string.Join(" ", hud.Slots.Select(s => s.KeyText))}");
        var window = Host.Window(WindowIds.Passives);
        Check(window.PadFocused && window.PromptBar.IsVisibleInTree() && window.PromptBar.Text.Contains("[B]"), $"строка кнопок «{window.PromptBar.Text}»");
        Check(window.PromptBar.Text.Contains("[LB⁠/⁠RB] Окна") && !window.PromptBar.Text.Contains("[A] "), $"пары подсказок разрываются: «{window.PromptBar.Text}»");
        Check(FocusOwner is IPadControl || FocusOwner != null && FocusOwner.HasThemeStylebox("focus"), $"у элемента в фокусе {FocusOwner?.Name} нет рамки");

        var viewport = _main.GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Relative = new Vector2(3, 0), Position = new Vector2(400, 400) });
        Check(Device.Active == InputKind.Gamepad, "сдвиг мыши на 3 px переключил на мышь");
        viewport.PushInput(new InputEventMouseMotion { Relative = new Vector2(5, 0), Position = new Vector2(410, 400) });
        await NextFrame();
        Check(Device.Active == InputKind.KeyboardMouse, "сдвиг мыши на 5 px не переключил на мышь");
        Check(FocusOwner == null && Host.FocusedWindow == null && !window.PromptBar.IsVisibleInTree(), $"после мыши фокус на {FocusOwner?.Name}, окно {Host.FocusedWindow}");
        Check(WindowHost.Specs.All(s => bar.KeyLabel(s.Id).Text == GameWindow.KeyName(s.Key)), "буквы клавиш не вернулись");
        Check(hud.Slots.All(s => s.KeyText == s.Number.ToString()), "цифры слотов не вернулись");
        Check(window.TitleLabel.Text.Contains($"({GameWindow.KeyName(window.Spec.Key)})"), $"заголовок {window.TitleLabel.Text}");
        Axis(JoyAxis.LeftX, 0.1f);
        Check(Device.Active == InputKind.KeyboardMouse, "стик 0,1 переключил на геймпад");
        Axis(JoyAxis.LeftX, 0f);
        // Below the switch threshold the stick neither switches nor moves the hero (the prompts would say keyboard).
        Axis(JoyAxis.LeftX, 0.3f);
        Check(Device.Active == InputKind.KeyboardMouse && Room.Hero.MoveInput == Vector2.Zero, $"стик 0,3 в режиме мыши: {Device.Active}, движение {Room.Hero.MoveInput}");
        Axis(JoyAxis.LeftX, 0f);

        // Back on the gamepad: the window focused before the mouse gets its control back.
        PadTap(JoyButton.Y);
        await Settle();
        Check(Device.Active == InputKind.Gamepad && Host.FocusedWindow == WindowIds.Passives && FocusOwner != null && window.IsAncestorOf(FocusOwner),
            $"после возврата на геймпад фокус {Host.FocusedWindow}, {FocusOwner?.Name}");
        await PadMode();

        // The gamepad put the focus on the seed; the owner takes the keyboard: the field lets go, so 1–5 go to the fight, not into it.
        Host.FocusWindow(WindowIds.Meter);
        await Settle();
        Meter.Seed.GetLineEdit().GrabFocus();
        await NextFrame();
        Check(FocusOwner == Meter.Seed.GetLineEdit(), "фокус не на поле сида");
        var seedText = Meter.Seed.GetLineEdit().Text;
        _main.GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Key1, Keycode = Key.Key1, Unicode = '1', Pressed = true });
        _main.GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Key1, Keycode = Key.Key1, Unicode = '1', Pressed = false });
        await NextFrame();
        Check(Device.Active == InputKind.KeyboardMouse && FocusOwner == null && Meter.Seed.GetLineEdit().Text == seedText,
            $"после клавиши фокус на {FocusOwner?.Name}, сид «{Meter.Seed.GetLineEdit().Text}»");
        Meter.SetSimMode(Dialoid.Core.Simulation.SimMode.Roll, 1);
        await PadMode();
    }

    /// <summary>The gamepad disconnects with a held channel and the stick tilted: the channel is let go, the hero stops, the keyboard is active, the message shows.</summary>
    private async Task GamepadDisconnect()
    {
        await LoadForCombat(WarriorMelee);
        await PadMode();
        PlaceNearDummy();
        var ww = SlotOf("whirlwind");
        var button = InputActions.SlotButton(ww) ?? throw new Failure("Вихрь на триггере");
        Pad(button, true);
        await Run(0.4);
        Check(Training.State!.HeldSlot == ww, "Вихрь не зажат");
        Axis(JoyAxis.LeftX, 0.8f);
        Check(Room.Hero.MoveInput != Vector2.Zero, "стик не двигает");
        Device.OnJoyConnectionChanged(0, false);
        await Run(0.3);
        Check(Training.State!.HeldSlot == null, "канал не отпущен при отключении");
        Check(Room.Hero.MoveInput == Vector2.Zero, "движение не обнулено");
        Check(Device.Active == InputKind.KeyboardMouse, "не переключилось на клавиатуру и мышь");
        Check(_main.Hud!.ToastShown == InputDevice.DisconnectedText, $"сообщение «{_main.Hud.ToastShown}»");
        Device.OnJoyConnectionChanged(0, true);
        Check(Device.LastMessage == InputDevice.ConnectedText && Device.Active == InputKind.KeyboardMouse, "подключение переключило без нажатия");
        Pad(button, false);
        Axis(JoyAxis.LeftX, 0f);
        Device.Set(InputKind.KeyboardMouse);
    }

    /// <summary>
    /// UI review of task 11: at 1366×768 the log (its default place) ends at least 8 px left of the combat screen and the meter
    /// takes at most a third of the width.
    /// </summary>
    private async Task LogClearOfHud()
    {
        var root = _main.GetTree().Root;
        var size = root.Size;
        var headless = DisplayServer.GetName() == "headless";
        try
        {
            if (headless) root.Size = new Vector2I(1366, 768);
            else
            {
                DisplayServer.WindowSetSize(new Vector2I(1366, 768));
                await Settle();
            }
            Device.Set(InputKind.KeyboardMouse);
            UseSelfTestWindowConfig();
            _main.Hud!.HideToast();
            await Settle();
            _main.OpenWindow(WindowIds.Log);
            _main.OpenWindow(WindowIds.Meter);
            await NextFrame();
            await Settle();
            var screen = _main.Size;
            var log = Host.Window(WindowIds.Log).GetGlobalRect();
            var hud = _main.Hud!.BottomRect;
            GD.Print($"SELFTEST NOTE 1366: экран {screen}, журнал {log}, панель боя {hud}, измеритель {Host.Window(WindowIds.Meter).GetGlobalRect()}");
            Check(!log.Grow(CombatHud.WindowGap - 0.5f).Intersects(hud) && log.Size.X >= WindowHost.MinNarrowLog - 0.5f, $"журнал не пересекает панель боя: журнал {log}, панель {hud}");
            var charts = Host.DefaultRect(WindowHost.SpecOf(WindowIds.Charts));
            Check(!charts.Intersects(log), $"графики {charts} заходят на журнал {log}");
            var meter = Host.Window(WindowIds.Meter).GetGlobalRect();
            Check(meter.Size.X <= screen.X * WindowHost.MaxSideFraction + 0.5f, $"измеритель шире трети: {meter.Size.X} из {screen.X}");
            var follow = LogWindow.Lines.GetParent().GetNode<Control>("Filters/Follow");
            var kinds = LogWindow.KindsMenu;
            Check(Mathf.Abs(follow.GetGlobalRect().Position.Y - kinds.GetGlobalRect().Position.Y) < 2, "«Автопрокрутка» ушла с ряда фильтров");
            Check(log.Size.Y >= 260, $"журнал ниже 260 px: {log.Size.Y}");
            Check(Meter.SegmentOption.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis, "у списка сегментов нет многоточия");
            if (_screenshotDir != null && !headless)
            {
                PlaceNearDummy();
                Directory.CreateDirectory(_screenshotDir);
                await Shot("task-13-log-hud-1366-keyboard");
            }
        }
        finally
        {
            _main.CloseAllWindows();
            if (headless) root.Size = size;
            else DisplayServer.WindowSetSize(ScreenshotWindow);
            await Settle();
        }
    }

    /// <summary>A hit of several types in the log uses short type names (one type keeps its word), so «Рой» rows stay short.</summary>
    private Task LogShortTypes()
    {
        var shortened = CombatLogWindow.ShortTypes("попадание · 42 физическим + 275 молнией + 1 121 огнем");
        Check(shortened == "попадание · 42 физ. + 275 молн. + 1 121 огн.", $"«{shortened}»");
        var single = CombatLogWindow.ShortTypes("попадание · 1 121 огнем");
        Check(single == "попадание · 1 121 огнем", $"«{single}»");
        return Task.CompletedTask;
    }

    /// <summary>Gamepad screenshots for the UI review (window run only).</summary>
    private async Task GamepadScreenshots()
    {
        try
        {
            await LoadForCombat(WarriorMelee);
            await PadMode();
            Room.PlaceHero(Room.Dummy.TargetPosition(0) + new Vector3(1, 0, -1).Normalized() * 1.3f + TowardsCamera * 0.4f);
            await Fight(2.0, close: false);
            await Shot("task-13-room-hud-gamepad");

            PadTap(JoyButton.DpadLeft);
            await Settle();
            var view = _main.Passives!.View;
            view.FitToView();
            view.GrabFocus();
            if (view.Nodes.FirstOrDefault(n => n.Caption != null && view.StateOf(n.Id) == TreeNodeState.Available) is { } node) view.PadSelect(node.Id);
            await Shot("task-13-passives-gamepad");
            // The same zoom with the mouse: captions that show here must not vanish on the pad (curator round 1).
            Device.Set(InputKind.KeyboardMouse);
            await Shot("task-13-passives-mouse-same-zoom");
            Device.Set(InputKind.Gamepad);

            Host.FocusWindow(WindowIds.Meter);
            await Settle();
            Meter.Bars.GrabFocus();
            PadTap(JoyButton.DpadDown);
            await Shot("task-13-meter-gamepad");

            await PadMode();
            Host.FocusWindow(WindowIds.Log);
            await Settle();
            LogWindow.SkillOption.GrabFocus();
            await Shot("task-13-log-gamepad");

            await PadMode();
            PadTap(JoyButton.Start);
            await Shot("task-13-top-bar-gamepad");

            PadTap(JoyButton.B);
            PadTap(JoyButton.DpadRight);
            await Settle();
            await Shot("task-13-skills-gamepad");
        }
        finally
        {
            Host.FocusCombat();
            _main.CloseAllWindows();
            Device.Set(InputKind.KeyboardMouse);
            await CombatRestore();
        }
    }
}
