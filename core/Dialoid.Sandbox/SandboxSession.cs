using Dialoid.Core.Builds;
using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Explain;
using Dialoid.Core.Passives;
using Dialoid.Core.Skills;
using Attribute = Dialoid.Core.Character.Attribute;

namespace Dialoid.Sandbox;

/// <summary>
/// The editable build behind the debug scene. Commands either change the build (Revision grows, Changed fires) or
/// refuse with a Russian message. Legality, points and compilation come from Dialoid.Core only; over-budget states
/// (too many points after lowering the level, ascendancy below its unlock level) are allowed and shown as Violations.
/// Derived values are computed lazily once per revision.
/// Skill slots are positional (0..SlotCount-1, may have gaps); <see cref="Build"/> lists the filled ones in slot order.
/// Not thread-safe: use one session from one thread (the UI thread); background work takes a <see cref="Build"/> copy.
/// </summary>
public sealed class SandboxSession
{
    /// <summary>
    /// BuildRules violation codes an imported build may carry: the over-budget and level states the session's own commands
    /// also reach (lowering the level, switching weapons). Any other code refuses the import, so a new Core code blocks
    /// until it is reviewed and listed here.
    /// </summary>
    private static readonly HashSet<string> ImportAcceptedCodes = new()
    {
        "too_many_passive_points", "too_many_ascendancy_points", "too_many_skill_points", "ascendancy_locked",
        "slot_locked", "skill_locked_by_level", "skill_level_above_cap", "weapon_type_mismatch",
    };

    private readonly Build _build;
    private readonly CharacterCompiler _compiler;
    private readonly SkillSlot?[] _slots = new SkillSlot?[SlotCount];
    private readonly SkillSlot?[] _undo = new SkillSlot?[SlotCount]; // copies; null with _hasUndo = "the slot was empty"
    private readonly bool[] _hasUndo = new bool[SlotCount];
    private Derived? _derived;

    /// <summary>Number of skill slots at the maximum level.</summary>
    public static int SlotCount { get; } = BuildRules.SlotsUnlocked(BuildRules.MaxLevel);

    public GameData Data { get; }

    /// <summary>Grows on every successful change.</summary>
    public int Revision { get; private set; }

    /// <summary>Raised after every successful change.</summary>
    public event Action? Changed;

    private SandboxSession(GameData data, Build build)
    {
        Data = data;
        _build = build;
        _compiler = new CharacterCompiler(data);
    }

    /// <summary>An empty level 1 build of the class: only the free class start is taken.</summary>
    public static SandboxSession Create(GameData data, string classId)
    {
        if (!data.Classes.ContainsKey(classId)) throw new ArgumentException(Texts.UnknownClass(classId), nameof(classId));
        return new SandboxSession(data, new Build { ClassId = classId });
    }

    /// <summary>A fresh copy of the build; changing it does not touch the session.</summary>
    public Build Build => BuildRules.Normalized(_build);

    private ClassDefinition Class => Data.Classes[_build.ClassId];
    private string StartId => Class.PassiveStartNodeId;

    // ---- commands -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Switches the class and resets passives, attribute choices, ascendancy, skill slots (with their undo) and rotation.
    /// Level, weapon and items are kept. Choosing the current class again is a deliberate "reset class" and also resets.
    /// </summary>
    public CommandResult SetClass(string classId)
    {
        if (!Data.Classes.ContainsKey(classId)) return CommandResult.Fail(Texts.UnknownClass(classId));
        _build.ClassId = classId;
        _build.PassiveNodes.Clear();
        _build.AttributeChoices.Clear();
        _build.AscendancyId = null;
        _build.AscendancyNodes.Clear();
        Array.Clear(_slots);
        ClearUndo();
        _build.Rotation.Clear();
        return Commit();
    }

    /// <summary>
    /// Level 1..MaxLevel. Taken nodes stay; spare spending shows up as violations. Lowering the level moves the filled
    /// skill slots to the first positions (with their undo memory), so slot positions match the exported build and
    /// BuildRules' slot_locked fires exactly when more skills are slotted than the level opens.
    /// </summary>
    public CommandResult SetLevel(int level)
    {
        if (level < 1 || level > BuildRules.MaxLevel) return CommandResult.Fail(Texts.LevelOutOfRange(level, BuildRules.MaxLevel));
        if (level == _build.Level) return CommandResult.Success;
        if (level < _build.Level) CompactSlots();
        _build.Level = level;
        return Commit();
    }

    /// <summary>Takes a node adjacent to the taken ones, or drops a taken node if the rest stays connected to the start.</summary>
    public CommandResult TogglePassive(string nodeId)
    {
        var tree = Data.Passives;
        if (!tree.Nodes.TryGetValue(nodeId, out var node)) return CommandResult.Fail(Texts.UnknownPassive(nodeId));
        if (node.Kind == PassiveKind.Start) return CommandResult.Fail(Texts.StartNodeFixed(node.Name));

        var allocated = AllocatedWithStart();
        if (allocated.Contains(nodeId))
        {
            if (!tree.CanDeallocate(allocated, nodeId, StartId)) return CommandResult.Fail(Texts.PassiveBreaksPath(node.Name));
            _build.PassiveNodes.Remove(nodeId);
            _build.AttributeChoices.Remove(nodeId);
            return Commit();
        }

        if (!tree.CanAllocate(allocated, nodeId)) return CommandResult.Fail(Texts.PassiveUnreachable(node.Name));
        _build.PassiveNodes.Add(nodeId);
        // A new attribute node starts on the class primary so the build stays compilable; SetAttributeChoice changes it.
        if (node.Kind == PassiveKind.Attribute) _build.AttributeChoices[nodeId] = Class.Primary;
        return Commit();
    }

    /// <summary>Chooses the attribute of a taken attribute node.</summary>
    public CommandResult SetAttributeChoice(string nodeId, Attribute attribute)
    {
        if (!Data.Passives.Nodes.TryGetValue(nodeId, out var node)) return CommandResult.Fail(Texts.UnknownPassive(nodeId));
        if (node.Kind != PassiveKind.Attribute) return CommandResult.Fail(Texts.NotAttributeNode(node.Name));
        if (!_build.PassiveNodes.Contains(nodeId)) return CommandResult.Fail(Texts.AttributeNodeNotTaken(node.Name));
        if (_build.AttributeChoices.TryGetValue(nodeId, out var current) && current == attribute) return CommandResult.Success;
        _build.AttributeChoices[nodeId] = attribute;
        return Commit();
    }

    /// <summary>Picks an ascendancy of the current class (null removes it). A change clears the ascendancy nodes.</summary>
    public CommandResult SetAscendancy(string? ascendancyId)
    {
        if (ascendancyId == _build.AscendancyId) return CommandResult.Success;
        if (ascendancyId != null && (!Data.Ascendancies.TryGetValue(ascendancyId, out var asc) || asc.ClassId != _build.ClassId))
            return CommandResult.Fail(Texts.AscendancyWrongClass(ascendancyId, _build.ClassId));
        _build.AscendancyId = ascendancyId;
        _build.AscendancyNodes.Clear();
        return Commit();
    }

    /// <summary>Takes or drops a notable of the chosen ascendancy. Overspending shows up as a violation.</summary>
    public CommandResult ToggleAscendancyNode(string nodeId)
    {
        if (_build.AscendancyId == null || !Data.Ascendancies.TryGetValue(_build.AscendancyId, out var asc))
            return CommandResult.Fail(Texts.NoAscendancy);
        if (asc.Notables.All(n => n.Id != nodeId)) return CommandResult.Fail(Texts.UnknownAscendancyNode(nodeId, asc.Name));
        if (!_build.AscendancyNodes.Remove(nodeId)) _build.AscendancyNodes.Add(nodeId);
        return Commit();
    }


    /// <summary>
    /// Puts a skill into an open slot (null empties the slot). The replaced skill is remembered with its level and nodes for
    /// <see cref="UndoSlot"/>. A new skill starts at the highest level its cap and the character level allow, with no nodes.
    /// Refused for a slot closed by the level, an unknown or already slotted skill, or a skill that is neither the class's
    /// nor granted by an equipped item. A class skill below its unlock level is accepted and shown as the
    /// skill_locked_by_level violation (the same over-budget policy as passives).
    /// </summary>
    public CommandResult SetSlotSkill(int slot, string? skillId)
    {
        if (!ValidSlot(slot)) return CommandResult.Fail(Texts.SlotOutOfRange(slot, SlotCount));
        if (skillId == null)
        {
            if (_slots[slot] == null) return CommandResult.Success;
            Replace(slot, null);
            return Commit();
        }
        if (_slots[slot]?.SkillId == skillId) return CommandResult.Success;
        if (slot >= SlotsOpen) return CommandResult.Fail(Texts.SlotLocked(slot, _build.Level, SlotsOpen));
        if (!Data.Skills.TryGetValue(skillId, out var skill)) return CommandResult.Fail(Texts.UnknownSkill(skillId));
        if (CheckSlottable(slot, skill) is { } refusal) return refusal;
        int cap = BuildRules.SkillLevelCap(_build, skillId, Data);
        Replace(slot, new SkillSlot { SkillId = skillId, Level = Math.Max(1, Math.Min(cap, _build.Level)) });
        return Commit();
    }

    /// <summary>
    /// Brings back what the slot held before its last change (a skill with its level and nodes, or emptiness); what it
    /// replaces becomes the new undo, so a second undo redoes.
    /// </summary>
    public CommandResult UndoSlot(int slot)
    {
        if (!ValidSlot(slot)) return CommandResult.Fail(Texts.SlotOutOfRange(slot, SlotCount));
        if (!_hasUndo[slot]) return CommandResult.Fail(Texts.NothingToUndo(slot));
        var previous = _undo[slot];
        if (previous != null)
        {
            if (slot >= SlotsOpen) return CommandResult.Fail(Texts.SlotLocked(slot, _build.Level, SlotsOpen));
            if (!Data.Skills.TryGetValue(previous.SkillId, out var skill)) return CommandResult.Fail(Texts.UnknownSkill(previous.SkillId));
            if (CheckSlottable(slot, skill) is { } refusal) return refusal;
        }
        else if (_slots[slot] == null) return CommandResult.Fail(Texts.NothingToUndo(slot));
        Replace(slot, previous == null ? null : Copy(previous));
        return Commit();
    }

    /// <summary>Skill level 1..<see cref="BuildRules.SkillLevelCap"/>. Spent node points stay; overspending is a violation.</summary>
    public CommandResult SetSkillLevel(int slot, int level)
    {
        if (SlotSkill(slot, out var current, out var skill) is { } refusal) return refusal;
        int cap = BuildRules.SkillLevelCap(_build, skill.Id, Data);
        if (level < 1 || level > cap) return CommandResult.Fail(Texts.SkillLevelOutOfRange(skill.Name, level, cap));
        if (level == current.Level) return CommandResult.Success;
        current.Level = level;
        return Commit();
    }

    /// <summary>One more point into a skill tree node: needs a free point, met requirements and a free exclusive group.</summary>
    public CommandResult AddSkillNodePoint(int slot, string nodeId)
    {
        if (SlotSkill(slot, out var current, out var skill) is { } refusal) return refusal;
        if (skill.Tree.FirstOrDefault(n => n.Id == nodeId) is not { } node)
            return CommandResult.Fail(Texts.UnknownSkillNode(nodeId, skill.Name));
        var points = current.NodePoints;
        int have = points.GetValueOrDefault(nodeId);
        if (have >= node.MaxPoints) return CommandResult.Fail(Texts.SkillNodeFull(node.Name, node.MaxPoints));
        int total = SkillTree.PointsForLevel(current.Level);
        if (SkillTree.PointsSpent(points) >= total) return CommandResult.Fail(Texts.NoFreeSkillPoints(skill.Name, total));
        if (!SkillTree.RequirementsMet(node, points))
            return CommandResult.Fail(Texts.SkillNodeRequirements(node.Name, string.Join(", ",
                node.Requires.Select(r => Texts.SkillNodeRequirement(NodeName(skill, r.NodeId), r.Points)))));

        var after = new Dictionary<string, int>(points) { [nodeId] = have + 1 };
        if (SkillTree.ViolatedGroups(skill, after).FirstOrDefault(g => g.Nodes.Contains(nodeId)) is { Group: not null } clash)
            return CommandResult.Fail(Texts.SkillNodeExclusive(node.Name,
                string.Join(", ", clash.Nodes.Where(id => id != nodeId).Select(id => NodeName(skill, id)))));

        points[nodeId] = have + 1;
        return Commit();
    }

    /// <summary>One point out of a node; refused when another taken node would lose its requirement.</summary>
    public CommandResult RemoveSkillNodePoint(int slot, string nodeId)
    {
        if (SlotSkill(slot, out var current, out var skill) is { } refusal) return refusal;
        if (skill.Tree.FirstOrDefault(n => n.Id == nodeId) is not { } node)
            return CommandResult.Fail(Texts.UnknownSkillNode(nodeId, skill.Name));
        var points = current.NodePoints;
        int have = points.GetValueOrDefault(nodeId);
        if (have <= 0) return CommandResult.Fail(Texts.SkillNodeNoPoints(node.Name));

        var after = new Dictionary<string, int>(points);
        if (have == 1) after.Remove(nodeId);
        else after[nodeId] = have - 1;
        var broken = skill.Tree.FirstOrDefault(n => n.Id != nodeId && after.GetValueOrDefault(n.Id) > 0 && !SkillTree.RequirementsMet(n, after));
        if (broken != null) return CommandResult.Fail(Texts.SkillNodeRequired(node.Name, broken.Name));

        current.NodePoints = after;
        return Commit();
    }

    /// <summary>Equips a weapon (null: none). A weapon the slotted attacks cannot use shows up as a violation.</summary>
    public CommandResult SetWeapon(string? weaponId)
    {
        if (weaponId == _build.WeaponId) return CommandResult.Success;
        if (weaponId != null && !Data.Weapons.ContainsKey(weaponId)) return CommandResult.Fail(Texts.UnknownWeapon(weaponId));
        _build.WeaponId = weaponId;
        return Commit();
    }

    /// <summary>Equips or takes off an item. Taking off is refused while a skill only that item grants is slotted.</summary>
    public CommandResult ToggleItem(string itemId)
    {
        if (!Data.Items.TryGetValue(itemId, out var item)) return CommandResult.Fail(Texts.UnknownItem(itemId));
        if (!_build.ItemIds.Contains(itemId))
        {
            _build.ItemIds.Add(itemId);
            return Commit();
        }

        var without = BuildRules.Normalized(_build);
        without.ItemIds.RemoveAll(i => i == itemId);
        foreach (var s in Filled())
            if (BuildRules.SkillLevelCap(without, s.SkillId, Data) == 0)
                return CommandResult.Fail(Texts.ItemSkillSlotted(item.Name, SkillName(s.SkillId)));
        _build.ItemIds.RemoveAll(i => i == itemId);
        return Commit();
    }

    /// <summary>Replaces the rotation (cast priority order). Every skill must be in a slot.</summary>
    public CommandResult SetRotation(IReadOnlyList<string> skillIds)
    {
        if (skillIds == null || skillIds.Any(id => id == null)) return CommandResult.Fail(Texts.RotationEmptyId);
        var slotted = Filled().Select(s => s.SkillId).ToHashSet();
        if (skillIds.FirstOrDefault(id => !slotted.Contains(id)) is { } missing)
            return CommandResult.Fail(Texts.RotationSkillNotSlotted(SkillName(missing)));
        if (skillIds.SequenceEqual(_build.Rotation)) return CommandResult.Success;
        _build.Rotation.Clear();
        _build.Rotation.AddRange(skillIds);
        return Commit();
    }

    /// <summary>The build as a share code ("DLD1:...").</summary>
    public string ExportCode() => BuildCodec.Export(Build);

    /// <summary>
    /// Replaces the build with a share code ("DLD1:...") or build JSON. Refused (build and Revision unchanged) when the text
    /// does not parse, names unknown ids, or holds a state the session's commands cannot make (any BuildRules violation not
    /// in ImportAcceptedCodes, skill levels outside 1..NativeSkillCap, more slots than SlotCount).
    /// Undo memory of the slots is dropped.
    /// </summary>
    public CommandResult Import(string text)
    {
        text ??= "";
        var trimmed = text.TrimStart();
        Build? parsed;
        string? error;
        bool ok = trimmed.StartsWith('{')
            ? BuildCodec.TryFromJson(text, Data, out parsed, out error)
            : BuildCodec.TryImport(text, Data, out parsed, out error);
        if (!ok || parsed == null) return CommandResult.Fail(error ?? Texts.ImportUnparsed);

        var build = BuildRules.Normalized(parsed);
        if (build.Slots.Count > SlotCount) return CommandResult.Fail(Texts.ImportTooManySlots(build.Slots.Count, SlotCount));
        if (build.Slots.FirstOrDefault(s => s.Level < 1 || s.Level > BuildRules.NativeSkillCap) is { } badLevel)
            return CommandResult.Fail(Texts.ImportSkillLevel(SkillName(badLevel.SkillId), badLevel.Level, BuildRules.NativeSkillCap));

        // BuildRules skips the ascendancy class/node checks below the unlock level, so validate a copy raised to it too.
        var raised = BuildRules.Normalized(build);
        raised.Level = Math.Max(raised.Level, BuildRules.AscendancyUnlockLevel);
        var blocking = BuildRules.Validate(build, Data).Concat(BuildRules.Validate(raised, Data))
            .Where(v => !ImportAcceptedCodes.Contains(v.Code)).Select(v => v.Message).Distinct().ToList();
        if (blocking.Count > 0) return CommandResult.Fail(Texts.ImportRefused(string.Join("; ", blocking)));

        _build.ClassId = build.ClassId;
        _build.Level = build.Level;
        _build.AscendancyId = build.AscendancyId;
        _build.AscendancyNodes = build.AscendancyNodes;
        _build.PassiveNodes = build.PassiveNodes;
        _build.AttributeChoices = build.AttributeChoices;
        _build.WeaponId = build.WeaponId;
        _build.ItemIds = build.ItemIds;
        _build.Rotation = build.Rotation;
        Array.Clear(_slots);
        ClearUndo();
        for (int i = 0; i < build.Slots.Count; i++) _slots[i] = build.Slots[i];
        return Commit();
    }

    // ---- derived values -------------------------------------------------------------------------------------------

    public int PassivePointsTotal => PassiveTree.PointsForLevel(_build.Level);

    public int PassivePointsSpent => _build.PassiveNodes.Count;

    public int AscendancyPointsTotal => Ascendancy.PointsForLevel(_build.Level);

    public int AscendancyPointsSpent =>
        _build.AscendancyId != null && Data.Ascendancies.TryGetValue(_build.AscendancyId, out var asc)
            ? asc.Notables.Where(n => _build.AscendancyNodes.Contains(n.Id)).Sum(n => n.Cost)
            : 0;

    /// <summary>Nodes that can be taken right now (adjacent to the taken ones), for highlighting. A copy.</summary>
    public IReadOnlySet<string> AllocatablePassives => new HashSet<string>(Current.Allocatable);

    /// <summary>Broken rules of the current build (read-only).</summary>
    public IReadOnlyList<RuleViolation> Violations => Current.Violations;

    /// <summary>Slots open at the current level.</summary>
    public int SlotsOpen => BuildRules.SlotsUnlocked(_build.Level);

    /// <summary>A copy of the slot's skill, or null for an empty or out-of-range slot.</summary>
    public SkillSlot? GetSlot(int slot) => ValidSlot(slot) && _slots[slot] is { } s ? Copy(s) : null;

    /// <summary>Level cap of the slotted skill (0 for an empty slot).</summary>
    public int SkillLevelCap(int slot) =>
        GetSlot(slot) is { } s ? BuildRules.SkillLevelCap(_build, s.SkillId, Data) : 0;

    /// <summary>Tree points the slotted skill's level gives (0 for an empty slot).</summary>
    public int SkillPointsTotal(int slot) => GetSlot(slot) is { } s ? SkillTree.PointsForLevel(s.Level) : 0;

    public int SkillPointsSpent(int slot) => GetSlot(slot) is { } s ? SkillTree.PointsSpent(s.NodePoints) : 0;

    /// <summary>Skill sheet lines of the slot (empty for an empty slot or a build that cannot be compiled).</summary>
    public IReadOnlyList<NumberLine> SkillLines(int slot) => Current.SkillLines(slot);

    /// <summary>The compiled character, or null when the build cannot be compiled.</summary>
    public CompiledCharacter? Compiled => Current.Compiled;

    /// <summary>Character sheet lines (empty when the build cannot be compiled).</summary>
    public IReadOnlyList<NumberLine> CharacterLines => Current.CharacterLines;

    private CommandResult Commit()
    {
        _build.Slots = Filled().ToList();
        Revision++;
        _derived = null;
        Changed?.Invoke();
        return CommandResult.Success;
    }

    /// <summary>Taken passives plus the class start: the shape PassiveTree.CanAllocate/CanDeallocate expect.</summary>
    private HashSet<string> AllocatedWithStart() => new(_build.PassiveNodes) { StartId };

    private Derived Current => _derived ??= new Derived(this);

    private static bool ValidSlot(int slot) => slot >= 0 && slot < SlotCount;

    private IEnumerable<SkillSlot> Filled() => _slots.Where(s => s != null)!;

    private static SkillSlot Copy(SkillSlot s) =>
        new() { SkillId = s.SkillId, Level = s.Level, NodePoints = new Dictionary<string, int>(s.NodePoints) };

    private string SkillName(string id) => Data.Skills.TryGetValue(id, out var s) ? s.Name : id;

    private static string NodeName(SkillDefinition skill, string nodeId) =>
        skill.Tree.FirstOrDefault(n => n.Id == nodeId)?.Name ?? nodeId;

    /// <summary>Puts a slot value in, remembers a copy of the old one (null: was empty) for undo, drops a removed skill from the rotation.</summary>
    private void Replace(int slot, SkillSlot? value)
    {
        var old = _slots[slot];
        _undo[slot] = old == null ? null : Copy(old);
        _hasUndo[slot] = true;
        _slots[slot] = value;
        if (old != null && Filled().All(s => s.SkillId != old.SkillId)) _build.Rotation.RemoveAll(id => id == old.SkillId);
    }

    private void ClearUndo()
    {
        Array.Clear(_undo);
        Array.Clear(_hasUndo);
    }

    /// <summary>Moves filled slots to positions 0..n-1 in order; each keeps its undo memory, empty positions lose theirs.</summary>
    private void CompactSlots()
    {
        var slots = new SkillSlot?[SlotCount];
        var undo = new SkillSlot?[SlotCount];
        var hasUndo = new bool[SlotCount];
        int k = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i] == null) continue;
            slots[k] = _slots[i];
            undo[k] = _undo[i];
            hasUndo[k] = _hasUndo[i];
            k++;
        }
        Array.Copy(slots, _slots, SlotCount);
        Array.Copy(undo, _undo, SlotCount);
        Array.Copy(hasUndo, _hasUndo, SlotCount);
    }

    /// <summary>Refusal for putting the skill into the slot: duplicate in another slot, or not available to the build.</summary>
    private CommandResult? CheckSlottable(int slot, SkillDefinition skill)
    {
        for (int i = 0; i < SlotCount; i++)
            if (i != slot && _slots[i]?.SkillId == skill.Id) return CommandResult.Fail(Texts.SkillAlreadySlotted(skill.Name, i));
        return BuildRules.SkillLevelCap(_build, skill.Id, Data) == 0 ? CommandResult.Fail(Texts.SkillNotAvailable(skill.Name)) : null;
    }

    /// <summary>The live slot and its skill definition, or a refusal.</summary>
    private CommandResult? SlotSkill(int slot, out SkillSlot current, out SkillDefinition skill)
    {
        current = null!;
        skill = null!;
        if (!ValidSlot(slot)) return CommandResult.Fail(Texts.SlotOutOfRange(slot, SlotCount));
        if (_slots[slot] is not { } s) return CommandResult.Fail(Texts.SlotEmpty(slot));
        if (!Data.Skills.TryGetValue(s.SkillId, out var def)) return CommandResult.Fail(Texts.UnknownSkill(s.SkillId));
        current = s;
        skill = def;
        return null;
    }

    /// <summary>Lazily computed values of one revision.</summary>
    private sealed class Derived
    {
        private readonly SandboxSession _s;
        private IReadOnlySet<string>? _allocatable;
        private IReadOnlyList<RuleViolation>? _violations;
        private bool _compiledDone;
        private CompiledCharacter? _compiled;
        private IReadOnlyList<NumberLine>? _lines;
        private readonly Dictionary<int, IReadOnlyList<NumberLine>> _skillLines = new();

        public Derived(SandboxSession s) => _s = s;

        public IReadOnlySet<string> Allocatable => _allocatable ??= ComputeAllocatable();

        public IReadOnlyList<RuleViolation> Violations =>
            _violations ??= BuildRules.Validate(_s._build, _s.Data).ToList().AsReadOnly();

        public CompiledCharacter? Compiled
        {
            get
            {
                if (_compiledDone) return _compiled;
                _compiledDone = true;
                try { _compiled = _s._compiler.Compile(BuildRules.Normalized(_s._build)); }
                catch (ArgumentException) { _compiled = null; } // the compiler's documented refusal for unknown ids
                return _compiled;
            }
        }

        public IReadOnlyList<NumberLine> CharacterLines =>
            _lines ??= Compiled is { } ch ? CharacterSheet.Describe(ch, _s.Data) : Array.Empty<NumberLine>();

        public IReadOnlyList<NumberLine> SkillLines(int slot)
        {
            if (_skillLines.TryGetValue(slot, out var cached)) return cached;
            return _skillLines[slot] = ComputeSkillLines(slot);
        }

        /// <summary>Compiled skills follow the filled slots in order, so the slot's index among filled slots picks its skill.</summary>
        private IReadOnlyList<NumberLine> ComputeSkillLines(int slot)
        {
            if (!ValidSlot(slot) || _s._slots[slot] == null || Compiled is not { } ch) return Array.Empty<NumberLine>();
            int index = _s._slots.Take(slot).Count(x => x != null);
            if (index >= ch.Skills.Count || ch.Skills[index].SkillId != _s._slots[slot]!.SkillId) return Array.Empty<NumberLine>();
            WeaponStub? weapon = null;
            if (_s._build.WeaponId != null) _s.Data.Weapons.TryGetValue(_s._build.WeaponId, out weapon);
            return SkillSheet.Describe(ch, ch.Skills[index], _s.Data, weapon);
        }

        private IReadOnlySet<string> ComputeAllocatable()
        {
            var tree = _s.Data.Passives;
            var allocated = _s.AllocatedWithStart();
            return tree.Nodes.Keys.Where(id => tree.CanAllocate(allocated, id)).ToHashSet();
        }
    }
}
