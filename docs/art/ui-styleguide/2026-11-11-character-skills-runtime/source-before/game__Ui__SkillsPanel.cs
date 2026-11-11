using Dialoid.Core.Character;
using Dialoid.Core.Explain;
using Dialoid.Core.Skills;
using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui;

/// <summary>Skill catalog with explicit detail navigation, assignment and rotation editing.</summary>
public partial class SkillsPanel : SessionPanel
{
    /// <summary>Key of the skill level number (an owner-set <see cref="NumberLabel.InputLine"/>).</summary>
    public const string SkillLevelKey = NumberLabel.InputKeyPrefix + "skill_level";

    private static readonly Color Grey = RoomHudTheme.MutedText;

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
        Theme = RoomHudTheme.ReferenceWindowTheme();
        GetChild<ScrollContainer>(0).FollowFocus = true;
        _open = open;
    }

    // ---- handlers (the controls and the self-test call these) -------------------------------------------------------

    public CommandResult SelectSkill(int slot, string? skillId)
    {
        var result = Run(Session.SetSlotSkill(slot, skillId));
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
        _browsedSkillId = Session.GetSlot(slot)?.SkillId;
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
        _catalogButtons.Clear();
        _previewNodeIds.Clear();
        PointsText = "";
        AssignmentSlotOptions = null;
        AssignSkillButton = null;
        UndoSkillButton = null;
        ShownRotation = ShownOrder();

        var build = Session.Build;
        if (DrawBrowsedSkill()) return;
        if (_commandError != null) AddText("Команда отклонена: " + _commandError, Red);
        DrawCatalog();
        _treeSlot = -1;
        SkillGraph = null;
        SkillGraphEdges = Array.Empty<(string, string)>();
        SelectedNodeId = null;
        SelectedNodeLabel = null;
        DrawRotation(build);

        var violations = Session.Violations.Where(v => v.Code.Contains("skill", StringComparison.Ordinal)
            || v.Code.Contains("slot", StringComparison.Ordinal) || v.Code.Contains("rotation", StringComparison.Ordinal)).ToList();
        if (violations.Count > 0)
        {
            Content!.AddChild(new HSeparator());
            AddText("Нарушения:", Red);
            foreach (var v in violations) AddText("• " + v.Message, Red);
        }
    }

    private void DrawTree()
    {
        if (_treeSlot < 0 || Session.GetSlot(_treeSlot) is not { } slot || !Session.Data.Skills.TryGetValue(slot.SkillId, out var skill))
        {
            AddText("Для просмотра дерева выберите иконку скилла выше.");
            return;
        }

        var heading = new HBoxContainer();
        if (GameIcons.Skill(slot.SkillId) is { } picture) heading.AddChild(GameIcons.View(picture, 40));
        heading.AddChild(RoomHudTheme.Heading(skill.Name, 20));
        Content!.AddChild(heading);
        DrawAssignmentControls();
        var levelNumber = NumberLabel.For(LevelLine(slot.Level, _treeSlot), _open, skill.Name);
        _skillNumbers.Add(levelNumber);
        Content!.AddChild(levelNumber);

        int total = Session.SkillPointsTotal(_treeSlot), spent = Session.SkillPointsSpent(_treeSlot);
        PointsText = $"Очки дерева: потрачено {spent} из {total}, свободно {Session.SkillPointsFree(_treeSlot)}";
        AddText(PointsText, spent > total ? Red : null);

        DrawSkillGraph(skill, _treeSlot);

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

    /// <summary>Caption under the rotation: the simulator always tries every non-generator first (core Rotation).</summary>
    public const string GeneratorsNote =
        "Генераторы применяются только когда все остальные скиллы заблокированы (откат, ресурс, оружие), где бы они ни стояли в списке.";

    /// <summary>Appended to a generator's rotation entry.</summary>
    public const string GeneratorMark = " (генератор)";

    private void DrawRotation(Build build)
    {
        Content!.AddChild(RoomHudTheme.Heading("Приоритет навыков", 18));
        var order = ShownOrder();
        ShownRotation = order;
        if (order.Count == 0)
        {
            AddText("Слоты пусты.");
            return;
        }
        if (build.Rotation.Count == 0) AddText("Своя ротация не задана: используется порядок слотов. Сдвиг создаст ротацию.", Grey);
        if (order.Any(Session.IsGeneratorSkill)) AddText(GeneratorsNote, Grey);

        for (int i = 0; i < order.Count; i++)
        {
            int index = i;
            var row = new HBoxContainer();
            var mark = Session.IsGeneratorSkill(order[i]) ? GeneratorMark : "";
            if (GameIcons.Skill(order[i]) is { } rotationIcon) row.AddChild(GameIcons.View(rotationIcon, 20));
            row.AddChild(new Label { Text = $"{i + 1}. {SkillName(order[i])}{mark}", AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var up = new Button { Text = "↑", TooltipText = "Повысить приоритет", Disabled = i == 0 };
            up.Pressed += () => MoveInRotation(index, -1);
            row.AddChild(up);
            var down = new Button { Text = "↓", TooltipText = "Понизить приоритет", Disabled = i == order.Count - 1 };
            down.Pressed += () => MoveInRotation(index, +1);
            row.AddChild(down);
            Content!.AddChild(row);
        }

        if (build.Rotation.Count == 0) return;
        foreach (var missing in build.Slots.Select(s => s.SkillId).Where(id => !build.Rotation.Contains(id)))
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = $"не в ротации: {SkillName(missing)}", AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, Modulate = Grey });
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
