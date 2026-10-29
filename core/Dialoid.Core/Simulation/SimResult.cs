using Dialoid.Core.Character;
using Dialoid.Core.Combat;

namespace Dialoid.Core.Simulation;

/// <summary>Simulation output (spec 11.3). Damage totals include hits and damage over time; DoT is attributed to its source skill and type.</summary>
public sealed record SimResult(double DpsSingleTarget, double DpsPack, IReadOnlyDictionary<string, double> DamageBySkill,
    IReadOnlyDictionary<DamageType, double> DamageByType, double HitDamage, double DotDamage,
    double? TimeToKill, double? TimeToDeath, IReadOnlyList<SimEvent> Log, IReadOnlyList<RuleViolation> Violations,
    IReadOnlyDictionary<DamageType, double>? DamageTaken = null)
{
    /// <summary>Defensive bookkeeping; set only when the dummy attacks (<see cref="SimConfig.DummyAttacks"/>), null otherwise.</summary>
    public DefenseTotals? Defense { get; init; }

    /// <summary>
    /// DPS per source skill: <see cref="DamageBySkill"/> divided by the same elapsed time as <see cref="DpsSingleTarget"/> and
    /// <see cref="DpsPack"/>. Damage to every target counts, so the values sum to <see cref="DpsPack"/> (empty when rejected).
    /// </summary>
    public IReadOnlyDictionary<string, double> DpsBySkill { get; init; } = new SortedDictionary<string, double>(StringComparer.Ordinal);

    /// <summary>DPS per damage type: <see cref="DamageByType"/> over the same elapsed time; sums to <see cref="DpsPack"/> (empty when rejected).</summary>
    public IReadOnlyDictionary<DamageType, double> DpsByType { get; init; } = new SortedDictionary<DamageType, double>();

    /// <summary>Character resource and life at time 0, at the first step boundary at or after each 0.5 s of simulation time, and at
    /// the end of the run (empty when rejected).</summary>
    public IReadOnlyList<ResourceSample> ResourceSeries { get; init; } = Array.Empty<ResourceSample>();

    /// <summary>What the dummies' defense took off every hit and DoT tick, summed per source skill and in total (empty when rejected).
    /// Each skill's Final equals its <see cref="DamageBySkill"/> up to rounding.</summary>
    public MitigationTotals Mitigation { get; init; } = MitigationTotals.Empty;

    /// <summary>All-zero result carrying the reasons the simulation did not run.</summary>
    public static SimResult Rejected(IReadOnlyList<RuleViolation> violations) =>
        new(0, 0, new SortedDictionary<string, double>(StringComparer.Ordinal), new SortedDictionary<DamageType, double>(),
            0, 0, null, null, Array.Empty<SimEvent>(), violations);
}

/// <summary>
/// What happened to the dummy's attacks in defensive mode. Incoming damage counts the attacks that were not dodged, after gear
/// scaling and before any mitigation; armor and resist mitigation are the amounts they removed (elemental armor counts as armor).
/// The character's damage_taken multiplier applies after them, so DamageTaken = (incoming − armor − resist) × that multiplier.
/// A type whose taken amount is ≤ 0 (e.g. resist 100) is still counted in IncomingBeforeMitigation but is missing from
/// <see cref="SimResult.DamageTaken"/>.
/// </summary>
public sealed record DefenseTotals(int AttacksReceived, int AttacksDodged, IReadOnlyDictionary<DamageType, double> IncomingBeforeMitigation,
    double MitigatedByArmor, double MitigatedByResist, double LifeRegenerated);

/// <summary>One point of <see cref="SimResult.ResourceSeries"/>.</summary>
public sealed record ResourceSample(double Time, double Resource, double Life);
