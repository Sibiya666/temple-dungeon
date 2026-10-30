namespace Dialoid.Core.Combat;

/// <summary>Groups of damage types that rules refer to.</summary>
public static class DamageTypes
{
    /// <summary>Fire, cold and lightning. Poison and dark are not elemental (Stone skin armors only the elements and poison).</summary>
    public static bool IsElemental(DamageType t) => t is DamageType.Fire or DamageType.Cold or DamageType.Lightning;
}
