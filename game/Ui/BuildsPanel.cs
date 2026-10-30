using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Билды» tab (spec §13.7): the reference builds grouped as in data/builds with a «Загрузить» button, and a text
/// field with «Экспорт» (fills it with the build code) and «Импорт» (errors under the field). Loading and import are
/// <see cref="ReferenceBuilds.Load"/> / <see cref="SandboxSession.Import"/>; the other panels redraw on Changed.
/// The panel is built once; it listens to Changed only to clear its status lines, which a later change makes stale
/// (its own load/import sets the status right after the synchronous Changed).
/// </summary>
public partial class BuildsPanel : SessionPanel
{
    private static readonly IReadOnlyDictionary<string, string> GroupTitles = new Dictionary<string, string>
    {
        [ReferenceBuilds.MainGroup] = "Основные (60 уровень)",
        ["l20"] = "Версии 20 уровня",
        ["parity"] = "Паритет классов",
        ["keystones"] = "Ключевые узлы",
        ["attributes"] = "Без атрибутных узлов",
    };

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
        InitSession(session, withContent: false);
        References = dataDirectory == null ? Array.Empty<ReferenceBuildInfo>() : ReferenceBuilds.List(dataDirectory);

        AddChild(new Label { Text = "Эталонные билды (data/builds). Двойной щелчок или «Загрузить»:", AutowrapMode = TextServer.AutowrapMode.WordSmart });
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
        // Without a keyboard (the gamepad, spec §4): the code goes through the clipboard; the field itself is for typing.
        ButtonPrompts.Skip(_code);
        var clipboard = new HFlowContainer();
        var copy = new Button { Name = "CopyCode", Text = "Копировать код", TooltipText = "Код текущего билда — в буфер обмена (и в поле выше)" };
        copy.Pressed += () => CopyCode();
        var paste = new Button { Name = "PasteCode", Text = "Вставить из буфера", TooltipText = "Импортировать билд из кода в буфере обмена" };
        paste.Pressed += () => PasteCode();
        clipboard.AddChild(copy);
        clipboard.AddChild(paste);
        AddChild(clipboard);
        _importStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_importStatus);
    }

    // ---- handlers (the buttons and the self-test call these) --------------------------------------------------------

    /// <summary>Raised after an accepted load or import (Main clears the breakdown history).</summary>
    public event Action? BuildReplaced;

    /// <summary>Loads a reference build into the session and shows the result.</summary>
    public CommandResult Load(ReferenceBuildInfo info)
    {
        var result = ReferenceBuilds.Load(Session, info);
        SetStatus(_loadStatus, result.Ok ? $"Загружен: {info.Title}" : result.Error ?? "", result.Ok);
        if (result.Ok) BuildReplaced?.Invoke();
        return result;
    }

    /// <summary>Puts the current build's code into the field.</summary>
    public void ExportCode()
    {
        _code.Text = Session.ExportCode();
        SetStatus(_importStatus, "Код билда в поле выше.", true);
    }

    /// <summary>«Копировать код»: the build's code into the clipboard and the field.</summary>
    public string CopyCode()
    {
        var code = Session.ExportCode();
        _code.Text = code;
        DisplayServer.ClipboardSet(code);
        SetStatus(_importStatus, "Код билда скопирован в буфер обмена.", true);
        return code;
    }

    /// <summary>«Вставить из буфера»: imports the clipboard's code (the field shows it).</summary>
    public CommandResult PasteCode()
    {
        var text = DisplayServer.ClipboardGet();
        _code.Text = text;
        return ImportText(text);
    }

    /// <summary>Replaces the build with a code or JSON; a refusal shows its reason.</summary>
    public CommandResult ImportText(string text)
    {
        var result = Session.Import(text);
        SetStatus(_importStatus, result.Ok ? "Билд импортирован." : "Импорт отклонен: " + result.Error, result.Ok);
        if (result.Ok) BuildReplaced?.Invoke();
        return result;
    }

    /// <summary>Clears both status lines synchronously (labels only, nothing is freed); no redraw needed.</summary>
    protected override void OnSessionChanged()
    {
        _loadStatus.Text = "";
        _importStatus.Text = "";
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
