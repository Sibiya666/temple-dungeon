namespace Dialoid.Core.Combat;

/// <summary>Armor and resistance math (spec 5.4, 6.1).</summary>
public static class Mitigation
{
    public const double ArmorCap = 0.85;

    /// <summary>Share of a physical hit absorbed by armor: armor / (armor + 10 x hit), capped at 0.85.</summary>
    public static double ArmorReduction(double armor, double hitDamage)
    {
        if (armor <= 0 || hitDamage <= 0) return 0;
        return Math.Min(ArmorCap, armor / (armor + 10 * hitDamage));
    }

    /// <summary>Damage multiplier from a resist: effective resist = max(-100, resist - penetration); multiplier = 1 - eff/100.</summary>
    public static double ResistMultiplier(double resist, double penetration)
    {
        var eff = Math.Max(-100, resist - penetration);
        return 1 - eff / 100;
    }
}
