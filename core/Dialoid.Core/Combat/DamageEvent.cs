namespace Dialoid.Core.Combat;

/// <summary>
/// One damage type of a <see cref="DamageEvent"/> and what the target's defenses took off it.
/// Placeholder shape for the training room plan, task 3, which fills it in (and the identity
/// BeforeDefense − ByArmor − ByResist − ByTakenMultiplier − Nullified = Final).
/// </summary>
public sealed record DamagePart(DamageType Type, double BeforeDefense, double ByArmor, double ByResist, double ByTakenMultiplier,
    double Nullified, double Final);

/// <summary>One hit or DoT tick on one target. Not produced yet: the engine starts emitting these in the training room plan, task 3.</summary>
public sealed record DamageEvent(double Time, string SkillId, int Target, bool IsDot, bool IsCrit, IReadOnlyList<DamagePart> Parts);
