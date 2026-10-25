using System.Text.Json;
using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Effects;

/// <summary>
/// Context of <see cref="IMechanic.OnCompile"/>. Character phase (<see cref="Skill"/> is null): add or remove character
/// modifiers and set character rules. Skill phase: edit <see cref="Skill"/>, a draft of one compiled skill.
/// Character-phase operations throw in the skill phase (the character evaluator is already built by then).
/// </summary>
public sealed class MechanicCompileContext
{
    /// <summary>Override priority of keystone rules: above anything data uses, so "fixed at X" really is fixed (spec 7.4).</summary>
    public const int KeystonePriority = 1_000_000;

    private readonly List<Modifier>? _characterMods;
    private readonly Dictionary<string, double>? _rules;

    internal MechanicCompileContext(GameData data, Build build, ClassDefinition cls, MechanicEffect effect, SourceRef source,
        List<Modifier>? characterMods, Dictionary<string, double>? rules, MechanicSkillDraft? skill)
    {
        Data = data;
        Build = build;
        Class = cls;
        Effect = effect;
        Source = source;
        _characterMods = characterMods;
        _rules = rules;
        Skill = skill;
    }

    public GameData Data { get; }
    public Build Build { get; }
    public ClassDefinition Class { get; }
    public MechanicEffect Effect { get; }
    public IReadOnlyDictionary<string, JsonElement> Params => Effect.Params;

    /// <summary>The node that granted the mechanic (keystone, passive, ascendancy); for a skill-scoped mechanic without a known node: kind "mechanic".</summary>
    public SourceRef Source { get; }

    /// <summary>Null in the character phase; the skill being edited in the skill phase.</summary>
    public MechanicSkillDraft? Skill { get; }

    public double Number(string name, double fallback) => MechanicParams.Number(Effect, name, fallback);
    public string Text(string name, string fallback) => MechanicParams.Text(Effect, name, fallback);

    /// <summary>Builds a modifier stamped with <see cref="Source"/> (so the stat breakdown names the keystone).</summary>
    public Modifier Mod(string stat, ModKind kind, double value, PerStat? per = null, int priority = 0, TagSet? tags = null) =>
        new(stat, kind, value, tags ?? TagSet.Empty, Array.Empty<Condition>(), per, Source, priority);

    /// <summary>Character phase: adds a modifier to the character pool (seen by every skill).</summary>
    public void AddCharacterModifier(Modifier m) => CharacterMods().Add(m);

    /// <summary>Character phase: removes matching modifiers from the character pool (e.g. an attribute bonus a keystone replaces). Returns the count.</summary>
    public int RemoveCharacterModifiers(Func<Modifier, bool> match) => CharacterMods().RemoveAll(m => match(m));

    /// <summary>Character phase: sets a numeric rule read by combat/simulation code (keys in <see cref="CharacterRules"/>).</summary>
    public void SetRule(string key, double value)
    {
        RequireCharacterPhase();
        _rules![key] = value;
    }

    private List<Modifier> CharacterMods()
    {
        RequireCharacterPhase();
        return _characterMods!;
    }

    private void RequireCharacterPhase()
    {
        if (Skill != null || _characterMods == null || _rules == null)
            throw new InvalidOperationException($"Mechanic '{Effect.MechanicId}': character modifiers and rules can only change in the character phase.");
    }
}

/// <summary>Mutable view of one compiled skill during the skill phase of <see cref="IMechanic.OnCompile"/>.</summary>
public sealed class MechanicSkillDraft
{
    private readonly CompiledSkill _skill;
    private readonly StatEvaluator _characterEvaluator;
    private readonly GameData _data;
    private readonly List<Modifier> _modifiers;
    private readonly List<Conversion> _conversions;
    private readonly HashSet<DamageType> _nullified;

    internal MechanicSkillDraft(CompiledSkill skill, StatEvaluator characterEvaluator, GameData data)
    {
        _skill = skill;
        _characterEvaluator = characterEvaluator;
        _data = data;
        _modifiers = skill.Modifiers.ToList();
        _conversions = skill.Conversions.ToList();
        _nullified = new HashSet<DamageType>(skill.NullifiedDamageTypes);
        ResourceCost = skill.ResourceCost;
        ResourceGain = skill.ResourceGain;
        Cooldown = skill.Base.Cooldown;
        ChannelLimit = skill.ChannelLimit;
    }

    public string SkillId => _skill.SkillId;
    public TagSet Tags => _skill.Tags;
    public SkillBase Base => _skill.Base;
    public bool IsChannel => Tags.Contains("channel");
    public bool IsGenerator => Tags.Contains("generator");

    /// <summary>Base resource cost (channel: per second) and gain; the resource_cost / resource_gen stats still apply on top.</summary>
    public double ResourceCost { get; set; }
    public double ResourceGain { get; set; }

    /// <summary>Base cooldown in seconds (null: none); cooldown_recovery still divides it.</summary>
    public double? Cooldown { get; set; }

    /// <summary>Channel skills only: the channel stops after MaxSec and the skill goes on cooldown.</summary>
    public ChannelLimit? ChannelLimit { get; set; }

    public IReadOnlyList<Modifier> Modifiers => _modifiers;
    public IReadOnlyList<Conversion> Conversions => _conversions;
    public IReadOnlySet<DamageType> NullifiedDamageTypes => _nullified;

    /// <summary>Adds a modifier that applies to this skill only (its hits, ailments, zones and stats).</summary>
    public void AddModifier(Modifier m) => _modifiers.Add(m);

    /// <summary>Adds a rightward conversion (spec 6.3); totals above 100% are scaled down by the resolver.</summary>
    public void AddConversion(DamageType from, DamageType to, double percent, SourceRef source)
    {
        if (to <= from) throw new ArgumentException($"Conversion {from} -> {to} is not rightward.", nameof(to));
        _conversions.Add(new Conversion(from, to, percent, source));
    }

    /// <summary>Hit portions whose final type (after conversion) is <paramref name="type"/> deal nothing and apply no ailments.</summary>
    public void NullifyFinalDamage(DamageType type) => _nullified.Add(type);

    /// <summary>Evaluates a stat for this skill (character pool + the skill's modifiers so far, no conditions). A stat missing from the data yields the base.</summary>
    public double Evaluate(string stat, double? baseValue = null) =>
        _data.Stats.ContainsKey(stat)
            ? _characterEvaluator.WithExtra(_modifiers).Evaluate(stat, Tags, null, baseValue).Value
            : baseValue ?? 0;

    internal CompiledSkill ToCompiled() => _skill with
    {
        Base = _skill.Base with { Cooldown = Cooldown },
        ResourceCost = ResourceCost,
        ResourceGain = ResourceGain,
        Modifiers = _modifiers.ToList(),
        Conversions = _conversions.ToList(),
        ChannelLimit = ChannelLimit,
        NullifiedDamageTypes = new HashSet<DamageType>(_nullified),
    };
}
