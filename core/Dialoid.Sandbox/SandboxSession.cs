using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Explain;
using Dialoid.Core.Passives;
using Attribute = Dialoid.Core.Character.Attribute;

namespace Dialoid.Sandbox;

/// <summary>
/// The editable build behind the debug scene. Commands either change the build (Revision grows, Changed fires) or
/// refuse with a Russian message. Legality, points and compilation come from Dialoid.Core only; over-budget states
/// (too many points after lowering the level, ascendancy below its unlock level) are allowed and shown as Violations.
/// Derived values are computed lazily once per revision.
/// </summary>
public sealed class SandboxSession
{
    private readonly Build _build;
    private readonly CharacterCompiler _compiler;
    private Derived? _derived;

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

    /// <summary>Switches the class and resets passives, attribute choices and ascendancy. The level is kept.</summary>
    public CommandResult SetClass(string classId)
    {
        if (!Data.Classes.ContainsKey(classId)) return CommandResult.Fail(Texts.UnknownClass(classId));
        _build.ClassId = classId;
        _build.PassiveNodes.Clear();
        _build.AttributeChoices.Clear();
        _build.AscendancyId = null;
        _build.AscendancyNodes.Clear();
        return Commit();
    }

    /// <summary>Level 1..MaxLevel. Taken nodes stay; spare spending shows up as violations.</summary>
    public CommandResult SetLevel(int level)
    {
        if (level < 1 || level > BuildRules.MaxLevel) return CommandResult.Fail(Texts.LevelOutOfRange(level, BuildRules.MaxLevel));
        if (level == _build.Level) return CommandResult.Success;
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

    // ---- derived values -------------------------------------------------------------------------------------------

    public int PassivePointsTotal => PassiveTree.PointsForLevel(_build.Level);

    public int PassivePointsSpent => _build.PassiveNodes.Count;

    public int AscendancyPointsTotal => Ascendancy.PointsForLevel(_build.Level);

    public int AscendancyPointsSpent =>
        _build.AscendancyId != null && Data.Ascendancies.TryGetValue(_build.AscendancyId, out var asc)
            ? asc.Notables.Where(n => _build.AscendancyNodes.Contains(n.Id)).Sum(n => n.Cost)
            : 0;

    /// <summary>Nodes that can be taken right now (adjacent to the taken ones), for highlighting.</summary>
    public IReadOnlySet<string> AllocatablePassives => Current.Allocatable;

    public IReadOnlyList<RuleViolation> Violations => Current.Violations;

    /// <summary>The compiled character, or null when the build cannot be compiled.</summary>
    public CompiledCharacter? Compiled => Current.Compiled;

    /// <summary>Character sheet lines (empty when the build cannot be compiled).</summary>
    public IReadOnlyList<NumberLine> CharacterLines => Current.CharacterLines;

    private CommandResult Commit()
    {
        Revision++;
        _derived = null;
        Changed?.Invoke();
        return CommandResult.Success;
    }

    /// <summary>Taken passives plus the class start: the shape PassiveTree.CanAllocate/CanDeallocate expect.</summary>
    private HashSet<string> AllocatedWithStart() => new(_build.PassiveNodes) { StartId };

    private Derived Current => _derived ??= new Derived(this);

    /// <summary>Lazily computed values of one revision.</summary>
    private sealed class Derived
    {
        private readonly SandboxSession _s;
        private IReadOnlySet<string>? _allocatable;
        private IReadOnlyList<RuleViolation>? _violations;
        private bool _compiledDone;
        private CompiledCharacter? _compiled;
        private IReadOnlyList<NumberLine>? _lines;

        public Derived(SandboxSession s) => _s = s;

        public IReadOnlySet<string> Allocatable => _allocatable ??= ComputeAllocatable();

        public IReadOnlyList<RuleViolation> Violations => _violations ??= BuildRules.Validate(_s._build, _s.Data);

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

        private IReadOnlySet<string> ComputeAllocatable()
        {
            var tree = _s.Data.Passives;
            var allocated = _s.AllocatedWithStart();
            return tree.Nodes.Keys.Where(id => tree.CanAllocate(allocated, id)).ToHashSet();
        }
    }
}
