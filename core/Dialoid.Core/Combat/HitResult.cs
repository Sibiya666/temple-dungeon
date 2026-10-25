using Dialoid.Core.Character;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Combat;

public sealed record TargetDefense(double Armor, IReadOnlyDictionary<DamageType, double> Resists, double DamageTakenIncreased);

public enum CritOutcome { Roll, Expected }

/// <param name="IsCrit">null = expected value; true/false = forced outcome.</param>
/// <param name="Weapon">Equipped weapon (the compiled character does not carry it); required for attack skills.</param>
public sealed record HitInput(CompiledCharacter Character, CompiledSkill Skill, TargetDefense Target,
    IConditionContext Conditions, bool? IsCrit, Func<double, double, double> PickInRange, WeaponStub? Weapon = null);

/// <summary>Final and AilmentBase are keyed by the final damage type of each portion.</summary>
public sealed record HitResult(IReadOnlyDictionary<DamageType, double> Final, IReadOnlyDictionary<DamageType, double> AilmentBase,
    bool IsCrit, double CritMultiplierApplied, IReadOnlyList<StatResult> Breakdown);
