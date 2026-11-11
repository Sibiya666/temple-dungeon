using Dialoid.Core.Character;
using Dialoid.Core.Passives;
using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Асцендант» tab (spec §13.3): the ascendancy of the current class (chosen here or on «Персонаж», from level 20) in a
/// <see cref="TreeView"/>. The data has no coordinates, so the start node sits in the centre and the notables lie on a
/// circle in data order, each linked to the start. The start comes free with the ascendancy (taken while one is chosen);
/// a click on a notable is <see cref="SandboxSession.ToggleAscendancyNode"/>, overspending shows as a violation.
/// The panel is built once; a session change repaints the view and refreshes the labels (the view refits only when the
/// ascendancy changes).
/// </summary>
public partial class AscendancyPanel : SessionPanel
{
    /// <summary>Radius of the notable circle, in view units.</summary>
    public const float LayoutRadius = 220f;

    private OptionButton _selector = null!;
    private Label _lockHint = null!;
    private Label _points = null!;
    private Label _status = null!;
    private Label _hover = null!;
    private TextureRect _hoverIcon = null!;
    private Label _nodeTitle = null!;
    private Label _nodeContext = null!;
    private Label _iconDebt = null!;
    private Button _nodeAction = null!;
    private Label _subtitle = null!;
    private Label _violations = null!;
    private string? _commandError;
    private string? _info;
    private string? _shownAscendancyId;
    private bool _graphShown;

    /// <summary>The tree view (the self-test clicks it by node coordinates).</summary>
    public TreeView View { get; private set; } = null!;

    /// <summary>Ascendancy id of each selector item (null: none), for the current class.</summary>
    public IReadOnlyList<string?> AscendancyChoices { get; private set; } = Array.Empty<string?>();

    public string PointsText => _points.Text;

    /// <summary>Text of the status line (a refused command or a hint), empty when none.</summary>
    public string StatusText => _status.Text;

    public string HoverText => _hover.Text;

    public void Setup(SandboxSession session)
    {
        InitSession(session, withContent: false);
        Theme = RoomHudTheme.TowerWindowTheme();
        _subtitle = RoomHudTheme.TowerHeading("Путь персонажа / Асцендант", 22);
        AddChild(_subtitle);

        var top = new HFlowContainer(); // wraps in a narrow window (plan 4 task 10)
        _selector = new OptionButton();
        _selector.ItemSelected += i => SelectAscendancy(AscendancyChoices[(int)i]);
        top.AddChild(_selector);
        _lockHint = new Label();
        top.AddChild(_lockHint);
        _points = new Label();
        top.AddChild(_points);
        var fit = new Button { Text = "Вписать" };
        GameIcons.Decorate(fit, "expand");
        fit.Pressed += () => View.FitToView();
        AddChild(top);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_status);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        var columns = new TowerColumns { LeftRatio = .62f, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.AddChild(columns);
        var graph = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddChild(graph);
        var graphToolbar = new HBoxContainer();
        graphToolbar.AddChild(RoomHudTheme.TowerHeading("ДЕРЕВО АСЦЕНДАНТА", 15));
        graphToolbar.AddChild(fit);
        graph.AddChild(graphToolbar);
        graph.AddChild(new HSeparator());
        View = new TreeView { Name = "AscendancyTree", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            DescriptionInPanel = true, ReferenceStyle = true };
        View.CustomMinimumSize = new Vector2(0, 320);
        View.NodeClicked += id => NodeClicked(id);
        View.HoveredChanged += _ => UpdateHover();
        graph.AddChild(View);
        var detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(detail);
        detail.AddChild(RoomHudTheme.TowerHeading("ВЫБРАННЫЙ УЗЕЛ", 15));
        detail.AddChild(new HSeparator());
        var identity = new HBoxContainer();
        detail.AddChild(identity);
        _hoverIcon = new TextureRect { Name = "SelectedNodeIcon", CustomMinimumSize = new Vector2(72, 72),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore };
        identity.AddChild(_hoverIcon);
        _nodeTitle = RoomHudTheme.TowerHeading("Выберите узел", 24);
        identity.AddChild(_nodeTitle);
        _nodeContext = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _nodeContext.AddThemeColorOverride("font_color", RoomHudTheme.MutedText);
        detail.AddChild(_nodeContext);
        _hover = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detail.AddChild(_hover);
        _nodeAction = new Button { Text = "Взять узел", Disabled = true, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _nodeAction.Pressed += () => { if (View.SelectedNodeId is { } id) NodeClicked(id); };
        detail.AddChild(_nodeAction);
        _iconDebt = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _iconDebt.AddThemeColorOverride("font_color", RoomHudTheme.MutedText);
        detail.AddChild(_iconDebt);
        AddChild(ButtonPrompts.Help(new Label
        {
            Text = "ЛКМ по узлу — взять / снять · колесо — масштаб · перетаскивание — сдвиг",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        }, "A — взять / снять · LT/RT — масштаб · правый стик — сдвиг · R3 — вписать дерево"));

        _violations = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _violations.AddThemeColorOverride("font_color", Red);
        AddChild(_violations);
        Resized += ApplyReferenceLayout;
        ApplyReferenceLayout();
    }

    private void ApplyReferenceLayout()
    {
        var narrow = Size.X < 760;
        _subtitle.Visible = !narrow;
        View.CustomMinimumSize = new Vector2(0, narrow ? 220 : 320);
    }

    // ---- handlers -------------------------------------------------------------------------------------------------------

    public CommandResult SelectAscendancy(string? ascendancyId) => Run(Session.SetAscendancy(ascendancyId));

    /// <summary>
    /// A click on a node: a notable is toggled by the session; the start node only explains that it comes with the
    /// ascendancy (returns null then).
    /// </summary>
    public CommandResult? NodeClicked(string nodeId)
    {
        if (Current() is { } asc && asc.Start.Id == nodeId)
        {
            _commandError = null;
            _info = $"«{asc.Start.Name}» — стартовый узел: он взят, пока выбран асцендант «{asc.Name}».";
            RequestRedraw();
            return null;
        }
        return Run(Session.ToggleAscendancyNode(nodeId));
    }

    private CommandResult Run(CommandResult result)
    {
        _info = null;
        _commandError = result.Ok ? null : result.Error;
        RequestRedraw();
        return result;
    }

    protected override void OnSessionChanged()
    {
        _commandError = null;
        _info = null;
        RequestRedraw();
    }

    // ---- drawing --------------------------------------------------------------------------------------------------------

    protected override void DrawContent()
    {
        var build = Session.Build;
        var data = Session.Data;

        // Selector: the current class's ascendancies; locked below level 20 unless one is already chosen (it can be removed).
        var choices = new List<string?> { null };
        choices.AddRange(data.Ascendancies.Values.Where(a => a.ClassId == build.ClassId)
            .OrderBy(a => a.Id, StringComparer.Ordinal).Select(a => (string?)a.Id));
        AscendancyChoices = choices;
        _selector.Clear();
        foreach (var id in choices) _selector.AddItem(id == null ? "(нет)" : data.Ascendancies[id].Name);
        _selector.AddThemeConstantOverride("icon_max_width", 24);
        for (int i = 1; i < choices.Count; i++)
        {
            var start = data.Ascendancies[choices[i]!].Start;
            _selector.SetItemIcon(i, AscendancyIcons.For(start));
            _selector.GetPopup().SetItemIconMaxWidth(i, 24);
        }
        _selector.Selected = Math.Max(0, choices.IndexOf(build.AscendancyId));
        bool locked = build.Level < BuildRules.AscendancyUnlockLevel;
        _selector.Disabled = locked && build.AscendancyId == null;
        _lockHint.Text = locked ? $"открывается с {BuildRules.AscendancyUnlockLevel} уровня (сейчас {build.Level})" : "";

        int spent = Session.AscendancyPointsSpent, total = Session.AscendancyPointsTotal;
        _points.Text = $"Очки асценданта: {spent}/{total}";
        _points.RemoveThemeColorOverride("font_color");
        if (spent > total) _points.AddThemeColorOverride("font_color", Red);

        _status.RemoveThemeColorOverride("font_color");
        if (_commandError != null)
        {
            _status.Text = "Команда отклонена: " + _commandError;
            _status.AddThemeColorOverride("font_color", Red);
        }
        else _status.Text = _info ?? "";
        _status.Visible = _status.Text.Length > 0;

        var violations = Session.Violations.Where(v => v.Code.Contains("ascendancy", StringComparison.Ordinal))
            .Select(v => "• " + v.Message).ToList();
        _violations.Text = violations.Count == 0 ? "" : "Нарушения:\n" + string.Join("\n", violations);
        _violations.Visible = _violations.Text.Length > 0;

        var asc = Current();
        if (asc?.Id != _shownAscendancyId || !_graphShown)
        {
            View.SetGraph(asc == null ? Array.Empty<TreeViewNode>() : Layout(asc), asc == null ? [] : Links(asc), refit: true);
            _shownAscendancyId = asc?.Id;
            _graphShown = true;
        }
        View.SetStates(States(asc));
        UpdateHover();
    }

    /// <summary>The chosen ascendancy, or null.</summary>
    private Ascendancy? Current() =>
        Session.Build.AscendancyId is { } id && Session.Data.Ascendancies.TryGetValue(id, out var asc) ? asc : null;

    /// <summary>Start in the centre, notables on a circle in data order starting at the top.</summary>
    public static List<TreeViewNode> Layout(Ascendancy asc)
    {
        var nodes = new List<TreeViewNode> { new(asc.Start.Id, Vector2.Zero, 58f, asc.Start.Name, null, TooltipOf(asc.Start, true))
            { Icon = AscendancyIcons.For(asc.Start) } };
        int count = asc.Notables.Count;
        for (int i = 0; i < count; i++)
        {
            var n = asc.Notables[i];
            float angle = -Mathf.Pi / 2 + Mathf.Tau * i / count;
            nodes.Add(new TreeViewNode(n.Id, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * LayoutRadius, 56f, n.Name,
                n.Cost.ToString(), TooltipOf(n, false)) { Icon = AscendancyIcons.For(n) });
        }
        return nodes;
    }

    private static List<(string, string)> Links(Ascendancy asc) => asc.Notables.Select(n => (asc.Start.Id, n.Id)).ToList();

    /// <summary>Start: taken while chosen; notables: taken when in the build, else available (the session toggles any).</summary>
    private Dictionary<string, TreeNodeState> States(Ascendancy? asc)
    {
        var states = new Dictionary<string, TreeNodeState>();
        if (asc == null) return states;
        states[asc.Start.Id] = TreeNodeState.Taken;
        var taken = Session.Build.AscendancyNodes;
        foreach (var n in asc.Notables) states[n.Id] = taken.Contains(n.Id) ? TreeNodeState.Taken : TreeNodeState.Available;
        return states;
    }

    private static string TooltipOf(AscendancyNode n, bool start) =>
        start ? $"{n.Name} (старт, бесплатно)\n{n.Description}" : $"{n.Name} (notable, стоимость {n.Cost})\n{n.Description}";

    private void UpdateHover()
    {
        var selected = View.SelectedNodeId;
        _hoverIcon.Texture = View.Nodes.FirstOrDefault(n => n.Id == selected)?.Icon;
        var asc = Current();
        _iconDebt.Text = asc != null && asc.Id != "berserker" ? "Иконки этого восхождения — прежние служебные символы. Индивидуальный комплект ещё не подготовлен." : "";
        if (asc == null)
        {
            _nodeTitle.Text = "Асцендант не выбран";
            _nodeContext.Text = "";
            _nodeAction.Disabled = true;
            _hover.Text = "Асцендант не выбран" + (Session.Build.Level < BuildRules.AscendancyUnlockLevel
                ? $": он открывается с {BuildRules.AscendancyUnlockLevel} уровня."
                : ": выберите его выше или на вкладке «Персонаж».");
            return;
        }
        var id = selected;
        var node = id == asc.Start.Id ? asc.Start : asc.Notables.FirstOrDefault(n => n.Id == id);
        _nodeTitle.Text = node?.Name ?? "Выберите узел";
        var start = node == asc.Start;
        var taken = node != null && (start || Session.Build.AscendancyNodes.Contains(node.Id));
        _nodeContext.Text = node == null ? asc.Name : $"{asc.Name} · {(start ? "старт · бесплатно" : $"стоимость {node.Cost}")} · {(taken ? "взят" : "доступен")}";
        _nodeAction.Disabled = node == null || start;
        _nodeAction.Text = start ? "Стартовый узел взят" : taken ? "Снять узел" : "Взять узел";
        _hover.Text = node == null
            ? "Наведите на узел, чтобы увидеть описание."
            : TooltipOf(node, node == asc.Start).ReplaceLineEndings(" — ");
    }
}
