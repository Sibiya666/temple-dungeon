using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Skills;

namespace Dialoid.Core.Simulation;

/// <summary>What one use did: targets hit and whether a crit event happened on the primary target.</summary>
internal readonly record struct UseOutcome(int TargetsHit, bool PrimaryCrit, IReadOnlyList<int> Targets);

/// <summary>
/// Turns one use of a skill into hits (spec 11.2): picks the covered targets, resolves every hit through
/// <see cref="HitCalculator"/>, applies ailments and creates/refreshes the skill's ground zone. All hits of a use land at
/// the moment of use (time spread inside a use is ignored — documented approximation).
/// </summary>
internal sealed class UseResolver
{
    private readonly SimState _s;
    private readonly DummyDefinition _dummy;
    private readonly WeaponStub? _weapon;
    private readonly IRandomSource _random;
    private readonly SimConditionContext _conditions;
    private readonly DamageLedger _ledger;
    private readonly SimLog _log;
    private readonly IReadOnlyList<AilmentDefinition> _ailments;
    private readonly GameData _data;
    private readonly bool _rollMode;
    private readonly Dictionary<string, int> _cursor = new(StringComparer.Ordinal);

    /// <summary>Called for every hit: (skill, target index, result, is the first hit on the primary target). Proc hook point.</summary>
    public Action<SkillRuntime, int, HitResult, bool>? OnHit;

    /// <summary>Called before an ailment instance lands: (skill, target index, instance) → instance to apply. Mechanic hook point.</summary>
    public Func<SkillRuntime, int, AilmentInstance, AilmentInstance>? OnAilment;

    /// <summary>Called after an ailment instance actually landed (the target's stacking rule accepted it). Mechanic hook point.</summary>
    public Action<SkillRuntime, int, AilmentInstance>? OnAilmentLanded;

    public UseResolver(SimState s, DummyDefinition dummy, WeaponStub? weapon, IRandomSource random, SimConditionContext conditions,
        DamageLedger ledger, SimLog log, GameData data, bool rollMode)
    {
        _s = s;
        _dummy = dummy;
        _weapon = weapon;
        _random = random;
        _conditions = conditions;
        _ledger = ledger;
        _log = log;
        _rollMode = rollMode;
        _data = data;
        _ailments = data.Ailments.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
    }

    public TargetDefense DefenseOf(TargetState t) => new(_dummy.Armor, _dummy.Resists, t.DamageTakenIncreased);

    public UseOutcome Apply(SkillRuntime skill)
    {
        var primary = _s.PrimaryIndex;
        if (primary < 0) return new UseOutcome(0, false, Array.Empty<int>());

        var targets = PickTargets(skill, primary);
        var critChance = skill.CritChance(_conditions, _weapon);
        HitResult? primaryHit = null;
        bool? primaryCrit = null;

        foreach (var ti in targets)
        {
            for (var h = 0; h < Math.Max(1, skill.Skill.Base.HitsPerUse); h++)
            {
                // target_* conditions refer to this target while its hit, hooks and ailments are resolved.
                var outer = _conditions.HitTarget;
                _conditions.HitTarget = ti;
                try
                {
                    bool first = ti == primary && h == 0;
                    var (hit, isCrit) = ResolveHit(skill, ti, critChance, first);
                    if (first) { primaryHit = hit; primaryCrit = isCrit; }
                }
                finally { _conditions.HitTarget = outer; }
            }
        }

        // Average mode has no discrete crits: a crit "event" (recently_crit, on_crit) goes through the chance accumulator.
        var critEvent = _rollMode ? primaryCrit == true : _random.Chance($"critevent:{skill.ChannelKey}", critChance / 100);
        if (primaryHit != null) MakeZone(skill, primaryHit, targets);
        return new UseOutcome(targets.Count, critEvent, targets);
    }

    /// <summary>One hit on one target: damage, log, the hit hook, then the hit's ailments.</summary>
    private (HitResult Hit, bool? IsCrit) ResolveHit(SkillRuntime skill, int ti, double critChance, bool first)
    {
        var target = _s.Targets[ti];
        var isCrit = _random.CritOrExpected($"crit:{skill.ChannelKey}", critChance);
        var defense = DefenseOf(target);
        var hit = HitCalculator.Resolve(new HitInput(skill.Character, skill.Skill, defense, _conditions, isCrit,
            _random.InRange, _weapon));
        double total = 0;
        foreach (var (type, amount) in hit.Final)
        {
            _ledger.AddHit(skill.Id, ti, type, amount);
            total += amount;
        }
        if (!target.Immortal) target.Life -= total;
        _log.Add(_s.Time, "hit", $"{skill.Id} -> #{ti}: {SimLog.Num(total)}");
        if (isCrit == true) _log.Add(_s.Time, "crit", $"{skill.Id} -> #{ti}");

        OnHit?.Invoke(skill, ti, hit, first);
        ApplyAilments(skill, ti, target, hit, defense);
        return (hit, isCrit);
    }

    /// <summary>Primary target plus floor(C−1) others; the fractional part goes through the chance channel cover:&lt;skill&gt;
    /// (Average: accumulator, Roll: one more target with that probability). Others are taken round-robin among the living.</summary>
    private List<int> PickTargets(SkillRuntime skill, int primary)
    {
        var result = new List<int> { primary };
        var n = _s.Targets.Count;
        var extra = Math.Max(0, skill.Coverage(n, _dummy.PackRadius, _conditions) - 1);
        var count = (int)Math.Floor(extra + 1e-9);
        var frac = extra - count;
        if (frac > 1e-9 && _random.Chance($"cover:{skill.ChannelKey}", frac)) count++;
        if (count == 0) return result;

        var cursor = _cursor.GetValueOrDefault(skill.ChannelKey, primary);
        for (var step = 1; step <= n && result.Count <= count; step++)
        {
            var i = (cursor + step) % n;
            if (i == primary || !_s.IsAlive(i)) continue;
            result.Add(i);
            _cursor[skill.ChannelKey] = i;
        }
        return result;
    }

    private void ApplyAilments(SkillRuntime skill, int ti, TargetState target, HitResult hit, TargetDefense defense)
    {
        foreach (var def in _ailments)
        {
            var ailBase = SkillMath.AilmentBase(hit.AilmentBase, def);
            if (ailBase <= 0) continue;

            // ailments.json only names existing stats
            var chance = skill.Stat(def.ChanceStat, _conditions, SkillMath.AilmentChanceBase(_data, def, skill.Skill));
            if (chance <= 0 || !_random.Chance($"ail:{def.Id}:{skill.ChannelKey}:{ti}", chance / 100)) continue;

            var instance = AilmentCalculator.MakeInstance(def, ailBase, skill.Character, skill.Skill, defense, _conditions)
                with { Triggered = skill.IsTriggeredCopy };
            Land(skill, ti, target, def, instance);
            _log.Add(_s.Time, "ailment", $"{def.Id} -> #{ti} ({skill.Id})");
        }
    }

    /// <summary>Pre-apply mechanic hook, then the target's stacking rule, then (if it landed) the post-apply hook.</summary>
    private bool Land(SkillRuntime skill, int ti, TargetState target, AilmentDefinition def, AilmentInstance instance)
    {
        if (OnAilment != null) instance = OnAilment(skill, ti, instance);
        if (!target.Apply(instance, MaxStacks(skill, def), def.StackRule)) return false;
        OnAilmentLanded?.Invoke(skill, ti, instance);
        return true;
    }

    /// <summary>Max stacks: the stat, with its data default as the base (bleed 8).</summary>
    private int MaxStacks(SkillRuntime skill, AilmentDefinition def)
    {
        var stackBase = _data.Stats[def.MaxStacksStat].Default;
        return (int)Math.Clamp(skill.Stat(def.MaxStacksStat, _conditions, stackBase), 0, int.MaxValue);
    }

    /// <summary>
    /// Lands a copy of an instance that already landed elsewhere (spread_ignite) on target <paramref name="ti"/>: the
    /// target's stacking rule with the skill's max stacks, and no mechanic hooks (the original went through them).
    /// Returns false when the target refused it.
    /// </summary>
    public bool LandCopy(SkillRuntime skill, int ti, AilmentInstance instance, string origin)
    {
        if (!_s.IsAlive(ti) || !_data.Ailments.TryGetValue(instance.AilmentId, out var def)) return false;
        if (!_s.Targets[ti].Apply(instance, MaxStacks(skill, def), def.StackRule)) return false;
        _log.Add(_s.Time, "ailment", $"{def.Id} -> #{ti} ({skill.Id}, {origin})");
        return true;
    }

    /// <summary>Proc or mechanic ailment (spec 8.4): no chance roll. The base comes from the triggering hit; a damaging ailment with no base is skipped.</summary>
    public bool ForceAilment(SkillRuntime skill, int ti, AilmentDefinition def, IReadOnlyDictionary<DamageType, double>? hitBase,
        string origin = "proc")
    {
        var ailBase = hitBase != null ? SkillMath.AilmentBase(hitBase, def) : 0;
        if (ailBase <= 0 && def.DamageFraction > 0) return false;

        var target = _s.Targets[ti];
        var outer = _conditions.HitTarget;
        _conditions.HitTarget = ti;
        try
        {
            var instance = AilmentCalculator.MakeInstance(def, ailBase, skill.Character, skill.Skill, DefenseOf(target), _conditions)
                with { Triggered = skill.IsTriggeredCopy };
            Land(skill, ti, target, def, instance);
        }
        finally { _conditions.HitTarget = outer; }
        _log.Add(_s.Time, "ailment", $"{def.Id} -> #{ti} ({skill.Id}, {origin})");
        return true;
    }

    /// <summary>Behavior ground_duration/ground_fraction: one zone per skill, refreshed by each use, covering the same targets as the hit.
    /// Its base is the primary hit's total ailment base; its damage type is the skill's GroundDamageType, else the largest share of that base.</summary>
    private void MakeZone(SkillRuntime skill, HitResult hit, IReadOnlyList<int> targets)
    {
        var b = skill.Skill.Behavior;
        if (!b.TryGetValue("ground_duration", out var duration) || duration <= 0) return;
        if (!b.TryGetValue("ground_fraction", out var fraction) || fraction <= 0) return;
        double total = 0, best = -1;
        var type = DamageType.Fire;
        foreach (var (t, v) in hit.AilmentBase)
        {
            total += v;
            if (v > best) { best = v; type = t; }
        }
        if (total <= 0) return;
        type = skill.Skill.GroundDamageType ?? type;
        _s.Zones[skill.Id] = new GroundZone(skill.Id, type, total, fraction, duration, targets.ToList())
        {
            Triggered = skill.IsTriggeredCopy,
        };
    }
}
