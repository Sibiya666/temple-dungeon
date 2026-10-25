using Dialoid.Core.Character;
using Dialoid.Core.Combat;

namespace Dialoid.Core.Simulation;

/// <summary>Simulation output (spec 11.3). Damage totals include hits and damage over time; DoT is attributed to its source skill and type.</summary>
public sealed record SimResult(double DpsSingleTarget, double DpsPack, IReadOnlyDictionary<string, double> DamageBySkill,
    IReadOnlyDictionary<DamageType, double> DamageByType, double HitDamage, double DotDamage,
    double? TimeToKill, double? TimeToDeath, IReadOnlyList<SimEvent> Log, IReadOnlyList<RuleViolation> Violations)
{
    /// <summary>All-zero result carrying the reasons the simulation did not run.</summary>
    public static SimResult Rejected(IReadOnlyList<RuleViolation> violations) =>
        new(0, 0, new SortedDictionary<string, double>(StringComparer.Ordinal), new SortedDictionary<DamageType, double>(),
            0, 0, null, null, Array.Empty<SimEvent>(), violations);
}
