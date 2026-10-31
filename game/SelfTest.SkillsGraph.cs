using Dialoid.Game.Ui;
using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
    private async Task AllSkillTreeLayouts()
    {
        await FreshBuild(20, WindowIds.Skills);
        var session = _main.Session!;
        var obstacle = new Rect2(0, 0, 10, 10);
        Check(SkillGraphRouting.Intersects(new Vector2(-5, 5), new Vector2(15, 5), obstacle), "пропущено горизонтальное пересечение");
        Check(SkillGraphRouting.Intersects(new Vector2(5, -5), new Vector2(5, 15), obstacle), "пропущено вертикальное пересечение");
        Check(SkillGraphRouting.Intersects(new Vector2(-2, -2), Vector2.Zero, obstacle), "пропущено касание угла");
        Check(SkillGraphRouting.Intersects(new Vector2(5, 5), new Vector2(5, 5), obstacle), "пропущена точка внутри иконки");
        Check(!SkillGraphRouting.Intersects(new Vector2(-1, -5), new Vector2(-1, 15), obstacle), "ложное пересечение параллельной линии");
        foreach (var skill in session.Data.Skills.Values.Where(s => s.Tree.Count > 0))
        {
            var layout = SkillTreeLayouts.For(skill);
            Check(layout != null, "нет отдельной раскладки " + skill.Id);
            foreach (var node in skill.Tree)
            {
                var point = layout![node.Id];
                Check(float.IsFinite(point.X) && float.IsFinite(point.Y), "неверная координата " + skill.Id);
                Check(point.Length() >= 74, "узел перекрывает центр " + skill.Id + "/" + node.Id);
                foreach (var other in skill.Tree.Where(n => string.CompareOrdinal(n.Id, node.Id) > 0))
                {
                    float a = node.ExclusiveGroup != null ? 34 : 30;
                    float b = other.ExclusiveGroup != null ? 34 : 30;
                    Check(point.DistanceTo(layout[other.Id]) >= a + b + 4,
                        "перекрываются узлы " + skill.Id + ": " + node.Id + "/" + other.Id);
                }
            }
            var positions = new Dictionary<string, Vector2>(layout!) { ["__skill_center"] = Vector2.Zero };
            var radii = skill.Tree.ToDictionary(n => n.Id, n => n.ExclusiveGroup != null ? 34f : 30f);
            radii["__skill_center"] = 34;
            var edges = skill.Tree.SelectMany(n => n.Requires.Count == 0
                ? new[] { (A: "__skill_center", B: n.Id) }
                : n.Requires.Select(r => (A: r.NodeId, B: n.Id))).ToList();
            edges.AddRange(skill.Tree.Where(n => n.ExclusiveGroup != null).GroupBy(n => n.ExclusiveGroup)
                .SelectMany(group => group.SelectMany((a, i) => group.Skip(i + 1).Select(b => (a.Id, b.Id)))));
            foreach (var (a, b) in edges)
            {
                var blocks = positions.Where(p => p.Key != a && p.Key != b)
                    .Select(p => new Rect2(p.Value - Vector2.One * (radii[p.Key] + 4), Vector2.One * (radii[p.Key] + 4) * 2)).ToArray();
                var path = SkillGraphRouting.Route(positions[a], positions[b], blocks);
                Check(path.Count >= 2 && path[0] == positions[a] && path[^1] == positions[b], "маршрут потерял конец " + skill.Id);
                for (int i = 1; i < path.Count; i++)
                    Check(blocks.All(block => !SkillGraphRouting.Intersects(path[i - 1], path[i], block)),
                        "линия проходит под посторонней иконкой " + skill.Id + ": " + a + "→" + b);
            }
        }
        foreach (var cls in session.Data.Classes.Values)
        {
            Check(_main.Character!.SelectClass(cls.Id).Ok && _main.Character.SelectLevel(20).Ok, "не выбрать класс " + cls.Id);
            _main.Skills!.PadClosed();
            await Settle();
            foreach (var id in cls.SkillIds.Where(id => session.Data.Skills[id].Tree.Count > 0))
            {
                var panel = _main.Skills!;
                long before = session.Revision;
                panel.CatalogButtons[id].EmitSignal(Button.SignalName.Pressed);
                await Settle();
                var graph = panel.SkillGraph ?? throw new Failure("нет графа " + id);
                var skill = session.Data.Skills[id];
                Check(graph.Nodes.Count == skill.Tree.Count + 1, "потеряны узлы " + id);
                Check(graph.Nodes.All(n => n.Icon != null), "нет изображения " + id);
                Check(session.Revision == before, "просмотр изменил билд " + id);
                foreach (var node in skill.Tree)
                    foreach (var req in node.Requires)
                        Check(panel.SkillGraphEdges.Contains((req.NodeId, node.Id)), "пропущено требование " + id + "/" + node.Id);
                if (_screenshotDir != null) await Shot("skill-tree-" + id, full: true);
                panel.PadBack();
                await Settle();
                Check(panel.SkillGraph == null, "граф остался в каталоге " + id);
            }
        }
        var mistItem = session.Data.Items.Values.First(i => i.GrantsSkill == "poison_cloud");
        Check(session.ToggleItem(mistItem.Id).Ok, "не экипировать источник Ядовитого тумана");
        await Settle();
        var mistPanel = _main.Skills!;
        var mistRevision = session.Revision;
        mistPanel.CatalogButtons["poison_cloud"].EmitSignal(Button.SignalName.Pressed);
        await Settle();
        Check(mistPanel.SkillGraph?.Nodes.Count == session.Data.Skills["poison_cloud"].Tree.Count + 1,
            "не построено дерево предметного навыка");
        Check(session.Revision == mistRevision, "просмотр предметного навыка изменил билд");
        if (_screenshotDir != null) await Shot("skill-tree-poison_cloud", full: true);
        mistPanel.PadBack();
        await Settle();
    }

    private async Task SkillsRadialGraph()
    {
        await FreshBuild(20, WindowIds.Skills);
        var panel = _main.Skills!;
        var session = _main.Session!;
        var definition = session.Data.Skills["cleave"];
        var revision = session.Revision;
        panel.CatalogButtons["cleave"].EmitSignal(Button.SignalName.Pressed);
        await Settle();
        var view = panel.SkillGraph ?? throw new Failure("вместо графа показан список");
        Check(view.Nodes.Count == definition.Tree.Count + 1, "не все 17 узлов и центр показаны");
        Check(view.Nodes.Select(n => n.Position).Distinct().Count() == view.Nodes.Count, "узлы наложены друг на друга");
        foreach (var node in definition.Tree)
        {
            Check(view.Nodes.Single(n => n.Id == node.Id).Icon != null, "нет иконки " + node.Id);
            foreach (var requirement in node.Requires)
                Check(panel.SkillGraphEdges.Any(e => (e.A == requirement.NodeId && e.B == node.Id)
                    || (e.B == requirement.NodeId && e.A == node.Id)), "нет связи требования " + node.Id);
        }
        Check(session.Revision == revision && session.Build.Slots.Count == 0, "предпросмотр поменял билд");
        var position = view.NodeLocalPosition("split")!.Value;
        view.ClickAt(position);
        Check(session.Revision == revision, "выбор узла изменил очки");
        Check(panel.SelectedNodeText.Contains("Точный удар") && panel.SelectedNodeText.Contains("Широкий замах"),
            "панель не объясняет альтернативу и требование Раскола");
        view.PadSelect("searing_blade");
        view.PadInput(PadCommand.Accept);
        Check(session.Revision == revision, "A в предпросмотре вложила очко");
        if (_screenshotDir != null) await Shot("skills-radial-preview", full: true);

        Check(panel.SelectSkill(0, "cleave").Ok, "не удалось установить cleave");
        await Settle();
        Check(!panel.AddPoint(0, "split").Ok, "граф позволил обойти требования Раскола");
        await Settle();
        view = panel.SkillGraph!;
        view.ClickAt(view.NodeLocalPosition("heavy_hand")!.Value);
        var add = panel.FindChild("SkillNodeAdd", true, false) as Button;
        Check(add != null && !add.Disabled, "нет доступной кнопки вложения для выбранного узла");
        var beforeZoom = view.Zoom;
        view.ZoomAt(view.Size / 2, 1.2f);
        var zoom = view.Zoom;
        add!.EmitSignal(Button.SignalName.Pressed);
        await Settle();
        view = panel.SkillGraph ?? throw new Failure("граф исчез после вложения");
        Check(Math.Abs(view.Zoom - zoom) < 0.01 && zoom > beforeZoom, "вложение сбросило масштаб графа");
        Check(view.StateOf("heavy_hand") == TreeNodeState.Taken, "граф не обновил вложенный узел");
        Check(view.Nodes.Single(n => n.Id == "heavy_hand").Badge?.Contains('1') == true, "ранг не обновился");
        var afterPoint = session.Revision;
        view.ClickAt(view.NodeLocalPosition("precise_strike")!.Value);
        Check(session.Revision == afterPoint, "выбор заблокированного узла изменил билд");
        Check(panel.RemovePoint(0, "heavy_hand").Ok, "ранг не снялся");
        await Settle();
        if (_screenshotDir != null) await Shot("skills-radial-editor", full: true);
    }
}
