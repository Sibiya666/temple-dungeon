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
        AddText("Скиллы персонажа");
        AddText("Выберите иконку, чтобы открыть дерево. Просмотр не меняет слоты.", Grey);
        var grid = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        Content!.AddChild(grid);
        foreach (var id in CatalogIds())
        {
            var skill = Session.Data.Skills[id];
            var locked = skill.UnlockLevel > Session.Build.Level;
            var tile = new VBoxContainer { CustomMinimumSize = new Vector2(140, 0) };
            grid.AddChild(tile);
            var button = new Button
            {
                Name = "Skill_" + id,
                Icon = GameIcons.Skill(id),
                ExpandIcon = true,
                IconAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(140, 64),
                TooltipText = $"{skill.Name}\nОткрыть дерево скилла" + (locked ? $"\nОткроется на {skill.UnlockLevel} уровне" : ""),
                FocusMode = FocusModeEnum.All,
            };
            button.AddThemeConstantOverride("icon_max_width", 48);
            button.SetMeta(ButtonPrompts.AcceptMeta, "Открыть дерево");
            button.Pressed += () =>
            {
                if (Content.GetParent() is ScrollContainer scroll)
                {
                    _catalogScroll = scroll.ScrollVertical;
                    scroll.ScrollVertical = 0;
                }
                _focusDetail = button.HasFocus();
                _browsedSkillId = id;
                RequestRedraw();
            };
            tile.AddChild(button);
            tile.AddChild(new Label
            {
                Text = skill.Name,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(140, 0),
            });
            if (locked) tile.AddChild(new Label { Text = $"С {skill.UnlockLevel} уровня", Modulate = Grey, HorizontalAlignment = HorizontalAlignment.Center });
            _catalogButtons.Add(id, button);
            if (_returnToSkill == id)
            {
                button.CallDeferred(Control.MethodName.GrabFocus);
                _returnToSkill = null;
            }
        }
        Content.AddChild(new HSeparator());
    }

    private bool DrawBrowsedSkill()
    {
        if (_browsedSkillId == null) return false;
        if (!CatalogIds().Contains(_browsedSkillId))
        {
            _browsedSkillId = null;
            return false;
        }
        var back = new Button { Text = "← Назад к скиллам" };
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

    private void DrawDefinition(SkillDefinition skill)
    {
        var heading = new HBoxContainer();
        if (GameIcons.Skill(_browsedSkillId!) is { } icon) heading.AddChild(GameIcons.View(icon, 40));
        heading.AddChild(new Label { Text = skill.Name });
        Content!.AddChild(heading);
        if (skill.UnlockLevel > Session.Build.Level)
            AddText($"Откроется на {skill.UnlockLevel} уровне. Дерево можно изучить заранее.", Grey);
        AddText("Просмотр дерева. Для вложения очков установите скилл в слот.");
        foreach (var node in skill.Tree)
        {
            _previewNodeIds.Add(node.Id);
            AddText($"{node.Name} — до {node.MaxPoints} очк.");
            AddText(node.Description, Grey);
            if (node.Requires.Count > 0)
                AddText("Требует: " + string.Join(", ", node.Requires.Select(r => $"«{NodeName(skill, r.NodeId)}» {r.Points} очк.")), Grey);
            Content.AddChild(new HSeparator());
        }
    }

    public bool PadButton(PadCommand command, Control? focus) => false;
    public bool PadBack()
    {
        if (_browsedSkillId == null) return false;
        _returnToSkill = _browsedSkillId;
        _browsedSkillId = null;
        RequestRedraw();
        if (Content!.GetParent() is ScrollContainer scroll)
            scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, _catalogScroll);
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
        if (hadDetail) RequestRedraw();
    }
}
