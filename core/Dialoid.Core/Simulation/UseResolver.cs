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
            var target = _s.Targets[ti];
            for (var h = 0; h < Math.Max(1, skill.Skill.Base.HitsPerUse); h++)
            {
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

                bool first = ti == primary && h == 0;
                if (first) { primaryHit = hit; primaryCrit = isCrit; }
                OnHit?.Invoke(skill, ti, hit, first);
                ApplyAilments(skill, ti, target, hit, defense);
            }
        }

        // Average mode has no discrete crits: a crit "event" (recently_crit, on_crit) goes through the chance accumulator.
        var critEvent = _rollMode ? primaryCrit == true : _random.Chance($"critevent:{skill.ChannelKey}", critChance / 100);
        if (primaryHit != null) MakeZone(skill, primaryHit, targets);
        return new UseOutcome(targets.Count, critEvent, targets);
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
            var ailBase = hit.AilmentBase.GetValueOrDefault(def.SourceType);
            if (def.Id == "poison" && def.SourceType != DamageType.Physical)
                ailBase += hit.AilmentBase.GetValueOrDefault(DamageType.Physical); // spec 6.7: poison from poison and physical
            if (ailBase <= 0) continue;

            // The skill's own chance is the stat's base (on top of the stat default), so Increased/More/Less scale it too.
            var statDefault = _data.Stats.TryGetValue(def.ChanceStat, out var sd) ? sd.Default : 0;
            var chance = skill.Stat(def.ChanceStat, _conditions, skill.Skill.Base.AilmentChances.GetValueOrDefault(def.Id) + statDefault);
            if (chance <= 0 || !_random.Chance($"ail:{def.Id}:{skill.ChannelKey}:{ti}", chance / 100)) continue;

            var instance = AilmentCalculator.MakeInstance(def, ailBase, skill.Character, skill.Skill, defense, _conditions)
                with { Triggered = skill.IsTriggeredCopy };
            Land(skill, ti, target, def, instance);
            _log.Add(_s.Time, "ailment", $"{def.Id} -> #{ti} ({skill.Id})");
        }
    }

    /// <summary>Mechanic hook, then the target's stacking rule. Max stacks: the stat (its data default as the base: bleed 8), 1 without the stat.</summary>
    private void Land(SkillRuntime skill, int ti, TargetState target, AilmentDefinition def, AilmentInstance instance)
    {
        if (OnAilment != null) instance = OnAilment(skill, ti, instance);
        var stackBase = _data.Stats.TryGetValue(def.MaxStacksStat, out var ms) ? ms.Default : 1;
        var maxStacks = (int)Math.Clamp(skill.Stat(def.MaxStacksStat, _conditions, stackBase), 0, int.MaxValue);
        target.Apply(instance, maxStacks, def.StackRule);
    }

    /// <summary>Proc or mechanic ailment (spec 8.4): no chance roll. The base comes from the triggering hit; a damaging ailment with no base is skipped.</summary>
    public bool ForceAilment(SkillRuntime skill, int ti, AilmentDefinition def, IReadOnlyDictionary<DamageType, double>? hitBase,
        string origin = "proc")
    {
        double ailBase = 0;
        if (hitBase != null)
        {
            ailBase = hitBase.GetValueOrDefault(def.SourceType);
            if (def.Id == "poison" && def.SourceType != DamageType.Physical)
                ailBase += hitBase.GetValueOrDefault(DamageType.Physical);
        }
        if (ailBase <= 0 && def.DamageFraction > 0) return false;

        var target = _s.Targets[ti];
        var instance = AilmentCalculator.MakeInstance(def, ailBase, skill.Character, skill.Skill, DefenseOf(target), _conditions)
            with { Triggered = skill.IsTriggeredCopy };
        Land(skill, ti, target, def, instance);
        _log.Add(_s.Time, "ailment", $"{def.Id} -> #{ti} ({skill.Id}, {origin})");
        return true;
    }

    /// <summary>Behavior ground_duration/ground_fraction: one zone per skill, refreshed by each use, covering the same targets as the hit.
    /// Its base is the primary hit's total ailment base; its damage type is the largest share of that base.</summary>
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
        _s.Zones[skill.Id] = new GroundZone(skill.Id, type, total, fraction, duration, targets.ToList())
        {
            Triggered = skill.IsTriggeredCopy,
        };
    }
}
