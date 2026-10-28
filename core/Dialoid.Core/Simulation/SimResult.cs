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

    /// <summary>Character resource and life at time 0, every 0.5 s of simulation time and at the end of the run (empty when rejected).</summary>
    public IReadOnlyList<ResourceSample> ResourceSeries { get; init; } = Array.Empty<ResourceSample>();

    /// <summary>All-zero result carrying the reasons the simulation did not run.</summary>
    public static SimResult Rejected(IReadOnlyList<RuleViolation> violations) =>
        new(0, 0, new SortedDictionary<string, double>(StringComparer.Ordinal), new SortedDictionary<DamageType, double>(),
            0, 0, null, null, Array.Empty<SimEvent>(), violations);
}

/// <summary>
/// What happened to the dummy's attacks in defensive mode. Incoming damage counts the attacks that were not dodged, after gear
/// scaling and before any mitigation; armor and resist mitigation are the amounts they removed (elemental armor counts as armor).
/// The character's damage_taken multiplier applies after them, so DamageTaken = (incoming − armor − resist) × that multiplier.
/// </summary>
public sealed record DefenseTotals(int AttacksReceived, int AttacksDodged, IReadOnlyDictionary<DamageType, double> IncomingBeforeMitigation,
    double MitigatedByArmor, double MitigatedByResist, double LifeRegenerated);

/// <summary>One point of <see cref="SimResult.ResourceSeries"/>.</summary>
public sealed record ResourceSample(double Time, double Resource, double Life);
