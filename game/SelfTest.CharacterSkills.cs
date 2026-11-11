using Dialoid.Game.Ui;
using Dialoid.Game.Input;
using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
    private async Task CharacterSkillsFocusNeighbors()
    {
        var root = new Control { Position = new Vector2(20, 100), Size = new Vector2(500, 80) };
        var outside = new Button { Text = "outside", Position = new Vector2(550, 100), Size = new Vector2(80, 40) };
        Host.AddChild(root);
        Host.AddChild(outside);
        try
        {
            var from = new Button { Text = "from", Size = new Vector2(80, 40) };
            var near = new Button { Text = "near", Position = new Vector2(100, 0), Size = new Vector2(80, 40) };
            var explicitTarget = new Button { Text = "explicit", Position = new Vector2(240, 0), Size = new Vector2(80, 40) };
            root.AddChild(from);
            root.AddChild(near);
            root.AddChild(explicitTarget);
            await Settle();
            from.FocusNeighborRight = from.GetPathTo(explicitTarget);
            Check(Host.Navigate(from, PadCommand.Right, root) == explicitTarget && explicitTarget.HasFocus(), "явный маршрут фокуса проигнорирован");
            explicitTarget.Disabled = true;
            Check(Host.Navigate(from, PadCommand.Right, root) == near, "недоступный сосед не вернул обычную навигацию");
            from.FocusNeighborRight = new NodePath("missing-control");
            Check(Host.Navigate(from, PadCommand.Right, root) == near, "устаревшая ссылка не вернула обычную навигацию");
            from.FocusNeighborRight = from.GetPathTo(outside);
            Check(Host.Navigate(from, PadCommand.Right, root) == near, "фокус вышел за корень окна");
        }
        finally
        {
            root.QueueFree();
            outside.QueueFree();
            Host.FocusCombat();
        }
    }

    private async Task CharacterSkillsCommands()
    {
        await LoadForCombat("warrior_berserker_whirlwind.json");
        Host.OpenWindow(WindowIds.Character);
        await Settle();
        var character = _main.Character!;
        var session = _main.Session!;
        var revision = session.Revision;
        var slider = character.FindChild("LevelSlider", true, false) as HSlider;
        Check(slider != null, "нет доступного ползунка уровня");
        slider!.GrabFocus();
        Check(character.SelectLevel(59).Ok && session.Build.Level == 59 && session.Revision > revision, "уровень не изменён командой сессии");
        await Settle();
        Check((character.FindChild("LevelSlider", true, false) as HSlider)?.HasFocus() == true, "пересоздание потеряло фокус уровня");
        await CharacterLinesMatch();
        Check(character.NumberFor(CharacterPanel.LevelKey)?.Line.Value == 59, "число уровня устарело");
        character.NumberFor("life")!.Open();
        Check(_main.Breakdown!.Current?.Line.Key == "life" && _main.Breakdown.Current.Revision == session.Revision, "разбор не соответствует ревизии живого числа");
        Check(character.SelectWeapon(null).Ok && session.Build.WeaponId == null, "оружие не снято командой");
        var item = session.Data.Items.Keys.First();
        var hadItem = session.Build.ItemIds.Contains(item);
        Check(character.ToggleItem(item).Ok && session.Build.ItemIds.Contains(item) != hadItem, "экипировка не изменена командой");
        await FreshBuild(20, WindowIds.Skills);
        var skills = _main.Skills!;
        skills.PadClosed();
        await Settle();
        revision = session.Revision;
        skills.CatalogButtons["cleave"].EmitSignal(Button.SignalName.Pressed);
        await Settle();
        Check(session.Revision == revision && skills.TreeSlot < 0, "просмотр неназначенного навыка изменил билд");
        Check(skills.AssignSkillButton is { Disabled: false }, "доступный навык нельзя назначить");
        skills.AssignSkillButton!.EmitSignal(Button.SignalName.Pressed);
        await Settle();
        var slot = skills.TreeSlot;
        Check(slot >= 0 && session.GetSlot(slot)?.SkillId == "cleave", "назначение не сохранило слот");
        Check(skills.SelectLevel(slot, 2).Ok, "уровень навыка не изменён");
        await Settle();
        var graph = skills.SkillGraph!;
        graph.ClickAt(graph.NodeLocalPosition("heavy_hand")!.Value);
        var plus = skills.FindChild("SkillNodeAdd", true, false) as Button;
        Check(plus is { Disabled: false }, "не доступно вложение в начальный узел");
        revision = session.Revision;
        graph.PadSelect("split");
        Check(skills.SelectedNodeId == "heavy_hand" && !plus!.Disabled && session.Revision == revision, "фокус узла изменил выбранное действие или билд");
        graph.PadInput(PadCommand.Accept);
        Check(skills.SelectedNodeId == "split" && session.Revision == revision, "A не выбрала узел без изменения билда");
        graph.PadSelect("heavy_hand");
        graph.PadInput(PadCommand.Accept);
        plus!.EmitSignal(Button.SignalName.Pressed);
        await Settle();
        Check(session.GetSlot(slot)!.NodePoints.GetValueOrDefault("heavy_hand") == 1, "ранг не изменён");
        Check(skills.RemovePoint(slot, "heavy_hand").Ok, "очко не снято");
        Check(skills.Undo(slot).Ok, "изменение не отменено");
        await Settle();
        Check(session.GetSlot(slot) == null, "undo не вернул состояние слота до назначения");
        await LoadForCombat("warrior_berserker_whirlwind.json");
    }

    private async Task CharacterSkillsHost()
    {
        foreach (var id in new[] { WindowIds.Character, WindowIds.Skills })
        {
            Host.OpenWindow(id);
            await Settle();
            var window = Host.Window(id);
            var side = window.GetRect();
            var content = window.Content;
            Host.SetExpanded(id, true);
            await Settle();
            Check(window.Expanded && window.Size.X > side.Size.X, $"{id}: нет полного рабочего пространства");
            Check(window.Content == content, $"{id}: подменено содержимое");
            Check(window.GetRect().End.X <= Host.Size.X + 1 && window.GetRect().End.Y <= Host.Size.Y + 1, $"{id}: разворот за экраном");
            Host.SetExpanded(id, false);
            await Settle();
            Check(window.GetRect().IsEqualApprox(side), $"{id}: потеряна боковая геометрия");
            Host.CloseAll();
        }
    }

    private async Task CharacterSkillsReference()
    {
        var original = _main.GetTree().Root.Size;
        try
        {
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1600, 900) })
            {
                if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = size;
                else DisplayServer.WindowSetSize(size);
                await Settle();
                UseSelfTestWindowConfig();
                await LoadForCombat("warrior_berserker_whirlwind.json");
                Check(_main.Session!.Violations.Count == 0, "эталон невалиден");
                Host.CloseAll();
                Host.OpenWindow(WindowIds.Character);
                await Settle();
                await CharacterLinesMatch();
                CheckCharacterSkillsBounds(WindowIds.Character);
                CheckCharacterSkillNumberText(_main.Character!.Numbers);
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-character-side");
                foreach (var scroll in _main.Character!.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>()) scroll.ScrollVertical = int.MaxValue;
                await Settle();
                CheckCharacterSkillsBounds(WindowIds.Character);
                CheckCharacterSkillNumberText(_main.Character.Numbers);
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-character-side-bottom");
                foreach (var scroll in _main.Character.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>()) scroll.ScrollVertical = 0;
                Host.SetExpanded(WindowIds.Character, true);
                await Settle();
                CheckCharacterSkillsBounds(WindowIds.Character);
                CheckCharacterSkillNumberText(_main.Character.Numbers);
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-character-expanded");
                _main.Character!.NumberFor("life")!.Open();
                Check(_main.Breakdown!.Current?.Line.Key == "life", "число здоровья не открыло настоящий разбор");
                Host.CloseWindow(WindowIds.Breakdown);
                Check(_main.Character.SelectLevel(1).Ok, "не подготовлена проверка недопустимого билда");
                await Settle();
                Check(_main.Character.ShownViolations.Count > 0, "ошибки билда скрыты");
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-character-invalid");
                Host.SetExpanded(WindowIds.Character, false);
                await LoadForCombat(WarriorMelee);
                Host.CloseAll();
                Host.OpenWindow(WindowIds.Skills);
                _main.Skills!.PadClosed();
                await Settle();
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-skills-catalog");
                CheckCharacterSkillsBounds(WindowIds.Skills);
                _main.Skills.CatalogButtons["cleave"].EmitSignal(Button.SignalName.Pressed);
                await Settle();
                Check(_main.Skills.BrowsedSkillId == "cleave", "не открыт реальный навык эталона");
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-skills-detail");
                foreach (var scroll in _main.Skills.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>()) scroll.ScrollVertical = int.MaxValue;
                await Settle();
                CheckCharacterSkillsBounds(WindowIds.Skills);
                CheckCharacterSkillNumberText(_main.Skills.SkillNumbers);
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-skills-detail-bottom");
                Host.SetExpanded(WindowIds.Skills, true);
                await Settle();
                foreach (var scroll in _main.Skills.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>()) scroll.ScrollVertical = 0;
                await Settle();
                CheckCharacterSkillsBounds(WindowIds.Skills);
                _main.Skills.SkillGraph!.FitToView();
                await Settle();
                CheckFittedSkillGraphVisible();
                CheckCharacterSkillNumberText(_main.Skills.SkillNumbers);
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-skills-expanded");
                Device.Set(Dialoid.Game.Input.InputKind.Gamepad);
                Host.FocusWindow(WindowIds.Skills);
                // Host restores its remembered field and the gamepad footer resizes the viewport.
                // Let both queued layout/focus operations finish before choosing the graph overview.
                await Settle();
                _main.Skills.SkillGraph.GrabFocus();
                await Settle();
                _main.Skills.SkillGraph.PadSelect("heavy_hand");
                await Settle();
                _main.Skills.GetChild<ScrollContainer>(0).ScrollVertical = 0;
                await Settle();
                CheckFittedSkillGraphVisible();
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-skills-pad");
                PadTap(JoyButton.B);
                await Settle();
                Check(_main.Skills.BrowsedSkillId == null && _main.Skills.CatalogButtons["cleave"].HasFocus(), "B потеряла возврат к выбранному навыку");
                Host.SetExpanded(WindowIds.Skills, false);
                Host.FocusCombat();
                Device.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
                await FreshBuild(1, WindowIds.Skills);
                var locked = _main.Skills.CatalogButtons.Keys.First(id => _main.Session.Data.Skills[id].UnlockLevel > 1);
                var revision = _main.Session.Revision;
                _main.Skills.CatalogButtons[locked].EmitSignal(Button.SignalName.Pressed);
                await Settle();
                Check(_main.Session.Revision == revision && _main.Skills.AssignSkillButton is { Disabled: true }, "просмотр закрытого навыка изменяет билд или разрешает назначение");
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-skills-locked");
                await FreshBuild(20, WindowIds.Skills);
                _main.Skills.PadClosed();
                await Settle();
                _main.Skills.CatalogButtons["cleave"].EmitSignal(Button.SignalName.Pressed);
                await Settle();
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-skills-unassigned");
                Check(_main.Skills.SelectSkill(0, "cleave").Ok && _main.Skills.SelectLevel(0, 2).Ok && _main.Skills.AddPoint(0, "heavy_hand").Ok, "не подготовлен настоящий вложенный ранг");
                await Settle();
                _main.Skills.SkillGraph!.PadSelect("heavy_hand");
                await Settle();
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-skills-point");
                await LoadForCombat("warrior_berserker_whirlwind.json");
                Host.CloseAll();
                Host.FocusCombat();
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"charskills-{size.Y}-world-hud");
            }
        }
        finally
        {
            Host.SetExpanded(WindowIds.Character, false);
            Host.SetExpanded(WindowIds.Skills, false);
            Host.CloseAll();
            Host.FocusCombat();
            Device.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
            if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = original;
            else DisplayServer.WindowSetSize(original);
        }
    }

    private void CheckCharacterSkillsBounds(string id)
    {
        var rect = Host.Window(id).GetGlobalRect();
        Check(Host.Window(id).Content.GetGlobalRect().End.X <= rect.End.X + 1, $"{id}: содержимое шире окна");
        foreach (var control in Host.Window(id).Content.FindChildren("*", "Control", true, false).OfType<Control>())
        {
            if (!control.IsVisibleInTree() || control.FocusMode != Control.FocusModeEnum.All) continue;
            var at = control.GetGlobalRect();
            Check(at.Position.X >= rect.Position.X - 1 && at.End.X <= rect.End.X + 1, $"{id}: {control.Name} обрезан по ширине");
        }
    }

    private static void CheckCharacterSkillNumberText(IEnumerable<NumberLabel> numbers)
    {
        foreach (var number in numbers.Where(n => n.IsVisibleInTree()))
        {
            var face = number.GetThemeStylebox("normal");
            var width = Mathf.Max(1, number.Size.X - face.GetContentMargin(Side.Left) - face.GetContentMargin(Side.Right));
            var font = number.GetThemeFont("font");
            var text = font.GetMultilineStringSize(number.Text, HorizontalAlignment.Left, width, number.GetThemeFontSize("font_size"));
            var neededHeight = text.Y + face.GetContentMargin(Side.Top) + face.GetContentMargin(Side.Bottom);
            Check(number.Size.Y + 2 >= neededHeight, $"число {number.Line.Key} обрезано по высоте: {number.Size.Y} вместо {neededHeight}");
        }
    }

    private void CheckFittedSkillGraphVisible()
    {
        var graph = _main.Skills!.SkillGraph!;
        Node? ancestor = graph.GetParent();
        while (ancestor != null && ancestor is not ScrollContainer) ancestor = ancestor.GetParent();
        var scroll = ancestor as ScrollContainer ?? throw new Failure("у дерева навыка нет области прокрутки");
        var visible = graph.GetGlobalRect().Intersection(scroll.GetGlobalRect());
        foreach (var node in graph.Nodes)
        {
            var radius = node.Radius * graph.Zoom + 4;
            var icon = new Rect2(graph.GlobalPosition + graph.WorldToLocal(node.Position) - Vector2.One * radius, Vector2.One * radius * 2);
            Check(visible.Grow(1).Encloses(icon), $"иконка/ранг {node.Id} обрезаны: {icon}, видимая область {visible}");
        }
    }
}
