using System.Text.Json;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Effects;

/// <summary>
/// Context of the simulator hooks (<see cref="IMechanic.OnUse"/>, OnHit, OnKill, OnTick): the event, the character
/// (life, resource, stats, buffs), per-mechanic counters and the targets. Created per call; state lives in the run.
/// Target indices are 0..TargetCount-1 (0 is the first pack member).
/// </summary>
public sealed class MechanicSimContext
{
    private readonly SimState _s;
    private readonly ProcEngine _procs;
    private readonly SkillRuntime? _skill;
    private readonly SimConditionContext _conditions;
    private readonly SimLog _log;
    private readonly GameData _data;

    internal MechanicSimContext(SimState s, ProcEngine procs, SimConditionContext conditions, SimLog log, GameData data,
        MechanicEffect effect, SourceRef source, SkillRuntime? skill, bool triggered, int targetIndex = -1, HitResult? hit = null,
        bool firstOnPrimary = false, double resourcePaid = 0)
    {
        _s = s;
        _procs = procs;
        _conditions = conditions;
        _log = log;
        _data = data;
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

    // ---- Counters (charges, stacks...): per run, namespaced by mechanic id ----

    public double Counter(string key = "") => _s.MechanicCounters.GetValueOrDefault(CounterKey(key));
    public void SetCounter(string key, double value) => _s.MechanicCounters[CounterKey(key)] = value;
    private string CounterKey(string key) => Effect.MechanicId + ":" + key;

    // ---- Buffs: timed modifiers on every skill and the character (re-applying refreshes) ----

    public void ApplyBuff(string buffId, double durationSec, IReadOnlyList<Modifier> mods) => _procs.ApplyBuff(buffId, durationSec, mods);
    public void RemoveBuff(string buffId) => _procs.RemoveBuff(buffId);
    public bool HasBuff(string buffId) => _s.Buffs.TryGetValue(buffId, out var left) && left > 0;

    // ---- Targets ----

    public int TargetCount => _s.Targets.Count;
    public bool IsAlive(int target) => _s.IsAlive(target);
    public bool HasAilment(int target, string ailmentId) => _s.Targets[target].HasAilment(ailmentId);
    public int AilmentStacks(int target, string ailmentId) => _s.Targets[target].Stacks(ailmentId);
    public IReadOnlyList<AilmentInstance> Ailments(int target) => _s.Targets[target].Ailments;
    public void RemoveAilment(int target, string ailmentId) => _s.Targets[target].RemoveAilment(ailmentId);

    /// <summary>Sets this mechanic's contribution (0 removes it) to a target's "damage taken" Increased bucket (spec 6.6).</summary>
    public void SetTargetDamageTaken(int target, double increasedPercent) =>
        _s.Targets[target].SetDamageTaken(Effect.MechanicId, increasedPercent);

    public void Log(string text) => _log.Add(_s.Time, "mechanic", $"{Effect.MechanicId}: {text}");
}
