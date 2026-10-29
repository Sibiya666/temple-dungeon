using Dialoid.Core.Character;
using Dialoid.Core.Explain;
using Dialoid.Core.Skills;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Скиллы» tab (spec §13.4): the skill slots (a slot closed by the level says when it opens), each with its skill
/// choice (the class's skills and the skills of the equipped items), level and «Вернуть»; the tree of the chosen slot's
/// skill as a node list with «+»/«−»; the free points; the skill's SkillLines through <see cref="NumberLabel"/>; the
/// rotation editor. Every change is a <see cref="SandboxSession"/> command (a refusal goes to the status line); an empty
/// node that cannot take a point now is greyed with the session's own refusal text
/// (<see cref="SandboxSession.WhyCannotAddSkillNodePoint"/>); «Вернуть» is off when <see cref="SandboxSession.CanUndoSlot"/> says no. The panel redraws itself whole
/// (deferred, see <see cref="SessionPanel"/>).
/// </summary>
public partial class SkillsPanel : SessionPanel
{
    /// <summary>Key of the skill level number (an owner-set <see cref="NumberLabel.InputLine"/>).</summary>
    public const string SkillLevelKey = NumberLabel.InputKeyPrefix + "skill_level";

    private static readonly Color Grey = new(0.55f, 0.55f, 0.58f);

    private Action<NumberLine, string?> _open = null!;
    private string? _commandError;
    private int _treeSlot = -1;
    private readonly List<NumberLabel> _skillNumbers = new();
    private readonly List<OptionButton?> _slotOptions = new();
    private readonly List<IReadOnlyList<string?>> _slotChoices = new();
    private readonly Dictionary<string, string> _blocked = new();
    private readonly List<Button> _undoButtons = new();

    /// <summary>The «Вернуть» button of each open slot drawn now (closed slots have none), by slot order.</summary>
    public IReadOnlyList<Button> UndoButtons => _undoButtons;

    /// <summary>The last refused command's error, or null.</summary>
    public string? CommandError => _commandError;

    /// <summary>Slot whose tree and SkillLines are shown, or -1 when every slot is empty.</summary>
    public int TreeSlot => _treeSlot;

    /// <summary>SkillLines labels of <see cref="TreeSlot"/> shown now (without the skill level).</summary>
    public IReadOnlyList<NumberLabel> SkillNumbers => _skillNumbers.Where(n => !NumberLabel.IsInput(n.Line)).ToList();

    /// <summary>The skill level label shown now, or null.</summary>
    public NumberLabel? LevelNumber => _skillNumbers.FirstOrDefault(n => NumberLabel.IsInput(n.Line));

    /// <summary>Skill selector of each open slot drawn now (null for a closed slot).</summary>
    public IReadOnlyList<OptionButton?> SlotOptions => _slotOptions;

    /// <summary>Skill id of each item of <see cref="SlotOptions"/>[slot] (null: empty).</summary>
    public IReadOnlyList<IReadOnlyList<string?>> SlotChoices => _slotChoices;

    /// <summary>Tree nodes shown greyed now, with their reason.</summary>
    public IReadOnlyDictionary<string, string> BlockedNodes => _blocked;

    /// <summary>Text of the free points counter.</summary>
    public string PointsText { get; private set; } = "";

    /// <summary>The rotation as shown (skill ids, in order).</summary>
    public IReadOnlyList<string> ShownRotation { get; private set; } = Array.Empty<string>();

    public void Setup(SandboxSession session, Action<NumberLine, string?> open)
    {
        InitSession(session);
        _open = open;
    }

    // ---- handlers (the controls and the self-test call these) -------------------------------------------------------

    public CommandResult SelectSkill(int slot, string? skillId)
    {
        var result = Run(Session.SetSlotSkill(slot, skillId));
        if (result.Ok && skillId != null) _treeSlot = slot;
        return result;
    }

    public CommandResult Undo(int slot) => Run(Session.UndoSlot(slot));
    public CommandResult SelectLevel(int slot, int level) => Run(Session.SetSkillLevel(slot, level));
    public CommandResult AddPoint(int slot, string nodeId) => Run(Session.AddSkillNodePoint(slot, nodeId));
    public CommandResult RemovePoint(int slot, string nodeId) => Run(Session.RemoveSkillNodePoint(slot, nodeId));

    /// <summary>Shows the slot's tree and numbers.</summary>
    public void ShowTree(int slot)
    {
        _treeSlot = slot;
        RequestRedraw();
    }

    /// <summary>Moves the rotation entry at <paramref name="index"/> by <paramref name="delta"/> (−1 up, +1 down).</summary>
    public CommandResult MoveInRotation(int index, int delta)
    {
        var order = ShownOrder().ToList();
        int to = index + delta;
        if (index < 0 || index >= order.Count || to < 0 || to >= order.Count) return Run(CommandResult.Success);
        (order[index], order[to]) = (order[to], order[index]);
        return Run(Session.SetRotation(order));
    }

    /// <summary>Appends a slotted skill to an explicit rotation.</summary>
    public CommandResult AddToRotation(string skillId) => Run(Session.SetRotation(Session.Build.Rotation.Append(skillId).ToList()));

    /// <summary>Clears the explicit rotation (the simulator then uses the slot order).</summary>
    public CommandResult ResetRotation() => Run(Session.SetRotation(Array.Empty<string>()));

    /// <summary>A refusal is shown; any accepted command (even a no-op) clears the old refusal.</summary>
    private CommandResult Run(CommandResult result)
    {
        _commandError = result.Ok ? null : result.Error;
        RequestRedraw();
        return result;
    }

    protected override void OnSessionChanged()
    {
        _commandError = null;
        RequestRedraw();
    }

    /// <summary>The explicit rotation, or the slotted skills in slot order when the build has none.</summary>
    private IReadOnlyList<string> ShownOrder()
    {
        var build = Session.Build;
        return build.Rotation.Count > 0 ? build.Rotation : build.Slots.Select(s => s.SkillId).ToList();
    }

    // ---- drawing ------------------------------------------------------------------------------------------------------

    protected override void DrawContent()
    {
        _skillNumbers.Clear();
        _slotOptions.Clear();
        _slotChoices.Clear();
        _blocked.Clear();
        _undoButtons.Clear();
        PointsText = "";

        var build = Session.Build;
        if (_commandError != null) AddText("Команда отклонена: " + _commandError, Red);
        AddText($"Открыто слотов: {Session.SlotsOpen} из {SandboxSession.SlotCount}");

        if (_treeSlot < 0 || Session.GetSlot(_treeSlot) == null)
            _treeSlot = Enumerable.Range(0, SandboxSession.SlotCount).FirstOrDefault(i => Session.GetSlot(i) != null, -1);

        for (int i = 0; i < SandboxSession.SlotCount; i++) DrawSlot(i, build);

        Content!.AddChild(new HSeparator());
        DrawTree();

        Content.AddChild(new HSeparator());
        DrawRotation(build);

        var violations = Session.Violations.Where(v => v.Code.Contains("skill", StringComparison.Ordinal)
            || v.Code.Contains("slot", StringComparison.Ordinal) || v.Code.Contains("rotation", StringComparison.Ordinal)).ToList();
        if (violations.Count > 0)
        {
            Content.AddChild(new HSeparator());
            AddText("Нарушения:", Red);
            foreach (var v in violations) AddText("• " + v.Message, Red);
        }
    }

    private void DrawSlot(int slot, Build build)
    {
        var data = Session.Data;
        var current = Session.GetSlot(slot);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = $"Слот {slot + 1}:", CustomMinimumSize = new Vector2(80, 0) });
        Content!.AddChild(row);

        if (slot >= Session.SlotsOpen)
        {
            _slotOptions.Add(null);
            _slotChoices.Add(Array.Empty<string?>());
            row.AddChild(new Label { Text = $"откроется на {OpenLevel(slot)} уровне", Modulate = Grey });
            if (current != null)
            {
                row.AddChild(new Label { Text = "— " + SkillName(current.SkillId) });
                var remove = new Button { Text = "Убрать" };
                remove.Pressed += () => SelectSkill(slot, null);
                row.AddChild(remove);
            }
            return;
        }

        // Choices: the class's skills, then the skills of the equipped items, then the slotted one if neither lists it.
        var choices = new List<string?> { null };
        if (data.Classes.TryGetValue(build.ClassId, out var cls)) choices.AddRange(cls.SkillIds);
        foreach (var itemId in build.ItemIds)
            if (data.Items.TryGetValue(itemId, out var item) && !choices.Contains(item.GrantsSkill)) choices.Add(item.GrantsSkill);
        if (current != null && !choices.Contains(current.SkillId)) choices.Add(current.SkillId);
        var native = cls?.SkillIds.ToHashSet() ?? new HashSet<string>();

        var box = new OptionButton { CustomMinimumSize = new Vector2(260, 0) };
        foreach (var id in choices)
        {
            if (id == null) { box.AddItem("(пусто)"); continue; }
            var text = SkillName(id);
            if (!native.Contains(id)) text += " (предмет)";
            else if (data.Skills.TryGetValue(id, out var def) && def.UnlockLevel > build.Level) text += $" (с {def.UnlockLevel} ур.)";
            box.AddItem(text);
        }
        box.Selected = choices.IndexOf(current?.SkillId);
        box.ItemSelected += i => SelectSkill(slot, choices[(int)i]);
        row.AddChild(box);
        _slotOptions.Add(box);
        _slotChoices.Add(choices);

        if (current != null)
        {
            int cap = Session.SkillLevelCap(slot);
            var spin = new SpinBox
            {
                MinValue = 1,
                MaxValue = Math.Max(cap, current.Level),
                Step = 1,
                Value = current.Level,
                TooltipText = $"Уровень скилла, кап {cap}",
            };
            spin.ValueChanged += v => SelectLevel(slot, (int)v);
            row.AddChild(new Label { Text = "уровень" });
            row.AddChild(spin);
            var tree = new Button { Text = slot == _treeSlot ? "▶ Дерево" : "Дерево", ToggleMode = false };
            tree.Pressed += () => ShowTree(slot);
            row.AddChild(tree);
        }
        var undo = new Button
        {
            Text = "Вернуть",
            TooltipText = "Вернуть, что стояло в слоте до последней смены",
            Disabled = !Session.CanUndoSlot(slot),
        };
        undo.Pressed += () => Undo(slot);
        _undoButtons.Add(undo);
        row.AddChild(undo);
    }

    private void DrawTree()
    {
        if (_treeSlot < 0 || Session.GetSlot(_treeSlot) is not { } slot || !Session.Data.Skills.TryGetValue(slot.SkillId, out var skill))
        {
            AddText("Поставьте скилл в слот, чтобы увидеть его дерево.");
            return;
        }

        AddText($"Дерево скилла «{skill.Name}» (слот {_treeSlot + 1})");
        var levelNumber = NumberLabel.For(LevelLine(slot.Level, _treeSlot), _open, skill.Name);
        _skillNumbers.Add(levelNumber);
        Content!.AddChild(levelNumber);

        int total = Session.SkillPointsTotal(_treeSlot), spent = Session.SkillPointsSpent(_treeSlot);
        PointsText = $"Очки дерева: потрачено {spent} из {total}, свободно {Session.SkillPointsFree(_treeSlot)}";
        AddText(PointsText, spent > total ? Red : null);

        var points = slot.NodePoints;
        foreach (var node in skill.Tree)
        {
            int have = points.GetValueOrDefault(node.Id);
            var reason = have > 0 ? null : Session.WhyCannotAddSkillNodePoint(_treeSlot, node.Id);
            if (reason != null) _blocked[node.Id] = reason;

            var tooltip = node.Description;
            if (node.Requires.Count > 0)
                tooltip += "\nТребует: " + string.Join(", ", node.Requires.Select(r => $"«{NodeName(skill, r.NodeId)}» {r.Points} очк."));
            if (reason != null) tooltip += "\nСейчас нельзя: " + reason;

            var row = new HBoxContainer { TooltipText = tooltip };
            var name = new Label
            {
                Text = $"{node.Name} — {have}/{node.MaxPoints}",
                CustomMinimumSize = new Vector2(320, 0),
                MouseFilter = MouseFilterEnum.Stop,
                TooltipText = tooltip,
            };
            if (reason != null) name.AddThemeColorOverride("font_color", Grey);
            else if (have > 0) name.AddThemeColorOverride("font_color", Green);
            row.AddChild(name);
            var id = node.Id;
            int slotIndex = _treeSlot;
            var plus = new Button { Text = "+", TooltipText = tooltip };
            plus.Pressed += () => AddPoint(slotIndex, id);
            row.AddChild(plus);
            var minus = new Button { Text = "−", TooltipText = tooltip };
            minus.Pressed += () => RemovePoint(slotIndex, id);
            row.AddChild(minus);
            Content.AddChild(row);
        }

        Content.AddChild(new HSeparator());
        var lines = Session.SkillLines(_treeSlot);
        if (lines.Count == 0) AddText("Числа скилла не посчитаны: исправьте нарушения.", Red);
        foreach (var line in lines)
        {
            var label = NumberLabel.For(line, _open, skill.Name);
            _skillNumbers.Add(label);
            Content.AddChild(label);
        }
    }

    private void DrawRotation(Build build)
    {
        AddText("Ротация (порядок приоритета применения):");
        var order = ShownOrder();
        ShownRotation = order;
        if (order.Count == 0)
        {
            AddText("Слоты пусты.");
            return;
        }
        if (build.Rotation.Count == 0) AddText("Своя ротация не задана: используется порядок слотов. Сдвиг создаст ротацию.", Grey);

        for (int i = 0; i < order.Count; i++)
        {
            int index = i;
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = $"{i + 1}. {SkillName(order[i])}", CustomMinimumSize = new Vector2(320, 0) });
            var up = new Button { Text = "вверх", Disabled = i == 0 };
            up.Pressed += () => MoveInRotation(index, -1);
            row.AddChild(up);
            var down = new Button { Text = "вниз", Disabled = i == order.Count - 1 };
            down.Pressed += () => MoveInRotation(index, +1);
            row.AddChild(down);
            Content!.AddChild(row);
        }

        if (build.Rotation.Count == 0) return;
        foreach (var missing in build.Slots.Select(s => s.SkillId).Where(id => !build.Rotation.Contains(id)))
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = $"не в ротации: {SkillName(missing)}", CustomMinimumSize = new Vector2(320, 0), Modulate = Grey });
            var add = new Button { Text = "добавить" };
            add.Pressed += () => AddToRotation(missing);
            row.AddChild(add);
            Content!.AddChild(row);
        }
        var reset = new Button { Text = "Сбросить ротацию (порядок слотов)", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        reset.Pressed += () => ResetRotation();
        Content!.AddChild(reset);
    }

    /// <summary>The first level at which the slot opens (from <see cref="BuildRules.SlotsUnlocked"/>).</summary>
    private static int OpenLevel(int slot) =>
        Enumerable.Range(1, BuildRules.MaxLevel).FirstOrDefault(l => BuildRules.SlotsUnlocked(l) > slot, BuildRules.MaxLevel);

    private string SkillName(string id) => Session.Data.Skills.TryGetValue(id, out var s) ? s.Name : id;

    private static string NodeName(SkillDefinition skill, string nodeId) =>
        skill.Tree.FirstOrDefault(n => n.Id == nodeId)?.Name ?? nodeId;

    private static NumberLine LevelLine(int level, int slot) =>
        NumberLabel.InputLine("skill_level", "Уровень скилла", level, $"уровень скилла в слоте {slot + 1}, поле на вкладке «Скиллы»");
}
