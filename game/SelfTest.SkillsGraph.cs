using Dialoid.Game.Ui;
using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
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
