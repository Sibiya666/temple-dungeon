using Dialoid.Core.Skills;
using Dialoid.Game.Ui.Art;
using Godot;

namespace Dialoid.Game.Ui;

public partial class SkillsPanel
{
    private const string GraphRoot = "__skill_center";
    private string? _graphSkillId;
    private string? _graphRestoreFocus;
    private bool _graphHadFocus;
    private readonly Dictionary<string, string> _graphSelections = new();
    public TreeView? SkillGraph { get; private set; }
    public IReadOnlyList<(string A, string B)> SkillGraphEdges { get; private set; } = Array.Empty<(string, string)>();
    public string? SelectedNodeId { get; private set; }
    public Label? SelectedNodeLabel { get; private set; }
    public string SelectedNodeText => SelectedNodeLabel?.Text ?? "";

    private void DrawSkillGraph(SkillDefinition skill, int? editableSlot)
    {
        if (editableSlot >= Session.SlotsOpen || skill.UnlockLevel > Session.Build.Level) editableSlot = null;
        float? zoom = null;
        Vector2 pan = default;
        if (_graphSkillId == skill.Id && GodotObject.IsInstanceValid(SkillGraph)
            && SkillGraph!.Size.X >= 200 && SkillGraph.Size.Y >= 200)
        {
            zoom = SkillGraph!.Zoom;
            pan = SkillGraph.Pan;
        }
        bool restoreGraphFocus = _graphHadFocus || (GodotObject.IsInstanceValid(SkillGraph) && SkillGraph!.HasFocus());
        _graphSkillId = skill.Id;
        var points = editableSlot is { } slot ? Session.GetSlot(slot)!.NodePoints : new Dictionary<string, int>();
        var positions = SkillPositions(skill);
        var nodes = new List<TreeViewNode>
        {
            new(GraphRoot, Vector2.Zero, 34, null, null, skill.Name + "\n" + skill.Description)
            { Icon = GameIcons.Skill(skill.Id) }
        };
        var edges = new List<(string A, string B)>();
        var states = new Dictionary<string, TreeNodeState> { [GraphRoot] = TreeNodeState.Taken };
        string Detail(SkillTreeNode node)
        {
            int have = points.GetValueOrDefault(node.Id);
            var text = $"{node.Name} — {have}/{node.MaxPoints}\n{node.Description}";
            text += have < node.MaxPoints ? $"\nСледующий ранг: {have + 1}/{node.MaxPoints} (1 очко)." : "\nМаксимальный ранг.";
            foreach (var requirement in node.Requires)
                text += $"\nТребует: {NodeName(skill, requirement.NodeId)}: {points.GetValueOrDefault(requirement.NodeId)}, нужно {requirement.Points}.";
            if (node.Requires.Count == 0) text += "\nНачальная ветвь: без требований к другим узлам.";
            if (node.ExclusiveGroup != null)
                text += "\n× Взаимоисключение: " + string.Join(", ", skill.Tree.Where(n => n.Id != node.Id && n.ExclusiveGroup == node.ExclusiveGroup)
                    .Select(n => $"{n.Name} ({points.GetValueOrDefault(n.Id)}/{n.MaxPoints})"));
            if (skill.Id == "cleave" && node.Id == "precise_strike")
                text += "\nПосле выбора бонус площади не работает для этого удара.";
            if (editableSlot is { } index && Session.WhyCannotAddSkillNodePoint(index, node.Id) is { } reason)
                text += "\nСейчас нельзя добавить: " + reason;
            if (editableSlot == null) text += "\nПросмотр: вложение очков недоступно.";
            return text;
        }
        foreach (var node in skill.Tree)
        {
            int have = points.GetValueOrDefault(node.Id);
            var reason = editableSlot is { } index ? Session.WhyCannotAddSkillNodePoint(index, node.Id) : "Просмотр";
            // Preserve the list panel's contract: only empty blocked nodes enter this dictionary.
            if (editableSlot != null && have == 0 && reason != null) _blocked[node.Id] = reason;
            states[node.Id] = have > 0 ? TreeNodeState.Taken : reason == null ? TreeNodeState.Available : TreeNodeState.Unavailable;
            nodes.Add(new TreeViewNode(node.Id, positions[node.Id], node.ExclusiveGroup != null ? 34 : 30, null,
                $"{have}/{node.MaxPoints}", Detail(node)) { Icon = GameIcons.SkillNode(skill.Id, node.Id, node.Effects) });
            if (node.Requires.Count == 0) edges.Add((GraphRoot, node.Id));
            else foreach (var requirement in node.Requires) edges.Add((requirement.NodeId, node.Id));
        }
        SkillGraphEdges = edges;
        var toolbar = new HBoxContainer();
        var fit = new Button { Text = "Вписать дерево" };
        toolbar.AddChild(fit);
        Content!.AddChild(toolbar);
        AddText("Колесо / LT/RT — масштаб · перетаскивание — сдвиг", Grey);
        var graph = new SkillTreeGraphView
        {
            CustomMinimumSize = new Vector2(0, 310), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            DescriptionInPanel = true
        };
        SkillGraph = graph;
        Content.AddChild(graph);
        graph.FocusEntered += () => _graphHadFocus = true;
        graph.FocusExited += () => Callable.From(() =>
        {
            // Removing the old content for a rank rebuild must not discard its focus target.
            if (graph == SkillGraph && GodotObject.IsInstanceValid(graph) && graph.IsInsideTree() && !graph.HasFocus())
                _graphHadFocus = false;
        }).CallDeferred();
        // TreeView._Ready sets None; opt into the window's normal gamepad focus routing afterwards.
        graph.FocusMode = FocusModeEnum.All;
        fit.Pressed += graph.FitToView;
        graph.SetGraph(nodes, edges, true);
        graph.Links = edges;
        graph.SetStates(states);
        SelectedNodeLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Content.AddChild(SelectedNodeLabel);
        var actions = new HBoxContainer();
        var plus = new Button { Name = "SkillNodeAdd", Text = "+ Вложить" };
        var minus = new Button { Name = "SkillNodeRemove", Text = "− Снять" };
        plus.FocusEntered += () => _graphHadFocus = false;
        minus.FocusEntered += () => _graphHadFocus = false;
        actions.AddChild(plus);
        actions.AddChild(minus);
        toolbar.AddChild(actions);
        void Select(string id)
        {
            SelectedNodeId = id;
            graph.SelectedId = id;
            graph.QueueRedraw();
            _graphSelections[skill.Id] = id;
            var node = skill.Tree.FirstOrDefault(n => n.Id == id);
            SelectedNodeLabel.Text = node == null ? skill.Name + "\n" + skill.Description : Detail(node);
            plus.Disabled = node == null || editableSlot == null || Session.WhyCannotAddSkillNodePoint(editableSlot.Value, id) != null;
            minus.Disabled = node == null || editableSlot == null || points.GetValueOrDefault(id) == 0;
        }
        graph.NodeClicked += Select;
        graph.HoveredChanged += id =>
        {
            if (Dialoid.Game.Input.InputDevice.Gamepad && graph.HasFocus() && id != null) Select(id);
        };
        plus.Pressed += () => { _graphRestoreFocus = "SkillNodeAdd"; if (editableSlot is { } index && SelectedNodeId is { } id) AddPoint(index, id); };
        minus.Pressed += () => { _graphRestoreFocus = "SkillNodeRemove"; if (editableSlot is { } index && SelectedNodeId is { } id) RemovePoint(index, id); };
        var selected = _graphSelections.GetValueOrDefault(skill.Id);
        if (!skill.Tree.Any(n => n.Id == selected)) selected = skill.Tree.FirstOrDefault()?.Id ?? GraphRoot;
        Select(selected!);
        graph.PadSelect(selected!);
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(graph) || graph != SkillGraph) return;
            if (restoreGraphFocus) graph.GrabFocus();
            else if (_graphRestoreFocus is { } target)
            {
                _graphRestoreFocus = null;
                var action = target == "SkillNodeAdd" ? plus : minus;
                if (!action.Disabled) action.GrabFocus();
                else graph.GrabFocus();
            }
            if (zoom is { } z)
            {
                graph.ZoomAt(Vector2.Zero, z / graph.Zoom);
                graph.PanBy(pan - graph.Pan);
            }
            else graph.RequestFit();
        }).CallDeferred();
    }

    private static Dictionary<string, Vector2> SkillPositions(SkillDefinition skill)
    {
        // One shared entrance for form and elements; each point is unique and each prerequisite is drawn from data.
        var cleave = new Dictionary<string, Vector2>
        {
            ["heavy_hand"] = new(35, -72), ["wide_swing"] = new(-35, -145),
            ["split"] = new(-110, -215), ["precise_strike"] = new(5, -225), ["edge"] = new(80, -275),
            ["searing_blade"] = new(120, -75), ["burning_wounds"] = new(205, -100),
            ["thunder_strike"] = new(205, -10), ["crushing_force"] = new(120, 25),
            ["lacerate"] = new(35, 90), ["deep_cut"] = new(65, 165), ["bloodletting"] = new(65, 245),
            ["thirst"] = new(-85, 25), ["frenzy"] = new(-170, -30), ["echo"] = new(-170, 75),
            ["rolling_echo"] = new(-230, 145), ["steel_skin"] = new(-50, -65)
        };
        if (skill.Id == "cleave" && skill.Tree.All(n => cleave.ContainsKey(n.Id))) return cleave;
        var result = new Dictionary<string, Vector2>();
        var depths = new Dictionary<string, int>();
        int Depth(string id, HashSet<string> path)
        {
            if (depths.TryGetValue(id, out var known)) return known;
            if (!path.Add(id)) return 1;
            var node = skill.Tree.FirstOrDefault(n => n.Id == id);
            int depth = node == null || node.Requires.Count == 0 ? 1 : 1 + node.Requires.Max(r => Depth(r.NodeId, new HashSet<string>(path)));
            return depths[id] = depth;
        }
        float previousRadius = 0;
        foreach (var ring in skill.Tree.GroupBy(n => Depth(n.Id, new HashSet<string>())).OrderBy(g => g.Key))
        {
            var ordered = ring.OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
            float radius = Math.Max(previousRadius + 100, ordered.Count * 80 / Mathf.Tau);
            previousRadius = radius;
            for (int i = 0; i < ordered.Count; i++)
            {
                float angle = -Mathf.Pi / 2 + Mathf.Tau * i / ordered.Count;
                result[ordered[i].Id] = Vector2.FromAngle(angle) * radius;
            }
        }
        return result;
    }
}

/// <summary>Keep the shared tree navigation, but describe selection rather than passive allocation.</summary>
public partial class SkillTreeGraphView : TreeView, IPadControl
{
    public IReadOnlyList<(string A, string B)> Links { get; set; } = Array.Empty<(string, string)>();
    public string? SelectedId { get; set; }

    // Skill artwork is square. Draw it edge-to-edge inside a thin, unscaled frame;
    // the shared passive tree's solid circles are intentionally not used here.
    public override void _Draw()
    {
        var background = new Color("10171b");
        // The window artwork stays visible through the graph; only individual nodes are opaque.
        foreach (var (a, b) in Links)
        {
            if (NodeLocalPosition(a) is not { } from || NodeLocalPosition(b) is not { } to) continue;
            bool taken = StateOf(a) == TreeNodeState.Taken && StateOf(b) == TreeNodeState.Taken;
            DrawLine(from, to, new Color(taken ? "789395" : "39474d"), taken ? 1.5f : 1f, true);
        }
        var font = GetThemeDefaultFont();
        foreach (var node in Nodes)
        {
            var center = WorldToLocal(node.Position);
            float radius = Mathf.Max(node.Radius * Zoom, 5f);
            var rect = new Rect2(center - Vector2.One * radius, Vector2.One * radius * 2);
            bool taken = StateOf(node.Id) == TreeNodeState.Taken;
            bool available = StateOf(node.Id) == TreeNodeState.Available;
            var border = new Color(taken ? "92a9aa" : available ? "627d82" : "39474d");
            DrawRect(rect, background);
            if (node.Icon != null)
                DrawTextureRect(node.Icon, rect.Grow(-1), false,
                    taken || available ? Colors.White : new Color(0.68f, 0.71f, 0.72f));
            DrawRect(rect, border, false, 1f);
            if (node.Id == SelectedId || node.Id == HoveredId)
                DrawRect(rect.Grow(2), new Color("b9c9cb"), false, 1f);
            if (node.Badge != null && radius >= 10)
            {
                int size = Mathf.Clamp((int)(radius * 0.65f), 10, 14);
                var textSize = font.GetStringSize(node.Badge, HorizontalAlignment.Left, -1, size);
                var label = new Rect2(rect.End - new Vector2(textSize.X + 4, font.GetHeight(size)),
                    new Vector2(textSize.X + 4, font.GetHeight(size)));
                DrawRect(label, new Color(0.035f, 0.055f, 0.065f, 0.94f));
                DrawString(font, label.Position + new Vector2(2, font.GetAscent(size)), node.Badge,
                    HorizontalAlignment.Left, -1, size, new Color("d0dbdc"));
            }
        }
        if (HasFocus()) DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), new Color("536b72"), false, 1f);
    }

    public override void _Ready()
    {
        base._Ready();
        FocusMode = FocusModeEnum.All;
    }

    IEnumerable<(string Button, string Label)> IPadControl.PadPrompts()
    {
        yield return ("[A]", "Выбрать узел");
        yield return ("[LT/RT]", "Масштаб");
        yield return ("[R3]", "Вписать");
    }
}
