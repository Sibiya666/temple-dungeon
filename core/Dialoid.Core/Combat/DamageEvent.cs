namespace Dialoid.Core.Combat;

/// <summary>
/// One damage type of a <see cref="DamageEvent"/> and what the target's defense took off it, in the order the hit pipeline applies
/// it: armor (physical hits and physical zones) or resistance with penetration (every other type, and bleed), then the target's
/// damage-taken multiplier. Each "By…" is the amount before that step minus the amount after it, so
/// BeforeDefense − ByArmor − ByResist − ByTakenMultiplier − Nullified = Final (up to rounding). ByResist is negative when
/// penetration pushes the resistance below 0; ByTakenMultiplier is negative when the multiplier is above 1 (shock added damage).
/// Nullified is damage of a type the skill deals nothing of (avatar_of_fire's unconverted physical), counted before defense; such
/// a part has Final 0.
/// </summary>
public sealed record DamagePart(DamageType Type, double BeforeDefense, double ByArmor, double ByResist, double ByTakenMultiplier,
    double Nullified, double Final)
{
    internal DamagePart Scaled(double k) =>
        new(Type, BeforeDefense * k, ByArmor * k, ByResist * k, ByTakenMultiplier * k, Nullified * k, Final * k);
}

/// <summary>One hit or DoT tick on one target. A DoT tick is one ailment instance or ground zone over one engine step;
/// IsCrit is set only for hits that rolled a crit (never in Average mode, where crits are an expectation).</summary>
public sealed record DamageEvent(double Time, string SkillId, int Target, bool IsDot, bool IsCrit, IReadOnlyList<DamagePart> Parts);

/// <summary>
/// The damage parts of a whole fight summed over every target and damage type: per source skill and in total. The Type of these
/// summed parts carries no meaning (always <see cref="DamageType.Physical"/>, the default).
/// </summary>
public sealed record MitigationTotals(IReadOnlyDictionary<string, DamagePart> BySkill, DamagePart Total)
{
    /// <summary>No damage at all. Shared, so its dictionary is read-only.</summary>
    public static MitigationTotals Empty { get; } =
        new(System.Collections.ObjectModel.ReadOnlyDictionary<string, DamagePart>.Empty, new DamagePart(default, 0, 0, 0, 0, 0, 0));
}

/// <summary>Mutable running sum of <see cref="DamagePart"/> fields (the hit pipeline and the fight totals add into it).</summary>
internal sealed class DamagePartSum
{
    public double BeforeDefense, ByArmor, ByResist, ByTakenMultiplier, Nullified, Final;

    public void Add(DamagePart p)
    {
        BeforeDefense += p.BeforeDefense;
        ByArmor += p.ByArmor;
        ByResist += p.ByResist;
        ByTakenMultiplier += p.ByTakenMultiplier;
        Nullified += p.Nullified;
        Final += p.Final;
    }

    public DamagePart ToPart(DamageType type) => new(type, BeforeDefense, ByArmor, ByResist, ByTakenMultiplier, Nullified, Final);
}
