using Dialoid.Core.Skills;
using Dialoid.Game.Ui.Art;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>Browse skill definitions independently of assigning them to a combat slot.</summary>
public partial class SkillsPanel : IPadPanel
{
    private readonly Dictionary<string, Button> _catalogButtons = new();
    private readonly List<string> _previewNodeIds = new();
    private string? _browsedSkillId;
    private string? _returnToSkill;
    private bool _focusDetail;
    private int _catalogScroll;
    private string? _assignmentSkillId;
    private int _assignmentSlot = -1;
    public OptionButton? AssignmentSlotOptions { get; private set; }
    public Button? AssignSkillButton { get; private set; }
    public Button? UndoSkillButton { get; private set; }

    public IReadOnlyDictionary<string, Button> CatalogButtons => _catalogButtons;
    public IReadOnlyList<string> PreviewNodeIds => _previewNodeIds;
    public string? BrowsedSkillId => _browsedSkillId;

    private IReadOnlyList<string> CatalogIds()
    {
        var ids = new List<string>();
        if (Session.Data.Classes.TryGetValue(Session.Build.ClassId, out var cls)) ids.AddRange(cls.SkillIds);
        foreach (var itemId in Session.Build.ItemIds)
            if (Session.Data.Items.TryGetValue(itemId, out var item) && item.GrantsSkill is { } granted)
                ids.Add(granted);
        ids.AddRange(Session.Build.Slots.Select(s => s.SkillId));
        return ids.Where(Session.Data.Skills.ContainsKey).Distinct().ToList();
    }

    private void DrawCatalog()
    {
        Content!.AddChild(RoomHudTheme.TowerHeading("Путь персонажа / Навыки", 24));
        SkillText(Content, "КАТАЛОГ НАВЫКОВ", Grey);
        var instruction = AddText("Выберите навык, чтобы изучить дерево.", Grey);
        instruction.AddThemeFontSizeOverride("font_size", RoomHudTheme.CaptionSize);
        var grid = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", RoomHudTheme.GroupGap);
        grid.AddThemeConstantOverride("v_separation", RoomHudTheme.GroupGap);
        Content!.AddChild(grid);
        foreach (var id in CatalogIds())
        {
            var skill = Session.Data.Skills[id];
            var locked = skill.UnlockLevel > Session.Build.Level;
            var tile = new VBoxContainer { CustomMinimumSize = new Vector2(148, 0) };
            tile.AddThemeConstantOverride("separation", 10);
            grid.AddChild(tile);
            var button = new Button
            {
                Name = "Skill_" + id,
                CustomMinimumSize = new Vector2(112, 112),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                TooltipText = $"{skill.Name}\nОткрыть дерево скилла" + (locked ? $"\nОткроется на {skill.UnlockLevel} уровне" : ""),
                FocusMode = FocusModeEnum.All,
            };
            var portrait = SkillPortrait.Create(GameIcons.Skill(id));
            if (locked) portrait.Modulate = new Color(0.7f, 0.73f, 0.74f);
            button.AddChild(portrait);
            button.SetMeta(ButtonPrompts.AcceptMeta, "Открыть дерево");
            button.Pressed += () =>
            {
                if (Content.GetParent() is ScrollContainer scroll)
                {
                    _catalogScroll = scroll.ScrollVertical;
                    scroll.ScrollVertical = 0;
                }
                _focusDetail = button.HasFocus();
                _graphHadFocus = false;
                _graphRestoreFocus = null;
                _browsedSkillId = id;
                RequestRedraw();
            };
            tile.AddChild(button);
            tile.AddChild(new Label
            {
                Text = skill.Name,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(148, 44),
            });
            int assigned = Enumerable.Range(0, Dialoid.Sandbox.SandboxSession.SlotCount)
                .FirstOrDefault(i => Session.GetSlot(i)?.SkillId == id, -1);
            var status = new Label { Text = locked ? $"С {skill.UnlockLevel} уровня" : assigned >= 0 ? $"Слот {assigned + 1}" : "Не назначен",
                Modulate = Grey, HorizontalAlignment = HorizontalAlignment.Center };
            status.AddThemeFontSizeOverride("font_size", RoomHudTheme.CaptionSize);
            tile.AddChild(status);
            _catalogButtons.Add(id, button);
            if (_returnToSkill == id)
            {
                RestoreCatalogFocus(button);
                _returnToSkill = null;
            }
        }
        Content.AddChild(new HSeparator());
    }

    private async void RestoreCatalogFocus(Button button)
    {
        button.CallDeferred(Control.MethodName.GrabFocus);
        // Flow wrapping and the scroll range settle after the replacement children are sorted.
        var tree = GetTree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(button) || !button.IsInsideTree() || !button.HasFocus()) return;
        if (Content!.GetParent() is ScrollContainer scroll) scroll.EnsureControlVisible(button);
    }

    private bool DrawBrowsedSkill()
    {
        if (_browsedSkillId == null) return false;
        if (!CatalogIds().Contains(_browsedSkillId))
        {
            _browsedSkillId = null;
            return false;
        }
        var back = new Button { Name = "BackToSkills", Text = "← Все навыки", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        back.Pressed += () => PadBack();
        Content!.AddChild(back);
        if (_focusDetail)
        {
            back.CallDeferred(Control.MethodName.GrabFocus);
            _focusDetail = false;
        }
        _treeSlot = Enumerable.Range(0, Dialoid.Sandbox.SandboxSession.SlotCount)
            .FirstOrDefault(i => Session.GetSlot(i)?.SkillId == _browsedSkillId, -1);
        if (_commandError != null) AddText("Команда отклонена: " + _commandError, Red);
        var skill = Session.Data.Skills[_browsedSkillId];
        if (_treeSlot >= 0 && skill.UnlockLevel <= Session.Build.Level) DrawTree();
        else DrawDefinition(skill);
        return true;
    }

    private void DrawAssignmentControls(VBoxContainer target)
    {
        if (_assignmentSkillId != _browsedSkillId)
        {
            _assignmentSkillId = _browsedSkillId;
            _assignmentSlot = _treeSlot >= 0 ? _treeSlot
                : Enumerable.Range(0, Session.SlotsOpen).FirstOrDefault(i => Session.GetSlot(i) == null, -1);
            if (_assignmentSlot < 0 && Session.SlotsOpen > 0) _assignmentSlot = 0;
        }
        var row = new HFlowContainer();
        target.AddChild(RoomHudTheme.TowerHeading("НАЗНАЧЕНИЕ", 15));
        target.AddChild(row);
        var options = new OptionButton();
        AssignmentSlotOptions = options;
        for (int i = 0; i < Dialoid.Sandbox.SandboxSession.SlotCount; i++)
        {
            bool closed = i >= Session.SlotsOpen;
            options.AddItem($"Слот {i + 1}" + (closed ? $" (с {OpenLevel(i)} уровня)" : ""));
            options.SetItemDisabled(i, closed);
        }
        options.Select(_assignmentSlot);
        options.ItemSelected += index => _assignmentSlot = (int)index;
        row.AddChild(options);
        var definition = Session.Data.Skills[_browsedSkillId!];
        bool locked = definition.UnlockLevel > Session.Build.Level;
        var assign = new Button { Text = "Назначить", Disabled = Session.SlotsOpen == 0 || locked,
            TooltipText = locked ? $"Назначение откроется на {definition.UnlockLevel} уровне персонажа" : "Назначить навык в выбранный боевой слот" };
        AssignSkillButton = assign;
        assign.Pressed += () => SelectSkill(options.Selected, _browsedSkillId);
        row.AddChild(assign);
        if (_treeSlot < 0) return;
        int slot = _treeSlot;
        var current = Session.GetSlot(slot)!;
        int cap = Session.SkillLevelCap(slot);
        var level = new SpinBox
        {
            MinValue = 1, MaxValue = Math.Max(cap, current.Level), Step = 1,
            Value = current.Level, TooltipText = $"Уровень скилла, кап {cap}",
        };
        level.ValueChanged += value => SelectLevel(slot, (int)value);
        ButtonPrompts.EditMode(level);
        row.AddChild(new Label { Text = "уровень" });
        row.AddChild(level);
        var undo = new Button
        {
            Text = "Вернуть", Disabled = !Session.CanUndoSlot(slot),
            TooltipText = "Вернуть, что стояло в слоте до последней смены",
        };
        UndoSkillButton = undo;
        undo.Pressed += () => Undo(slot);
        row.AddChild(undo);
    }
    private void DrawDefinition(SkillDefinition skill)
    {
        var (graphPane, detail) = DrawSkillWorkspace(skill);
        DrawAssignmentControls(detail);
        if (skill.UnlockLevel > Session.Build.Level)
            SkillText(detail, $"Откроется на {skill.UnlockLevel} уровне. Дерево можно изучить заранее.", Grey);
        SkillText(detail, skill.UnlockLevel > Session.Build.Level
            ? "Просмотр дерева · назначение и вложение очков пока недоступны."
            : "Просмотр · для вложения очков назначьте навык в слот.", Grey);
        foreach (var node in skill.Tree) _previewNodeIds.Add(node.Id);
        DrawSkillGraph(skill, null, graphPane, detail);
    }

    private (VBoxContainer Graph, VBoxContainer Detail) DrawSkillWorkspace(SkillDefinition skill)
    {
        var columns = new TowerColumns
        {
            Name = "SkillsDetailColumns", LeftRatio = .64f, Breakpoint = 780,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        Content!.AddChild(columns);
        var graph = new VBoxContainer { Name = "SkillsTreePane", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var detail = new VBoxContainer { Name = "SkillsDetailPane", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(graph);
        columns.AddChild(detail);
        var identity = new HBoxContainer();
        var portrait = new TextureRect
        {
            Name = "BrowsedSkillPortrait", Texture = GameIcons.Skill(skill.Id),
            CustomMinimumSize = new Vector2(72, 72), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        identity.AddChild(portrait);
        identity.AddChild(RoomHudTheme.TowerHeading(skill.Name, 24));
        detail.AddChild(identity);
        SkillText(detail, _treeSlot >= 0 ? $"Назначен · слот {_treeSlot + 1}" : "Просмотр навыка", Grey);
        SkillText(detail, skill.Description);
        detail.AddChild(new HSeparator());
        return (graph, detail);
    }

    private static Label SkillText(VBoxContainer target, string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (color is { } tint) label.AddThemeColorOverride("font_color", tint);
        target.AddChild(label);
        return label;
    }

    private void ApplySkillLayout()
    {
        if (GodotObject.IsInstanceValid(SkillGraph))
            SkillGraph!.CustomMinimumSize = new Vector2(0, SkillGraphHeight());
    }

    // The graph is a bounded workspace, never a virtual page taller than its visible scroll viewport.
    private float SkillGraphHeight() => Mathf.Clamp(GetChild<ScrollContainer>(0).Size.Y - 140, 220, 620);

    private void ConfigureSkillFocus()
    {
        if (_browsedSkillId == null || Content == null || !Content.IsInsideTree()) return;
        var targets = ButtonPrompts.Targets(Content);
        if (targets.Count < 2) return;
        // Spatial navigation sees the tall graph beside several short rows. Give vertical traversal
        // an explicit reading order so the graph's overlapping rectangle cannot swallow those rows.
        for (int i = 0; i < targets.Count; i++)
        {
            targets[i].FocusNeighborTop = i > 0 ? targets[i].GetPathTo(targets[i - 1]) : new NodePath("");
            targets[i].FocusNeighborBottom = i + 1 < targets.Count ? targets[i].GetPathTo(targets[i + 1]) : new NodePath("");
        }
        if (SkillGraph == null || AssignmentSlotOptions == null || AssignSkillButton == null) return;
        var entry = !AssignmentSlotOptions.Disabled ? (Control)AssignmentSlotOptions : targets.FirstOrDefault(t =>
            t.FindParent("SkillsDetailPane") != null);
        if (entry == null) return;
        SkillGraph.FocusNeighborRight = SkillGraph.GetPathTo(entry);
        entry.FocusNeighborLeft = entry.GetPathTo(SkillGraph);
    }

    public bool PadButton(PadCommand command, Control? focus) => false;
    public bool PadBack()
    {
        if (_browsedSkillId == null) return false;
        _assignmentSkillId = null;
        _graphHadFocus = false;
        _graphRestoreFocus = null;
        _returnToSkill = _browsedSkillId;
        _browsedSkillId = null;
        if (Content!.GetParent() is ScrollContainer scroll)
            scroll.ScrollVertical = _catalogScroll;
        // Restore the old viewport before rebuilding; a deferred scroll assignment after GrabFocus
        // would undo FollowFocus and could leave the focused portrait above the visible area.
        RequestRedraw();
        return true;
    }
    public IEnumerable<(string Button, string Label)> PadPrompts(Control? focus)
    {
        if (_browsedSkillId != null) yield return ("[B]", "Назад к скиллам");
    }
    public void PadClosed()
    {
        var hadDetail = _browsedSkillId != null;
        _browsedSkillId = null;
        _returnToSkill = null;
        _focusDetail = false;
        _graphHadFocus = false;
        _graphRestoreFocus = null;
        if (hadDetail) RequestRedraw();
    }
}
