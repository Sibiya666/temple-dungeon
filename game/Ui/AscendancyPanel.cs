using Dialoid.Core.Character;
using Dialoid.Core.Passives;
using Dialoid.Sandbox;
using Godot;

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

        var top = new HBoxContainer();
        top.AddChild(new Label { Text = "Асцендант:" });
        _selector = new OptionButton();
        _selector.ItemSelected += i => SelectAscendancy(AscendancyChoices[(int)i]);
        top.AddChild(_selector);
        _lockHint = new Label();
        top.AddChild(_lockHint);
        _points = new Label { CustomMinimumSize = new Vector2(260, 0) };
        top.AddChild(_points);
        var fit = new Button { Text = "Вписать" };
        fit.Pressed += () => View.FitToView();
        top.AddChild(fit);
        top.AddChild(PassivePanel.Legend());
        AddChild(top);

        AddChild(new Label
        {
            Text = "Стартовый узел дается вместе с асцендантом. ЛКМ по notable — взять/снять, колесо — масштаб, перетаскивание — сдвиг.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_status);
        _hover = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_hover);

        View = new TreeView { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        View.CustomMinimumSize = new Vector2(0, 300);
        View.NodeClicked += id => NodeClicked(id);
        View.HoveredChanged += _ => UpdateHover();
        AddChild(View);

        _violations = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _violations.AddThemeColorOverride("font_color", Red);
        AddChild(_violations);
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
            _info = $"«{asc.Start.Name}» — стартовый узел: он взят, пока выбран асцендант «{asc.Name}».";
            RequestRedraw();
            return null;
        }
        return Run(Session.ToggleAscendancyNode(nodeId));
    }

    private CommandResult Run(CommandResult result)
    {
        _info = null;
        if (!result.Ok) _commandError = result.Error;
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

        var violations = Session.Violations.Where(v => v.Code.Contains("ascendancy", StringComparison.Ordinal))
            .Select(v => "• " + v.Message).ToList();
        _violations.Text = violations.Count == 0 ? "" : "Нарушения:\n" + string.Join("\n", violations);

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
        var nodes = new List<TreeViewNode> { new(asc.Start.Id, Vector2.Zero, 30f, asc.Start.Name, null, TooltipOf(asc.Start, true)) };
        int count = asc.Notables.Count;
        for (int i = 0; i < count; i++)
        {
            var n = asc.Notables[i];
            float angle = -Mathf.Pi / 2 + Mathf.Tau * i / count;
            nodes.Add(new TreeViewNode(n.Id, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * LayoutRadius, 22f, n.Name,
                n.Cost.ToString(), TooltipOf(n, false)));
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
        var asc = Current();
        if (asc == null)
        {
            _hover.Text = "Асцендант не выбран" + (Session.Build.Level < BuildRules.AscendancyUnlockLevel
                ? $": он открывается с {BuildRules.AscendancyUnlockLevel} уровня."
                : ": выберите его выше или на вкладке «Персонаж».");
            return;
        }
        var id = View.HoveredId;
        var node = id == asc.Start.Id ? asc.Start : asc.Notables.FirstOrDefault(n => n.Id == id);
        _hover.Text = node == null
            ? "Наведите на узел, чтобы увидеть описание."
            : TooltipOf(node, node == asc.Start).ReplaceLineEndings(" — ");
    }
}
