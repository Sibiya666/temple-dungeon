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

    private void DrawSkillGraph(SkillDefinition skill, int? editableSlot, VBoxContainer graphPane, VBoxContainer detailPane)
    {
        if (editableSlot >= Session.SlotsOpen || skill.UnlockLevel > Session.Build.Level) editableSlot = null;
        float? zoom = null;
        Vector2 pan = default;
        Vector2 previousSize = default;
        if (_graphSkillId == skill.Id && GodotObject.IsInstanceValid(SkillGraph)
            && SkillGraph!.Size.X >= 200 && SkillGraph.Size.Y >= 200)
        {
            zoom = SkillGraph!.Zoom;
            pan = SkillGraph.Pan;
            previousSize = SkillGraph.Size;
        }
        bool restoreGraphFocus = _graphSkillId == skill.Id &&
            (_graphHadFocus || (GodotObject.IsInstanceValid(SkillGraph) && SkillGraph!.HasFocus()));
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
        var toolbar = new HFlowContainer();
        toolbar.AddChild(RoomHudTheme.TowerHeading("ДЕРЕВО НАВЫКА", 15));
        var fit = new Button { Text = "Вписать дерево" };
        toolbar.AddChild(fit);
        graphPane.AddChild(toolbar);
        graphPane.AddChild(new HSeparator());
        var graphHint = SkillText(graphPane, "Колесо / LT/RT — масштаб · перетаскивание — сдвиг", Grey);
        graphHint.AddThemeFontSizeOverride("font_size", RoomHudTheme.CaptionSize);
        var graph = new SkillTreeGraphView
        {
            Name = "SkillsTreeGraph", CustomMinimumSize = new Vector2(0, SkillGraphHeight()),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkBegin,
            DescriptionInPanel = true
        };
        SkillGraph = graph;
        graphPane.AddChild(graph);
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
        graph.Requirements = skill.Tree.SelectMany(n => n.Requires.Select(r => (r.NodeId, n.Id, r.Points))).ToArray();
        graph.Alternatives = skill.Tree.Where(n => n.ExclusiveGroup != null).GroupBy(n => n.ExclusiveGroup)
            .SelectMany(group => group.SelectMany((a, i) => group.Skip(i + 1).Select(b => (a.Id, b.Id)))).ToArray();
        graph.RouteLinks();
        graph.SetStates(states);
        SelectedNodeLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        detailPane.AddChild(new HSeparator());
        detailPane.AddChild(RoomHudTheme.TowerHeading("ВЫБРАННЫЙ УЗЕЛ", 15));
        detailPane.AddChild(GameIcons.Description(SelectedNodeLabel, out var selectedIcon));
        var actions = new HBoxContainer();
        var plus = new Button { Name = "SkillNodeAdd", Text = "+ Вложить" };
        var minus = new Button { Name = "SkillNodeRemove", Text = "− Снять" };
        plus.FocusEntered += () => _graphHadFocus = false;
        minus.FocusEntered += () => _graphHadFocus = false;
        actions.AddChild(plus);
        actions.AddChild(minus);
        detailPane.AddChild(actions);
        if (skill.Tree.Any(node => !GameIcons.HasDedicatedSkillNodeArt(skill.Id, node.Id)))
            SkillText(detailPane, "Часть узлов пока использует общие знаки эффектов: комплект смысловых иконок не завершён.", Grey);
        void Select(string id)
        {
            SelectedNodeId = id;
            graph.SelectedId = id;
            graph.QueueRedraw();
            _graphSelections[skill.Id] = id;
            var node = skill.Tree.FirstOrDefault(n => n.Id == id);
            selectedIcon.Texture = node == null ? GameIcons.Skill(skill.Id) : GameIcons.SkillNode(skill.Id, node.Id, node.Effects);
            SelectedNodeLabel.Text = node == null ? skill.Name + "\n" + skill.Description : Detail(node);
            plus.Disabled = node == null || editableSlot == null || Session.WhyCannotAddSkillNodePoint(editableSlot.Value, id) != null;
            minus.Disabled = node == null || editableSlot == null || points.GetValueOrDefault(id) == 0;
            ConfigureSkillFocus();
        }
        graph.NodeClicked += Select;
        // Navigation moves the focus corners. A commits selection, just as a mouse click does;
        // merely passing another node must not replace the action the player was navigating toward.
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
            if (zoom is { } z && graph.Size.DistanceTo(previousSize) < 2)
            {
                graph.ZoomAt(Vector2.Zero, z / graph.Zoom);
                graph.PanBy(pan - graph.Pan);
            }
            else graph.RequestFit();
        }).CallDeferred();
    }

    private static Dictionary<string, Vector2> SkillPositions(SkillDefinition skill)
    {
        if (SkillTreeLayouts.For(skill) is { } authored) return authored;
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
    public IReadOnlyList<(string A, string B, int Points)> Requirements { get; set; } = Array.Empty<(string, string, int)>();
    public IReadOnlyList<(string A, string B)> Alternatives { get; set; } = Array.Empty<(string, string)>();
    public IReadOnlyDictionary<(string A, string B), IReadOnlyList<Vector2>> Routes { get; private set; }
        = new Dictionary<(string, string), IReadOnlyList<Vector2>>();
    public string? SelectedId { get; set; }

    public void RouteLinks()
    {
        Routes = Links.Concat(Alternatives).Distinct().ToDictionary(edge => edge, edge =>
        {
            var from = Nodes.Single(n => n.Id == edge.A).Position;
            var to = Nodes.Single(n => n.Id == edge.B).Position;
            var obstacles = Nodes.Where(n => n.Id != edge.A && n.Id != edge.B)
                .Select(n => new Rect2(n.Position - Vector2.One * (n.Radius + 4), Vector2.One * (n.Radius + 4) * 2)).ToArray();
            return SkillGraphRouting.Route(from, to, obstacles);
        });
    }

    private Vector2 LinkMiddle(string a, string b)
    {
        var path = Routes[(a, b)];
        float length = 0;
        for (int i = 1; i < path.Count; i++) length += path[i - 1].DistanceTo(path[i]);
        float remaining = length / 2;
        for (int i = 1; i < path.Count; i++)
        {
            float segment = path[i - 1].DistanceTo(path[i]);
            if (remaining <= segment) return WorldToLocal(path[i - 1].Lerp(path[i], segment > 0 ? remaining / segment : 0));
            remaining -= segment;
        }
        return WorldToLocal(path[^1]);
    }

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
            var route = Routes[(a, b)];
            for (int i = 1; i < route.Count; i++)
                DrawLine(WorldToLocal(route[i - 1]), WorldToLocal(route[i]),
                    new Color(taken ? "789395" : "39474d"), taken ? 1.5f : 1f, true);
        }
        var font = GetThemeDefaultFont();
        foreach (var (a, b) in Alternatives)
        {
            if (NodeLocalPosition(a) is not { } from || NodeLocalPosition(b) is not { } to) continue;
            var color = new Color(a == SelectedId || b == SelectedId ? "9da9ad" : "56646a");
            var route = Routes[(a, b)];
            for (int i = 1; i < route.Count; i++)
            {
                var start = WorldToLocal(route[i - 1]);
                var delta = WorldToLocal(route[i]) - start;
                float length = delta.Length();
                if (length < 1) continue;
                var direction = delta / length;
                for (float distance = 0; distance < length; distance += 9)
                    DrawLine(start + direction * distance, start + direction * Mathf.Min(distance + 4, length), color, 1f, true);
            }
            var midpoint = LinkMiddle(a, b);
            DrawRect(new Rect2(midpoint - new Vector2(6, 8), new Vector2(12, 16)), background);
            DrawString(font, midpoint + new Vector2(-4, 4), "×", HorizontalAlignment.Left, -1, 12, color);
        }
        foreach (var (a, b, points) in Requirements.Where(r => r.B == SelectedId))
        {
            if (NodeLocalPosition(a) is not { } from || NodeLocalPosition(b) is not { } to) continue;
            var midpoint = LinkMiddle(a, b);
            DrawRect(new Rect2(midpoint - new Vector2(7, 8), new Vector2(14, 16)), background);
            DrawString(font, midpoint + new Vector2(-4, 4), points.ToString(), HorizontalAlignment.Left, -1, 12, new Color("d0dbdc"));
        }
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
            if (node.Id == SelectedId)
                DrawRect(rect.Grow(2), new Color("b9c9cb"), false, 1f);
            if (node.Id == HoveredId && !Dialoid.Game.Input.InputDevice.Gamepad)
                DrawRect(rect.Grow(1), new Color("82979b"), false, 1f);
            var focusId = Dialoid.Game.Input.InputDevice.Gamepad ? HoveredId : SelectedId;
            if (node.Id == focusId && HasFocus())
            {
                // Corner focus remains distinct from the closed selection line, even when they coincide.
                var focus = rect.Grow(4);
                var color = RoomHudTheme.Focus;
                float arm = Mathf.Min(8, focus.Size.X / 3);
                DrawLine(focus.Position, focus.Position + new Vector2(arm, 0), color, 2f);
                DrawLine(focus.Position, focus.Position + new Vector2(0, arm), color, 2f);
                DrawLine(focus.End, focus.End - new Vector2(arm, 0), color, 2f);
                DrawLine(focus.End, focus.End - new Vector2(0, arm), color, 2f);
            }
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
        // The pad footer and restored side window change the actual workspace after the initial fit.
        // Resize must fit the new rectangle; focus alone continues to preserve a manual view.
        Resized += FitToView;
    }

    public override void _Notification(int what)
    {
        // Shared TreeView's passive-tree autofocus zoom is unsuitable for this bounded skill overview.
        // Keep its navigation/focus handling, but preserve the user's exact view when entering this control.
        var zoom = Zoom;
        var pan = Pan;
        base._Notification(what);
        if (what == NotificationFocusEnter && Dialoid.Game.Input.InputDevice.Gamepad)
        {
            ZoomAt(Vector2.Zero, zoom / Zoom);
            PanBy(pan - Pan);
        }
    }

    IEnumerable<(string Button, string Label)> IPadControl.PadPrompts()
    {
        yield return ("[A]", "Выбрать узел");
        yield return ("[LT/RT]", "Масштаб");
        yield return ("[R3]", "Вписать");
    }
}
