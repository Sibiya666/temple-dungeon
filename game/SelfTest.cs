using Dialoid.Core.Builds;
using Dialoid.Core.Effects;
using Dialoid.Core.Passives;
using Dialoid.Core.Simulation;
using Dialoid.Game.Ui;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Headless self-test of the debug scene (tools/godot_check.sh --selftest). It drives the real panels through the same
/// handlers their controls call and prints one line per step: «SELFTEST OK name» or «SELFTEST FAIL name: why».
/// Then it quits with 0 when every step passed, else 1.
/// To add a check, add a step in <see cref="Steps"/>: a name and an async body that throws <see cref="Failure"/> (via
/// <see cref="Check"/>) when the check fails; <see cref="NextFrame"/> lets the scene process a frame.
/// The acceptance steps (spec §15.4) print the table «билд → DPS до/после, узел» after the results.
/// With <c>-- --selftest --screenshots DIR</c> in a real window (not headless) it also saves one PNG per tab into DIR.
/// </summary>
public sealed partial class SelfTest
{
    /// <summary>A failed check; its message is printed after the step name.</summary>
    public sealed class Failure(string message) : Exception(message);

    /// <summary>One named check.</summary>
    public sealed record Step(string Name, Func<Task> Run);

    /// <summary>User argument (after "--selftest") followed by the directory for the tab screenshots.</summary>
    public const string ScreenshotsArg = "--screenshots";

    /// <summary>Window size of the screenshot run.</summary>
    public static readonly Vector2I ScreenshotWindow = new(1600, 1000);

    private const string TrainingDummy = "training";
    private const int AcceptanceSeed = 1;

    private readonly Main _main;
    private readonly List<string> _table = new();
    private readonly string? _screenshotDir;

    public SelfTest(Main main)
    {
        _main = main;
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, ScreenshotsArg);
        if (at >= 0) _screenshotDir = at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : "";
    }

    /// <summary>Runs every step after the first frame, prints the results and quits the scene.</summary>
    public async void Start()
    {
        int failed;
        try
        {
            failed = await RunAll();
        }
        catch (Exception e)
        {
            GD.Print($"SELFTEST FAIL selftest: {e.GetType().Name}: {e.Message}");
            failed = 1;
        }
        GD.Print(failed == 0 ? "SELFTEST PASSED" : $"SELFTEST FAILED: {failed}");
        _main.GetTree().Quit(failed == 0 ? 0 : 1);
    }

    private async Task<int> RunAll()
    {
        if (_main.LoadError != null)
        {
            GD.Print($"SELFTEST FAIL load: {_main.LoadError.ReplaceLineEndings(" | ")}");
            return 1;
        }

        if (_screenshotDir != null && DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetSize(ScreenshotWindow);
            await Settle();
        }
        await NextFrame();
        var steps = Steps();
        int failed = 0;
        foreach (var step in steps)
        {
            try
            {
                await step.Run();
                GD.Print($"SELFTEST OK {step.Name}");
            }
            catch (Exception e)
            {
                failed++;
                var why = e is Failure ? e.Message : $"{e.GetType().Name}: {e.Message}";
                GD.Print($"SELFTEST FAIL {step.Name}: {why.ReplaceLineEndings(" | ")}");
            }
        }
        if (_table.Count > 0)
        {
            GD.Print("SELFTEST TABLE билд | главный скилл | DPS до | узел скилла «−»: DPS после, строка множителя, DPS после «+» | notable: DPS после, строка, DPS после возврата");
            foreach (var row in _table) GD.Print("SELFTEST TABLE " + row);
        }
        GD.Print($"SELFTEST {steps.Count - failed}/{steps.Count}");
        return failed;
    }

    /// <summary>All steps in order. Later tasks append theirs here.</summary>
    private List<Step> Steps()
    {
        var steps = new List<Step>
        {
            new("panels", () =>
            {
                Check(_main.Breakdown != null && _main.Character != null && _main.Builds != null, "панели не созданы");
                return Task.CompletedTask;
            }),
        };

        steps.AddRange(RoomSteps());

        var mains = _main.Builds?.References.Where(r => r.Group == ReferenceBuilds.MainGroup).ToList() ?? new();
        steps.Add(new("builds.main-listed", () =>
        {
            Check(mains.Count > 0, "в BuildsPanel нет основных эталонов");
            return Task.CompletedTask;
        }));
        foreach (var info in mains) steps.Add(new($"builds.load {info.Title}", () => LoadReference(info)));
        steps.Add(new("character.lines-match", CharacterLinesMatch));
        steps.Add(new("builds.import-garbage", ImportGarbage));
        if (mains.Count > 0) steps.Add(new("character.illegal-level", () => IllegalLevel(mains[0])));
        if (mains.Count > 0) steps.Add(new("character.signal-redraw", () => SignalRedraw(mains[0])));
        steps.Add(new("passives.click-take", PassiveClickTake));
        steps.Add(new("passives.click-unavailable", PassiveClickUnavailable));
        steps.Add(new("passives.attribute-menu", PassiveAttributeMenu));
        steps.Add(new("passives.hover-zoom-pan", PassiveHoverZoomPan));
        steps.Add(new("ascendancy.take", AscendancyTake));
        steps.Add(new("passives.drag-vs-click", PassiveDragVsClick));
        steps.Add(new("passives.noop-clears-error", PassiveNoopClearsError));
        steps.Add(new("skills.slots", SkillsSlots));
        if (mains.Count > 0)
        {
            var reference = mains.FirstOrDefault(m => m.Path.EndsWith("warrior_berserker_whirlwind.json", StringComparison.Ordinal)) ?? mains[0];
            steps.Add(new("skills.node-point", () => SkillsNodePoint(reference)));
            steps.Add(new("simulation.reference", () => SimulationReference(reference)));
            steps.Add(new("simulation.stale", () => SimulationStale(reference)));
            steps.Add(new("simulation.violations", () => SimulationViolations(reference)));
        }
        foreach (var info in mains) steps.Add(new($"acceptance {info.Title}", () => Acceptance(info)));
        if (mains.Count > 0) steps.Add(new("class-change-resets", () => ClassChangeResets(mains[^1])));
        if (mains.Count > 0) steps.Add(new("passives.captions-no-overlap", () => PassiveCaptions(mains[0])));
        if (mains.Count > 0) steps.AddRange(CombatSteps());
        if (_screenshotDir != null)
        {
            var pyro = mains.FirstOrDefault(m => m.Path.EndsWith("sorcerer_pyromancer_fireball.json", StringComparison.Ordinal));
            steps.Add(new("screenshots", () => Screenshots(pyro)));
        }
        if (mains.Count > 0)
            steps.Add(new("restore-reference", async () =>
            {
                Check(_main.Builds!.Load(mains[0]).Ok, "эталон не загрузился");
                _main.SelectTab(Main.TabTitles[0]);
                await Settle();
            }));
        return steps;
    }

    /// <summary>The shown CharacterLine labels are the session's lines one to one, with the NumberLabel text rule.</summary>
    private async Task CharacterLinesMatch()
    {
        await Settle();
        var shown = _main.Character!.LineNumbers;
        var lines = _main.Session!.CharacterLines;
        Check(lines.Count > 0, "у сессии нет CharacterLines");
        Check(shown.Count == lines.Count, $"показано {shown.Count} чисел, в сессии {lines.Count}");
        for (int i = 0; i < lines.Count; i++)
        {
            var (label, line) = (shown[i], lines[i]);
            Check(label.Line.Key == line.Key && label.Line.Value == line.Value, $"№{i}: {label.Line.Key} вместо {line.Key}");
            Check(label.Text == NumberLabel.TextOf(line), $"№{i}: текст «{label.Text}»");
        }
    }

    /// <summary>A garbage import is refused with a Russian status and leaves the build alone.</summary>
    private async Task ImportGarbage()
    {
        var session = _main.Session!;
        var builds = _main.Builds!;
        var revision = session.Revision;
        var code = session.ExportCode();
        var result = builds.ImportText("мусор");
        Check(!result.Ok, "импорт мусора принят");
        Check(session.Revision == revision, $"ревизия {revision} → {session.Revision}");
        Check(session.ExportCode() == code, "импорт мусора изменил код билда");
        Check(builds.ImportStatus.StartsWith("Импорт отклонен", StringComparison.Ordinal), $"статус «{builds.ImportStatus}»");
        Check(builds.ImportStatus.Any(c => c is >= 'а' and <= 'я'), "статус не на русском");
        await Settle();
    }

    /// <summary>Level 1 on a level-60 reference: no exception, violations shown, the panel still draws.</summary>
    private async Task IllegalLevel(ReferenceBuildInfo info)
    {
        var character = _main.Character!;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        await Settle();
        var result = character.SelectLevel(1);
        Check(result.Ok, $"уровень 1 отклонен: {result.Error}");
        await Settle();
        Check(character.ShownViolations.Count > 0, "нарушений не показано");
        Check(character.NumberFor(CharacterPanel.LevelKey)?.Line.Value == 1, "число уровня не 1");
        Check(character.Numbers.Count > 0 && !character.RedrawPending, "панель не перерисовалась");
        Check(_main.Builds.Load(info).Ok, "эталон не перезагрузился");
        await Settle();
        Check(character.ShownViolations.Count == 0, "после перезагрузки остались нарушения");
    }

    /// <summary>
    /// Fires the controls' own signals (as a click does) on controls the resulting redraw frees: weapon ItemSelected and
    /// an item's Toggled. The redraw must be deferred and land on the next frame.
    /// </summary>
    private async Task SignalRedraw(ReferenceBuildInfo info)
    {
        var character = _main.Character!;
        var session = _main.Session!;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        await Settle();

        var weapons = character.WeaponOptions!;
        var choices = character.WeaponChoices;
        var target = Enumerable.Range(0, choices.Count).First(i => choices[i] != session.Build.WeaponId);
        var revision = session.Revision;
        weapons.EmitSignal(OptionButton.SignalName.ItemSelected, target);
        Check(session.Revision == revision + 1, "оружие не сменилось");
        Check(character.RedrawPending, "перерисовка не отложена");
        Check(GodotObject.IsInstanceValid(weapons) && weapons.IsInsideTree(), "OptionButton удален во время сигнала");
        await Settle();
        Check(session.Build.WeaponId == choices[target] && character.WeaponOptions != weapons, "панель не перерисовалась после оружия");

        var box = character.ItemBoxes.FirstOrDefault(b => !b.ButtonPressed);
        if (box != null)
        {
            revision = session.Revision;
            box.ButtonPressed = true; // emits Toggled
            Check(session.Revision == revision + 1, "предмет не надет");
            Check(GodotObject.IsInstanceValid(box) && box.IsInsideTree(), "CheckBox удален во время сигнала");
            await Settle();
        }
        Check(_main.Builds.Load(info).Ok, "эталон не перезагрузился");
        await Settle();
    }

    /// <summary>Loads a main reference via BuildsPanel; CharacterPanel shows 0 violations; life opens its breakdown.</summary>
    private async Task LoadReference(ReferenceBuildInfo info)
    {
        var builds = _main.Builds!;
        var character = _main.Character!;
        var result = builds.Load(info);
        Check(result.Ok, $"загрузка отклонена: {result.Error}");
        Check(builds.LoadStatus.Contains(info.Title), $"статус загрузки «{builds.LoadStatus}»");
        await Settle();

        Check(character.ShownViolations.Count == 0, "нарушения: " + string.Join("; ", character.ShownViolations));
        var life = character.NumberFor("life");
        Check(life != null, "нет числа здоровья");
        Check(life!.Line.Breakdown != null, "у здоровья нет разбора");
        Check(life.Line.Value > 0, $"здоровье {life.Line.Value}");

        life.Open();
        Check(_main.CurrentTab == Main.BreakdownTab, $"открыта вкладка «{_main.CurrentTab}», а не «{Main.BreakdownTab}»");
        var shown = _main.Breakdown!;
        Check(shown.Current?.Line.Key == "life", "в «Разборе» не здоровье");
        Check(shown.ShownLines.Count > 0, "в «Разборе» нет строк");
        _main.SelectTab(Main.TabTitles[0]);
    }

    /// <summary>A fresh build of the start class at <paramref name="level"/>, with <paramref name="tab"/> shown.</summary>
    private async Task FreshBuild(int level, string tab)
    {
        var character = _main.Character!;
        Check(character.SelectClass(Main.StartClassId).Ok, "сброс класса отклонен");
        Check(character.SelectLevel(level).Ok, $"уровень {level} отклонен");
        _main.SelectTab(tab);
        await Settle();
    }

    /// <summary>Where a node is drawn in the view; it must be inside the view.</summary>
    private static Vector2 NodePoint(TreeView view, string id)
    {
        var pos = view.NodeLocalPosition(id);
        Check(pos != null, $"узла {id} нет в TreeView");
        Check(new Rect2(Vector2.Zero, view.Size).HasPoint(pos!.Value), $"узел {id} вне TreeView ({pos} при размере {view.Size})");
        return pos.Value;
    }

    /// <summary>Clicking an available node by its coordinates takes it and the counter follows; a second click drops it.</summary>
    private async Task PassiveClickTake()
    {
        var panel = _main.Passives!;
        var session = _main.Session!;
        await FreshBuild(10, "Пассивки");
        Check(panel.View.Size.X > 0 && panel.View.Size.Y > 0, $"размер TreeView {panel.View.Size}");
        Check(panel.PointsText == $"Очки: 0/{session.PassivePointsTotal}", $"счетчик «{panel.PointsText}»");

        var id = session.AllocatablePassives.OrderBy(x => x, StringComparer.Ordinal).First();
        Check(panel.View.StateOf(id) == TreeNodeState.Available, $"доступный узел {id} не подсвечен");
        var pos = NodePoint(panel.View, id);
        var hit = panel.View.ClickAt(pos);
        Check(hit == id, $"клик попал в {hit ?? "пустоту"}, а не в {id}");
        Check(session.Build.PassiveNodes.Contains(id) && session.PassivePointsSpent == 1, "узел не взят");
        await Settle();
        Check(panel.PointsText == $"Очки: 1/{session.PassivePointsTotal}", $"счетчик «{panel.PointsText}»");
        Check(panel.View.StateOf(id) == TreeNodeState.Taken, "взятый узел не окрашен как взятый");

        Check(panel.View.ClickAt(NodePoint(panel.View, id)) == id, "второй клик мимо");
        Check(!session.Build.PassiveNodes.Contains(id), "узел не снят");
        await Settle();
        Check(panel.PointsText == $"Очки: 0/{session.PassivePointsTotal}", $"счетчик после снятия «{panel.PointsText}»");
    }

    /// <summary>Clicking an unavailable node leaves the build alone and shows the refusal in the status line.</summary>
    private async Task PassiveClickUnavailable()
    {
        var panel = _main.Passives!;
        var session = _main.Session!;
        await FreshBuild(10, "Пассивки");
        var allocatable = session.AllocatablePassives;
        var node = session.Data.Passives.Nodes.Values
            .Where(n => n.Kind != Dialoid.Core.Passives.PassiveKind.Start && !allocatable.Contains(n.Id))
            .OrderBy(n => n.Id, StringComparer.Ordinal).First();
        Check(panel.View.StateOf(node.Id) == TreeNodeState.Unavailable, $"узел {node.Id} не помечен недоступным");
        var revision = session.Revision;
        Check(panel.View.ClickAt(NodePoint(panel.View, node.Id)) == node.Id, "клик мимо недоступного узла");
        Check(session.Revision == revision, "недоступный узел изменил билд");
        await Settle();
        Check(panel.StatusText.StartsWith("Команда отклонена: ", StringComparison.Ordinal) && panel.StatusText.Contains(node.Name),
            $"статус «{panel.StatusText}»");

        Check(panel.View.ClickAt(new Vector2(1, 1)) == null && session.Revision == revision, "клик в пустоту что-то изменил");
        var ok = session.AllocatablePassives.OrderBy(x => x, StringComparer.Ordinal).First();
        Check(panel.NodeClicked(ok).Ok, "доступный узел не взят");
        await Settle();
        Check(panel.StatusText == "", $"статус не очистился: «{panel.StatusText}»");
    }

    /// <summary>Right click on a taken attribute node opens the menu; picking «Ловкость» sets the choice through the session.</summary>
    private async Task PassiveAttributeMenu()
    {
        var panel = _main.Passives!;
        var session = _main.Session!;
        await FreshBuild(10, "Пассивки");
        var id = session.AllocatablePassives.OrderBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault(x => session.Data.Passives.Nodes[x].Kind == Dialoid.Core.Passives.PassiveKind.Attribute);
        Check(id != null, "рядом со стартом нет узла атрибута");
        Check(panel.NodeClicked(id!).Ok, "узел атрибута не взят");
        await Settle();

        var pos = NodePoint(panel.View, id!);
        Check(panel.View.RightClickAt(pos) == id, "ПКМ мимо узла атрибута");
        Check(panel.AttributeMenu.Visible && panel.AttributeMenuNodeId == id, "меню атрибута не открылось");
        var dex = PassivePanel.AttributeOrder.ToList().IndexOf(Dialoid.Core.Character.Attribute.Dex);
        panel.AttributeMenu.EmitSignal(PopupMenu.SignalName.IndexPressed, dex);
        panel.AttributeMenu.Hide();
        Check(session.Build.AttributeChoices.GetValueOrDefault(id!) == Dialoid.Core.Character.Attribute.Dex, "атрибут не выбран");
        await Settle();
        Check(panel.View.Nodes.First(n => n.Id == id).Badge == "Л", "на узле нет буквы атрибута");
        Check(panel.View.HoverAt(pos) == id && panel.HoverText.Contains("Ловкость"), $"подсказка «{panel.HoverText}»");

        var small = session.Data.Passives.Nodes.Values.First(n => n.Kind == Dialoid.Core.Passives.PassiveKind.Small);
        Check(!panel.NodeRightClicked(small.Id, pos), "меню атрибута открылось для малого узла");
    }

    /// <summary>Hover shows the description; zoom keeps the point under the cursor; zoom and pan survive a session change.</summary>
    private async Task PassiveHoverZoomPan()
    {
        var panel = _main.Passives!;
        var session = _main.Session!;
        var view = panel.View;
        await FreshBuild(10, "Пассивки");
        view.FitToView();
        var id = session.AllocatablePassives.OrderBy(x => x, StringComparer.Ordinal).First();
        var node = session.Data.Passives.Nodes[id];
        var pos = NodePoint(view, id);
        Check(view.HoverAt(pos) == id, "наведение мимо узла");
        await Settle();
        Check(panel.HoverText.Contains(node.Name) && panel.HoverText.Contains(node.Description), $"строка наведения «{panel.HoverText}»");
        Check(view._GetTooltip(pos).Contains(node.Description), "в подсказке нет описания");

        var zoom = view.Zoom;
        view.ZoomAt(pos, 2f);
        Check(Mathf.IsEqualApprox(view.Zoom, zoom * 2f), $"масштаб {view.Zoom} вместо {zoom * 2f}");
        Check(view.NodeLocalPosition(id)!.Value.DistanceTo(pos) < 0.5f, "узел под курсором сдвинулся при масштабе");
        view.PanBy(new Vector2(30, -20));
        Check(view.NodeLocalPosition(id)!.Value.DistanceTo(pos + new Vector2(30, -20)) < 0.5f, "сдвиг не применился");
        var (z, p) = (view.Zoom, view.Pan);
        Check(view.ClickAt(view.NodeLocalPosition(id)!.Value) == id, "клик после масштаба мимо");
        await Settle();
        Check(session.Build.PassiveNodes.Contains(id), "узел не взят после масштаба");
        Check(view.Zoom == z && view.Pan == p, "масштаб или сдвиг сбросились после изменения сессии");
        view.FitToView();
        Check(view.HoverAt(new Vector2(1, 1)) == null, "наведение в пустоту нашло узел");
    }

    /// <summary>At level 20: choose the ascendancy (its start node is taken), take one notable by a click, check the points.</summary>
    private async Task AscendancyTake()
    {
        var panel = _main.AscendancyView!;
        var session = _main.Session!;
        await FreshBuild(19, "Асцендант");
        Check(panel.HoverText.Contains("открывается с 20"), $"нет подсказки о 20 уровне: «{panel.HoverText}»");
        Check(panel.View.Nodes.Count == 0, "без асценданта нарисованы узлы");

        Check(_main.Character!.SelectLevel(20).Ok, "уровень 20 отклонен");
        await Settle();
        var choices = panel.AscendancyChoices;
        Check(choices.Count > 1 && choices[0] == null, "в выборе асценданта нет вариантов");
        Check(panel.SelectAscendancy(choices[1]).Ok, "асцендант не выбран");
        await Settle();
        var asc = session.Data.Ascendancies[choices[1]!];
        Check(session.Build.AscendancyId == asc.Id, "асцендант не в билде");
        Check(panel.View.Nodes.Count == asc.Notables.Count + 1, $"узлов в TreeView {panel.View.Nodes.Count}");
        Check(panel.View.StateOf(asc.Start.Id) == TreeNodeState.Taken, "стартовый узел не взят");
        Check(panel.PointsText == $"Очки асценданта: 0/{session.AscendancyPointsTotal}", $"счетчик «{panel.PointsText}»");

        var revision = session.Revision;
        Check(panel.View.ClickAt(NodePoint(panel.View, asc.Start.Id)) == asc.Start.Id, "клик мимо стартового узла");
        await Settle();
        Check(session.Revision == revision && panel.StatusText.Contains("стартовый узел"), $"статус «{panel.StatusText}»");

        var notable = asc.Notables[0];
        Check(panel.View.StateOf(notable.Id) == TreeNodeState.Available, "notable не подсвечен доступным");
        Check(panel.View.ClickAt(NodePoint(panel.View, notable.Id)) == notable.Id, "клик мимо notable");
        Check(session.Build.AscendancyNodes.Contains(notable.Id), "notable не взят");
        Check(session.AscendancyPointsSpent == notable.Cost, $"потрачено {session.AscendancyPointsSpent}");
        await Settle();
        Check(panel.PointsText == $"Очки асценданта: {notable.Cost}/{session.AscendancyPointsTotal}", $"счетчик «{panel.PointsText}»");
        Check(panel.View.StateOf(notable.Id) == TreeNodeState.Taken, "notable не окрашен взятым");
        Check(panel.StatusText == "", $"статус не очистился: «{panel.StatusText}»");
    }

    /// <summary>
    /// Synthetic mouse events into the passive TreeView's _GuiInput: press, move 20 px, release pans and does not toggle;
    /// press and release in place toggles. The node is dropped again afterwards.
    /// </summary>
    private async Task PassiveDragVsClick()
    {
        var panel = _main.Passives!;
        var session = _main.Session!;
        var view = panel.View;
        await FreshBuild(10, "Пассивки");
        view.FitToView();
        var id = session.AllocatablePassives.OrderBy(x => x, StringComparer.Ordinal).First();
        var pos = NodePoint(view, id);
        var revision = session.Revision;
        var pan = view.Pan;
        var shift = new Vector2(20, 0);

        view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = pos });
        view._GuiInput(new InputEventMouseMotion { Position = pos + shift, Relative = shift });
        view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = pos + shift });
        await Settle();
        Check(session.Revision == revision && !session.Build.PassiveNodes.Contains(id), "перетаскивание переключило узел");
        Check(view.Pan.DistanceTo(pan + shift) < 0.5f, $"сдвиг {view.Pan - pan} вместо {shift}");

        pos = NodePoint(view, id);
        view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = pos });
        view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = pos });
        await Settle();
        Check(session.Build.PassiveNodes.Contains(id), "клик без движения не взял узел");

        var zoom = view.Zoom;
        view._GuiInput(new InputEventMagnifyGesture { Position = pos, Factor = 1.5f });
        Check(Mathf.IsEqualApprox(view.Zoom, zoom * 1.5f), $"жест масштаба: {view.Zoom} вместо {zoom * 1.5f}");
        pan = view.Pan;
        view._GuiInput(new InputEventPanGesture { Position = pos, Delta = new Vector2(0, 1) });
        Check(view.Pan != pan, "жест сдвига не сдвинул дерево");

        Check(panel.NodeClicked(id).Ok, "узел не снялся обратно");
        view.FitToView();
        await Settle();
    }

    /// <summary>A refusal in the status line is cleared by an accepted no-op command (the same attribute again).</summary>
    private async Task PassiveNoopClearsError()
    {
        var panel = _main.Passives!;
        var session = _main.Session!;
        await FreshBuild(10, "Пассивки");
        var id = session.AllocatablePassives.OrderBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault(x => session.Data.Passives.Nodes[x].Kind == Dialoid.Core.Passives.PassiveKind.Attribute);
        Check(id != null, "рядом со стартом нет узла атрибута");
        Check(panel.NodeClicked(id!).Ok, "узел атрибута не взят");
        var unavailable = session.Data.Passives.Nodes.Values
            .First(n => n.Kind != Dialoid.Core.Passives.PassiveKind.Start && !session.AllocatablePassives.Contains(n.Id)
                        && !session.Build.PassiveNodes.Contains(n.Id));
        Check(!panel.NodeClicked(unavailable.Id).Ok, "недоступный узел взят");
        await Settle();
        Check(panel.StatusText != "", "отказ не показан");
        var revision = session.Revision;
        Check(panel.ChooseAttribute(id!, session.Build.AttributeChoices[id!]).Ok, "тот же атрибут отклонен");
        Check(session.Revision == revision, "тот же атрибут изменил билд");
        await Settle();
        Check(panel.StatusText == "", $"старый отказ остался: «{panel.StatusText}»");
    }

    /// <summary>Closed slots, the OptionButton signal, level, undo, a refusal and the rotation editor on a fresh build.</summary>
    private async Task SkillsSlots()
    {
        var panel = _main.Skills!;
        var session = _main.Session!;
        await FreshBuild(1, "Скиллы");
        Check(panel.SlotOptions.Count == SandboxSession.SlotCount, $"слотов нарисовано {panel.SlotOptions.Count}");
        Check(panel.SlotOptions[0] != null && panel.SlotOptions[1] == null, "закрытый слот нарисован открытым");
        Check(panel.UndoButtons.Count == 1 && panel.UndoButtons[0].Disabled, "«Вернуть» включена без истории слота");
        Check(!panel.SelectSkill(1, panel.SlotChoices[0][1]).Ok, "закрытый слот принял скилл");
        await Settle();
        Check(panel.CommandError != null, "отказ не показан");

        Check(_main.Character!.SelectLevel(20).Ok, "уровень 20 отклонен");
        await Settle();
        var choices = panel.SlotChoices[0];
        var skills = choices.Where(c => c != null && session.Data.Skills[c].UnlockLevel <= 20).Select(c => c!).ToList();
        Check(skills.Count >= 2, "у класса меньше двух скиллов до 20 уровня");

        var box = panel.SlotOptions[0]!;
        box.EmitSignal(OptionButton.SignalName.ItemSelected, choices.ToList().IndexOf(skills[0]));
        Check(session.GetSlot(0)?.SkillId == skills[0], "скилл не встал через сигнал OptionButton");
        Check(GodotObject.IsInstanceValid(box) && box.IsInsideTree() && panel.RedrawPending, "OptionButton удален во время сигнала");
        await Settle();
        Check(panel.CommandError == null, $"после успешной команды отказ остался: {panel.CommandError}");
        Check(panel.TreeSlot == 0 && panel.LevelNumber != null, "дерево слота 1 не показано");
        Check(panel.LevelNumber!.Line.Value == session.GetSlot(0)!.Level, "число уровня скилла не совпадает");

        Check(panel.SelectLevel(0, 3).Ok, "уровень скилла 3 отклонен");
        await Settle();
        Check(panel.LevelNumber!.Line.Value == 3 && panel.PointsText.Contains("из 3"), $"уровень {panel.LevelNumber.Line.Value}, «{panel.PointsText}»");
        Check(!panel.UndoButtons[0].Disabled, "«Вернуть» выключена после смены скилла");
        Check(panel.PointsText.EndsWith($"свободно {session.SkillPointsFree(0)}", StringComparison.Ordinal), $"«{panel.PointsText}»");
        var tree = session.Data.Skills[skills[0]].Tree;
        var gated = tree.FirstOrDefault(n => n.Requires.Count > 0);
        if (gated != null)
        {
            var why = session.WhyCannotAddSkillNodePoint(0, gated.Id);
            Check(why != null && panel.BlockedNodes.GetValueOrDefault(gated.Id) == why, $"причина серого узла «{panel.BlockedNodes.GetValueOrDefault(gated.Id)}»");
            Check(!panel.AddPoint(0, gated.Id).Ok && panel.CommandError == why, "отказ «+» не совпал с причиной в подсказке");
        }
        foreach (var free in tree.Where(n => n.Requires.Count == 0))
            Check(!panel.BlockedNodes.ContainsKey(free.Id) || session.WhyCannotAddSkillNodePoint(0, free.Id) != null, $"узел {free.Id} серый без причины");

        Check(panel.SelectSkill(0, skills[1]).Ok, "второй скилл не встал");
        Check(panel.Undo(0).Ok && session.GetSlot(0)?.SkillId == skills[0] && session.GetSlot(0)!.Level == 3, "«Вернуть» не вернул скилл с уровнем");

        Check(panel.SelectSkill(1, skills[1]).Ok, "скилл во второй слот не встал");
        await Settle();
        Check(panel.ShownRotation.SequenceEqual(new[] { skills[0], skills[1] }), "ротация не по порядку слотов");
        Check(panel.MoveInRotation(0, +1).Ok, "сдвиг ротации отклонен");
        Check(session.Build.Rotation.SequenceEqual(new[] { skills[1], skills[0] }), "ротация не изменилась");
        await Settle();
        Check(panel.ShownRotation.SequenceEqual(new[] { skills[1], skills[0] }), "показанная ротация не изменилась");
        Check(panel.ResetRotation().Ok && session.Build.Rotation.Count == 0, "ротация не сбросилась");
        await Settle();
    }

    /// <summary>On a reference: taking a point back out and putting it in again changes the SkillLines both times.</summary>
    private async Task SkillsNodePoint(ReferenceBuildInfo info)
    {
        var panel = _main.Skills!;
        var session = _main.Session!;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        _main.SelectTab("Скиллы");
        await Settle();
        static string Snapshot(IEnumerable<NumberLabel> labels) => string.Join("|", labels.Select(l => l.Line.Key + "=" + l.Line.Value));

        for (int slot = 0; slot < SandboxSession.SlotCount; slot++)
        {
            if (session.GetSlot(slot) is not { } current || !session.Data.Skills.TryGetValue(current.SkillId, out var skill)) continue;
            panel.ShowTree(slot);
            await Settle();
            Check(panel.TreeSlot == slot, "дерево слота не открылось");
            var original = Snapshot(panel.SkillNumbers);
            Check(panel.SkillNumbers.Count > 0, $"у скилла «{skill.Name}» нет SkillLines");
            foreach (var node in skill.Tree.Where(n => current.NodePoints.GetValueOrDefault(n.Id) > 0))
            {
                if (!panel.RemovePoint(slot, node.Id).Ok) continue;
                await Settle();
                var without = Snapshot(panel.SkillNumbers);
                Check(panel.AddPoint(slot, node.Id).Ok, $"очко в «{node.Name}» не вернулось: {panel.CommandError}");
                await Settle();
                var with = Snapshot(panel.SkillNumbers);
                Check(with == original, "после возврата очка числа не те, что были");
                if (without == with) continue;
                Check(panel.CommandError == null, "после успешной команды остался отказ");
                return;
            }
        }
        throw new Failure("ни одно очко дерева не изменило SkillLines");
    }

    /// <summary>Awaits a task with a time limit (the simulation runs on the thread pool).</summary>
    private static async Task WithTimeout(Task task, int milliseconds, string what)
    {
        var done = await Task.WhenAny(task, Task.Delay(milliseconds));
        Check(done == task, $"{what}: нет результата за {milliseconds / 1000} с");
        await task;
    }

    /// <summary>The reference on the training dummy: button off while running, then DPS &gt; 0, the graph and the log.</summary>
    private async Task SimulationReference(ReferenceBuildInfo info)
    {
        var panel = _main.Simulation!;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        _main.SelectTab("Симуляция");
        await Settle();
        var training = panel.DummyIds.ToList().IndexOf("training");
        Check(training >= 0, "нет манекена training");
        panel.DummyOptions.Selected = training;
        panel.SelectDummy(training);
        panel.ModeOptions.Selected = 0;
        panel.DummyAttacks.ButtonPressed = false;

        var run = panel.Simulate();
        Check(panel.Running && panel.RunButton.Disabled && panel.RunButton.Text == SimulationPanel.RunningText, "кнопка не выключена на время расчета");
        await Settle();
        if (panel.Running) Check(panel.StatusText == SimulationPanel.RunningText, $"статус во время расчета «{panel.StatusText}»");
        await WithTimeout(run, 60000, "симуляция");
        await Settle();
        Check(panel.AppliedOnMainThread, "результат применен не в главном потоке");
        Check(!panel.Running && !panel.RunButton.Disabled, "кнопка не включилась после расчета");
        var dps = panel.NumberFor("sim_dps_single");
        Check(dps != null, $"нет DPS; статус «{panel.StatusText}»; " + string.Join("; ", panel.ShownViolations));
        Check(dps!.Line.Value > 0 && dps.Line.Value == panel.LastOutcome!.Result.DpsSingleTarget, $"DPS {dps.Line.Value}");
        Check(panel.NumberFor("sim_dps_pack") != null && panel.NumberFor("sim_hit_damage") != null, "нет DPS по паку или урона попаданиями");
        var result = panel.LastOutcome!.Result;
        Check(result.DpsBySkill.Count > 0 && result.DpsByType.Count > 0, "в SimResult нет DPS по скиллам или типам");
        foreach (var (skill, value) in result.DpsBySkill)
            Check(panel.NumberFor("sim_dps_skill_" + skill)?.Line.Value == value, $"DPS скилла {skill} не показан");
        foreach (var (type, value) in result.DpsByType)
            Check(panel.NumberFor("sim_dps_type_" + type.ToString().ToLowerInvariant())?.Line.Value == value, $"DPS типа {type} не показан");
        Check(panel.Graph != null && panel.Graph.Samples.Count > 1, "нет графика ресурса");
        Check(panel.Log == null, "лог показан без кнопки");
        panel.ToggleLog();
        await Settle();
        Check(panel.Log != null && panel.Log.GetParsedText().Length > 0, "лог не показан");
        Check(panel.Log!.GetParsedText().Contains("] " + BreakdownText.EventKindName("use") + ":", StringComparison.Ordinal)
              && !panel.Log.GetParsedText().Contains("] use:", StringComparison.Ordinal), "виды событий в логе не по-русски");

        dps.Open();
        Check(_main.CurrentTab == Main.BreakdownTab && _main.Breakdown!.Current?.Line.Key == "sim_dps_single", "DPS не открылся в «Разборе»");
        _main.SelectTab("Симуляция");

        // «Манекен бьет» on a dummy without an attack: no defense run, the panel says the dummy does not attack.
        Check(_main.Session!.Data.Dummies["training"].Attack == null, "у манекена training появилась атака");
        panel.DummyAttacks.ButtonPressed = true;
        await WithTimeout(panel.Simulate(), 60000, "симуляция с галкой на неатакующем манекене");
        await Settle();
        var noAttack = panel.LastOutcome!.Result;
        Check(noAttack.Defense == null && noAttack.TimeToDeath == null, "у неатакующего манекена есть защитные итоги");
        Check(panel.TimeToDeathText == BreakdownText.NoTimeToDeath(true, noAttack)
              && panel.TimeToDeathText!.Contains("не атакует", StringComparison.Ordinal),
            $"строка времени до смерти «{panel.TimeToDeathText}»");
        panel.DummyAttacks.ButtonPressed = false;

        // Defensive mode on the attacking dummy.
        var attacking = panel.DummyIds.ToList().IndexOf("attacking_boss");
        if (attacking >= 0)
        {
            panel.DummyOptions.Selected = attacking;
            panel.SelectDummy(attacking);
            panel.DummyAttacks.ButtonPressed = true;
            await WithTimeout(panel.Simulate(), 60000, "симуляция с атакой манекена");
            await Settle();
            Check(panel.LastOutcome!.Result.Defense != null && panel.NumberFor("sim_def_attacks") != null, "нет защитных итогов");
            panel.DummyAttacks.ButtonPressed = false;
            panel.DummyOptions.Selected = training;
            panel.SelectDummy(training);
        }
    }

    /// <summary>A skill node changed after the press: the arriving result is stale and not shown.</summary>
    private async Task SimulationStale(ReferenceBuildInfo info)
    {
        var panel = _main.Simulation!;
        var session = _main.Session!;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        _main.SelectTab("Симуляция");
        await Settle();

        var run = panel.Simulate();
        int slot = Enumerable.Range(0, SandboxSession.SlotCount).First(i => session.GetSlot(i)?.NodePoints.Count > 0);
        var revision = session.Revision;
        var node = session.GetSlot(slot)!.NodePoints.Keys.FirstOrDefault(id => _main.Skills!.RemovePoint(slot, id).Ok);
        Check(node != null && session.Revision == revision + 1, "узел скилла не изменился");
        await WithTimeout(run, 60000, "симуляция");
        await Settle();
        Check(panel.LastOutcome != null && panel.LastOutcome.Revision != session.Revision, "результат не от старой ревизии");
        Check(panel.StatusText == SimulationPanel.StaleText, $"статус «{panel.StatusText}»");
        Check(panel.Numbers.Count == 0, "устаревшие числа показаны");
    }

    /// <summary>A rejected run (level 1 on a level-60 reference) shows the violations instead of numbers.</summary>
    private async Task SimulationViolations(ReferenceBuildInfo info)
    {
        var panel = _main.Simulation!;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        Check(_main.Character!.SelectLevel(1).Ok, "уровень 1 отклонен");
        _main.SelectTab("Симуляция");
        await WithTimeout(panel.Simulate(), 60000, "симуляция");
        await Settle();
        Check(panel.ShownViolations.Count > 0 && panel.Numbers.Count == 0, $"нарушения не показаны; статус «{panel.StatusText}»");
        Check(_main.Builds.Load(info).Ok, "эталон не перезагрузился");
        await Settle();
        Check(panel.StatusText == SimulationPanel.StaleText, "после смены билда результат не помечен устаревшим");
    }

    /// <summary>
    /// Runs «Тренировочный», mode «среднее», seed 1, pack from the dummy, no dummy attack, through the SimulationPanel and
    /// returns the applied, current outcome.
    /// </summary>
    private async Task<SimOutcome> SimulateTraining()
    {
        var panel = _main.Simulation!;
        _main.SelectTab("Симуляция");
        var training = panel.DummyIds.ToList().IndexOf(TrainingDummy);
        Check(training >= 0, "нет манекена training");
        panel.DummyOptions.Selected = training;
        panel.SelectDummy(training);
        panel.ModeOptions.Selected = 0;
        panel.Seed.Value = AcceptanceSeed;
        panel.DummyAttacks.ButtonPressed = false;
        await WithTimeout(panel.Simulate(), 60000, "симуляция");
        await Settle();
        var outcome = panel.LastOutcome;
        Check(outcome != null && outcome.Revision == _main.Session!.Revision, $"нет свежего результата; статус «{panel.StatusText}»");
        Check(outcome!.Request.Mode == SimMode.Average && outcome.Request.DummyId == TrainingDummy, "запрос не «Тренировочный», «среднее»");
        Check(outcome.Result.Violations.Count == 0, "нарушения: " + string.Join("; ", outcome.Result.Violations.Select(v => v.Message)));
        var dps = panel.NumberFor("sim_dps_single");
        Check(dps != null && dps.Line.Value == outcome.Result.DpsSingleTarget, "DPS панели не из SimResult");
        return outcome;
    }

    /// <summary>The slot of the build's main skill: the slotted skill with the highest DPS in the outcome.</summary>
    private int MainSkillSlot(SimOutcome outcome)
    {
        var session = _main.Session!;
        var slots = Enumerable.Range(0, SandboxSession.SlotCount).Where(i => session.GetSlot(i) != null).ToList();
        Check(slots.Count > 0, "в билде нет скиллов");
        return slots.OrderByDescending(i => outcome.Result.DpsBySkill.GetValueOrDefault(session.GetSlot(i)!.SkillId)).First();
    }

    /// <summary>The main skill's SkillLines labels as shown on the «Скиллы» tab.</summary>
    private async Task<IReadOnlyList<NumberLabel>> ShowSkill(int slot)
    {
        var panel = _main.Skills!;
        _main.SelectTab("Скиллы");
        panel.ShowTree(slot);
        await Settle();
        Check(panel.TreeSlot == slot, "дерево главного скилла не открылось");
        return panel.SkillNumbers;
    }

    /// <summary>Opens a label (as its press does) and returns the «Разбор» lines.</summary>
    private List<string> OpenBreakdown(NumberLabel label)
    {
        label.Open();
        Check(_main.CurrentTab == Main.BreakdownTab && _main.Breakdown!.Current?.Line.Key == label.Line.Key, $"«{label.Line.Key}» не открылся в «Разборе»");
        return _main.Breakdown!.ShownLines.ToList();
    }

    private static bool NamesNode(string line, string name) => line.EndsWith("— " + name, StringComparison.Ordinal);

    /// <summary>Clicks a passive node by its TreeView coordinates on the «Пассивки» tab; returns whether the build changed.</summary>
    private async Task<bool> ClickPassive(string id)
    {
        var panel = _main.Passives!;
        var session = _main.Session!;
        _main.SelectTab("Пассивки");
        await Settle();
        panel.View.FitToView();
        var revision = session.Revision;
        var hit = panel.View.ClickAt(NodePoint(panel.View, id));
        Check(hit == id, $"клик попал в {hit ?? "пустоту"}, а не в {id}");
        await Settle();
        return session.Revision != revision;
    }

    /// <summary>
    /// Spec §15.4 on one main reference: load it via BuildsPanel, simulate «Тренировочный» in mode «среднее» via
    /// SimulationPanel and compare with Simulator.Run on the same file (exact). Then two node changes on the main skill:
    /// a skill-tree point named in its hit multiplier breakdown taken out with the SkillsPanel «−» (DPS changes, the node's
    /// line in that breakdown changes or goes; «+» brings DPS back exactly), and a taken passive notable named in the
    /// skill's breakdown dropped by a PassivePanel click (same checks; reported when every notable holds the path).
    /// </summary>
    private async Task Acceptance(ReferenceBuildInfo info)
    {
        var session = _main.Session!;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        Check(_main.Breakdown!.History.Count == 0 && _main.Breakdown.Current == null, "история «Разбора» не очищена после загрузки");
        await Settle();
        var before = await SimulateTraining();
        var dpsBefore = before.Result.DpsSingleTarget;

        // Review Focus 5: the panel's DPS is Simulator.Run's on the same file, to the last bit.
        Check(BuildCodec.TryFromJson(File.ReadAllText(info.Path), session.Data, out var fileBuild, out var parseError), $"файл не читается: {parseError}");
        var config = new SimConfig(TrainingDummy, SimMode.Average, AcceptanceSeed,
            PackSizeOverride: session.Data.Dummies[TrainingDummy].PackSize, DummyAttacks: false);
        var reference = new Simulator(session.Data, MechanicRegistry.Default).Run(fileBuild!, config);
        Check(reference.DpsSingleTarget == dpsBefore, $"DPS панели {dpsBefore:R}, Simulator.Run {reference.DpsSingleTarget:R}");
        Check(reference.DpsPack == before.Result.DpsPack, $"DPS по паку панели {before.Result.DpsPack:R}, Simulator.Run {reference.DpsPack:R}");

        var slot = MainSkillSlot(before);
        var skillName = session.Data.Skills[session.GetSlot(slot)!.SkillId].Name;
        var skillColumn = await SkillNodeChange(slot, skillName, dpsBefore);
        var notableColumn = await NotableChange(slot, skillName, dpsBefore, fileBuild!);
        _table.Add($"{info.Title} | «{skillName}» | {BreakdownText.Whole(dpsBefore)} | {skillColumn} | {notableColumn}");
        _main.SelectTab(Main.TabTitles[0]);
    }

    /// <summary>
    /// A point of the main skill's tree named in a hit multiplier breakdown: «−» through SkillsPanel, DPS changes and the
    /// node's line in that breakdown changes or goes, «+» brings DPS back exactly. Fails when no such node exists.
    /// </summary>
    private async Task<string> SkillNodeChange(int slot, string skillName, double dpsBefore)
    {
        var session = _main.Session!;
        var skills = _main.Skills!;
        var current = session.GetSlot(slot)!;
        var labels = await ShowSkill(slot);
        // Hit multipliers first; when every node they name holds a dependent node (the session refuses «−»), the other
        // damage numbers of the hit: crit chance and damage, penetration. The table names the refused ones.
        static int Rank(string key) => key.StartsWith("hit_mult_", StringComparison.Ordinal) ? 0
            : key.StartsWith("crit_", StringComparison.Ordinal) || key.StartsWith("penetration_", StringComparison.Ordinal) ? 1 : 2;
        var damage = labels.Where(l => Rank(l.Line.Key) < 2).OrderBy(l => Rank(l.Line.Key)).ToList();
        Check(damage.Any(l => Rank(l.Line.Key) == 0), $"у «{skillName}» нет множителя урона попадания");
        var candidates = session.Data.Skills[current.SkillId].Tree
            .Where(n => current.NodePoints.GetValueOrDefault(n.Id) > 0)
            .Select(n => (Node: n, Label: damage.FirstOrDefault(l => BreakdownPanel.LinesOf(l.Line).Any(x => NamesNode(x, n.Name)))))
            .Where(x => x.Label != null)
            .OrderBy(x => Rank(x.Label!.Line.Key)).ThenBy(x => x.Node.Id, StringComparer.Ordinal).ToList();
        Check(candidates.Any(x => Rank(x.Label!.Line.Key) == 0), $"ни один взятый узел дерева «{skillName}» не назван в разборе множителя урона попадания");

        var refused = new List<string>();
        foreach (var (node, label) in candidates)
        {
            var key = label!.Line.Key;
            var named = OpenBreakdown(label).Where(x => NamesNode(x, node.Name)).ToList();
            await ShowSkill(slot);
            var points = session.GetSlot(slot)!.NodePoints[node.Id];
            if (!skills.RemovePoint(slot, node.Id).Ok)
            {
                Check(skills.CommandError != null, "отказ «−» без причины");
                refused.Add($"{node.Name}: {skills.CommandError}");
                continue;
            }
            Check(session.GetSlot(slot)!.NodePoints.GetValueOrDefault(node.Id) == points - 1, "очко узла не снято");
            var dpsAfter = (await SimulateTraining()).Result.DpsSingleTarget;
            Check(dpsAfter != dpsBefore, $"без очка в «{node.Name}» DPS не изменился ({dpsBefore:R})");

            var after = (await ShowSkill(slot)).FirstOrDefault(l => l.Line.Key == key);
            Check(after != null, $"после «−» нет числа {key}");
            var namedAfter = OpenBreakdown(after!).Where(x => NamesNode(x, node.Name)).ToList();
            Check(!named.SequenceEqual(namedAfter), $"строка «{node.Name}» в разборе {key} не изменилась");

            await ShowSkill(slot);
            Check(skills.AddPoint(slot, node.Id).Ok, $"очко в «{node.Name}» не вернулось: {skills.CommandError}");
            var dpsBack = (await SimulateTraining()).Result.DpsSingleTarget;
            Check(dpsBack == dpsBefore, $"DPS после «+» {dpsBack:R}, был {dpsBefore:R}");
            var change = $"«{named[0].Trim()}» → {(namedAfter.Count == 0 ? "нет строки" : $"«{namedAfter[0].Trim()}»")}";
            return $"{node.Name} ({points}→{points - 1}): {BreakdownText.Whole(dpsAfter)}, {change} в {key}, после «+» {BreakdownText.Whole(dpsBack)}" +
                   (refused.Count > 0 ? $" (из hit_mult «−» отклонен: {string.Join("; ", refused)})" : "");
        }
        throw new Failure($"«−» отклонен у всех узлов «{skillName}» из разбора урона попадания: {string.Join("; ", refused)}");
    }

    /// <summary>
    /// A taken passive notable named in the main skill's breakdown dropped by a PassivePanel click: DPS changes, the
    /// breakdown loses the node's line, a second click brings DPS back exactly. When every notable holds the path to the
    /// start the session refuses each one; that is reported («—»), not faked with another node.
    /// </summary>
    private async Task<string> NotableChange(int slot, string skillName, double dpsBefore, Dialoid.Core.Character.Build fileBuild)
    {
        var session = _main.Session!;
        var labels = await ShowSkill(slot);
        var notables = session.Build.PassiveNodes
            .Select(id => session.Data.Passives.Nodes[id])
            .Where(n => n.Kind == PassiveKind.Notable)
            .OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        Check(notables.Count > 0, "в билде нет notables");
        // Damage numbers first (hit multipliers, then crit and penetration), then any other SkillLine naming the node.
        static int Rank(NumberLabel l) => l.Line.Key.StartsWith("hit_mult_", StringComparison.Ordinal) ? 0
            : l.Line.Key.Contains("crit", StringComparison.Ordinal) || l.Line.Key.StartsWith("penetration_", StringComparison.Ordinal) ? 1 : 2;
        var named = notables
            .Select(n => (Node: n, Label: labels.Where(l => BreakdownPanel.LinesOf(l.Line).Any(x => NamesNode(x, n.Name)))
                .OrderBy(Rank).FirstOrDefault()))
            .Where(x => x.Label != null)
            .OrderBy(x => Rank(x.Label!)).ThenBy(x => x.Node.Id, StringComparer.Ordinal).ToList();
        Check(named.Count > 0, $"ни один из {notables.Count} notables не назван в разборе скилла «{skillName}»");

        var refused = new List<string>();
        foreach (var (node, label) in named)
        {
            var key = label!.Line.Key;
            var linesBefore = OpenBreakdown(label);
            Check(linesBefore.Any(x => NamesNode(x, node.Name)), $"в «Разборе» {key} нет строки «{node.Name}»");

            if (!await ClickPassive(node.Id))
            {
                // Dropping it would break the path to the start: the session refuses, try the next notable.
                Check(_main.Passives!.StatusText.Contains(node.Name), $"отказ без причины: «{_main.Passives.StatusText}»");
                refused.Add(node.Name);
                continue;
            }
            Check(!session.Build.PassiveNodes.Contains(node.Id), "узел не снят");

            var dpsAfter = (await SimulateTraining()).Result.DpsSingleTarget;
            Check(dpsAfter != dpsBefore, $"без «{node.Name}» DPS не изменился ({dpsBefore:R})");

            var after = (await ShowSkill(slot)).FirstOrDefault(l => l.Line.Key == key);
            Check(after != null, $"после снятия нет числа {key}");
            var linesAfter = OpenBreakdown(after!);
            var gone = linesBefore.Except(linesAfter).Where(x => NamesNode(x, node.Name)).ToList();
            Check(gone.Count > 0 && !linesAfter.Any(x => NamesNode(x, node.Name)), $"разбор {key} не потерял строку «{node.Name}»");

            Check(await ClickPassive(node.Id), $"«{node.Name}» не взят обратно: {_main.Passives!.StatusText}");
            var dpsBack = (await SimulateTraining()).Result.DpsSingleTarget;
            Check(dpsBack == dpsBefore, $"DPS после возврата {dpsBack:R}, был {dpsBefore:R}");
            return $"{node.Name}: {BreakdownText.Whole(dpsAfter)}, ушла «{gone[0].Trim()}» из {key}, после возврата {BreakdownText.Whole(dpsBack)}" +
                   (refused.Count > 0 ? $" (держат путь: {string.Join(", ", refused)})" : "");
        }

        // No named notable can go. If some other notable can, the check is broken; if none can, the build's tree has no
        // removable notable at all: that is reported in the table, not faked with a different node.
        foreach (var node in notables.Where(n => !refused.Contains(n.Name)))
        {
            if (!await ClickPassive(node.Id)) { refused.Add(node.Name); continue; }
            Check(await ClickPassive(node.Id), $"«{node.Name}» не взят обратно");
            throw new Failure($"notables из разбора «{skillName}» держат путь к старту ({string.Join(", ", refused)}), а «{node.Name}» снимается, но не назван в разборе");
        }
        Check(session.Build.PassiveNodes.ToHashSet().SetEquals(fileBuild.PassiveNodes), "после отказов билд изменился");
        return $"— (все {notables.Count} notables держат путь к старту, сессия отклоняет снятие)";
    }

    /// <summary>
    /// Passive tree captions on a full reference, fitted: no two placed captions overlap and no caption covers a node.
    /// </summary>
    private async Task PassiveCaptions(ReferenceBuildInfo info)
    {
        var view = _main.Passives!.View;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        _main.SelectTab("Пассивки");
        await Settle();
        view.FitToView();
        // The headless window is small: zoom in (around the centre) until a notable is drawn 11 px wide, as in a 1600×1000 window.
        var notableRadius = view.Nodes.Where(n => n.Caption != null).Min(n => n.Radius);
        if (notableRadius * view.Zoom < 11f) view.ZoomAt(view.Size / 2, 11f / (notableRadius * view.Zoom));
        await Settle();
        var placed = view.LayoutCaptions();
        var captions = view.Nodes.Count(n => n.Caption != null);
        Check(placed.Count > captions / 2, $"подписано {placed.Count} из {captions} узлов (TreeView {view.Size}, масштаб {view.Zoom})");
        for (int i = 0; i < placed.Count; i++)
        {
            for (int j = i + 1; j < placed.Count; j++)
                Check(!placed[i].Rect.Intersects(placed[j].Rect), $"подписи {placed[i].Id} и {placed[j].Id} перекрываются");
            foreach (var n in view.Nodes)
                Check(!TreeView.CircleHitsRect(view.NodeLocalPosition(n.Id)!.Value, Mathf.Max(n.Radius * view.Zoom, 3f), placed[i].Rect),
                    $"подпись {placed[i].Id} закрывает узел {n.Id}");
        }
        Check(view.LayoutCaptions().SequenceEqual(placed), "раскладка подписей не детерминирована");
        GD.Print($"SELFTEST NOTE подписи дерева: {placed.Count} из {captions} при масштабе {view.Zoom}");
        view.FitToView();
    }

    /// <summary>Review Focus: switching the class after a reference resets the build; the panels redraw without errors.</summary>
    private async Task ClassChangeResets(ReferenceBuildInfo info)
    {
        var session = _main.Session!;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        await SimulateTraining();
        var other = session.Data.Classes.Keys.OrderBy(k => k, StringComparer.Ordinal).First(k => k != session.Build.ClassId);
        var level = session.Build.Level;
        _main.Character!.NumberFor("life")!.Open();
        Check(_main.Character.SelectClass(other).Ok, $"смена класса на {other} отклонена");
        Check(_main.Breakdown!.History.Count == 0 && _main.Breakdown.Current == null, "история «Разбора» не очищена после смены класса");
        var build = session.Build;
        Check(build.ClassId == other && build.Level == level, "класс не сменился или уровень сбросился");
        Check(build.PassiveNodes.Count == 0 && build.AttributeChoices.Count == 0, "пассивки не сброшены");
        Check(build.AscendancyId == null && build.AscendancyNodes.Count == 0, "асцендант не сброшен");
        Check(build.Slots.Count == 0 && build.Rotation.Count == 0, "скиллы или ротация не сброшены");
        foreach (var tab in Main.TabTitles)
        {
            _main.SelectTab(tab);
            await Settle();
        }
        Check(_main.Passives!.PointsText == $"Очки: 0/{session.PassivePointsTotal}", $"счетчик «{_main.Passives.PointsText}»");
        Check(_main.Skills!.TreeSlot == -1, "после сброса показано дерево скилла");
        Check(_main.Simulation!.StatusText == SimulationPanel.StaleText, $"статус симуляции «{_main.Simulation.StatusText}»");
        Check(!_main.Character.RedrawPending && !_main.Skills.RedrawPending && !_main.Passives.RedrawPending, "панели не перерисовались");
    }

    /// <summary>
    /// Screenshot run (real window only): the pyromancer reference after a training simulation, one PNG per tab, plus the
    /// «Разбор» of the main skill's hit multiplier opened by its NumberLabel.
    /// </summary>
    private async Task Screenshots(ReferenceBuildInfo? pyro)
    {
        Check(DisplayServer.GetName() != "headless", "скриншоты нужны в окне, а не в --headless");
        Check(!string.IsNullOrEmpty(_screenshotDir), $"после {ScreenshotsArg} нет каталога");
        Check(pyro != null, "нет эталона sorcerer_pyromancer_fireball");
        Directory.CreateDirectory(_screenshotDir!);
        Check(_main.Builds!.Load(pyro!).Ok, "эталон не загрузился");
        await Settle();
        var outcome = await SimulateTraining();
        var slot = MainSkillSlot(outcome);

        _main.Passives!.View.FitToView();
        _main.Character!.NumberFor("life")!.Open(); // the plain «Разбор» shot shows the life breakdown
        var names = new[] { "00-room", "01-character", "02-passives", "03-ascendancy", "04-skills", "05-simulation", "06-breakdown", "07-builds" };
        for (int i = 0; i < Main.TabTitles.Count; i++)
        {
            if (Main.TabTitles[i] == "Скиллы") await ShowSkill(slot);
            _main.SelectTab(Main.TabTitles[i]);
            await Shot(names[i]);
        }

        // The room with the hero next to the dummy (the combat spot), then back at the spawn.
        var room = Room;
        _main.SelectTab(Main.RoomTab);
        room.PlaceHero(room.DummySpot + TowardsCamera * 2.2f + new Vector3(0.6f, 0, -0.6f));
        await Shot("00-room-dummy");
        room.PlaceHero(room.HeroSpawn);

        var labels = await ShowSkill(slot);
        var mult = labels.FirstOrDefault(l => l.Line.Key.StartsWith("hit_mult_", StringComparison.Ordinal));
        Check(mult != null, "у главного скилла нет множителя урона попадания");
        OpenBreakdown(mult!);
        await Shot("08-breakdown-hit-multiplier");
        _main.SelectTab(Main.TabTitles[0]);
    }

    /// <summary>Waits until the current tab is drawn and saves the viewport as DIR/name.png (downscaled to the window width).</summary>
    private async Task Shot(string name)
    {
        await Settle();
        await _main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = _main.GetViewport().GetTexture().GetImage();
        Check(image != null && image.GetWidth() > 0, "пустой кадр");
        if (image!.GetWidth() > ScreenshotWindow.X)
            image.Resize(ScreenshotWindow.X, image.GetHeight() * ScreenshotWindow.X / image.GetWidth(), Image.Interpolation.Lanczos);
        var path = Path.Combine(_screenshotDir!, name + ".png");
        Check(image.SavePng(path) == Error.Ok, $"не сохранен {path}");
        GD.Print($"SELFTEST SCREENSHOT {path} {image.GetWidth()}x{image.GetHeight()}");
    }

    /// <summary>Throws a <see cref="Failure"/> with <paramref name="why"/> when the condition is false.</summary>
    public static void Check(bool condition, string why)
    {
        if (!condition) throw new Failure(why);
    }

    /// <summary>Waits for the next processed frame.</summary>
    public async Task NextFrame() => await _main.ToSignal(_main.GetTree(), SceneTree.SignalName.ProcessFrame);

    /// <summary>Waits two frames so deferred redraws (<see cref="Ui.SessionPanel.RequestRedraw"/>) have run.</summary>
    public async Task Settle()
    {
        await NextFrame();
        await NextFrame();
    }
}
