using Dialoid.Core.Simulation;
using Dialoid.Game.Room;
using Dialoid.Game.Ui;
using Dialoid.Game.Ui.Hud;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Self-test steps of the windows over the room and the combat screen (plan 4 task 10). Keys and mouse go through the real
/// input path (<see cref="Viewport.PushInput"/> on the main viewport: GUI, then the room's SubViewport), so the checks cover
/// the routing between windows, text fields, the fight and the camera.
/// </summary>
public sealed partial class SelfTest
{
    /// <summary>The self-test keeps window positions in its own file, never in the owner's.</summary>
    public const string SelfTestWindowsConfig = "user://windows-selftest.cfg";

    private IEnumerable<Step> WindowSteps()
    {
        yield return new("windows.layout", WindowsLayout);
        yield return new("windows.hotkeys", WindowsHotkeys);
        yield return new("windows.remember-position", WindowsRemember);
        yield return new("windows.focus-blocks-keys", WindowsFocusBlocksKeys);
        yield return new("windows.mouse-over-window", WindowsMouseOverWindow);
        yield return new("hud.combat-screen", HudCombatScreen);
        yield return new("hud.ailment-on-plate", HudAilmentOnPlate);
        yield return new("hud.plate-in-frame-at-spawn", HudPlateAtSpawn);
    }

    private void UseSelfTestWindowConfig()
    {
        var windows = _main.Windows;
        windows.CloseAll();
        windows.ConfigPath = SelfTestWindowsConfig;
        var file = ProjectSettings.GlobalizePath(SelfTestWindowsConfig);
        if (File.Exists(file)) File.Delete(file);
        windows.LoadConfig();
    }

    private void PushKey(Key key, char unicode = '\0')
    {
        var viewport = _main.GetViewport();
        viewport.PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Unicode = unicode, Pressed = true });
        viewport.PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Unicode = unicode, Pressed = false });
    }

    private void PushButton(Vector2 at, MouseButton button)
    {
        var viewport = _main.GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        viewport.PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at });
        viewport.PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
    }

    /// <summary>Windows hidden at start, their edges, opaque backgrounds, the key on every top-bar button and window title.</summary>
    private async Task WindowsLayout()
    {
        UseSelfTestWindowConfig();
        var windows = _main.Windows;
        var bar = _main.Bar ?? throw new Failure("нет верхней панели");
        Check(WindowHost.Specs.All(s => windows.Has(s.Id)), "не у каждого окна есть панель");
        foreach (var spec in WindowHost.Specs)
        {
            var key = GameWindow.KeyName(spec.Key);
            Check(bar.Buttons[spec.Id].Text == spec.Title && bar.KeyLabel(spec.Id).Text == key, $"кнопка «{bar.Buttons[spec.Id].Text}» с клавишей «{bar.KeyLabel(spec.Id).Text}»");
            Check(bar.Buttons[spec.Id].FocusMode == Control.FocusModeEnum.None, $"кнопка «{spec.Title}» берет фокус клавиатуры");
            windows.OpenWindow(spec.Id);
        }
        await Settle();
        var screen = _main.Size;
        foreach (var spec in WindowHost.Specs)
        {
            var w = windows.Window(spec.Id);
            var rect = w.GetGlobalRect();
            Check(w.GetThemeStylebox("panel") is StyleBoxFlat { BgColor.A: >= 0.95f }, $"фон окна «{spec.Title}» прозрачный");
            Check(rect.Position.Y >= bar.Size.Y - 0.5f && rect.End.X <= screen.X + 0.5f && rect.End.Y <= screen.Y + 0.5f && rect.Position.X >= -0.5f,
                $"окно «{spec.Title}» {rect} вне экрана {screen} или под верхней панелью");
            var left = rect.Position.X <= WindowHost.Margin + 0.5f;
            var right = rect.End.X >= screen.X - WindowHost.Margin - 0.5f;
            switch (spec.Edge)
            {
                case WindowEdge.Left: Check(left, $"окно «{spec.Title}» не у левого края: {rect}"); break;
                case WindowEdge.Right or WindowEdge.RightTop: Check(right, $"окно «{spec.Title}» не у правого края: {rect}"); break;
                case WindowEdge.LeftBottom:
                    Check(left && rect.End.Y >= screen.Y - WindowHost.Margin - 0.5f, $"журнал не слева внизу: {rect}");
                    break;
            }
            // The hero stands in the centre: a window at an edge never covers it; a side window takes at most a third.
            Check(!rect.HasPoint(screen / 2), $"окно «{spec.Title}» закрывает центр экрана: {rect}, минимум панели {w.Content.GetCombinedMinimumSize()}");
            Check(rect.Size.X <= screen.X * WindowHost.MaxSideFraction + 0.5f, $"окно «{spec.Title}» шире трети экрана: {rect.Size.X}, минимум панели {w.Content.GetCombinedMinimumSize()}");
        }
        GD.Print("SELFTEST NOTE ширина окон: " + string.Join(", ", WindowHost.Specs.Select(s => $"{s.Title} {windows.Window(s.Id).Size.X:0}")) +
                 $"; верхняя панель {bar.GetCombinedMinimumSize().X:0}");

        // The level number beside the slider is shown whole (no «…»).
        var level = _main.Character!.NumberFor(CharacterPanel.LevelKey) ?? throw new Failure("нет числа уровня");
        var font = level.GetThemeFont("font");
        var textWidth = font.GetStringSize(level.Text, HorizontalAlignment.Left, -1, level.GetThemeFontSize("font_size")).X;
        Check(level.IsVisibleInTree() && level.Size.X >= textWidth, $"число уровня «{level.Text}» шириной {level.Size.X} при тексте {textWidth}");
        // The window theme gives plain buttons a face; a flat NumberLabel keeps the default margins (it does not widen).
        var defaults = ThemeDB.GetDefaultTheme().GetStylebox("normal", "Button");
        var hover = level.GetThemeStylebox("hover");
        Check(level.Flat && hover.GetContentMargin(Side.Left) == defaults.GetContentMargin(Side.Left),
            $"поля числа {hover.GetContentMargin(Side.Left)} вместо {defaults.GetContentMargin(Side.Left)}");
        Check(bar.Buttons.Values.All(b => b.ButtonPressed), "кнопки открытых окон не нажаты");
        windows.CloseAll();
        await Settle();
        Check(bar.Buttons.Values.All(b => !b.ButtonPressed) && windows.OpenWindows.Count == 0, "окна не закрылись");
        // The dummy list: the presets of data/dummies.json in file order; choosing one restarts the fight on it.
        Check(bar.DummyIds.SequenceEqual(_main.Session!.Data.Dummies.Keys), "список манекенов не из dummies.json");
        bar.SelectDummy("swarm");
        Check(Training.DummyId == "swarm" && Room.Dummy.PresetId == "swarm", $"выбор «Рой» в списке не сменил манекен: {Training.DummyId}");
        bar.SelectDummy(TrainingRoom.DefaultDummyId);
        await Settle();
        Check(bar.DummyOptions.Selected == bar.DummyIds.ToList().IndexOf(TrainingRoom.DefaultDummyId), "список не вернулся к тренировочному");
        // Every button's caption fits: the bar is not wider than the screen.
        Check(bar.GetCombinedMinimumSize().X <= screen.X + 0.5f, $"верхняя панель шире экрана: {bar.GetCombinedMinimumSize().X} > {screen.X}");
    }

    /// <summary>C opens and closes the character, N the ascendancy; Esc closes the top window; every hotkey toggles its window.</summary>
    private async Task WindowsHotkeys()
    {
        var windows = _main.Windows;
        windows.CloseAll();
        _main.GetViewport().GuiReleaseFocus();
        PushKey(Key.C);
        await NextFrame();
        Check(windows.IsOpen(WindowIds.Character), "C не открыла «Персонажа»");
        PushKey(Key.C);
        await NextFrame();
        Check(!windows.IsOpen(WindowIds.Character), "C не закрыла «Персонажа»");

        PushKey(Key.N);
        PushKey(Key.C);
        await NextFrame();
        Check(windows.IsOpen(WindowIds.Ascendancy) && windows.IsOpen(WindowIds.Character), "N и C не открыли асцендант и персонажа");
        Check(windows.TopWindow == WindowIds.Character, $"верхнее окно {windows.TopWindow}, а не персонаж");
        PushKey(Key.Escape);
        await NextFrame();
        Check(!windows.IsOpen(WindowIds.Character) && windows.IsOpen(WindowIds.Ascendancy), "Esc закрыл не верхнее окно");
        PushKey(Key.Escape);
        await NextFrame();
        Check(windows.OpenWindows.Count == 0, "второй Esc не закрыл асцендант");

        foreach (var spec in WindowHost.Specs)
        {
            PushKey(spec.Key);
            await NextFrame();
            Check(windows.IsOpen(spec.Id), $"клавиша {GameWindow.KeyName(spec.Key)} не открыла «{spec.Title}»");
            PushKey(spec.Key);
            await NextFrame();
            Check(!windows.IsOpen(spec.Id), $"клавиша {GameWindow.KeyName(spec.Key)} не закрыла «{spec.Title}»");
        }
        Check(WindowHost.Specs.All(s => s.Key != Key.D && s.Key != Key.A && s.Key != Key.W && s.Key != Key.S), "клавиша окна занята шагом");
        Check(WindowHost.Specs.Select(s => s.Key).Distinct().Count() == WindowHost.Specs.Count, "у двух окон одна клавиша");
    }

    /// <summary>A moved window opens where it was left, also after the file is read again.</summary>
    private async Task WindowsRemember()
    {
        var windows = _main.Windows;
        windows.OpenWindow(WindowIds.Builds);
        await Settle();
        var target = new Vector2(240, _main.Bar!.Size.Y + 60);
        windows.MoveWindow(WindowIds.Builds, target);
        windows.CloseWindow(WindowIds.Builds);
        windows.LoadConfig(); // as at the next start
        Check(windows.RememberedPosition(WindowIds.Builds) == target, $"в {SelfTestWindowsConfig} позиция {windows.RememberedPosition(WindowIds.Builds)}");
        windows.OpenWindow(WindowIds.Builds);
        await Settle();
        Check(windows.Window(WindowIds.Builds).Position == target, $"окно открылось в {windows.Window(WindowIds.Builds).Position}, а не в {target}");
        windows.CloseAll();
        UseSelfTestWindowConfig(); // the default edges again for the other steps
    }

    /// <summary>
    /// Review Focus 3: with the focus in the seed field, digit 1 goes into the field and not into the fight, C types a letter
    /// instead of opening a window; Enter takes the focus off, and then 1 reaches the fight.
    /// </summary>
    private async Task WindowsFocusBlocksKeys()
    {
        await LoadForCombat(WarriorMelee);
        PlaceNearDummy();
        var slot = SlotOf("battle_cry");
        var digit = Key.Key1 + (slot - 1);
        var windows = _main.Windows;
        windows.OpenWindow(WindowIds.Simulation);
        await Settle();
        var field = _main.Simulation!.Seed.GetLineEdit();
        var before = field.Text;
        var seedBefore = _main.Simulation.Seed.Value;
        try
        {
            field.GrabFocus();
            await NextFrame();
            var typedOn = field.Text; // the SpinBox keeps its value in the field
            field.CaretColumn = typedOn.Length;
            var lines = Training.Log.Count;
            PushKey(digit, (char)('0' + slot));
            PushKey(Key.C, 'c');
            await Run(0.4);
            Check(field.Text == $"{typedOn}{slot}c", $"в поле сида «{field.Text}», а не «{typedOn}{slot}c»");
            Check(Training.Log.Count == lines && Training.State!.CooldownLeftBySlot.GetValueOrDefault(slot) == 0, "цифра при фокусе в поле ушла в бой");
            Check(!windows.IsOpen(WindowIds.Character), "C при фокусе в поле открыла окно");

            PushKey(Key.Escape);
            await NextFrame();
            Check(!field.HasFocus() && windows.IsOpen(WindowIds.Simulation), "Esc в поле не снял фокус или закрыл окно");
            field.GrabFocus();
            await NextFrame();
            PushKey(Key.Enter);
            await Settle();
            Check(!field.HasFocus(), "Enter не снял фокус с поля");
            field.GrabFocus();
            await NextFrame();
            PushButton(new Vector2(_main.Size.X * 0.5f, _main.Size.Y * 0.4f), MouseButton.Left);
            await NextFrame();
            Check(!field.HasFocus(), "клик по полу не снял фокус с поля");

            PushKey(digit, (char)('0' + slot));
            await Run(0.4);
            Check(Training.Log.Count > lines, "без фокуса цифра не дошла до боя");
        }
        finally
        {
            field.ReleaseFocus();
            field.Text = before;
            _main.Simulation.Seed.Value = seedBefore;
            windows.CloseAll();
        }
    }

    /// <summary>A click and the wheel over a window neither move the hero nor zoom; outside the windows they do.</summary>
    private async Task WindowsMouseOverWindow()
    {
        await LoadForCombat(WarriorMelee);
        Driver.SetProcess(true);
        var room = Room;
        var rig = room.CameraRig;
        room.PlaceHero(room.HeroSpawn);
        rig.ResetZoom();
        var windows = _main.Windows;
        windows.OpenWindow(WindowIds.Character);
        await Settle();
        // The window's own margin (no button there: a click on a number would open «Разбор»).
        var rect = windows.Window(WindowIds.Character).GetGlobalRect();
        var over = new Vector2(rect.Position.X + 3, rect.End.Y - 5);
        var start = room.Hero.Position;
        PushButton(over, MouseButton.Left);
        PushButton(over, MouseButton.WheelUp);
        PushButton(over, MouseButton.WheelDown);
        PushButton(over, MouseButton.WheelDown);
        await Settle();
        Check(room.Hero.MoveTarget == null && room.Hero.Position.DistanceTo(start) < 0.01f, "клик над окном сдвинул героя");
        Check(Mathf.IsEqualApprox(rig.Zoom, CameraRig.DefaultZoom), $"колесо над окном изменило зум: {rig.Zoom}");

        // The same events over the floor reach the camera and the hero (the routing works at all).
        var floor = new Vector2(_main.Size.X * 0.68f, _main.Size.Y * 0.42f);
        Check(windows.WindowAt(floor) == null, $"точка пола {floor} под окном {windows.WindowAt(floor)}: {windows.Window(WindowIds.Character).GetGlobalRect()}");
        PushButton(floor, MouseButton.WheelDown);
        await NextFrame();
        Check(rig.Zoom > CameraRig.DefaultZoom, "колесо над полом не изменило зум");
        PushButton(floor, MouseButton.Left);
        await NextFrame();
        Check(room.Hero.MoveTarget != null, "клик по полу не повел героя");
        rig.ResetZoom();
        room.PlaceHero(room.HeroSpawn);
        windows.CloseAll();
        Driver.SetProcess(false);
        await Settle();
    }

    /// <summary>
    /// The slot bar follows <see cref="TrainingRoom.State"/>: cooldown seconds equal the state's, a red frame when the resource
    /// is short, the queued press and the held channel highlighted, a blink on a refusal; Battle Cry's buff with its timer.
    /// </summary>
    private async Task HudCombatScreen()
    {
        var hud = _main.Hud ?? throw new Failure("нет экрана боя");
        await LoadForCombat(WarriorMelee);
        PlaceNearDummy();
        hud.Refresh();
        var state = Training.State!;
        Check(hud.Resource.Text == ResourceBarView.TextOf(Training.ResourceName, state.Resource, state.ResourceMax) && hud.Resource.Text.StartsWith(Training.ResourceName, StringComparison.Ordinal),
            $"полоса ресурса «{hud.Resource.Text}»");
        var shortSeen = false;
        foreach (var view in hud.Slots)
        {
            var info = Training.Slots[view.Number - 1];
            Check(view.Info == info, $"слот {view.Number} показывает не тот скилл");
            Check(view.NameText == (info?.Name ?? "пусто"), $"слот {view.Number}: «{view.NameText}»");
            // The red frame is the core's own resource check (live costs and buffs), never a formula of the game layer.
            var expectShort = info != null && state.ResourceShortSlots.Contains(view.Number);
            Check(view.ResourceShort == expectShort && (view.FrameColor == SkillSlotView.ShortColor) == expectShort,
                $"слот {view.Number}: красная рамка {view.ResourceShort}, в State {expectShort}");
            Check(info == null || view.CostNow == state.CostBySlot[view.Number], $"слот {view.Number}: стоимость {view.CostNow}");
            shortSeen |= expectShort;
            if (info != null)
                Check(view.TooltipText.Contains("Стоимость", StringComparison.Ordinal) && view.TooltipText.Contains("Перезарядка", StringComparison.Ordinal)
                      && (view.TooltipText.Contains("Время применения", StringComparison.Ordinal) || view.TooltipText.Contains("Канал", StringComparison.Ordinal)),
                    $"подсказка слота {view.Number}: «{view.TooltipText}»");
        }
        // Fury starts empty: the spenders must be short at the start of the fight.
        Check(state.Resource == 0 && shortSeen, $"в начале боя (ярость {state.Resource}) нет слота с красной рамкой");

        var cry = SlotOf("battle_cry");
        Check(Driver.PressSlot(cry).Accepted, "Боевой клич не принят");
        await Run(0.8);
        hud.Refresh();
        state = Training.State!;
        var view1 = hud.Slots[cry - 1];
        Check(state.CooldownLeftBySlot.TryGetValue(cry, out var left) && left > 0, "у Боевого клича нет перезарядки в State");
        Check(view1.CooldownLeft == left && view1.CooldownText == CombatHud.Seconds(left), $"слот показывает {view1.CooldownLeft} «{view1.CooldownText}», в State {left}");
        var buff = hud.HeroEffects.FirstOrDefault(e => e.Id == "battle_cry");
        Check(buff is { TimeLeft: > 0 }, "бафф Боевого клича не показан с таймером: " + string.Join(", ", hud.HeroTexts));
        Check(hud.HeroTexts.Any(t => t.StartsWith("Боевой клич ", StringComparison.Ordinal) && t.EndsWith(" с", StringComparison.Ordinal)), "текст баффа: " + string.Join(", ", hud.HeroTexts));
        // «Воодушевление» (−20 % cost) runs: the slot's cost is the core's live one, below the sheet's, and the frame follows it.
        if (state.Buffs.ContainsKey("inspiration"))
        {
            var spender = hud.Slots.First(v => v.Info is { Cost: > 0, IsChannel: false });
            Check(spender.CostNow < spender.Info!.Cost && spender.ResourceShort == state.ResourceShortSlots.Contains(spender.Number),
                $"«{spender.Info.Name}»: стоимость {spender.CostNow} при листе {spender.Info.Cost}, рамка {spender.ResourceShort}");
        }

        // A refusal blinks the slot (the text over the hero comes from the same event).
        Check(!Driver.PressSlot(cry).Accepted, "повторный клич принят");
        await Run(FrameSec * 2);
        hud.Refresh();
        var blinking = hud.Slots[cry - 1];
        Check(blinking.BlinkLeft > 0, "слот не мигнул при отказе");
        Check(blinking.FrameColor != SkillSlotView.ShortColor || blinking.ResourceShort, "мигание отказа выглядит как нехватка ресурса");
        // The refusal text never lies on the dummy plate (the hero stands beside the dummy here).
        Check(!hud.PlateShown || !hud.RefusalRect.Intersects(hud.PlateRect), $"текст отказа {hud.RefusalRect} на плашке {hud.PlateRect}");
        // A damage number never starts under the plate: it is lifted above the plate's top edge.
        if (hud.PlateShown)
        {
            var start = _main.RoomToScreen(hud.LiftAbovePlate(Room.Dummy.HitPoint(0) + new Vector3(0, 0.6f, 0)));
            Check(start is { } s && s.Y <= hud.PlateRect.Position.Y - CombatHud.NumberGap + 0.5f, $"число начинается в {start}, плашка {hud.PlateRect}");
        }
        Check(Room.Hero.RefusalText != null && hud.RefusalShown == Room.Hero.RefusalText, $"над героем текст отказа «{hud.RefusalShown}», у героя «{Room.Hero.RefusalText}»");

        // The held channel, then a press queued while busy.
        var ww = SlotOf("whirlwind");
        await Run(1.5, until: () => Training.State is { Busy: false });
        Driver.PressSlot(ww);
        await Run(0.3, until: () => Training.State?.HeldSlot == ww);
        hud.Refresh();
        Check(Training.State!.HeldSlot == ww && hud.Slots[ww - 1].Held, "зажатый канал не подсвечен");
        Driver.ReleaseSlot(ww);
        await Run(0.05);

        var cleave = SlotOf("cleave");
        await Run(2.0, until: () => Training.State is { Busy: false });
        Driver.PressSlot(cleave);
        await Run(FrameSec * 3);
        var queued = false;
        for (int i = 0; i < 30 && !queued; i++)
        {
            if (Training.State is { Busy: true }) queued = Driver.PressSlot(cleave).Queued;
            if (!queued) await Run(FrameSec);
        }
        hud.Refresh();
        Check(queued && Training.State!.QueuedSlot == cleave && hud.Slots[cleave - 1].Queued, "нажатие в очереди не подсвечено");
        await Run(1.0);
    }

    /// <summary>Ignite from the fire builds is on the dummy plate with stacks and timer.</summary>
    private async Task HudAilmentOnPlate()
    {
        var hud = _main.Hud!;
        foreach (var (file, skill) in new[] { (WarriorMelee, "cleave"), (Pyromancer, "fireball") })
        {
            await LoadForCombat(file);
            PlaceNearDummy(skill == "fireball" ? 4f : 1f);
            var slot = SlotOf(skill);
            await Run(10.0, _ => { if (Training.State is { Busy: false }) Driver.PressSlot(slot); },
                until: () => Training.State!.TargetEffects.Count > 0 && Training.State.TargetEffects[0].Any(e => e.Id == "ignite"));
            if (Training.State!.TargetEffects.Count == 0 || Training.State.TargetEffects[0].All(e => e.Id != "ignite")) continue;
            hud.Refresh();
            var ignite = Training.State.TargetEffects[0].First(e => e.Id == "ignite");
            var text = EffectChip.TextOf(ignite, BreakdownTextAilment("ignite"));
            Check(hud.PlateTexts.Contains(text), $"на плашке нет «{text}»: " + string.Join(", ", hud.PlateTexts));
            Check(text.StartsWith("Поджог", StringComparison.Ordinal) && (ignite.TimeLeft == null || text.EndsWith(" с", StringComparison.Ordinal)), $"текст поджога «{text}»");
            Check(hud.PlateName == Dialoid.Sandbox.BreakdownText.DummyName(_main.Session!.Data, Training.DummyId), $"имя на плашке «{hud.PlateName}»");
            GD.Print($"SELFTEST NOTE плашка манекена ({file}): {hud.PlateName}: {string.Join(", ", hud.PlateTexts)}");
            return;
        }
        throw new Failure("поджог не наложился ни огненным Рассечением, ни Огненным шаром за 10 с");
    }

    private static string BreakdownTextAilment(string id) => Dialoid.Sandbox.BreakdownText.AilmentName(id);

    /// <summary>The art review's check: at the spawn and the default zoom the dummy and its plate are fully in the frame.</summary>
    private async Task HudPlateAtSpawn()
    {
        var hud = _main.Hud!;
        await LoadForCombat(WarriorMelee);
        _main.CloseAllWindows();
        var room = Room;
        room.PlaceHero(room.HeroSpawn);
        room.CameraRig.ResetZoom();
        room.CameraRig.Follow();
        await Settle();
        hud.Refresh();
        await NextFrame();
        hud.Refresh();
        var screen = new Rect2(0, _main.Bar!.Size.Y, _main.Size.X, _main.Size.Y - _main.Bar.Size.Y);
        Check(hud.PlateShown, "плашка манекена не видна");
        var plate = hud.PlateRect;
        Check(screen.Encloses(plate), $"плашка {plate} не целиком в кадре {screen}");
        var feet = _main.RoomToScreen(room.Dummy.TargetPosition(0));
        Check(feet is { } f && screen.HasPoint(f), $"ноги манекена вне кадра: {feet}");
        GD.Print($"SELFTEST NOTE плашка при появлении: {plate}, ноги манекена {feet}, экран {_main.Size}");
    }

    /// <summary>
    /// Screenshots for the UI review (window run only): the room at the spawn with the combat screen; the screen mid-fight
    /// (a slot on cooldown, Battle Cry's buff with its timer, ignite on the dummy plate, a refusal over the hero); the passives
    /// window over the room; the character and the breakdown side by side; the top bar.
    /// </summary>
    private async Task WindowScreenshots()
    {
        var room = Room;
        var driver = Driver;
        try
        {
            await LoadForCombat(WarriorMelee);
            _main.CloseAllWindows();
            room.PlaceHero(room.HeroSpawn);
            room.CameraRig.ResetZoom();
            await Shot("task-10-room-spawn-hud");
            await Shot("task-10-top-bar", cropHeight: 64);

            room.PlaceHero(room.Dummy.TargetPosition(0) + new Vector3(1, 0, -1).Normalized() * 1.3f + TowardsCamera * 0.4f); // beside the dummy, both visible
            int cry = SlotOf("battle_cry"), cleave = SlotOf("cleave");
            Check(driver.PressSlot(cry).Accepted, "Боевой клич не принят");
            await Run(0.7); // the press fires at the next free step; a newer press would replace it
            await Run(6.0, _ => { if (Training.State is { Busy: false }) driver.PressSlot(cleave); },
                until: () => Training.State!.Time > 3 && Training.State.TargetEffects.Count > 0 && Training.State.TargetEffects[0].Any(e => e.Id == "ignite"));
            // Spend the fury on Crush until some spender is short (a red frame in the shot).
            var crush = SlotOf("crush");
            await Run(5.0, _ => { if (Training.State is { Busy: false }) driver.PressSlot(crush); },
                until: () => Training.State!.ResourceShortSlots.Count > 0);
            await Run(0.6, until: () => Training.State is { Busy: false });
            var again = driver.PressSlot(cry); // refused: on cooldown, the buff still runs
            GD.Print($"SELFTEST NOTE кадр боя: время {Training.State!.Time:0.00}, повторный клич {again.Refusal}, перезарядка {Training.State.CooldownLeftBySlot.GetValueOrDefault(cry):0.0}, баффы {string.Join(", ", Training.State.HeroEffects.Select(e => e.Id + "=" + e.TimeLeft))}");
            await Run(FrameSec * 3);
            await Shot("task-10-hud-fight");

            _main.OpenWindow(WindowIds.Passives);
            await Settle();
            _main.Passives!.View.FitToView();
            await Shot("task-10-passives-window");
            var view = _main.Passives.View;
            var shown = view.LayoutCaptions().Select(p => p.Id).ToHashSet();
            GD.Print("SELFTEST NOTE подписи в окне пассивок: нет " + string.Join(", ", view.Nodes.Where(n => n.Caption != null && !shown.Contains(n.Id))
                .Select(n => $"{n.Caption} {view.NodeLocalPosition(n.Id)} r={n.Radius * view.Zoom:0.0}")) + $" (вид {view.Size})");

            _main.CloseAllWindows();
            _main.OpenWindow(WindowIds.Character);
            _main.Character!.NumberFor("life")!.Open();
            await Shot("task-10-two-windows");
        }
        finally
        {
            _main.CloseAllWindows();
            await CombatRestore();
        }
    }
}
