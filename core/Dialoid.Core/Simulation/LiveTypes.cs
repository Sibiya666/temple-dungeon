using Dialoid.Core.Stats;

namespace Dialoid.Core.Simulation;

/// <summary>Settings of a live fight. The step is always <see cref="LiveFight.StepSec"/>; the dummy never attacks back.</summary>
/// <param name="InfiniteLife">Targets never die (training room default, spec 6.2a): no kills, no on-kill effects, the fight never ends;
/// a target brought to 0 life is refilled to its max. A preset immortal in the data gets the finite life of
/// <see cref="LiveFight.ImmortalStandInDummyId"/> instead (owner decision 2026-10-30: a life bar that drops and refills, and
/// "target below X %" behaves as on that preset).</param>
public sealed record LiveFightConfig(string DummyId, SimMode Mode, int Seed = 1, int? PackSizeOverride = null, bool InfiniteLife = true);

/// <summary>Why a key press was not taken (spec 6.2c). <see cref="OutOfReach"/> (a melee skill too far from the dummy) and
/// <see cref="TargetNotVisible"/> (a spell aimed at the dummy or the ground while the dummy is off the screen) are set by the
/// sandbox's reach rule (spec 6.3); the core never returns them.</summary>
public enum PressRefusal { None, EmptySlot, Cooldown, NotEnoughResource, Busy, BuffActive, WeaponMismatch, TriggeredOnly, OutOfReach, FightOver, TargetNotVisible }

/// <summary>The answer to a key press.</summary>
/// <param name="Accepted">The skill will be used at the next step the character is free.</param>
/// <param name="Queued">Accepted while the character is busy (it fires on release; a newer press replaces it).</param>
/// <param name="CooldownLeft">Seconds of cooldown left, for a <see cref="PressRefusal.Cooldown"/> refusal only.</param>
public sealed record PressResult(bool Accepted, bool Queued, PressRefusal Refusal, double? CooldownLeft)
{
    internal static PressResult Accept(bool queued) => new(true, queued, PressRefusal.None, null);
    internal static PressResult Refuse(PressRefusal why, double? cooldownLeft = null) => new(false, false, why, cooldownLeft);
}

/// <summary>What the game layer shows between steps.</summary>
/// <param name="Time">Fight time: the moment the next step starts.</param>
/// <param name="CooldownLeftBySlot">Slot (1-based) → seconds left, only for slots whose skill is on cooldown.</param>
/// <param name="Buffs">Active buff id → seconds left.</param>
/// <param name="Busy">A cast/attack/channel tick is in progress, so a press now is queued or refused.</param>
/// <param name="TargetAilments">Distinct ailment ids on the primary target (ordinal order; "freeze" while frozen).</param>
public sealed record LiveState(double Time, double Resource, double ResourceMax, double Life, IReadOnlyDictionary<int, double> CooldownLeftBySlot,
    IReadOnlyDictionary<string, double> Buffs, bool Busy, string? ChannelSkillId, IReadOnlyList<string> TargetAilments)
{
    /// <summary>The class resource id (fury, mana), for its name and color.</summary>
    public string ResourceId { get; init; } = "";

    /// <summary>Seconds until the cast/attack/channel tick in progress ends (0 when free).</summary>
    public double BusyLeft { get; init; }

    /// <summary>Fight slot (1-based) of the accepted press that has not fired yet (it fires at the next step the character is
    /// free); null when none.</summary>
    public int? QueuedSlot { get; init; }

    /// <summary>Fight slot (1-based) of the held key (channel or repeat); null when none is held.</summary>
    public int? HeldSlot { get; init; }

    /// <summary>The character's buffs (with their timers, none for an endless one) and the shown mechanic stacks
    /// (<see cref="LiveFight.ShownCounters"/>): buffs first, each group in ordinal id order.</summary>
    public IReadOnlyList<TimedEffect> HeroEffects { get; init; } = Array.Empty<TimedEffect>();

    /// <summary>Per target (index 0 is the first pack member): its ailments (stacks, longest time left), freeze, and the mechanic
    /// contributions to its damage-taken multiplier (debuffs without a timer), in ordinal id order.</summary>
    public IReadOnlyList<IReadOnlyList<TimedEffect>> TargetEffects { get; init; } = Array.Empty<IReadOnlyList<TimedEffect>>();

    /// <summary>Per target: current life, for the bar over the dummy (+∞ for an immortal target).</summary>
    public IReadOnlyList<double> TargetLife { get; init; } = Array.Empty<double>();

    /// <summary>Per target: max life (+∞ for an immortal target).</summary>
    public IReadOnlyList<double> TargetLifeMax { get; init; } = Array.Empty<double>();
}

/// <summary>What a <see cref="TimedEffect"/> is, so the game layer knows where its name comes from.</summary>
public enum TimedEffectKind
{
    /// <summary>A buff on the character (its id: the buff id, "mechanic:&lt;id&gt;" for a mechanic's endless buff).</summary>
    Buff,
    /// <summary>An ailment on a target (its id: the ailment id, "freeze" included).</summary>
    Ailment,
    /// <summary>A shown mechanic counter on the character (its id: the counter key, e.g. "static_charge:charges").</summary>
    MechanicStack,
    /// <summary>A mechanic's contribution to a target's damage-taken multiplier (its id: the mechanic id).</summary>
    Debuff,
}

/// <summary>One icon of the combat screen: a buff, ailment, mechanic stack or debuff.</summary>
/// <param name="TimeLeft">Seconds left; null when it has no timer (endless buffs, mechanic stacks, debuffs).</param>
/// <param name="Stacks">Instances (ailments), the counter's value (mechanic stacks), otherwise 1.</param>
public sealed record TimedEffect(string Id, double? TimeLeft, int Stacks)
{
    public TimedEffectKind Kind { get; init; }
}

/// <summary>A filled fight slot for the skill bar's tooltip (no conditions, no buffs: like the skill sheet). The actual busy time
/// of each use, with conditional speed bonuses, is <see cref="SimEvent.UseSec"/>.</summary>
/// <param name="Slot">Fight slot, 1-based.</param>
/// <param name="Cost">Resource paid per use (a channel: per tick).</param>
/// <param name="CostPerSec">A channel's cost per second; 0 for other skills.</param>
/// <param name="UseTimeSec">Cast/attack time of one use (a channel: one tick).</param>
public sealed record SlotInfo(int Slot, string SkillId, string Name, TagSet Tags, double Cost, double CostPerSec, double CooldownSec,
    double UseTimeSec, bool IsChannel);
