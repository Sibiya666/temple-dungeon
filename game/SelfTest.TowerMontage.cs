using Dialoid.Core.Tower;
using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
    private async Task TowerMontageDetailsInput()
    {
        var board = _main.TowerBoard!;
        var originalRank = board.Assembly!.Spec.Rank;
        var originalSize = _main.GetTree().Root.Size;
        var viewport = _main.GetViewport();
        void KeyTap(Key key)
        {
            foreach (bool pressed in new[] { true, false })
                viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
        }
        async Task Reach(Control target)
        {
            var visited = new HashSet<Control>();
            while (!target.HasFocus() && viewport.GuiGetFocusOwner() is { } focus && visited.Add(focus))
            { KeyTap(Key.Tab); await Settle(); }
            Check(target.HasFocus(), $"Tab не достиг {target.Name}");
        }
        try
        {
            if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = new Vector2I(1280, 720);
            else DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            board.SwitchRank(14);
            _main.OpenTowerBoard();
            var source = board.Inventory!.Groups(14).First(g => g.TypeId == "waves");
            board.SelectInventory(source.Id);
            board.SelectFloor(0);
            Check(board.InstallSelectedActivity() == TowerRefusal.None, "fixture этажа не установлен");
            await Settle();
            var grid = (GridContainer)board.FindChild("CurrencyGrid", true, false)!;
            Check(grid.GetChildren().OfType<Button>().Count() == board.Data!.Currencies.Count(c => c.Op != CurrencyOp.Shuffle),
                "UI потерял реальный вид расходника");
            Check(grid.GetChildren().OfType<Button>().All(b => board.GetGlobalRect().Encloses(b.GetGlobalRect())),
                "не все расходники видны в базовом 720p");
            var imbue = (Button)board.FindChild("Currency_imbue", true, false)!;
            await Reach(imbue);
            KeyTap(Key.Enter);
            await Settle();
            Check(board.Assembly.Spec.Floors[0].Mods.Count == 1, "GUI-намазка не добавила свойство");
            var toggle = (Button)board.FindChild("ToggleTargetDetails", true, false)!;
            await Reach(toggle);
            KeyTap(Key.Enter);
            await Settle();
            var details = (Control)board.FindChild("TargetDetails", true, false)!;
            Check(details.Visible, "GUI не раскрыл подробности цели");
            var fullStatus = (Label)board.FindChild("FullActionResult", true, false)!;
            Check(fullStatus.IsVisibleInTree() && fullStatus.Text == board.StatusText &&
                fullStatus.Text.Contains("Потрачено:") && fullStatus.Text.Contains("Осталось:"),
                "раскрытые через GUI подробности не показывают полный результат и расход валюты");
            var mod = (OptionButton)board.FindChild("TargetModifier", true, false)!;
            await Reach(mod);
            Check(!mod.Disabled && mod.ItemCount == 1, "адресный выбор свойства недоступен");
            KeyTap(Key.Enter);
            await Settle();
            KeyTap(Key.Home);
            KeyTap(Key.Enter);
            await Settle();
            var remove = (Button)board.FindChild("Currency_remove", true, false)!;
            int before = board.Wallet!.CountUsable("remove", 14);
            await Reach(remove);
            KeyTap(Key.Enter);
            await Settle();
            Check(board.Assembly.Spec.Floors[0].Mods.Count == 0 && board.Wallet.CountUsable("remove", 14) == before - 1,
                "GUI-адресное снятие не удалило выбранное свойство ровно за один расходник");
            Check(fullStatus.Text == board.StatusText, "полный результат не обновился после адресного снятия");
            CaptureTower("montage-720-expanded-target-remove-input.png");
            await Reach(toggle);
            var scroll = (ScrollContainer)board.FindChild("CurrencyTargetScroll", true, false)!;
            Check(scroll.GetGlobalRect().Encloses(fullStatus.GetGlobalRect()),
                "возврат к GUI-кнопке подробностей не открывает полный результат в области чтения");
            CaptureTower("montage-720-full-result-via-details.png");
            KeyTap(Key.Enter);
            await Settle();
            Check(!details.Visible, "подробности цели не закрылись через GUI");
        }
        finally
        {
            _main.CloseTowerBoard(); board.SwitchRank(originalRank);
            if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = originalSize;
            else DisplayServer.WindowSetSize(originalSize);
        }
    }

    private async Task TowerMontage()
    {
        var board = _main.TowerBoard!;
        var rank = board.Assembly!.Spec.Rank;
        var originalSize = _main.GetTree().Root.Size;
        try
        {
            board.SwitchRank(13);
            _main.OpenTowerBoard();
            await Settle();
            Check(board.FindChild("FloorEditor", true, false) is Control, "нет отдельного редактора выбранного этажа");
            var source = board.Inventory!.Groups(13).First(g => g.TypeId == "waves");
            board.SelectInventory(source.Id);
            board.SelectFloor(1);
            Check(!board.InventoryTarget && board.SelectedInventoryId == source.Id, "выбор этажа потерял источник установки");
            var sourceTitle = (Label)board.FindChild("ActivityDetailTitle", true, false)!;
            Check(sourceTitle.Text == board.Data!.FloorType(source.TypeId).Name, "detail источника подменён целью");
            Check(board.InstallSelectedActivity() == TowerRefusal.None, "не установлен источник");
            Check(board.ApplyCurrency("imbue") == TowerRefusal.None, "реальная намазка этажа не работает");
            Check(board.Assembly.Spec.Floors[1].Mods.Count == 1, "свойство не записано в этаж");
            await Settle();
            CaptureTower("montage-real-floor-currency.png");
            Check(board.RemoveSelectedActivity() == TowerRefusal.None, "модифицированный экземпляр не возвращён");
            var modified = board.Inventory.Groups(13).First(g => g.TypeId == source.TypeId && g.EverModified);
            board.SelectInventory(modified.Id);
            Check(board.InstallSelectedActivity() == TowerRefusal.None, "модифицированный экземпляр не установлен");
            board.SelectInventory(source.Id);
            Check(board.InstallSelectedActivity() == TowerRefusal.None, "замена на тот же тип ошибочно запрещена");
            Check(board.Assembly.Spec.Floors[1].Mods.Count == 0 && board.Inventory.Groups(13).Any(g => g.Id == modified.Id),
                "замена того же типа потеряла модифицированный экземпляр");
            board.SelectFloor(null);
            Check(!board.InventoryTarget && board.SelectedInventoryId == source.Id, "цель всей башни потеряла источник");
            Check(board.ApplyCurrency("imbue_tower") == TowerRefusal.None && board.Assembly.Spec.TowerMods.Count == 1,
                "намазка всей башни не работает");
            await Settle();
            CaptureTower("montage-whole-tower-currency.png");
            board.SelectInventory(source.Id);
            Check(board.InventoryTarget && ((Label)board.FindChild("InstallReason", true, false)!).Text == "Для установки выберите этаж.",
                "пояснение установки подменяет инвентарную цель намазки всей башней");
            board.Inventory.Add("waves", 13, 20);
            for (int i = 0; i < 9; i++)
            {
                board.SelectInventory(source.Id);
                Check(board.ApplyCurrency("imbue") == TowerRefusal.None, "не создан модифицированный экземпляр каталога");
            }
            Check(board.Inventory.Groups(13).Count > 16, "fixture не создал вторую страницу");
            await Settle();
            CaptureTower("montage-real-inventory-currency.png");
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1600, 900) })
            {
                if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = size;
                else DisplayServer.WindowSetSize(size);
                board.SelectFloor(1);
                await Settle();
                var grid = (GridContainer)board.FindChild("ActivityGrid", true, false)!;
                var page = (OptionButton)board.FindChild("ActivityPage", true, false)!;
                Check(page.Visible && page.ItemCount == 2, "страницы не появились после разделения стека");
                Check(grid.GetChildren().OfType<Button>().Count(c => c.Visible) == 16, "первая страница потеряла экземпляры");
                CaptureTower($"montage-{size.Y}-modified-layout.png");
                foreach (var name in new[] { "Floor_0", "Floor_4", "WholeTowerTarget", "InstallActivity", "RemoveActivity", "ShuffleInventory", "ValidateEntry", "TowerSummit", "ActivityPage", "ActivityGrid" })
                    Check(board.GetGlobalRect().Encloses(((Control)board.FindChild(name, true, false)!).GetGlobalRect()), $"{name} вне экрана в насыщенном каталоге при {size}");
                if (DisplayServer.GetName() != "headless") await _main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                CaptureTower($"montage-{size.Y}-modified-page1.png");
                page.Select(1);
                page.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
                await Settle();
                Check(grid.GetChildren().OfType<Button>().Count(c => c.Visible) == board.Inventory.Groups(13).Count - 16,
                    "вторая страница показывает неверные группы");
                CaptureTower($"montage-{size.Y}-modified-page2.png");
                page.Select(0);
                page.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            }
        }
        finally
        {
            _main.CloseTowerBoard(); board.SwitchRank(rank);
            if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = originalSize;
            else DisplayServer.WindowSetSize(originalSize);
        }
    }
}
