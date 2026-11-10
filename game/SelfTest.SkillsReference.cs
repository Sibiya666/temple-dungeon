using Dialoid.Game.Ui;
using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
    // A focused capture and regression pass on the real UI, at the supported compact and normal viewports.
    private async Task SkillsReference()
    {
        var originalSize = _main.GetTree().Root.Size;
        try
        {
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1600, 900) })
            {
                if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = size;
                else DisplayServer.WindowSetSize(size);
                await Settle();
                UseSelfTestWindowConfig();
                await FreshBuild(1, WindowIds.Skills);
                var panel = _main.Skills!;
                panel.PadClosed();
                await Settle();
                var hostRect = Host.Window(WindowIds.Skills).GetGlobalRect();
                foreach (var button in panel.CatalogButtons.Values)
                {
                    Check(button.Size.X == button.Size.Y, "портрет не квадратный");
                    Check(button.GetGlobalRect().End.X <= hostRect.End.X, "каталог шире окна");
                }
                if (_screenshotDir != null) await Shot($"reference-{size.Y}-catalog");
                var locked = panel.CatalogButtons.Keys.First(id => _main.Session!.Data.Skills[id].UnlockLevel > 1);
                var revision = _main.Session!.Revision;
                PushButton(panel.CatalogButtons[locked].GetGlobalRect().GetCenter(), MouseButton.Left);
                await Settle();
                Check(panel.BrowsedSkillId == locked && _main.Session.Revision == revision, "просмотр изменил билд");
                Check(panel.AssignSkillButton is { Disabled: true }, "закрытый навык предлагает назначение");
                if (_screenshotDir != null) await Shot($"reference-{size.Y}-locked");
                panel.PadBack();
                await Settle();
                Check(_main.Character!.SelectLevel(20).Ok && panel.SelectSkill(0, "cleave").Ok, "настройка навыка");
                await Settle();
                panel.CatalogButtons["cleave"].EmitSignal(Button.SignalName.Pressed);
                await Settle();
                if (panel.GetChild<ScrollContainer>(0) is { } detailScroll) detailScroll.ScrollVertical = 0;
                await Settle();
                Check(panel.TreeSlot == 0 && panel.LevelNumber != null, "не показан редактор");
                if (_screenshotDir != null) await Shot($"reference-{size.Y}-detail");
                Device.Set(Dialoid.Game.Input.InputKind.Gamepad);
                Host.FocusWindow(WindowIds.Skills);
                panel.SkillGraph!.GrabFocus();
                panel.SkillGraph.PadSelect("heavy_hand");
                await Settle();
                if (_screenshotDir != null) await Shot($"reference-{size.Y}-gamepad");
                PadTap(JoyButton.B);
                await Settle();
                await Settle();
                Check(panel.BrowsedSkillId == null && Host.IsOpen(WindowIds.Skills), "B не вернула каталог");
                Check(panel.CatalogButtons["cleave"].HasFocus(), "фокус не вернулся на портрет");
                Check(panel.GetChild<ScrollContainer>(0).GetGlobalRect().HasPoint(panel.CatalogButtons["cleave"].GetGlobalRect().GetCenter()),
                    "фокус вернулся на невидимый портрет");
                if (_screenshotDir != null) await Shot($"reference-{size.Y}-catalog-focus");
                Host.FocusCombat();
                Device.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
            }
        }
        finally
        {
            if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = originalSize;
            else DisplayServer.WindowSetSize(originalSize);
            Host.FocusCombat();
            Device.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
            _main.CloseAllWindows();
        }
    }
}
