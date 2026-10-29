using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;

namespace Dialoid.Core.Simulation;

/// <summary>
/// The fixed-step combat engine shared by the offline simulator (<see cref="SimRun"/>) and live play. Each <see cref="Step"/>:
/// the character acts if free (one action at most, chosen by an <see cref="ICommandSource"/>), observers see the state, deaths from
/// hits are handled, then DoTs/zones, the resource, buffs, mechanics and life tick, the dummy attacks (defensive mode), deaths from
/// DoT are handled and the resource/life series is sampled. The caller decides how many steps to run.
/// </summary>
internal sealed class CombatEngine
{
    private const double Eps = 1e-9;

    private readonly CompiledCharacter _character;
    private readonly WeaponStub? _weapon;
    private readonly ResourceDefinition _resource;
    private readonly Dictionary<string, SkillRuntime> _skills;
    private readonly DummyDefinition _dummy;
    private readonly SimConfig _config;
    private readonly SimState _s;
    private readonly SimConditionContext _conditions;
    private readonly SimLog _log = new();
    private readonly DamageLedger _ledger;
    private readonly UseResolver _resolver;
    private readonly ProcEngine _procs;
    private bool _idle;
    private readonly bool _noDecay;
    private readonly bool _infiniteLife;

    // Fight progress: steps taken, and the end of the fight once a kill or the character's death ended it.
    private int _stepsDone;
    private bool _over;
    private double _elapsed;
    private double? _timeToKill;
    private double? _timeToDeath;

    // Defensive mode (spec 11.3): the dummy attacks the character until death or the survival cap.
    private readonly bool _defensive;
    private readonly SortedDictionary<DamageType, double> _damageTaken = new();
    private double _nextAttack;
    private int _attacksReceived, _attacksDodged;
    private readonly SortedDictionary<DamageType, double> _incoming = new();
    private double _mitigatedByArmor, _mitigatedByResist, _lifeRegenerated;

    // Resource/life series for the result: time 0, the first step boundary at or after each SampleIntervalSec, and the end of the run.
    private const double SampleIntervalSec = 0.5;
    private readonly List<ResourceSample> _series = new();
    private double _nextSample = SampleIntervalSec;

    private readonly IRandomSource _random;
    private readonly MechanicRuntime _mechanicHooks;

    public Action<SimState>? StepObserver { get; init; }

    /// <param name="infiniteLife">Targets never die: a target brought to 0 life is refilled to its max life without a kill
    /// (no kill event, no on-kill effects, ailments stay). Damage is accounted as usual.</param>
    public CombatEngine(GameData data, MechanicRegistry mechanics, CompiledCharacter character, WeaponStub? weapon,
        IReadOnlyList<SkillRuntime> skills, DummyDefinition dummy, SimConfig config, bool infiniteLife)
    {
        _defensive = config.DummyAttacks && dummy.Attack is { IntervalSec: > 0 };
        _nextAttack = dummy.Attack?.IntervalSec ?? 0;
        _character = character;
        _weapon = weapon;
        _resource = character.Class.Resource;
        _skills = skills.ToDictionary(s => s.Id, StringComparer.Ordinal);
        _dummy = dummy;
        _config = config;
        _random = RandomSources.Create(config.Mode, config.Seed);

        var pack = config.PackSizeOverride ?? dummy.PackSize;
        _s = new SimState(pack, dummy.LifePerTarget * Growth.Gear(character.Build.Level));
        foreach (var t in _s.Targets) t.FreezeDurationMultiplier = dummy.FreezeDurationMultiplier;
        _conditions = new SimConditionContext(_s);
        _ledger = new DamageLedger(pack);
        _resolver = new UseResolver(_s, dummy, weapon, _random, _conditions, _ledger, _log, data, config.Mode == SimMode.Roll)
        {
            OnHit = OnHit,
        };
        _procs = new ProcEngine(data, character, _skills, _s, _resolver, _log, weapon, mechanics);
        _mechanicHooks = new MechanicRuntime(mechanics, character, skills,
            new MechanicSimEnv(_s, _procs, _conditions, _log, data, _resolver, _random, config.Mode == SimMode.Roll, weapon));
        _resolver.OnAilment = _mechanicHooks.Ailment;
        _resolver.OnAilmentLanded = _mechanicHooks.AilmentLanded;
        _noDecay = character.Rules.GetValueOrDefault(CharacterRules.NoResourceDecay) != 0;

        var ev = character.Evaluator;
        _s.LifeMax = _s.Life = ev.Evaluate("life", default).Value;
        _s.ResourceMax = ev.Evaluate("resource_max", default).Value;
        bool startsEmpty = _resource.RegenPerSec == 0 && _resource.DecayPerSec > 0; // Fury-like resources start at 0
        _s.Resource = startsEmpty ? 0 : _s.ResourceMax;
        _infiniteLife = infiniteLife;
        // Alive at the cap => the cap; a fight ended by killing a non-respawning dummy => null (neither died nor survived the cap).
        _timeToDeath = _defensive ? _config.SurvivalCapSec : null;
        Sample(0);
    }

    /// <summary>Simulated time: the steps taken so far, or the moment the fight ended.</summary>
    public double Time => _over ? _elapsed : _stepsDone * _config.StepSec;

    /// <summary>Length of a full offline run: the survival cap in defensive mode (the dummy attacks), DurationSec otherwise.</summary>
    public double SpanSec => _defensive ? _config.SurvivalCapSec : _config.DurationSec;

    /// <summary>One step of <see cref="SimConfig.StepSec"/>. Returns false when the fight is over: every target dead for good
    /// (never with infinite life) or the character dead; further calls do nothing.</summary>
    public bool Step(ICommandSource commands)
    {
        if (_over) return false;
        var step = _config.StepSec;
        var t = _stepsDone * step;
        _s.Time = t;
        Act(commands, t, step);
        StepObserver?.Invoke(_s);
        if (HandleDeaths(t)) return EndByKill(t);

        TickTargets(step);
        TickResource(t, step);
        TickBuffs(step);
        _mechanicHooks.Tick(step);
        TickLife(step);
        if (_defensive && DummyAttackStep(t, step) is { } deathTime)
        {
            _timeToDeath = _elapsed = deathTime;
            _over = true;
            return false;
        }
        if (HandleDeaths(t + step)) return EndByKill(t + step);
        if (t + step >= _nextSample - Eps)
        {
            Sample(t + step);
            while (_nextSample <= t + step + Eps) _nextSample += SampleIntervalSec;
        }
        _stepsDone++;
        return true;
    }

    private bool EndByKill(double time)
    {
        _timeToKill = _elapsed = time;
        _timeToDeath = null;
        _over = true;
        return false;
    }

    /// <summary>The result of the fight so far; the series ends with a sample at <see cref="Time"/>. Does not change the engine,
    /// so it may be read mid-fight.</summary>
    public SimResult Result()
    {
        var step = _config.StepSec;
        var elapsed = Time;
        var series = _series;
        if (_series[^1].Time < elapsed - Eps) series = new List<ResourceSample>(_series) { new(elapsed, _s.Resource, _s.Life) };

        var defense = _defensive
            ? new DefenseTotals(_attacksReceived, _attacksDodged, new SortedDictionary<DamageType, double>(_incoming),
                _mitigatedByArmor, _mitigatedByResist, _lifeRegenerated)
            : null;
        return ResultBuilder.Build(_ledger, _log, Math.Max(elapsed, step), _timeToKill, _timeToDeath, _damageTaken, defense, series);
    }

    private void Sample(double time) => _series.Add(new ResourceSample(time, _s.Resource, _s.Life));

    private void Act(ICommandSource commands, double t, double step)
    {
        if (t + Eps < _s.BusyUntil) return; // still casting/attacking

        var skill = commands.Next(_s, _conditions, _weapon);
        if (skill == null)
        {
            _s.ChannelSkillId = null;
            if (!_idle && commands.WantsToAct)
                _log.Add(t, "no_resource", $"no usable skill: resource {SimLog.Num(_s.Resource)}/{SimLog.Num(_s.ResourceMax)}");
            _idle = true;
            return;
        }
        _idle = false;

        // A chained action starts exactly when the previous one ended, so use times that are not a multiple of the step do not drift.
        var start = _s.BusyUntil > t - step + Eps ? _s.BusyUntil : t;
        if (skill.IsChannel)
        {
            if (_s.ChannelSkillId != skill.Id || _s.ChannelExhausted)
            {
                _s.ChannelSkillId = skill.Id;
                _s.ChannelStart = start;
                _s.ChannelExhausted = false;
            }
        }
        else _s.ChannelSkillId = null;

        var speed = skill.TickSpeed(_s, start); // channel ramp (whirl_rage)
        var paid = Math.Min(_s.Resource, skill.Cost(_conditions) / speed);
        _s.Resource = Math.Max(0, _s.Resource - paid);
        _s.BusyUntil = start + Math.Max(step, skill.UseTime(_conditions, _weapon, step) / speed);
        var cd = skill.Cooldown(_conditions);
        if (cd > 0) _s.CooldownReadyAt[skill.Id] = start + cd;
        // Channel limit (blood_pact): channel time adds up per skill since its last forced cooldown (other skills used in
        // between do not reset it); the tick that reaches MaxSec ends the channel and starts the cooldown when it ends.
        if (skill.IsChannel && skill.Skill.ChannelLimit is { } limit)
        {
            var used = _s.ChannelTimeUsed.GetValueOrDefault(skill.Id) + (_s.BusyUntil - start);
            if (used >= limit.MaxSec - Eps)
            {
                _s.CooldownReadyAt[skill.Id] = Math.Max(_s.CooldownReadyAt.GetValueOrDefault(skill.Id), _s.BusyUntil + limit.CooldownSec);
                _s.ChannelExhausted = true;
                used = 0;
            }
            _s.ChannelTimeUsed[skill.Id] = used;
        }
        _s.LastUseTime[skill.Id] = t;
        _log.Add(t, "use", skill.Id);
        OnUse(skill, paid);

        var outcome = _resolver.Apply(skill);
        if (outcome.TargetsHit > 0)
        {
            _s.Resource = Math.Min(_s.ResourceMax, _s.Resource + skill.Gain(_conditions)); // once per use (spec 8.4)
            _s.LastHitTime = t;
        }
        if (outcome.PrimaryCrit)
        {
            _s.LastCritTime = t;
            OnCrit(skill);
        }
        _procs.EndUse();
        _procs.Flush();
    }

    private void TickTargets(double dt)
    {
        for (var i = 0; i < _s.Targets.Count; i++)
        {
            if (!_s.IsAlive(i)) continue;
            var target = _s.Targets[i];
            var index = i;
            var dmg = target.Tick(dt, (a, share) =>
            {
                _ledger.AddDot(a.SourceSkillId, index, a.Type, share);
                _procs.NoteDamage(index, a.SourceSkillId, a.Triggered);
            });
            if (!target.Immortal) target.Life -= dmg;
        }

        foreach (var id in _s.Zones.Keys.ToList())
        {
            var zone = _s.Zones[id];
            var skill = _skills[zone.SkillId];
            var span = Math.Min(dt, zone.Remaining);
            foreach (var i in zone.TargetIndices)
            {
                if (!_s.IsAlive(i)) continue;
                var target = _s.Targets[i];
                _conditions.HitTarget = i; // target_* conditions refer to the target in the zone
                var dps = AilmentCalculator.GroundZoneDps(zone.AilmentBase, zone.FractionPerSec, skill.Character, skill.Skill,
                    _resolver.DefenseOf(target), zone.Type, _conditions);
                _conditions.HitTarget = null;
                var share = dps * span;
                _ledger.AddDot(zone.SkillId, i, zone.Type, share);
                _procs.NoteDamage(i, zone.SkillId, zone.Triggered);
                if (!target.Immortal) target.Life -= share;
            }
            var left = zone.Remaining - dt;
            if (left <= Eps) _s.Zones.Remove(id);
            else _s.Zones[id] = zone with { Remaining = left };
        }
    }

    private void TickResource(double t, double dt)
    {
        var r = _s.Resource;
        r += _procs.Evaluator.Evaluate("resource_regen", default, _conditions).Value * dt;
        if (_resource.DecayPerSec > 0 && !_noDecay && (_s.LastHitTime is not { } last || t - last >= _resource.DecayDelaySec - Eps))
            r -= _resource.DecayPerSec * dt;
        _s.Resource = Math.Clamp(r, 0, _s.ResourceMax);
    }

    /// <summary>Life regeneration, every mode (spec 11.3): max life is re-read each step (buffs may change it).
    /// The required stats (life, life_regen_pct, ...) always exist: <see cref="RequiredStats"/> is checked at load.</summary>
    private void TickLife(double dt)
    {
        var ev = _procs.Evaluator;
        _s.LifeMax = ev.Evaluate("life", default, _conditions).Value;
        var regen = ev.Evaluate("life_regen_pct", default, _conditions).Value / 100 * _s.LifeMax;
        var before = _s.Life;
        _s.Life = Math.Min(_s.LifeMax, _s.Life + regen * dt);
        _lifeRegenerated += Math.Max(0, _s.Life - before);
    }

    private void TickBuffs(double dt)
    {
        var expired = false;
        foreach (var id in _s.Buffs.Keys.ToList())
        {
            var left = _s.Buffs[id] - dt;
            if (left <= Eps) { _s.Buffs.Remove(id); expired = true; }
            else _s.Buffs[id] = left;
        }
        if (expired) _procs.RefreshBuffs();
    }

    /// <summary>Logs kills, clears ailments, respawns when the dummy respawns (with infinite life, only refills life).
    /// Returns true when every target is dead for good.</summary>
    private bool HandleDeaths(double time)
    {
        for (var i = 0; i < _s.Targets.Count; i++)
        {
            var target = _s.Targets[i];
            if (!_s.IsAlive(i) || target.Immortal || target.Life > 0) continue;
            if (_infiniteLife) { target.Life = target.MaxLife; continue; }
            _log.Add(time, "kill", $"#{i}");
            OnKill(i);
            target.Reset();
            if (!_dummy.Respawn) _s.Dead[i] = true;
        }
        _procs.Flush();
        return !_dummy.Respawn && _s.PrimaryIndex < 0;
    }

    // ---- Hook points. Procs are handled by ProcEngine (Task 14); the dummy's attacks (defensive mode) come in Task 15. ----

    /// <summary>Mechanic OnUse, then on_use / every_n_uses procs.</summary>
    private void OnUse(SkillRuntime skill, double resourcePaid)
    {
        _mechanicHooks.Use(skill, resourcePaid);
        _procs.OnUse(skill);
    }

    /// <summary>Mechanic OnHit, then on_hit / every_n_hits procs (on_hit fires once per use, for the first hit on the primary target).</summary>
    private void OnHit(SkillRuntime skill, int targetIndex, HitResult hit, bool firstOnPrimary)
    {
        _mechanicHooks.Hit(skill, targetIndex, hit, firstOnPrimary);
        _procs.OnHit(skill, targetIndex, hit, firstOnPrimary);
    }

    /// <summary>on_crit procs (once per use, primary target).</summary>
    private void OnCrit(SkillRuntime skill) => _procs.OnCrit(skill);

    /// <summary>Mechanic OnKill (the dying target still has its ailments), then on_kill procs. Never fires for immortal dummies.</summary>
    private void OnKill(int targetIndex)
    {
        var src = _procs.LastSource(targetIndex);
        var skill = src is { } k && _skills.TryGetValue(k.Skill, out var sk) ? sk : null;
        _mechanicHooks.Kill(targetIndex, skill, src?.Triggered ?? false);
        _procs.OnKill(targetIndex);
    }

    /// <summary>
    /// One step of defensive mode: every dummy attack falling in (t, t + dt] (the first after one interval;
    /// skipped while the attacking target is frozen or dead). Returns the time of death, or null when the character survives the step.
    /// </summary>
    private double? DummyAttackStep(double t, double dt)
    {
        // Attack-time character state is read at the end of the step (at most one step of error).
        var attack = _dummy.Attack!;

        var end = t + dt;
        while (_nextAttack <= end + Eps)
        {
            var at = _nextAttack;
            _nextAttack += attack.IntervalSec;
            if (!_s.IsAlive(0) || _s.Targets[0].IsFrozen) continue;
            if (ReceiveAttack(attack, at)) return at;
        }
        return null;
    }

    /// <summary>One dummy hit: dodge first, then per-type mitigation. Returns true when the character dies.</summary>
    private bool ReceiveAttack(DummyAttack attack, double at)
    {
        var ev = _procs.Evaluator;
        var gear = Growth.Gear(_character.Build.Level);

        var dodge = ev.Evaluate("dodge_chance", default, _conditions).Value;
        var dodgeMax = ev.Evaluate("dodge_chance_max", default, _conditions).Value;
        var p = Math.Clamp(Math.Min(dodge, dodgeMax), 0, 100) / 100;
        _log.Add(at, "dummy_attack", $"interval {SimLog.Num(attack.IntervalSec)}");
        _attacksReceived++;
        if (_random.Chance("dodge", p))
        {
            _attacksDodged++;
            _log.Add(at, "dodge", "attack dodged");
            return false;
        }

        // Character damage_taken (percentage points, e.g. a risk node's +10%) applies after mitigation.
        var takenMult = 1 + Math.Max(-100, ev.Evaluate("damage_taken", default, _conditions).Value) / 100;

        // Armor applies to physical hits; a mechanic rule can make a share of it mitigate elemental hits too (stone_skin).
        var armor = ev.Evaluate("armor", default, _conditions).Value;
        var elementalArmor = _character.Rules.GetValueOrDefault(CharacterRules.ElementalArmorFraction);

        double total = 0;
        foreach (var type in Enum.GetValues<DamageType>())
        {
            if (!attack.DamagePerHit.TryGetValue(type, out var raw) || raw <= 0) continue;
            var hit = raw * gear;
            _incoming[type] = _incoming.GetValueOrDefault(type) + hit;
            double taken;
            if (type == DamageType.Physical)
            {
                taken = hit * (1 - Mitigation.ArmorReduction(armor, hit));
                _mitigatedByArmor += hit - taken;
            }
            else
            {
                var name = type.ToString().ToLowerInvariant();
                var resist = ev.Evaluate("resist_" + name, default, _conditions).Value;
                var cap = ev.Evaluate("resist_" + name + "_max", default, _conditions).Value;
                taken = hit * Mitigation.ResistMultiplier(Math.Min(resist, cap), 0);
                _mitigatedByResist += hit - taken;
                if (elementalArmor > 0)
                {
                    var afterResist = taken;
                    taken *= 1 - Mitigation.ArmorReduction(armor * elementalArmor, hit);
                    _mitigatedByArmor += afterResist - taken;
                }
            }
            if (taken <= 0) continue;
            taken *= takenMult;
            _damageTaken[type] = _damageTaken.GetValueOrDefault(type) + taken;
            total += taken;
        }

        _s.Life -= total;
        if (_s.Life > 0) return false;
        _s.Life = 0;
        _log.Add(at, "death", "character died");
        return true;
    }
}
