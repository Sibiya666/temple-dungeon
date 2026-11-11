using Dialoid.Game.Ui;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
    private async Task SevenWindowsHost()
    {
        var expanded = typeof(GameWindow).GetProperty("Expanded");
        var setExpanded = typeof(WindowHost).GetMethod("SetExpanded", new[] { typeof(string), typeof(bool) });
        Check(expanded != null && setExpanded != null, "нет разворота и восстановления окон");
        UseSelfTestWindowConfig();
        foreach (var id in new[] { WindowIds.Ascendancy, WindowIds.Builds, WindowIds.Breakdown, WindowIds.Simulation, WindowIds.Log, WindowIds.Meter, WindowIds.Charts })
        {
            Host.OpenWindow(id);
            await Settle();
            var w = Host.Window(id);
            var prior = w.GetRect();
            var content = w.Content;
            setExpanded!.Invoke(Host, new object[] { id, true });
            await Settle();
            Check((bool)expanded!.GetValue(w)! && w.Size.X > prior.Size.X, $"{id} не развёрнуто");
            Check(w.GetRect().End.X <= Host.Size.X + 1 && w.GetRect().End.Y <= Host.Size.Y + 1, $"{id} за экраном");
            Check(w.Content == content, "содержимое окна заменено при развороте");
            Host.CloseWindow(id);
            Host.OpenWindow(id);
            await Settle();
            Check((bool)expanded.GetValue(w)! && w.Size.X > prior.Size.X, "повторное открытие потеряло разворот");
            setExpanded.Invoke(Host, new object[] { id, false });
            await Settle();
            Check(!(bool)expanded.GetValue(w)! && w.GetRect().IsEqualApprox(prior), $"{id} не восстановило {prior}: {w.GetRect()}");
            Host.CloseWindow(id);
        }
        Host.OpenWindow(WindowIds.Passives);
        await Settle();
        var passives = Host.Window(WindowIds.Passives).GetRect();
        setExpanded!.Invoke(Host, new object[] { WindowIds.Passives, true });
        Check(Host.Window(WindowIds.Passives).GetRect().IsEqualApprox(passives), "затронуто окно Пассивок");
        _main.CloseAllWindows();
        Host.OpenWindow(WindowIds.Builds);
        await Settle();
        var expandButton = Host.Window(WindowIds.Builds).ExpandButton;
        Check(expandButton is { FocusMode: Control.FocusModeEnum.All }, "разворот недоступен клавиатуре/геймпаду");
        Device.Set(Dialoid.Game.Input.InputKind.Gamepad);
        Host.FocusWindow(WindowIds.Builds);
        expandButton!.GrabFocus();
        PadTap(JoyButton.A);
        await Settle();
        Check(Host.Window(WindowIds.Builds).Expanded, "A не активировала разворот");
        PadTap(JoyButton.A);
        await Settle();
        Check(!Host.Window(WindowIds.Builds).Expanded, "A не восстановила боковой режим");
        Host.FocusCombat();
        Device.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
        _main.CloseAllWindows();
    }

    private async Task SevenWindowsReference()
    {
        var originalSize = _main.GetTree().Root.Size;
        var ids = new[] { WindowIds.Ascendancy, WindowIds.Builds, WindowIds.Breakdown, WindowIds.Simulation, WindowIds.Log, WindowIds.Meter, WindowIds.Charts };
        try
        {
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1600, 900) })
            {
                if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = size;
                else DisplayServer.WindowSetSize(size);
                await Settle();
                await LoadForCombat("warrior_berserker_whirlwind.json");
                PlaceNearDummy();
                await Fight(4, close: false);
                Training.StopChannel();
                await Run(TrainingRoom.SegmentIdleSec + 40, until: () => Training.Current == null);
                Check(Training.Current == null && Training.Segments.Count > 0, "бой Берсерка не завершился после отпускания канала");
                await SimulateTraining();
                foreach (var id in ids)
                {
                    Host.CloseAll();
                    Host.OpenWindow(id);
                    Host.SetExpanded(id, true);
                    if (id == WindowIds.Ascendancy)
                    {
                        _main.AscendancyView!.View.FitToView();
                        if (_main.AscendancyView.View.NodeLocalPosition("blood_feast") is { } at) _main.AscendancyView.View.HoverAt(at);
                    }
                    if (id == WindowIds.Breakdown) _main.Character!.NumberFor("life")!.Open();
                    if (id is WindowIds.Meter or WindowIds.Charts) { Meter.SelectSegment(null); Meter.Refresh(); Charts.Refresh(); }
                    await Settle();
                    if (id == WindowIds.Ascendancy)
                    {
                        _main.AscendancyView!.View.FitToView();
                        if (_main.AscendancyView.View.NodeLocalPosition("blood_feast") is { } at) _main.AscendancyView.View.HoverAt(at);
                        await Settle();
                        CheckSevenAscendancyCaptions(_main.AscendancyView.View);
                    }
                    if (id == WindowIds.Meter)
                        for (var i = 0; i < Meter.ShownRows.Count; i++)
                            Check(Meter.Bars.RowAt(new Vector2(4, Meter.Bars.RowRect(i).GetCenter().Y - Meter.Bars.GlobalPosition.Y)) == i,
                                "полоса измерителя не совпадает с областью нажатия");
                    var rect = Host.Window(id).GetGlobalRect();
                    Check(rect.End.X <= size.X + 1 && rect.End.Y <= size.Y + 1, $"{id} выходит за {size}");
                    Check(Host.Window(id).Content.GetGlobalRect().End.X <= rect.End.X + 1, $"{id} содержимое шире окна");
                    if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"seven-{size.Y}-{id}");
                    Host.SetExpanded(id, false);
                    await Settle();
                    if (size.Y == 720 && _screenshotDir != null && DisplayServer.GetName() != "headless")
                    {
                        if (id == WindowIds.Ascendancy) CheckSevenAscendancySide();
                        await Shot($"seven-{size.Y}-{id}-side");
                        foreach (var scroll in Host.Window(id).Content.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>())
                            scroll.ScrollVertical = int.MaxValue;
                        await Settle();
                        await Shot($"seven-{size.Y}-{id}-side-bottom");
                        foreach (var scroll in Host.Window(id).Content.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>())
                            scroll.ScrollVertical = 0;
                    }
                }
                Host.CloseAll();
                Host.OpenWindow(WindowIds.Ascendancy);
                Host.SetExpanded(WindowIds.Ascendancy, true);
                Device.Set(Dialoid.Game.Input.InputKind.Gamepad);
                Host.FocusWindow(WindowIds.Ascendancy);
                _main.AscendancyView!.View.GrabFocus();
                _main.AscendancyView.View.PadSelect("blood_feast");
                await Settle();
                CheckSevenAscendancyCaptions(_main.AscendancyView.View);
                CheckSevenAscendancySide();
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"seven-{size.Y}-ascendancy-focus");
                _main.AscendancyView.View.PadSelect("berserk_frenzy");
                await Settle();
                CheckSevenAscendancyCaptions(_main.AscendancyView.View);
                CheckSevenAscendancySide();
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"seven-{size.Y}-ascendancy-start-disabled");
                Host.SetExpanded(WindowIds.Ascendancy, false);
                Host.FocusCombat();
                Device.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
                Host.CloseAll();
                Host.OpenWindow(WindowIds.Meter);
                Host.SetExpanded(WindowIds.Meter, true);
                Device.Set(Dialoid.Game.Input.InputKind.Gamepad);
                Host.FocusWindow(WindowIds.Meter);
                Meter.RequestReset();
                await Settle();
                Check(Meter.CancelButton.HasFocus(), "подтверждение сброса не выбрало безопасную отмену");
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"seven-{size.Y}-meter-confirm");
                PadTap(JoyButton.B);
                await Settle();
                Check(Meter.ResetButton.HasFocus() && !Meter.CancelButton.IsVisibleInTree(), "B не отменила сброс с возвратом фокуса");
                Host.SetExpanded(WindowIds.Meter, false);
                Host.FocusCombat();
                Device.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
                Host.CloseAll();
                _main.Character!.NumberFor("life")!.Open();
                Check(_main.Character.SelectLevel(59).Ok, "не удалось подготовить устаревший снимок");
                await Settle();
                Host.CloseAll();
                Host.OpenWindow(WindowIds.Simulation);
                Host.SetExpanded(WindowIds.Simulation, true);
                await Settle();
                Check(_main.Simulation!.StatusText == SimulationPanel.StaleText && _main.Simulation.Numbers.Count == 0,
                    "устаревшая симуляция показывает свежие числа");
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"seven-{size.Y}-simulation-stale");
                Host.SetExpanded(WindowIds.Simulation, false);
                Host.CloseAll();
                Host.OpenWindow(WindowIds.Breakdown);
                Host.SetExpanded(WindowIds.Breakdown, true);
                await Settle();
                Check(_main.Breakdown!.Current != null && _main.Breakdown.Current.Revision != _main.Session!.Revision,
                    "разбор не сохранил снимок прошлой ревизии");
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"seven-{size.Y}-breakdown-stale");
                Host.SetExpanded(WindowIds.Breakdown, false);
                Host.CloseAll();
                var validReference = _main.Builds!.References.First(r => r.Path.EndsWith("warrior_berserker_whirlwind.json", StringComparison.Ordinal));
                Check(_main.Builds.Load(validReference).Ok, "не восстановлен валидный билд перед возвратом в мир");
                await Settle();
                Check(_main.Session!.Violations.Count == 0 && Training.Running, "возврат в мир оставил недопустимый билд");
                Host.CloseAll();
                Host.FocusCombat();
                (Host.FindChild("SharedTooltip", true, false) as SharedTooltip)?.ResetHover();
                if (_screenshotDir != null && DisplayServer.GetName() != "headless") await Shot($"seven-{size.Y}-world-hud");
            }
        }
        finally
        {
            foreach (var id in ids) Host.SetExpanded(id, false);
            Host.CloseAll();
            Host.FocusCombat();
            Device.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
            if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = originalSize;
            else DisplayServer.WindowSetSize(originalSize);
        }
    }
}
