using System.Text.Json;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Simulation;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Effects;

/// <summary>
/// Context of the simulator hooks (<see cref="IMechanic.OnUse"/>, OnHit, OnKill, OnTick): the event, the character
/// (life, resource, stats, buffs), per-mechanic counters and the targets. Created per call; state lives in the run.
/// Target indices are 0..TargetCount-1 (0 is the first pack member).
/// </summary>
public sealed class MechanicSimContext
{
    private readonly MechanicSimEnv _env;
    private readonly SimState _s;
    private readonly ProcEngine _procs;
    private readonly SkillRuntime? _skill;
    private readonly SimConditionContext _conditions;
    private readonly SimLog _log;
    private readonly GameData _data;

    internal MechanicSimContext(MechanicSimEnv env, MechanicEffect effect, SourceRef source, SkillRuntime? skill, bool triggered,
        int targetIndex = -1, HitResult? hit = null, bool firstOnPrimary = false, double resourcePaid = 0)
    {
        _env = env;
        _s = env.State;
        _procs = env.Procs;
        _conditions = env.Conditions;
        _log = env.Log;
        _data = env.Data;
        Effect = effect;
        Source = source;
        _skill = skill;
        IsTriggeredCopy = triggered;
        TargetIndex = targetIndex;
        Hit = hit;
        IsFirstHitOnPrimary = firstOnPrimary;
        ResourcePaid = resourcePaid;
    }

    public MechanicEffect Effect { get; }
    public IReadOnlyDictionary<string, JsonElement> Params => Effect.Params;
    public SourceRef Source { get; }
    public double Number(string name, double fallback) => MechanicParams.Number(Effect, name, fallback);
    public string Text(string name, string fallback) => MechanicParams.Text(Effect, name, fallback);
    public IReadOnlyList<string> Texts(string name, params string[] fallback) => MechanicParams.Texts(Effect, name, fallback);

    /// <summary>Builds a modifier stamped with <see cref="Source"/> (for buffs set by the mechanic).</summary>
    public Modifier Mod(string stat, ModKind kind, double value, TagSet? tags = null) =>
        new(stat, kind, value, tags ?? TagSet.Empty, Array.Empty<Condition>(), null, Source);

    // ---- Event ----

    public double Time => _s.Time;
    /// <summary>The skill of the event (use/hit; kill: last damage source). Null for ticks and unattributed kills.</summary>
    public string? SkillId => _skill?.Id;
    public TagSet SkillTags => _skill?.Tags ?? TagSet.Empty;
    /// <summary>The event comes from a proc's triggered copy (or a kill by one).</summary>
    public bool IsTriggeredCopy { get; }
    /// <summary>Hit/kill target index; -1 for use and tick.</summary>
    public int TargetIndex { get; }
    /// <summary>OnHit only: the resolved hit (final damage and ailment base by type).</summary>
    public HitResult? Hit { get; }
    /// <summary>OnHit only: the first hit of the use on the primary target (on_hit procs fire on this one).</summary>
    public bool IsFirstHitOnPrimary { get; }
    /// <summary>OnUse only: resource paid for this use (already deducted).</summary>
    public double ResourcePaid { get; }

    /// <summary>
    /// OnHit only: did this hit crit? Roll mode: the hit's own roll. Average mode has no discrete crits, so this is a chance
    /// accumulator per mechanic, skill and target at the skill's crit chance (N hits give crit chance x N events).
    /// </summary>
    public bool IsCritEvent()
    {
        if (Hit == null || _skill == null) return false;
        if (_env.RollMode) return Hit.IsCrit;
        var chance = _skill.CritChance(_conditions, _env.Weapon) / 100;
        return _env.Random.Chance($"mech:{Effect.MechanicId}:crit:{_skill.ChannelKey}:{TargetIndex}", chance);
    }

    // ---- Character ----

    public double Life => _s.Life;
    public double LifeMax => _s.LifeMax;
    public double Resource => _s.Resource;
    public double ResourceMax => _s.ResourceMax;

    /// <summary>Self-inflicted damage: never lowers life below 1 (spec 5.4).</summary>
    public void DamageSelf(double amount) => _s.DamageSelf(amount);

    /// <summary>Pays a life cost; refused (false, nothing paid) when current life is not above the cost.</summary>
    public bool TryPayLife(double cost) => _s.TryPayLife(cost);

    public void Heal(double amount)
    {
        if (amount > 0) _s.Life = Math.Min(_s.LifeMax, _s.Life + amount);
    }

    /// <summary>Adds (or with a negative delta removes) resource, clamped to [0, ResourceMax].</summary>
    public void AddResource(double delta) => _s.Resource = Math.Clamp(_s.Resource + delta, 0, _s.ResourceMax);

    /// <summary>Evaluates a stat with live conditions and active buffs: for the event's skill when there is one, else for the character.
    /// A stat missing from the data yields the base (or 0).</summary>
    public double Stat(string stat, double? baseValue = null)
    {
        if (_skill != null) return _skill.Stat(stat, _conditions, baseValue);
        return _data.Stats.ContainsKey(stat) ? _procs.Evaluator.Evaluate(stat, default, _conditions, baseValue).Value : baseValue ?? 0;
    }

    /// <summary>The tag an ailment's damage carries (ailments.json <c>tag</c>, e.g. ailment_ignite); unknown id: "ailment_" + id.</summary>
    public string AilmentTag(string ailmentId) => _data.Ailments.TryGetValue(ailmentId, out var def) ? def.Tag : "ailment_" + ailmentId;

    // ---- Counters (charges, stacks...): per run, namespaced by mechanic id ----

    public double Counter(string key = "") => _s.MechanicCounters.GetValueOrDefault(CounterKey(key));
    public void SetCounter(string key, double value) => _s.MechanicCounters[CounterKey(key)] = value;
    /// <summary>A per-run object of this mechanic (a queue, a set...), created on first use; keyed like the counters.</summary>
    public T State<T>(string key) where T : class, new()
    {
        var k = CounterKey(key);
        if (_s.MechanicState.TryGetValue(k, out var o)) return (T)o;
        var created = new T();
        _s.MechanicState[k] = created;
        return created;
    }

    // A skill/tags-scoped mechanic keeps separate counters per skill carrying it.
    private string CounterKey(string key) =>
        Effect.Scope is Scope.Global || _skill == null ? Effect.MechanicId + ":" + key : Effect.MechanicId + ":" + _skill.Id + ":" + key;

    // ---- Buffs: timed modifiers on every skill and the character (re-applying refreshes) ----

    public void ApplyBuff(string buffId, double durationSec, IReadOnlyList<Modifier> mods) => _procs.ApplyBuff(buffId, durationSec, mods);
    public void RemoveBuff(string buffId) => _procs.RemoveBuff(buffId);
    public bool HasBuff(string buffId) => _s.Buffs.TryGetValue(buffId, out var left) && left > 0;

    // ---- Targets ----

    public int TargetCount => _s.Targets.Count;
    /// <summary>Lowest-index living target (-1: every target is dead).</summary>
    public int PrimaryTarget => _s.PrimaryIndex;
    public bool IsAlive(int target) => _s.IsAlive(target);
    public bool HasAilment(int target, string ailmentId) => _s.Targets[target].HasAilment(ailmentId);
    public int AilmentStacks(int target, string ailmentId) => _s.Targets[target].Stacks(ailmentId);
    public IReadOnlyList<AilmentInstance> Ailments(int target) => _s.Targets[target].Ailments;
    public void RemoveAilment(int target, string ailmentId) => _s.Targets[target].RemoveAilment(ailmentId);

    /// <summary>Applies an ailment of the event skill to a target with no chance roll (like a proc). Its base is this hit's
    /// ailment base (none outside OnHit, so only non-damaging ailments apply then). Returns false when nothing was applied.</summary>
    public bool ApplyAilment(int target, string ailmentId)
    {
        if (_skill == null || !_s.IsAlive(target) || !_data.Ailments.TryGetValue(ailmentId, out var def)) return false;
        return _env.Resolver.ForceAilment(_skill, target, def, Hit?.AilmentBase, $"mechanic {Effect.MechanicId}");
    }

    /// <summary>
    /// Lands a copy of an instance that already landed elsewhere (e.g. an ignite spreading over the pack) on a living target,
    /// by the target's stacking rule and the max stacks of the instance's source skill. No chance roll and no mechanic hooks
    /// (the original already went through them). Returns false when the target refused it or the skill is unknown.
    /// </summary>
    public bool LandAilment(int target, AilmentInstance instance)
    {
        var skill = _skill?.Id == instance.SourceSkillId ? _skill : _procs.Skill(instance.SourceSkillId);
        return skill != null && _env.Resolver.LandCopy(skill, target, instance, $"mechanic {Effect.MechanicId}");
    }

    /// <summary>Sets this mechanic's contribution (0 removes it) to a target's "damage taken" Increased bucket (spec 6.6).</summary>
    public void SetTargetDamageTaken(int target, double increasedPercent) =>
        _s.Targets[target].SetDamageTaken(Effect.MechanicId, increasedPercent);

    public void Log(string text) => _log.Add(_s.Time, "mechanic", $"{Effect.MechanicId}: {text}");
}

/// <summary>The run services a <see cref="MechanicSimContext"/> works with.</summary>
internal sealed record MechanicSimEnv(SimState State, ProcEngine Procs, SimConditionContext Conditions, SimLog Log, GameData Data,
    UseResolver Resolver, IRandomSource Random, bool RollMode, WeaponStub? Weapon);
