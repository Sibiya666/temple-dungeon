using Dialoid.Core.Character;
using Dialoid.Core.Passives;
using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;
using Attribute = Dialoid.Core.Character.Attribute;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Пассивки» tab (spec §13.2): the shared passive tree in a <see cref="TreeView"/> by the data coordinates, the
/// points counter, the refused command's status, the hovered node's description and the passive violations.
/// A click toggles a node (<see cref="SandboxSession.TogglePassive"/>), a right click on an attribute node opens the
/// attribute choice (<see cref="SandboxSession.SetAttributeChoice"/>). Colours come from the session: taken = the build's
/// nodes plus the class start, available = <see cref="SandboxSession.AllocatablePassives"/>, the rest unavailable.
/// The panel is built once; a session change only repaints the view (zoom and pan stay) and refreshes the labels.
/// </summary>
public partial class PassivePanel : SessionPanel
{
    /// <summary>Attributes of the right-click menu, in item order.</summary>
    public static readonly IReadOnlyList<Attribute> AttributeOrder = [Attribute.Str, Attribute.Int, Attribute.Will, Attribute.Dex];

    private Label _points = null!;
    private Label _status = null!;
    private Label _hover = null!;
    private TextureRect _hoverIcon = null!;
    private Label _violations = null!;
    private string? _commandError;
    private string? _menuNodeId;

    /// <summary>The tree view (the self-test clicks it by node coordinates).</summary>
    public TreeView View { get; private set; } = null!;

    /// <summary>The attribute choice menu of attribute nodes.</summary>
    public PopupMenu AttributeMenu { get; private set; } = null!;

    /// <summary>Node id the attribute menu was opened for, or null.</summary>
    public string? AttributeMenuNodeId => _menuNodeId;

    /// <summary>Text of the points counter.</summary>
    public string PointsText => _points.Text;

    /// <summary>Text of the status line (a refused command), empty when none.</summary>
    public string StatusText => _status.Text;

    /// <summary>Text of the hovered node's line.</summary>
    public string HoverText => _hover.Text;

    public void Setup(SandboxSession session)
    {
        InitSession(session, withContent: false);

        var top = new HFlowContainer(); // wraps in a window a third of the screen wide (plan 4 task 10)
        _points = new Label { CustomMinimumSize = new Vector2(220, 0) };
        top.AddChild(_points);
        var fit = new Button { Text = "Вписать дерево" };
        GameIcons.Decorate(fit, "expand");
        fit.Pressed += () => View.FitToView();
        top.AddChild(fit);
        top.AddChild(Legend());
        AddChild(top);

        AddChild(ButtonPrompts.Help(new Label
        {
            Text = "ЛКМ — взять/снять узел, ПКМ по узлу атрибута — выбрать атрибут, колесо — масштаб, перетаскивание — сдвиг.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        }, "A — взять или снять узел, X — атрибут, LT/RT — масштаб, правый стик — сдвиг, R3 — вписать дерево."));
        _status = WrappedLabel(Red);
        AddChild(_status);
        _hover = WrappedLabel(null);
        AddChild(GameIcons.Description(_hover, out _hoverIcon));

        View = new TreeView { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, DescriptionInPanel = true };
        View.CustomMinimumSize = new Vector2(0, 300);
        View.NodeClicked += id => NodeClicked(id);
        View.NodeRightClicked += (id, at) => NodeRightClicked(id, at);
        View.AltLabel = id => id != null && session.Data.Passives.Nodes.TryGetValue(id, out var n) && n.Kind == PassiveKind.Attribute ? "Атрибут" : null;
        View.HoveredChanged += _ => UpdateHover();
        AddChild(View);

        _violations = WrappedLabel(Red);
        AddChild(_violations);

        AttributeMenu = new PopupMenu();
        foreach (var a in AttributeOrder) AttributeMenu.AddIconItem(GameIcons.Symbol(a.StatId())!, a.RussianName());
        AttributeMenu.IndexPressed += i => ChooseFromMenu((int)i);
        AddChild(AttributeMenu);

        View.SetGraph(BuildNodes(), Edges(session.Data.Passives), refit: true);
    }

    // ---- handlers (the view's events and the self-test call these) ------------------------------------------------------

    /// <summary>Takes or drops a node.</summary>
    public CommandResult NodeClicked(string nodeId) => Run(Session.TogglePassive(nodeId));

    /// <summary>Chooses the attribute of a taken attribute node.</summary>
    public CommandResult ChooseAttribute(string nodeId, Attribute attribute) => Run(Session.SetAttributeChoice(nodeId, attribute));

    /// <summary>Right click: opens the attribute menu at the click for an attribute node. Returns whether it opened.</summary>
    public bool NodeRightClicked(string nodeId, Vector2 local)
    {
        if (!Session.Data.Passives.Nodes.TryGetValue(nodeId, out var node) || node.Kind != PassiveKind.Attribute) return false;
        _menuNodeId = nodeId;
        var current = Session.Build.AttributeChoices.TryGetValue(nodeId, out var a) ? a : (Attribute?)null;
        for (int i = 0; i < AttributeOrder.Count; i++)
        {
            AttributeMenu.SetItemAsRadioCheckable(i, true);
            AttributeMenu.SetItemChecked(i, AttributeOrder[i] == current);
        }
        var screen = (Vector2I)(View.GetScreenTransform() * local);
        AttributeMenu.Popup(new Rect2I(screen, Vector2I.Zero));
        // The gamepad lands on the current choice (A confirms, B cancels).
        var at = current is { } chosen ? AttributeOrder.ToList().IndexOf(chosen) : 0;
        AttributeMenu.SetFocusedItem(Math.Max(0, at));
        return true;
    }

    /// <summary>A pressed item of the attribute menu (by index in <see cref="AttributeOrder"/>).</summary>
    public CommandResult? ChooseFromMenu(int index)
    {
        if (_menuNodeId == null || index < 0 || index >= AttributeOrder.Count) return null;
        var nodeId = _menuNodeId;
        _menuNodeId = null;
        return ChooseAttribute(nodeId, AttributeOrder[index]);
    }

    /// <summary>A refusal is shown; an accepted command (even a no-op that raises no Changed) clears a stale refusal.</summary>
    private CommandResult Run(CommandResult result)
    {
        var error = result.Ok ? null : result.Error;
        if (error != _commandError)
        {
            _commandError = error;
            RequestRedraw();
        }
        return result;
    }

    protected override void OnSessionChanged()
    {
        _commandError = null;
        RequestRedraw();
    }

    // ---- drawing --------------------------------------------------------------------------------------------------------

    protected override void DrawContent()
    {
        int spent = Session.PassivePointsSpent, total = Session.PassivePointsTotal;
        _points.Text = $"Очки: {spent}/{total}";
        _points.RemoveThemeColorOverride("font_color");
        if (spent > total) _points.AddThemeColorOverride("font_color", Red);

        _status.Text = _commandError == null ? "" : "Команда отклонена: " + _commandError;

        var violations = Session.Violations.Where(v => IsPassiveViolation(v.Code)).Select(v => "• " + v.Message).ToList();
        _violations.Text = violations.Count == 0 ? "" : "Нарушения:\n" + string.Join("\n", violations);

        // Badges and tooltips depend on the attribute choices, so the nodes are rebuilt; the view keeps zoom and pan.
        View.SetGraph(BuildNodes(), Edges(Session.Data.Passives), refit: false);
        View.SetStates(States());
        UpdateHover();
    }

    /// <summary>Taken: the build's nodes and the class start; available: the session's allocatable set; else unavailable.</summary>
    private Dictionary<string, TreeNodeState> States()
    {
        var build = Session.Build;
        var taken = new HashSet<string>(build.PassiveNodes);
        if (Session.Data.Classes.TryGetValue(build.ClassId, out var cls)) taken.Add(cls.PassiveStartNodeId);
        var available = Session.AllocatablePassives;
        return Session.Data.Passives.Nodes.Keys.ToDictionary(id => id,
            id => taken.Contains(id) ? TreeNodeState.Taken
                : available.Contains(id) ? TreeNodeState.Available
                : TreeNodeState.Unavailable);
    }

    private List<TreeViewNode> BuildNodes()
    {
        var choices = Session.Build.AttributeChoices;
        return Session.Data.Passives.Nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n =>
        {
            Attribute? choice = n.Kind == PassiveKind.Attribute && choices.TryGetValue(n.Id, out var a) ? a : null;
            return new TreeViewNode(n.Id, new Vector2((float)n.X, (float)n.Y), RadiusOf(n.Kind),
                n.Kind is PassiveKind.Notable or PassiveKind.Keystone or PassiveKind.Start ? n.Name : null,
                choice is { } c ? c.RussianName()[..1] : null,
                TooltipOf(n, choice))
            {
                Icon = n.Kind == PassiveKind.Attribute ? GameIcons.Symbol(choice?.StatId() ?? "attribute")
                    : n.Kind == PassiveKind.Start ? GameIcons.Symbol("tree") : GameIcons.Perk(n.Id, n.Effects),
            };
        }).ToList();
    }

    /// <summary>Each undirected edge once.</summary>
    private static IEnumerable<(string, string)> Edges(PassiveTree tree)
    {
        var seen = new HashSet<(string, string)>();
        foreach (var (id, links) in tree.Edges)
            foreach (var other in links)
            {
                var pair = string.CompareOrdinal(id, other) < 0 ? (id, other) : (other, id);
                if (seen.Add(pair)) yield return pair;
            }
    }

    private static float RadiusOf(PassiveKind kind) => kind switch
    {
        PassiveKind.Start or PassiveKind.Keystone => 24f,
        PassiveKind.Notable => 18f,
        PassiveKind.Attribute => 12f,
        _ => 10f,
    };

    private static string TooltipOf(PassiveNode n, Attribute? choice)
    {
        var text = $"{n.Name} ({KindTitle(n.Kind)})\n{n.Description}";
        if (n.Kind == PassiveKind.Attribute)
            text += choice is { } c ? $"\nАтрибут: {c.RussianName()}" : "\nАтрибут не выбран (ПКМ — выбрать)";
        return text;
    }

    private void UpdateHover()
    {
        var id = View.HoveredId;
        _hoverIcon.Texture = View.Nodes.FirstOrDefault(n => n.Id == id)?.Icon ?? GameIcons.Symbol("tree");
        _hover.Text = id != null && Session.Data.Passives.Nodes.TryGetValue(id, out var n)
            ? TooltipOf(n, Session.Build.AttributeChoices.TryGetValue(id, out var a) ? a : null).ReplaceLineEndings(" — ")
            : "Наведите на узел, чтобы увидеть описание.";
    }

    private static bool IsPassiveViolation(string code) =>
        code.Contains("passive", StringComparison.Ordinal) || code.Contains("attribute", StringComparison.Ordinal)
        || code == "start_node_in_build";

    private static string KindTitle(PassiveKind kind) => kind switch
    {
        PassiveKind.Start => "старт",
        PassiveKind.Small => "малый",
        PassiveKind.Attribute => "атрибут",
        PassiveKind.Notable => "notable",
        PassiveKind.Keystone => "keystone",
        _ => kind.ToString(),
    };

    private static Label WrappedLabel(Color? color)
    {
        var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        return label;
    }

    /// <summary>The three colour swatches with their meaning.</summary>
    internal static HBoxContainer Legend()
    {
        var box = new HBoxContainer();
        foreach (var (color, text) in new[]
                 {
                     (TreeView.TakenColor, "взят"), (TreeView.AvailableColor, "доступен"), (TreeView.UnavailableColor, "недоступен"),
                 })
        {
            box.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            box.AddChild(new Label { Text = text + "   " });
        }
        return box;
    }
}
