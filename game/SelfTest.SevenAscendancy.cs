using Dialoid.Game.Ui;
using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
    private async Task SevenAscendancyIcons()
    {
        var session = _main.Session!;
        var asc = session.Data.Ascendancies["berserker"];
        var expected = new[] { "berserk_frenzy", "insatiable", "blood_feast", "reckless", "whirl_rage", "flesh_rend", "battle_trance" };
        var nodes = AscendancyPanel.Layout(asc);
        Check(nodes.Select(n => n.Id).SequenceEqual(expected), "набор узлов Берсерка изменён");
        foreach (var node in nodes)
        {
            var path = $"res://Ui/Art/Icons/ascendancy/berserker/{node.Id}.png";
            Check(node.Icon?.ResourcePath == path, $"нет собственной иконки {node.Id}: {node.Icon?.ResourcePath}");
        }
        Check(nodes.Select(n => n.Icon!.ResourcePath).Distinct().Count() == 7, "иконки узлов повторяются");
        Check(nodes[0].Position == Vector2.Zero && nodes.Skip(1).All(n => Mathf.Abs(n.Position.Length() - AscendancyPanel.LayoutRadius) < .01f),
            "дерево не соответствует центру и шести лучам");
        await FreshBuild(60, WindowIds.Ascendancy);
        var panel = _main.AscendancyView!;
        Check(panel.SelectAscendancy("berserker").Ok, "Берсерк не выбран");
        await Settle();
        Check(panel.View.ReferenceStyle && !_main.Passives!.View.ReferenceStyle, "стиль графа применён не только к Асценданту");
        Check(panel.View.Edges.Count == 6 && panel.View.Edges.All(e => e.A == "berserk_frenzy" && expected.Skip(1).Contains(e.B)), "добавлены посторонние зависимости узлов");
        panel.View.HoverAt(panel.View.NodeLocalPosition("blood_feast")!.Value);
        await Settle();
        var portrait = panel.FindChild("SelectedNodeIcon", true, false) as TextureRect;
        Check(portrait?.Texture?.ResourcePath == nodes.Single(n => n.Id == "blood_feast").Icon!.ResourcePath, "портрет detail не совпадает с узлом");
        Check(panel.HoverText.Contains("Кровавый пир"), "описание выбранного узла не показано");
        panel.View.HoverAt(new Vector2(-1000, -1000));
        Check(panel.View.SelectedNodeId == "blood_feast" && panel.HoverText.Contains("Кровавый пир"), "детали потеряли выбор при переходе от графа к действиям");
        _main.CloseAllWindows();
    }

    private static void CheckSevenAscendancyCaptions(TreeView view)
    {
        var labels = view.LayoutReferenceCaptions();
        Check(labels.Count == view.Nodes.Count, $"подписи дерева потеряны: {labels.Count}/{view.Nodes.Count}, размер {view.Size}; нет {string.Join(", ", view.Nodes.Select(n => n.Id).Except(labels.Select(l => l.Id)))}");
        for (var i = 0; i < labels.Count; i++)
        {
            Check(new Rect2(Vector2.Zero, view.Size).Encloses(labels[i].Rect), $"подпись {labels[i].Id} обрезана");
            for (var j = i + 1; j < labels.Count; j++)
                Check(!labels[i].Rect.Intersects(labels[j].Rect), $"подписи {labels[i].Id}/{labels[j].Id} пересекаются");
            foreach (var node in view.Nodes)
            {
                var socket = new Rect2(view.WorldToLocal(node.Position) - Vector2.One * node.Radius * view.Zoom,
                    Vector2.One * node.Radius * view.Zoom * 2);
                Check(!socket.Intersects(labels[i].Rect), $"подпись {labels[i].Id} закрывает узел {node.Id}");
            }
        }
    }

    private void CheckSevenAscendancySide()
    {
        var panel = _main.AscendancyView!;
        var view = panel.View;
        Node? ancestor = view.GetParent();
        while (ancestor != null && ancestor is not ScrollContainer) ancestor = ancestor.GetParent();
        var scroll = ancestor as ScrollContainer ?? throw new Failure("у графа нет области прокрутки");
        var visible = view.GetGlobalRect().Intersection(scroll.GetGlobalRect());
        foreach (var node in view.Nodes)
        {
            var center = view.GlobalPosition + view.WorldToLocal(node.Position);
            Check(visible.Grow(-2).HasPoint(center), $"узел {node.Id} не виден в боковом режиме: центр {center}, видимая область {visible}, view {view.Size}, pan {view.Pan}, zoom {view.Zoom}; scroll {scroll.Name}={scroll.GetGlobalRect()}, graph={view.GetGlobalRect()}, host={Host.Window(WindowIds.Ascendancy).GetRect()}, expanded={Host.Window(WindowIds.Ascendancy).Expanded}");
        }
        foreach (var label in view.LayoutReferenceCaptions())
            Check(visible.Encloses(new Rect2(label.Rect.Position + view.GlobalPosition, label.Rect.Size)), $"подпись {label.Id} видна без полного узла в боковом режиме");
    }
}
