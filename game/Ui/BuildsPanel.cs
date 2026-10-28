using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Билды» tab (spec §13.7): the reference builds grouped as in data/builds with a «Загрузить» button, and a text
/// field with «Экспорт» (fills it with the build code) and «Импорт» (errors under the field). Loading and import are
/// <see cref="ReferenceBuilds.Load"/> / <see cref="SandboxSession.Import"/>; the other panels redraw on Changed.
/// The panel itself shows nothing derived from the build, so it does not listen to Changed.
/// </summary>
public partial class BuildsPanel : VBoxContainer
{
    private static readonly Color Red = new(1f, 0.45f, 0.45f);
    private static readonly Color Green = new(0.55f, 0.9f, 0.55f);

    private static readonly IReadOnlyDictionary<string, string> GroupTitles = new Dictionary<string, string>
    {
        [ReferenceBuilds.MainGroup] = "Основные (60 уровень)",
        ["l20"] = "Версии 20 уровня",
        ["parity"] = "Паритет классов",
        ["keystones"] = "Keystones",
        ["attributes"] = "Без атрибутных узлов",
    };

    private SandboxSession _session = null!;
    private Tree _tree = null!;
    private Label _loadStatus = null!;
    private TextEdit _code = null!;
    private Label _importStatus = null!;

    /// <summary>The reference builds in the tree, in data/builds order.</summary>
    public IReadOnlyList<ReferenceBuildInfo> References { get; private set; } = Array.Empty<ReferenceBuildInfo>();

    /// <summary>Text of the load status line (success or error).</summary>
    public string LoadStatus => _loadStatus.Text;

    /// <summary>Text of the import status line (success or error).</summary>
    public string ImportStatus => _importStatus.Text;

    /// <summary>The code field.</summary>
    public string CodeText { get => _code.Text; set => _code.Text = value; }

    public void Setup(SandboxSession session, string? dataDirectory)
    {
        _session = session;
        References = dataDirectory == null ? Array.Empty<ReferenceBuildInfo>() : ReferenceBuilds.List(dataDirectory);

        AddChild(new Label { Text = "Эталонные билды (data/builds). Двойной щелчок или «Загрузить»:" });
        _tree = new Tree
        {
            HideRoot = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 300),
        };
        var root = _tree.CreateItem();
        var groups = new Dictionary<string, TreeItem>();
        for (int i = 0; i < References.Count; i++)
        {
            var info = References[i];
            if (!groups.TryGetValue(info.Group, out var group))
            {
                group = _tree.CreateItem(root);
                group.SetText(0, GroupTitles.GetValueOrDefault(info.Group, info.Group));
                group.SetSelectable(0, false);
                group.Collapsed = info.Group != ReferenceBuilds.MainGroup;
                groups[info.Group] = group;
            }
            var item = _tree.CreateItem(group);
            item.SetText(0, info.Title);
            item.SetTooltipText(0, info.Path);
            item.SetMetadata(0, i);
        }
        if (References.Count == 0) AddChild(new Label { Text = "Эталонные билды не найдены." });
        _tree.ItemActivated += LoadSelected;
        AddChild(_tree);

        var load = new Button { Text = "Загрузить", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        load.Pressed += LoadSelected;
        AddChild(load);
        _loadStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_loadStatus);

        AddChild(new HSeparator());
        AddChild(new Label { Text = "Код билда (DLD1:...) или JSON билда:" });
        _code = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, 120),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        AddChild(_code);
        var buttons = new HBoxContainer();
        var export = new Button { Text = "Экспорт" };
        export.Pressed += ExportCode;
        var import = new Button { Text = "Импорт" };
        import.Pressed += () => ImportText(_code.Text);
        buttons.AddChild(export);
        buttons.AddChild(import);
        AddChild(buttons);
        _importStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_importStatus);
    }

    // ---- handlers (the buttons and the self-test call these) --------------------------------------------------------

    /// <summary>Loads a reference build into the session and shows the result.</summary>
    public CommandResult Load(ReferenceBuildInfo info)
    {
        var result = ReferenceBuilds.Load(_session, info);
        SetStatus(_loadStatus, result.Ok ? $"Загружен: {info.Title}" : result.Error ?? "", result.Ok);
        return result;
    }

    /// <summary>Puts the current build's code into the field.</summary>
    public void ExportCode()
    {
        _code.Text = _session.ExportCode();
        SetStatus(_importStatus, "Код билда в поле выше.", true);
    }

    /// <summary>Replaces the build with a code or JSON; a refusal shows its reason.</summary>
    public CommandResult ImportText(string text)
    {
        var result = _session.Import(text);
        SetStatus(_importStatus, result.Ok ? "Билд импортирован." : "Импорт отклонен: " + result.Error, result.Ok);
        return result;
    }

    private void LoadSelected()
    {
        if (_tree.GetSelected() is not { } item || item.GetMetadata(0).VariantType != Variant.Type.Int)
        {
            SetStatus(_loadStatus, "Выберите билд в списке.", false);
            return;
        }
        Load(References[item.GetMetadata(0).AsInt32()]);
    }

    private static void SetStatus(Label label, string text, bool ok)
    {
        label.Text = text;
        label.AddThemeColorOverride("font_color", ok ? Green : Red);
    }
}
