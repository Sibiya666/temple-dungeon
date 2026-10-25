using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;

namespace Dialoid.Core.Simulation;

/// <summary>
/// One simulation run. Each fixed step: the character acts if free (one action at most), observers see the state,
/// deaths from hits are handled, then DoTs/zones tick, the resource regenerates/decays and deaths from DoT are handled.
/// The loop runs at most DurationSec / StepSec steps, so it always terminates.
/// </summary>
internal sealed class SimRun
{
    private const double Eps = 1e-9;

    private readonly CompiledCharacter _character;
    private readonly WeaponStub? _weapon;
    private readonly ResourceDefinition _resource;
    private readonly bool _regenAvailable;
    private readonly IReadOnlyDictionary<string, SkillRuntime> _skills;
    private readonly Rotation _rotation;
    private readonly DummyDefinition _dummy;
    private readonly SimConfig _config;
    private readonly SimState _s;
    private readonly SimConditionContext _conditions;
    private readonly SimLog _log = new();
    private readonly DamageLedger _ledger;
    private readonly UseResolver _resolver;
    private bool _idle;

    private readonly IRandomSource _random;
    private readonly MechanicRegistry _mechanics; // for mechanic hooks (Tasks 14–16)

    public Action<SimState>? StepObserver { get; init; }

    public SimRun(GameData data, MechanicRegistry mechanics, CompiledCharacter character, WeaponStub? weapon,
        IReadOnlyList<SkillRuntime> skills, Rotation rotation, DummyDefinition dummy, SimConfig config)
    {
        _mechanics = mechanics;
        _character = character;
        _weapon = weapon;
        _regenAvailable = data.Stats.ContainsKey("resource_regen");
        _resource = character.Class.Resource;
        _skills = skills.ToDictionary(s => s.Id, StringComparer.Ordinal);
        _rotation = rotation;
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

        var ev = character.Evaluator;
        _s.LifeMax = _s.Life = data.Stats.ContainsKey("life") ? ev.Evaluate("life", default).Value : 0;
        _s.ResourceMax = ev.Evaluate("resource_max", default).Value;
        bool startsEmpty = _resource.RegenPerSec == 0 && _resource.DecayPerSec > 0; // Fury-like resources start at 0
        _s.Resource = startsEmpty ? 0 : _s.ResourceMax;
    }

    public SimResult Execute()
    {
        var step = _config.StepSec;
        var steps = Math.Max(1, (int)Math.Round(_config.DurationSec / step));
        double elapsed = steps * step;
        double? timeToKill = null;

        for (var i = 0; i < steps; i++)
        {
            var t = i * step;
            _s.Time = t;
            Act(t, step);
            StepObserver?.Invoke(_s);
            if (HandleDeaths(t)) { timeToKill = elapsed = t; break; }

            TickTargets(step);
            TickResource(t, step);
            TickBuffs(step);
            if (_config.DummyAttacks) DummyAttackStep(t, step);
            if (HandleDeaths(t + step)) { timeToKill = elapsed = t + step; break; }
        }

        return ResultBuilder.Build(_ledger, _log, Math.Max(elapsed, step), timeToKill, timeToDeath: null);
    }

    private void Act(double t, double step)
    {
        if (t + Eps < _s.BusyUntil) return; // still casting/attacking

        var skill = _rotation.Pick(_s, _conditions, _weapon);
        if (skill == null)
        {
            _s.ChannelSkillId = null;
            if (!_idle && !_rotation.IsEmpty)
                _log.Add(t, "no_resource", $"no usable skill: resource {SimLog.Num(_s.Resource)}/{SimLog.Num(_s.ResourceMax)}");
            _idle = true;
            return;
        }
        _idle = false;

        // A chained action starts exactly when the previous one ended, so use times that are not a multiple of the step do not drift.
        var start = _s.BusyUntil > t - step + Eps ? _s.BusyUntil : t;
        if (skill.IsChannel)
        {
            if (_s.ChannelSkillId != skill.Id) { _s.ChannelSkillId = skill.Id; _s.ChannelStart = start; }
        }
        else _s.ChannelSkillId = null;

        _s.Resource = Math.Max(0, _s.Resource - skill.Cost(_conditions));
        _s.BusyUntil = start + skill.UseTime(_conditions, _weapon, step);
        var cd = skill.Cooldown(_conditions);
        if (cd > 0) _s.CooldownReadyAt[skill.Id] = start + cd;
        _s.LastUseTime[skill.Id] = t;
        _log.Add(t, "use", skill.Id);
        OnUse(skill);

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
    }

    private void TickTargets(double dt)
    {
        for (var i = 0; i < _s.Targets.Count; i++)
        {
            if (!_s.IsAlive(i)) continue;
            var target = _s.Targets[i];
            var index = i;
            var dmg = target.Tick(dt, (a, share) => _ledger.AddDot(a.SourceSkillId, index, a.Type, share));
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
                var dps = AilmentCalculator.GroundZoneDps(zone.AilmentBase, zone.FractionPerSec, skill.Character, skill.Skill,
                    _resolver.DefenseOf(target), zone.Type, _conditions);
                var share = dps * span;
                _ledger.AddDot(zone.SkillId, i, zone.Type, share);
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
        if (_regenAvailable) r += _character.Evaluator.Evaluate("resource_regen", default, _conditions).Value * dt;
        if (_resource.DecayPerSec > 0 && (_s.LastHitTime is not { } last || t - last >= _resource.DecayDelaySec - Eps))
            r -= _resource.DecayPerSec * dt;
        _s.Resource = Math.Clamp(r, 0, _s.ResourceMax);
    }

    private void TickBuffs(double dt)
    {
        foreach (var id in _s.Buffs.Keys.ToList())
        {
            var left = _s.Buffs[id] - dt;
            if (left <= Eps) _s.Buffs.Remove(id);
            else _s.Buffs[id] = left;
        }
    }

    /// <summary>Logs kills, clears ailments, respawns when the dummy respawns. Returns true when every target is dead for good.</summary>
    private bool HandleDeaths(double time)
    {
        for (var i = 0; i < _s.Targets.Count; i++)
        {
            var target = _s.Targets[i];
            if (!_s.IsAlive(i) || target.Immortal || target.Life > 0) continue;
            _log.Add(time, "kill", $"#{i}");
            OnKill(i);
            target.Reset();
            if (!_dummy.Respawn) _s.Dead[i] = true;
        }
        return !_dummy.Respawn && _s.PrimaryIndex < 0;
    }

    // ---- Hook points. Procs/triggers arrive in Task 14, the dummy's attacks (defensive mode) in Task 15. ----

    /// <summary>Task 14: on_use / every_n_uses procs.</summary>
    private void OnUse(SkillRuntime skill) { }

    /// <summary>Task 14: on_hit / every_n_hits procs (on_hit fires once per use, for the first hit on the primary target).</summary>
    private void OnHit(SkillRuntime skill, int targetIndex, HitResult hit, bool firstOnPrimary) { }

    /// <summary>Task 14: on_crit procs (once per use, primary target).</summary>
    private void OnCrit(SkillRuntime skill) { }

    /// <summary>Task 14: on_kill procs and "on death" mechanics. Never fires for immortal dummies.</summary>
    private void OnKill(int targetIndex) { }

    /// <summary>Task 15: the dummy attacks the character (uses _dummy.Attack, freeze skips attacks, TimeToDeath, SurvivalCapSec).</summary>
    private void DummyAttackStep(double t, double dt) { }
}
