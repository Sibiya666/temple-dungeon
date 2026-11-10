using Dialoid.Core.Tower;
using Dialoid.Game.Input;
using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
    private async Task TowerReference()
    {
        var board = _main.TowerBoard!;
        var original = _main.GetTree().Root.Size;
        async Task Capture(string name)
        {
            await Settle();
            if (_screenshotDir == null || DisplayServer.GetName() == "headless") return;
            await _main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            CaptureTower(name);
        }
        try
        {
            // Exercise geometry at both actual GPU viewports; dummy rendering needs no resize for semantic checks.
            var sizes = DisplayServer.GetName() == "headless" ? new[] { original }
                : new[] { new Vector2I(1280, 720), new Vector2I(1600, 900) };
            foreach (var size in sizes)
            {
                if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = size;
                else DisplayServer.WindowSetSize(size);
                _main.OpenTowerBoard();
                board.SwitchRank(8);
                board.SelectFloor(null);
                await Settle();
                var install = (Button)board.FindChild("InstallActivity", true, false)!;
                Check(install.Disabled, "без выбранного этажа установка доступна");
                await Capture($"reference-{size.Y}-empty.png");
                var group = board.Inventory!.Groups(8).First(g => g.TypeId == "waves");
                board.SelectInventory(group.Id);
                var node = (Button)board.FindChild("Activity_" + group.Id.Replace(':', '_'), true, false)!;
                await Settle();
                var count = node.GetNode<Label>("Count");
                Check(count.Text == group.Count.ToString(), "неверный счётчик активности");
                var nodeRect = node.GetGlobalRect();
                var iconRect = node.GetChild<TextureRect>(0).GetGlobalRect();
                var countRect = count.GetGlobalRect();
                if (DisplayServer.GetName() != "headless") node.GrabFocus();
                await Settle();
                Check(nodeRect.Encloses(countRect), "счётчик выходит за ноду");
                await Capture($"reference-{size.Y}-selected-focus.png");
                if (DisplayServer.GetName() != "headless")
                    Check(node.GetGlobalRect().IsEqualApprox(nodeRect) && node.GetChild<TextureRect>(0).GetGlobalRect().IsEqualApprox(iconRect)
                        && count.GetGlobalRect().IsEqualApprox(countRect), "фокус сдвинул иконку или счётчик");
                if (DisplayServer.GetName() != "headless") InputDevice.Instance!.Set(InputKind.Gamepad);
                await Capture($"reference-{size.Y}-gamepad.png");
                board.SelectFloor(0);
                Check(board.InstallSelectedActivity() == TowerRefusal.None, "активность не установлена");
                Check(board.Assembly!.Spec.Floors[0].TypeId == "waves", "неверный тип на этаже");
                var installedNode = (Button)board.FindChild("Activity_" + group.Id.Replace(':', '_'), true, false)!;
                Check(installedNode.GetNode<Label>("Count").Text == (group.Count - 1).ToString(), "неверное количество после установки");
                await Capture($"reference-{size.Y}-installed.png");
                Check(board.RemoveSelectedActivity() == TowerRefusal.None, "активность не возвращена");
                Check(board.Inventory.Groups(8).Single(g => g.Id == group.Id).Count == group.Count, "количество после возврата изменилось");
                _main.CloseTowerBoard();
                InputDevice.Instance!.Set(InputKind.KeyboardMouse);
                await Capture($"reference-{size.Y}-world-hud.png");
            }
        }
        finally
        {
            _main.CloseTowerBoard();
            InputDevice.Instance!.Set(InputKind.KeyboardMouse);
            if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = original;
            else DisplayServer.WindowSetSize(original);
            await Settle();
        }
    }
}