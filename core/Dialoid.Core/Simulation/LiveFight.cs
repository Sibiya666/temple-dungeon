using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
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

    internal FightSetup Setup { get; }

    /// <summary>The engine's live state (tests read the busy time and adjust the resource).</summary>
    internal SimState Sim => _engine.State;

    internal LiveFight(FightSetup setup, LiveFightConfig config)
    {
        Setup = setup;
        _engine = setup.Engine(ToSimConfig(config), config.InfiniteLife, log: SimLog.Rolling(LogKeep), damageKeep: DamageKeep);
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
    public void StopChannel() => _commands.Release();

    /// <summary>One engine step of <see cref="StepSec"/>. Does nothing once the fight is over (only without infinite life).</summary>
    public void Step() => _engine.Step(_commands);

    /// <summary>Test entry: one step with another command source (the rotation, to compare with the simulator).</summary>
    internal void Step(ICommandSource commands) => _engine.Step(commands);

    /// <summary>The log events since the previous call, oldest first. The live log never stops (no "log_truncated"); if the
    /// caller does not drain for a long time, only the most recent <see cref="LogKeep"/>–2×<see cref="LogKeep"/> events are kept.</summary>
    public IReadOnlyList<SimEvent> DrainEvents() => _engine.Log.Drain();

    /// <summary>Every hit and DoT tick since the previous call (oldest first), with what the dummy's defense took off it. Never
    /// stops; if the caller does not drain for a long time, only the most recent <see cref="DamageKeep"/>–2×<see cref="DamageKeep"/>
    /// events are kept. <see cref="Snapshot"/> carries the sums over the whole fight (<see cref="SimResult.Mitigation"/>).</summary>
    public IReadOnlyList<DamageEvent> DrainDamage() => _engine.Damage.Drain();

    public LiveState State
    {
        get
        {
            var s = _engine.State;
            var now = _engine.Time;
            var cooldowns = new SortedDictionary<int, double>();
            for (var slot = 1; slot <= Setup.Skills.Count; slot++)
                if (s.CooldownReadyAt.TryGetValue(Setup.Skills[slot - 1].Id, out var ready) && ready > now + Eps)
                    cooldowns[slot] = ready - now;
            var buffs = new SortedDictionary<string, double>(s.Buffs, StringComparer.Ordinal);
            var ailments = new SortedSet<string>(StringComparer.Ordinal);
            if (s.Primary is { } target)
            {
                foreach (var a in target.Ailments) ailments.Add(a.AilmentId);
                if (target.IsFrozen) ailments.Add("freeze");
            }
            return new LiveState(now, s.Resource, s.ResourceMax, s.Life, cooldowns, buffs, IsBusy, s.ChannelSkillId, ailments.ToList());
        }
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
