namespace Dialoid.Core.Simulation;

/// <param name="DummyId">Dummy preset id from dummies.json.</param>
/// <param name="StepSec">Fixed time step; the loop always runs DurationSec / StepSec steps at most.</param>
/// <param name="DummyAttacks">"Dummy hits the character" mode (Task 15).</param>
public sealed record SimConfig(string DummyId, SimMode Mode, int Seed = 1, double DurationSec = 60, double StepSec = 0.05,
    int? PackSizeOverride = null, bool DummyAttacks = false, double SurvivalCapSec = 180);

/// <summary>
/// One log line. Kind: use|hit|crit|ailment|kill|no_resource|death|proc|proc_blocked|mechanic|dummy_attack|dodge|log_truncated.
/// <see cref="Text"/> is machine-readable English; the optional fields carry the same facts structured, so the game layer never
/// has to parse the text. A field is null when it does not apply to the kind.
/// </summary>
public sealed record SimEvent(double Time, string Kind, string Text)
{
    /// <summary>use, hit, crit, ailment: the skill. proc: the skill a cast_skill proc casts, else the proc's own skill.
    /// proc_blocked: the triggered copy. mechanic: the event skill, if any.</summary>
    public string? SkillId { get; init; }

    /// <summary>hit, crit, ailment, kill (and mechanics acting on a target): the target index (0 is the first pack member).</summary>
    public int? Target { get; init; }

    /// <summary>ailment: the ailment id (freeze included). proc: the ailment of an apply_ailment proc.</summary>
    public string? AilmentId { get; init; }

    /// <summary>proc: the skill whose proc fired. kill: the skill credited with the kill (its last damage source), if any.</summary>
    public string? SourceSkillId { get; init; }

    /// <summary>mechanic: the mechanic id from the data (e.g. static_charge).</summary>
    public string? MechanicKey { get; init; }

    /// <summary>hit and its crit: <see cref="Combat.DamageEvent.Seq"/> of the hit's damage event (null for a hit that dealt no
    /// damage part at all, which has no damage event).</summary>
    public long? DamageSeq { get; init; }

    /// <summary>use: how long the character is busy with this use (seconds, after conditional speed bonuses and the channel ramp,
    /// at least one step). The game layer stretches the animation to it.</summary>
    public double? UseSec { get; init; }
}
