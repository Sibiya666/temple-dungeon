namespace Dialoid.Core.Simulation;

/// <summary>Settings of a live fight. The step is always <see cref="LiveFight.StepSec"/>; the dummy never attacks back.</summary>
/// <param name="InfiniteLife">Targets never die (training room default, spec 6.2a): no kills, no on-kill effects, the fight never ends.</param>
public sealed record LiveFightConfig(string DummyId, SimMode Mode, int Seed = 1, int? PackSizeOverride = null, bool InfiniteLife = true);

/// <summary>Why a key press was not taken (spec 6.2c). <see cref="OutOfReach"/> is set by the sandbox; the core never returns it.</summary>
public enum PressRefusal { None, EmptySlot, Cooldown, NotEnoughResource, Busy, BuffActive, WeaponMismatch, TriggeredOnly, OutOfReach, FightOver }

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
    IReadOnlyDictionary<string, double> Buffs, bool Busy, string? ChannelSkillId, IReadOnlyList<string> TargetAilments);
