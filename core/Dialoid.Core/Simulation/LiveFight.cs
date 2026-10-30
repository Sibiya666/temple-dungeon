using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Explain;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Simulation;

/// <summary>
/// A fight played in real time (training room, spec 6.2): the simulator's <see cref="CombatEngine"/>, but the skill to use comes
/// from key presses on slots 1–5 instead of the rotation, so the numbers follow the same rules by construction. The game layer
/// calls <see cref="Step"/> once per <see cref="StepSec"/> of play and sends presses between steps. Single-threaded.
/// </summary>
public sealed class LiveFight
{
    public const double StepSec = 0.05;
    /// <summary>A press while the character is busy is remembered when the character is free within this time (spec 6.2c).</summary>
    public const double QueueWindowSec = 0.4;

    private const double Eps = 1e-9;

    private readonly CombatEngine _engine;
    private readonly PlayerCommands _commands = new();

    /// <summary>A live fight's log never stops; it keeps this many recent events (up to twice as many) for <see cref="Snapshot"/>
    /// and as many undrained ones for <see cref="DrainEvents"/>, so an endless fight uses bounded memory.</summary>
    internal const int LogKeep = SimLog.Cap;

    /// <summary>Undrained damage events kept (up to twice as many) for <see cref="DrainDamage"/>, so memory stays bounded.</summary>
    internal const int DamageKeep = 10_000;

    /// <summary>With infinite life, a preset immortal in the data gets this preset's life per target (× G(level)) instead, so
    /// its life bar drops and refills and "target below X %" behaves as on it (owner decision 2026-10-30).</summary>
    internal const string ImmortalStandInDummyId = "boss";

    /// <summary>The mechanic counters (keys "mechanic_id:key" of global mechanics) shown as stacks in
    /// <see cref="LiveState.HeroEffects"/>; every other counter is bookkeeping (soul_heat's use counter, static_charge's flags...).
    /// The sandbox names each of them in Russian. Read-only (not a castable array).</summary>
    public static IReadOnlyList<string> ShownCounters { get; } = Array.AsReadOnly(new[] { "static_charge:charges" });

    private LiveState? _state; // cached until the next step or command

    internal FightSetup Setup { get; }

    /// <summary>The engine's live state (tests read the busy time and adjust the resource, so reading it drops the cached state).</summary>
    internal SimState Sim
    {
        get
        {
            _state = null;
            return _engine.State;
        }
    }

    /// <summary>One entry per filled fight slot, in slot order (for the skill bar's tooltips).</summary>
    public IReadOnlyList<SlotInfo> Slots { get; }

    internal LiveFight(FightSetup setup, LiveFightConfig config)
    {
        Setup = setup;
        var life = config.InfiniteLife && setup.Dummy.Immortal
                   && setup.Data.Dummies.TryGetValue(ImmortalStandInDummyId, out var standIn) && !standIn.Immortal
            ? standIn.LifePerTarget
            : (double?)null;
        _engine = setup.Engine(ToSimConfig(config), config.InfiniteLife, log: SimLog.Rolling(LogKeep), damageKeep: DamageKeep,
            lifePerTarget: life);
        Slots = setup.Skills.Select((s, i) => Describe(i + 1, s)).ToList();
    }

    private SlotInfo Describe(int slot, SkillRuntime skill)
    {
        var c = NoConditions.Instance;
        double Stat(string stat, double b) => skill.Stat(stat, c, b);
        var name = Setup.Data.Skills.TryGetValue(skill.Id, out var def) ? def.Name : skill.Id;
        var perSec = skill.IsChannel ? Math.Max(0, Stat("resource_cost", skill.Skill.ResourceCost)) : 0;
        return new SlotInfo(slot, skill.Id, name, skill.Tags, skill.Cost(c), perSec, skill.Cooldown(c),
            SkillMath.UseTime(skill.Skill, _engine.Weapon, Stat), skill.IsChannel);
    }

    /// <summary>
    /// Starts a fight after the same checks as <see cref="Simulator.Run"/> (build rules, dummy id, pack size). Returns null with
    /// the violations when the build or the config is illegal.
    /// </summary>
    public static LiveFight? Start(GameData data, MechanicRegistry mechanics, Build build, LiveFightConfig config,
        out IReadOnlyList<RuleViolation> violations)
    {
        var setup = FightSetup.Prepare(data, mechanics, build, ToSimConfig(config), out violations);
        return setup == null ? null : new LiveFight(setup, config);
    }

    // The dummy never attacks back in the training room, so the fight is never in defensive mode and DurationSec only has to
    // pass the simulator's config checks: a live fight lasts as long as the game layer keeps stepping it.
    private static SimConfig ToSimConfig(LiveFightConfig c) =>
        new(c.DummyId, c.Mode, c.Seed, StepSec: StepSec, PackSizeOverride: c.PackSizeOverride);

    /// <summary>Tags of the compiled skill in a slot (1-based; tree nodes may change them), or null for an empty slot.</summary>
    public TagSet? CompiledTags(int slot) => SkillAt(slot)?.Tags;

    /// <summary>
    /// A key press on a slot (1-based). Checked now with the rotation's checks; while the character is busy the press is queued
    /// when the character is free within <see cref="QueueWindowSec"/> (its cooldown must be ready by then), otherwise refused
    /// <see cref="PressRefusal.Busy"/>. A newer accepted press replaces an older one that has not fired yet.
    /// </summary>
    public PressResult Press(int slot)
    {
        _state = null;
        if (_engine.IsOver) return PressResult.Refuse(PressRefusal.FightOver);
        var skill = SkillAt(slot);
        if (skill == null) return PressResult.Refuse(PressRefusal.EmptySlot);
        if (skill.IsTriggeredOnly) return PressResult.Refuse(PressRefusal.TriggeredOnly);

        var s = _engine.State;
        var now = _engine.Time;
        var busy = IsBusy;
        var refusal = Rotation.Refusal(skill, s, _engine.Conditions, _engine.Weapon, busy ? s.BusyUntil : now);
        if (refusal == PressRefusal.Cooldown) return PressResult.Refuse(refusal, s.CooldownReadyAt[skill.Id] - now);
        if (refusal != PressRefusal.None) return PressResult.Refuse(refusal);
        if (busy && s.BusyUntil - now > QueueWindowSec + Eps) return PressResult.Refuse(PressRefusal.Busy);

        _commands.Press(skill);
        return PressResult.Accept(queued: busy);
    }

    /// <summary>
    /// Holds a slot's key: whenever the character is free and the skill is usable, it is used (a channel keeps channelling; any
    /// other skill repeats). Empty slots, triggered-only skills and skills the weapon cannot use are ignored. A pressed skill
    /// goes before the held one. <see cref="HoldRefusal"/> tells beforehand whether the slot will be held.
    /// </summary>
    public void StartChannel(int slot)
    {
        _state = null;
        if (SkillAt(slot) is { IsTriggeredOnly: false } skill && skill.WeaponFits(_engine.Weapon)) _commands.Hold(skill);
    }

    /// <summary>
    /// Why <see cref="StartChannel"/> would not hold the slot (1-based): <see cref="PressRefusal.FightOver"/>,
    /// <see cref="PressRefusal.EmptySlot"/>, <see cref="PressRefusal.TriggeredOnly"/> or <see cref="PressRefusal.WeaponMismatch"/>;
    /// <see cref="PressRefusal.None"/> when it will. Cooldown and resource are not checked: a held key waits for them.
    /// </summary>
    public PressRefusal HoldRefusal(int slot)
    {
        if (_engine.IsOver) return PressRefusal.FightOver;
        if (SkillAt(slot) is not { } skill) return PressRefusal.EmptySlot;
        if (skill.IsTriggeredOnly) return PressRefusal.TriggeredOnly;
        return skill.WeaponFits(_engine.Weapon) ? PressRefusal.None : PressRefusal.WeaponMismatch;
    }

    /// <summary>Releases the held key: the tick in progress finishes, no new one starts.</summary>
    public void StopChannel()
    {
        _state = null;
        _commands.Release();
    }

    /// <summary>One engine step of <see cref="StepSec"/>. Does nothing once the fight is over (only without infinite life).</summary>
    public void Step()
    {
        _state = null;
        _engine.Step(_commands);
    }

    /// <summary>Test entry: one step with another command source (the rotation, to compare with the simulator).</summary>
    internal void Step(ICommandSource commands)
    {
        _state = null;
        _engine.Step(commands);
    }

    /// <summary>The log events since the previous call, oldest first. The live log never stops (no "log_truncated"); if the
    /// caller does not drain for a long time, only the most recent <see cref="LogKeep"/>–2×<see cref="LogKeep"/> events are kept.</summary>
    public IReadOnlyList<SimEvent> DrainEvents() => _engine.Log.Drain();

    /// <summary>Every hit and DoT tick since the previous call (oldest first), with what the dummy's defense took off it. Never
    /// stops; if the caller does not drain for a long time, only the most recent <see cref="DamageKeep"/>–2×<see cref="DamageKeep"/>
    /// events are kept. <see cref="Snapshot"/> carries the sums over the whole fight (<see cref="SimResult.Mitigation"/>).</summary>
    public IReadOnlyList<DamageEvent> DrainDamage() => _engine.Damage.Drain();

    /// <summary>What the combat screen shows now. The same object until the next <see cref="Step"/> or command
    /// (<see cref="Press"/>, <see cref="StartChannel"/>, <see cref="StopChannel"/>), so reading it every frame is cheap.</summary>
    public LiveState State => _state ??= BuildState();

    private LiveState BuildState()
    {
        var s = _engine.State;
        var now = _engine.Time;
        var cooldowns = new SortedDictionary<int, double>();
        for (var slot = 1; slot <= Setup.Skills.Count; slot++)
            if (s.CooldownReadyAt.TryGetValue(Setup.Skills[slot - 1].Id, out var ready) && ready > now + Eps)
                cooldowns[slot] = ready - now;
        var buffs = new SortedDictionary<string, double>(s.Buffs, StringComparer.Ordinal);
        var ailments = new SortedSet<string>(StringComparer.Ordinal);
        if (s.Primary is { } primary)
        {
            foreach (var a in primary.Ailments) ailments.Add(a.AilmentId);
            if (primary.IsFrozen) ailments.Add("freeze");
        }

        var hero = new List<TimedEffect>();
        foreach (var (id, left) in buffs)
            hero.Add(new TimedEffect(id, double.IsPositiveInfinity(left) ? null : left, 1) { Kind = TimedEffectKind.Buff });
        foreach (var key in ShownCounters.Order(StringComparer.Ordinal))
            if (s.MechanicCounters.TryGetValue(key, out var value) && (int)Math.Round(value) is var stacks and > 0)
                hero.Add(new TimedEffect(key, null, stacks) { Kind = TimedEffectKind.MechanicStack });

        var targets = new List<IReadOnlyList<TimedEffect>>(s.Targets.Count);
        foreach (var t in s.Targets) targets.Add(TargetEffects(t));

        return new LiveState(now, s.Resource, s.ResourceMax, s.Life, cooldowns, buffs, IsBusy, s.ChannelSkillId, ailments.ToList())
        {
            ResourceId = Setup.Character.Class.Resource.Id,
            BusyLeft = Math.Max(0, s.BusyUntil - now),
            QueuedSlot = SlotOf(_commands.Pressed),
            HeldSlot = SlotOf(_commands.Held),
            HeroEffects = hero,
            TargetEffects = targets,
            TargetLife = s.Targets.Select(t => t.Life).ToList(),
            TargetLifeMax = s.Targets.Select(t => t.MaxLife).ToList(),
        };
    }

    // Ailments (instances as stacks, the longest time left), freeze, and mechanic damage-taken contributions; ordinal id order.
    private static IReadOnlyList<TimedEffect> TargetEffects(TargetState t)
    {
        var effects = new SortedDictionary<string, TimedEffect>(StringComparer.Ordinal);
        foreach (var a in t.Ailments)
            effects[a.AilmentId] = effects.TryGetValue(a.AilmentId, out var e)
                ? e with { TimeLeft = Math.Max(e.TimeLeft ?? 0, a.Remaining), Stacks = e.Stacks + 1 }
                : new TimedEffect(a.AilmentId, a.Remaining, 1) { Kind = TimedEffectKind.Ailment };
        if (t.IsFrozen) effects["freeze"] = new TimedEffect("freeze", t.FreezeRemaining, 1) { Kind = TimedEffectKind.Ailment };
        foreach (var key in t.DamageTakenSources.Keys)
            effects.TryAdd(key, new TimedEffect(key, null, 1) { Kind = TimedEffectKind.Debuff });
        return effects.Values.ToList();
    }

    private int? SlotOf(SkillRuntime? skill)
    {
        if (skill == null) return null;
        for (var i = 0; i < Setup.Skills.Count; i++)
            if (ReferenceEquals(Setup.Skills[i], skill)) return i + 1;
        return null;
    }

    /// <summary>
    /// The fight's result so far (the simulator's <see cref="SimResult"/>, over <see cref="LiveState.Time"/>). The dummy never attacks
    /// in a live fight, so TimeToDeath is always null. Its Log holds only the most recent events (see <see cref="LogKeep"/>);
    /// the damage totals cover the whole fight.
    /// </summary>
    public SimResult Snapshot() => _engine.Result();

    private bool IsBusy => _engine.State.BusyUntil > _engine.Time + Eps;

    private SkillRuntime? SkillAt(int slot) => slot >= 1 && slot <= Setup.Skills.Count ? Setup.Skills[slot - 1] : null;
}
