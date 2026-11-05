using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
    private static bool SameFloor(Dialoid.Core.Tower.FloorSpec a, Dialoid.Core.Tower.FloorSpec b) =>
        a.TypeId == b.TypeId && a.Variant == b.Variant && a.BiomeId == b.BiomeId && a.Mods.SequenceEqual(b.Mods);

    private void CaptureTower(string name)
    {
        if (_screenshotDir == null || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(_screenshotDir);
        _main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_screenshotDir, name));
    }
    private IEnumerable<Step> TowerSteps()
    {
        foreach (string gridName in new[] { "ActivityGrid", "CurrencyGrid" })
            yield return new Step("tower.wheel-" + gridName, async () =>
            {
                var board = _main.TowerBoard!;
                var windowSize = DisplayServer.WindowGetSize();
                var rootSize = _main.GetTree().Root.Size;
                try
                {
                    board.SwitchRank(11);
                    DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                    if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = new Vector2I(1280, 720);
                    _main.OpenTowerBoard();
                    await Settle();
                    Check(board.Size.X <= 1280 && board.Size.Y <= 720, "сценарий должен выполняться в 720p");
                    var grid = (GridContainer)board.FindChild(gridName, true, false)!;
                    var first = grid.GetChildren().OfType<Button>().First(b => !b.Disabled);
                    Node? ancestor = grid.GetParent();
                    while (ancestor is not ScrollContainer) ancestor = ancestor!.GetParent();
                    var scroll = (ScrollContainer)ancestor;
                    first.GrabFocus();
                    await Settle();
                    var viewport = _main.GetViewport();
                    void Mouse(MouseButton button, bool pressed, Vector2 point) => viewport.PushInput(
                        new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, Position = point, GlobalPosition = point }, true);
                    var click = first.GetGlobalRect().GetCenter();
                    Mouse(MouseButton.Left, true, click);
                    Mouse(MouseButton.Left, false, click);
                    await Settle();
                    Check(first.HasFocus(), gridName + ": клик не установил фокус");
                    string? selected = board.SelectedInventoryId;
                    bool inventoryTarget = board.InventoryTarget;
                    int? floor = board.SelectedFloor;
                    var point = scroll.GetGlobalRect().GetCenter();
                    for (int i = 0; i < 40; i++)
                    {
                        Mouse(MouseButton.WheelDown, true, point);
                        Mouse(MouseButton.WheelDown, false, point);
                        await NextFrame();
                    }
                    await Settle();
                    double maximum = scroll.GetVScrollBar().MaxValue - scroll.GetVScrollBar().Page;
                    GD.Print($"TOWER WHEEL {gridName}: {scroll.ScrollVertical}/{maximum}");
                    Check(maximum > 0 && scroll.ScrollVertical >= maximum - 1, gridName + ": колесо не достигло конца панели");
                    viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point, Relative = Vector2.One }, true);
                    await Settle();
                    Check(scroll.ScrollVertical >= maximum - 1, gridName + ": движение мыши вернуло старый фокус");
                    Check(board.SelectedInventoryId == selected && board.InventoryTarget == inventoryTarget && board.SelectedFloor == floor,
                        gridName + ": прокрутка изменила выбранную цель");
                    CaptureTower("wheel-" + gridName + "-720.png");
                    // Resume actual GUI navigation after mouse scrolling; each device must reveal its new focus.
                    foreach (bool pad in new[] { false, true })
                    {
                        if (pad)
                        {
                            viewport.PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = true }, true);
                            viewport.PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = false }, true);
                        }
                        else
                        {
                            viewport.PushInput(new InputEventKey { Keycode = Key.Tab, PhysicalKeycode = Key.Tab, Pressed = true }, true);
                            viewport.PushInput(new InputEventKey { Keycode = Key.Tab, PhysicalKeycode = Key.Tab, Pressed = false }, true);
                        }
                        await Settle();
                        await Settle();
                        var focus = viewport.GuiGetFocusOwner();
                        Check(focus != null && board.IsAncestorOf(focus), gridName + ": навигация не вернула фокус доске");
                        Node? parent = focus!.GetParent();
                        while (parent != null && parent is not ScrollContainer) parent = parent.GetParent();
                        Check(parent == scroll && focus != first, gridName + ": навигация не продолжилась в исходной панели");
                        if (parent is ScrollContainer focusedScroll)
                            Check(focus.GetGlobalRect().Position.Y >= focusedScroll.GetGlobalRect().Position.Y - 1 &&
                                focus.GetGlobalRect().End.Y <= focusedScroll.GetGlobalRect().End.Y + 1,
                                gridName + ": фокус навигации обрезан");
                    }
                }
                finally
                {
                    _main.CloseTowerBoard();
                    board.SwitchRank(1);
                    DisplayServer.WindowSetSize(windowSize);
                    if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = rootSize;
                    Dialoid.Game.Input.InputDevice.Instance!.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
                    await Settle();
                }
            });
        yield return new Step("tower.close-unfocusable", async () =>
        {
            var prior = _main.Bar!.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b != _main.Bar.TowerButton);
            var mode = prior.FocusMode;
            try
            {
                Dialoid.Game.Input.InputDevice.Instance!.Set(Dialoid.Game.Input.InputKind.Gamepad);
                prior.GrabFocus();
                _main.OpenTowerBoard();
                Dialoid.Game.Input.InputDevice.Instance.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
                prior.FocusMode = Control.FocusModeEnum.None;
                _main.CloseTowerBoard();
                await Settle();
                Check(!prior.HasFocus() && !_main.IsTowerOpen, "закрытие восстановило недоступный фокус");
            }
            finally { prior.FocusMode = mode; _main.CloseTowerBoard(); }
        });
        yield return new Step("tower.icons", () =>
        {
            var data = _main.TowerBoard!.Data!;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in data.FloorTypes)
            {
                var icon = Ui.Tower.TowerBoardArt.Activity(type.Id);
                Check(icon != null && icon.GetWidth() > 0, $"нет иконки активности {type.Id}");
                Check(paths.Add(icon!.ResourcePath), $"повтор иконки активности {type.Id}");
            }
            paths.Clear();
            foreach (var currency in data.Currencies)
            {
                var icon = Ui.Tower.TowerBoardArt.Currency(currency.Id);
                Check(icon != null && icon.GetWidth() > 0, $"нет иконки расходника {currency.Id}");
                Check(paths.Add(icon!.ResourcePath), $"повтор иконки расходника {currency.Id}");
            }
            return Task.CompletedTask;
        });
        yield return new Step("tower.empty-start", async () =>
        {
            var board = _main.TowerBoard!;
            Check(board.Assembly!.Spec.Floors.Count == 5 && board.Assembly.Spec.Floors.All(f => f.TypeId == null), "initial five floors not empty");
            _main.OpenTowerBoard();
            await Settle();
            CaptureTower("empty-start.png");
            _main.CloseTowerBoard();
            _main.OpenTowerBoard();
            Check(board.Assembly.Spec.Floors.All(f => f.TypeId == null), "reopen filled floors");
            board.SwitchRank(2);
            Check(board.Assembly!.Spec.Floors.All(f => f.TypeId == null), "new rank auto-filled");
            board.SwitchRank(1);
            _main.CloseTowerBoard();
        });
        yield return new Step("tower.explicit-shuffle", async () =>
        {
            var board = _main.TowerBoard!;
            try
            {
                board.SwitchRank(12);
                _main.OpenTowerBoard();
                await Settle();
                Check(board.Assembly!.Spec.Floors.All(f => f.TypeId == null), "rank 12 auto-filled");
                void Press(string name) => ((Button)board.FindChild(name, true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
                Button ByText(string prefix) => board.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text.StartsWith(prefix, StringComparison.Ordinal));
                var emptyShuffle = board.FindChild("ShuffleInventory", true, false) as Button ?? ByText("Перемешать");
                Check(!emptyShuffle.Disabled, "shuffle disabled; no working handler");
                int emptyCost = board.Assembly.NextShuffleCost, emptyMoney = board.Wallet!.CountUsable("shuffle", 12);
                int emptyOwned = board.Inventory!.Groups(12).Sum(g => g.Count);
                emptyShuffle.EmitSignal(BaseButton.SignalName.Pressed);
                Check(board.Assembly.Spec.Floors.All(f => f.TypeId != null) && board.Inventory.Groups(12).Sum(g => g.Count) == emptyOwned - 5, "empty shuffle did not transfer five instances");
                Check(board.Wallet.CountUsable("shuffle", 12) == emptyMoney - emptyCost, "empty shuffle payment incorrect");
                for (int slot = 0; slot < 5; slot++)
                {
                    board.SelectFloor(slot);
                    ByText("Снять активность этажа").EmitSignal(BaseButton.SignalName.Pressed);
                }
                var stack = board.Inventory!.Groups(12).First(g => g.TypeId == "trial");
                Press("Activity_" + stack.Id.Replace(':', '_'));
                Check(board.InventoryTarget && board.SelectedInventoryId == stack.Id, "left node target missing");
                int before = board.Wallet!.CountUsable("imbue", 12);
                Press("Currency_imbue");
                var singleton = board.Inventory.Groups(12).Single(g => g.Id == board.SelectedInventoryId);
                Check(singleton.EverModified && singleton.Count == 1 && singleton.Mods.Count == 1, "right button did not split singleton");
                Check(board.Wallet.CountUsable("imbue", 12) == before - 1 && ((Button)board.FindChild("Currency_imbue", true, false)!).GetNode<Label>("Count").Text == (before - 1).ToString(), "currency count stale");
                board.SelectFloor(0);
                ByText("Установить на этаж").EmitSignal(BaseButton.SignalName.Pressed);
                var preserved = board.Assembly.Spec.Floors[0];
                Check(preserved.Mods.SequenceEqual(singleton.Mods), "install lost mods");
                var shuffle = board.FindChild("ShuffleInventory", true, false) as Button ?? ByText("Перемешать");
                Check(!shuffle.Disabled, "shuffle disabled; no working handler");
                int cost = board.Assembly.NextShuffleCost, money = board.Wallet.CountUsable("shuffle", 12), owned = board.Inventory.Groups(12).Sum(g => g.Count);
                shuffle.EmitSignal(BaseButton.SignalName.Pressed);
                Check(board.Assembly.Spec.Floors.All(f => f.TypeId != null), "Pressed did not fill floors");
                Check(SameFloor(preserved, board.Assembly.Spec.Floors[0]), "installed FloorSpec changed");
                Check(board.Inventory.Groups(12).Sum(g => g.Count) == owned - 4, "wrong transfer count");
                Check(board.Wallet.CountUsable("shuffle", 12) == money - cost && board.Assembly.ShuffleBonusActive, "wrong shuffle payment/bonus");
                await Settle();
                CaptureTower("shuffled.png");
                var floors = board.Assembly.Spec.Floors.ToArray();
                int after = board.Wallet.CountUsable("shuffle", 12);
                int shuffles = board.Assembly.ShufflesDone;
                shuffle.EmitSignal(BaseButton.SignalName.Pressed);
                Check(board.Wallet.CountUsable("shuffle", 12) == after && floors.Zip(board.Assembly.Spec.Floors).All(pair => SameFloor(pair.First, pair.Second)), "full repeat mutated state");
                Check(board.Assembly.ShufflesDone == shuffles, "full repeat advanced counter");
                Check(board.StatusText.Contains("нет пустых этажей", StringComparison.Ordinal), "full refusal missing");
                int inputPresses = 0;
                void CountPress() => inputPresses++;
                shuffle.Pressed += CountPress;
                shuffle.GrabFocus();
                await Settle();
                _main.GetViewport().PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true }, true);
                _main.GetViewport().PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = false }, true);
                await Settle();
                Check(inputPresses == 1 && board.Wallet.CountUsable("shuffle", 12) == after && board.Assembly.ShufflesDone == shuffles, "pad did not activate shuffle or full repeat charged");
                var point = shuffle.GetGlobalRect().GetCenter();
                _main.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point }, true);
                _main.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point }, true);
                await Settle();
                shuffle.Pressed -= CountPress;
                Check(inputPresses == 2 && board.Wallet.CountUsable("shuffle", 12) == after && board.Assembly.ShufflesDone == shuffles, "mouse did not activate shuffle or full repeat charged");
                board.SwitchRank(1);
                Check(board.Assembly!.Spec.Floors.All(f => f.TypeId == null), "rank 1 poisoned");
                board.SwitchRank(12);
                Check(floors.Zip(board.Assembly!.Spec.Floors).All(pair => SameFloor(pair.First, pair.Second)), "rank change lost assembly");
                board.SelectFloor(0);
                ByText("Снять активность этажа").EmitSignal(BaseButton.SignalName.Pressed);
                Check(board.Inventory.Groups(12).Single(g => g.Id == singleton.Id).Mods.SequenceEqual(singleton.Mods), "return lost mods");
                Press("Activity_" + singleton.Id.Replace(':', '_'));
                Press("Currency_clear");
                var returned = board.Inventory.Groups(12).Single(g => g.Id == singleton.Id);
                Check(returned.EverModified && returned.Count == 1 && returned.Mods.Count == 0, "cleared singleton restacked");
            }
            finally { board.SwitchRank(1); _main.CloseTowerBoard(); }
        });
        yield return new Step("tower.crafting", () =>
        {
            var board = _main.TowerBoard!;
            Check(board.Data != null && board.Assembly != null && board.Wallet != null, "данные башни не загружены");
            var data = board.Data!;
            board.SwitchRank(1);
            board.SelectFloor(0);
            var currency = data.Currencies.First(c => c.Op == Dialoid.Core.Tower.CurrencyOp.AddMod && c.ModKind == Dialoid.Core.Tower.ModKind.General);
            int before = board.Wallet!.CountUsable(currency.Id, 1);
            Check(board.ApplyCurrency(currency.Id) == Dialoid.Core.Tower.TowerRefusal.EmptySlot, "пустой этаж принял свойство");
            Check(board.Wallet.CountUsable(currency.Id, 1) == before, "отказ потратил валюту");
            for (int i = 0; i < data.Rules.Floors; i++)
            {
                board.SelectFloor(i);
                Check(board.SetFloorType(data.FloorTypes[i % data.FloorTypes.Count].Id) == Dialoid.Core.Tower.TowerRefusal.None, "тип не установлен");
            }
            board.SelectFloor(0);
            Check(board.ApplyCurrency(currency.Id) == Dialoid.Core.Tower.TowerRefusal.None, "намазка не применилась");
            Check(board.Assembly!.Spec.Floors[0].Mods.Count == 1, "свойство не появилось");
            Check(board.Wallet.CountUsable(currency.Id, 1) == before - 1, "неверное списание валюты");
            board.SwitchRank(2);
            Check(board.ValidateForTest() == Dialoid.Core.Tower.TowerRefusal.RankLocked, "проверка пропустила закрытый ранг");
            board.SwitchRank(1);
            Check(board.Wallet.CountUsable(currency.Id, 1) == before - 1, "смена ранга пополнила кошелёк");
            Check(board.Assembly!.Spec.Floors[0].Mods.Count == 1, "сборка потерялась при смене ранга");
            return Task.CompletedTask;
        });
        yield return new Step("tower.inventory-transfer", async () =>
        {
            var board = _main.TowerBoard!;
            board.SwitchRank(5);
            var inventory = board.Inventory!;
            var stack = inventory.Groups(5).Single(g => g.TypeId == "trial" && !g.EverModified);
            board.SelectInventory(stack.Id);
            Check(board.ApplyCurrency("imbue") == Dialoid.Core.Tower.TowerRefusal.None, "не удалось намазать активность в инвентаре");
            var singleton = inventory.Groups(5).Single(g => g.Id == board.SelectedInventoryId);
            Check(singleton.EverModified && singleton.Count == 1 && singleton.Mods.Count == 1, "намазанный экземпляр не отделился от стака");
            Check(inventory.Groups(5).Single(g => g.Id == stack.Id).Count == stack.Count - 1, "стак не уменьшился на один");
            board.SelectFloor(0);
            Check(board.InstallSelectedActivity() == Dialoid.Core.Tower.TowerRefusal.None, "экземпляр не перенесён на этаж");
            Check(!inventory.Groups(5).Any(g => g.Id == singleton.Id), "экземпляр скопирован вместо переноса");
            Check(board.Assembly!.Spec.Floors[0].Mods.SequenceEqual(singleton.Mods), "перенос изменил свойства");
            Check(board.RemoveSelectedActivity() == Dialoid.Core.Tower.TowerRefusal.None, "экземпляр не вернулся");
            Check(board.Assembly.Spec.Floors[0].TypeId == null, "этаж не очистился после снятия");
            var returned = inventory.Groups(5).Single(g => g.Id == singleton.Id);
            Check(returned.Mods.SequenceEqual(singleton.Mods), "возврат потерял свойства");
            board.SelectInventory(returned.Id);
            Check(board.ApplyCurrency("clear") == Dialoid.Core.Tower.TowerRefusal.None, "очищение не применилось к активности");
            returned = inventory.Groups(5).Single(g => g.Id == singleton.Id);
            Check(returned.EverModified && returned.Count == 1 && returned.Mods.Count == 0, "очищенный экземпляр снова попал в стак");
            if (_screenshotDir != null && DisplayServer.GetName() != "headless")
            {
                _main.OpenTowerBoard();
                await Settle();
                Directory.CreateDirectory(_screenshotDir);
                _main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_screenshotDir, "inventory-singleton.png"));
                _main.CloseTowerBoard();
            }
            board.SwitchRank(1);
        });
        yield return new Step("tower.open-close", async () =>
        {
            Check(_main.TowerBoard != null && _main.Bar?.TowerButton != null, "нет доски или кнопки Башня");
            _main.Bar!.TowerButton!.EmitSignal(BaseButton.SignalName.Pressed);
            await NextFrame();
            Check(_main.IsTowerOpen, "кнопка не открыла башню");
            Check(_main.Combat!.InputBlocked?.Invoke() == true, "доска не блокирует бой");
            Check(_main.Windows.ProcessMode == Node.ProcessModeEnum.Disabled, "горячие клавиши окон активны под доской");
            var focus = _main.GetViewport().GuiGetFocusOwner();
            Dialoid.Game.Input.InputDevice.Instance!.Set(Dialoid.Game.Input.InputKind.Gamepad);
            Check(_main.GetViewport().GuiGetFocusOwner() == focus, "смена устройства украла фокус доски");
            Dialoid.Game.Input.InputDevice.Instance.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
            Check(_main.GetViewport().GuiGetFocusOwner() == focus, "мышь украла фокус доски");
            await Settle();
            _main.Room!.CameraRig.ZoomBy(1);
            var heroPosition = _main.Combat!.Hero.Position;
            float zoom = _main.Room!.CameraRig.Zoom;
            var point = new Vector2(800, 950);
            foreach (var mouseButton in new[] { MouseButton.Left, MouseButton.WheelUp })
            {
                _main.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = mouseButton, Pressed = true, Position = point, GlobalPosition = point }, true);
                _main.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = mouseButton, Pressed = false, Position = point, GlobalPosition = point }, true);
            }
            await Settle();
            Check(_main.Combat.Hero.Position.IsEqualApprox(heroPosition), "клик под доской сдвинул героя");
            Check(Mathf.IsEqualApprox(_main.Room.CameraRig.Zoom, zoom), "колесо под доской изменило камеру");
            _main.GetViewport().PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.RightStick, Pressed = true }, true);
            _main.GetViewport().PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.RightStick, Pressed = false }, true);
            await Settle();
            Check(Mathf.IsEqualApprox(_main.Room.CameraRig.Zoom, zoom), "R3 под доской сбросил камеру");
            _main.Room.CameraRig.ZoomBy(-1);
            if (_screenshotDir != null && DisplayServer.GetName() != "headless")
            {
                Directory.CreateDirectory(_screenshotDir);
                _main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_screenshotDir, "tower-board.png"));
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                await Settle();
                Check(_main.TowerBoard!.Size.X <= 1280 && _main.TowerBoard.Size.Y <= 720, "доска выходит за 720p");
                var clear = _main.TowerBoard.FindChild("Currency_clear", true, false) as Button;
                Check(clear != null, "нет ноды Очищения");
                clear!.GrabFocus();
                await Settle();
                await Settle();
                var description = _main.TowerBoard.FindChild("CurrencyDescription", true, false) as Label;
                Check(description != null && description.Text.Contains("Удалить все свойства", StringComparison.Ordinal), "нет предпросмотра Очищения по фокусу");
                Check(description!.GetGlobalRect().End.Y <= 720, "описание расходника не помещается в 720p");
                Node? ancestor = clear.GetParent();
                while (ancestor != null && ancestor is not ScrollContainer) ancestor = ancestor.GetParent();
                var scroll = (ScrollContainer)ancestor!;
                Check(clear.GetGlobalRect().Position.Y >= scroll.GetGlobalRect().Position.Y - 1 && clear.GetGlobalRect().End.Y <= scroll.GetGlobalRect().End.Y + 1,
                    $"кнопка расходника обрезана после изменения высоты описания: кнопка {clear.GetGlobalRect()}, скролл {scroll.GetGlobalRect()}");
                _main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_screenshotDir, "tower-board-720.png"));
                DisplayServer.WindowSetSize(ScreenshotWindow);
                await Settle();
            }
            _main._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });
            await NextFrame();
            Check(!_main.IsTowerOpen && _main.Windows.ProcessMode == Node.ProcessModeEnum.Inherit, "доска не закрылась");
            _main._Input(new InputEventKey { PhysicalKeycode = Key.F6, Pressed = true });
            Check(_main.IsTowerOpen, "F6 не открыла доску");
            _main._Input(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true });
            Check(!_main.IsTowerOpen, "B не закрыла доску");
            Dialoid.Game.Input.InputDevice.Instance.Set(Dialoid.Game.Input.InputKind.Gamepad);
            _main.Windows.FocusBar();
            _main.Bar.TowerButton.GrabFocus();
            _main.GetViewport().PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true }, true);
            _main.GetViewport().PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = false }, true);
            await NextFrame();
            Check(_main.IsTowerOpen, "A на кнопке Башня не открыла доску");
            _main.GetViewport().PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = true }, true);
            _main.GetViewport().PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = false }, true);
            await NextFrame();
            Check(_main.TowerBoard!.IsAncestorOf(_main.GetViewport().GuiGetFocusOwner()), "навигация геймпадом вышла из доски");
            _main.CloseTowerBoard();
            Dialoid.Game.Input.InputDevice.Instance.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
        });
    }
}
