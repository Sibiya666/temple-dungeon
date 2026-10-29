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
/// </summary>
public sealed class SelfTest
{
    /// <summary>A failed check; its message is printed after the step name.</summary>
    public sealed class Failure(string message) : Exception(message);

    /// <summary>One named check.</summary>
    public sealed record Step(string Name, Func<Task> Run);

    private readonly Main _main;

    public SelfTest(Main main) => _main = main;

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
        var result = builds.ImportText("мусор");
        Check(!result.Ok, "импорт мусора принят");
        Check(session.Revision == revision, $"ревизия {revision} → {session.Revision}");
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
