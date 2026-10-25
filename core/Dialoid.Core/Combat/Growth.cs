namespace Dialoid.Core.Combat;

/// <summary>Linear growth curves (spec 6.1): skill 5% per level, gear 6% per character level.</summary>
public static class Growth
{
    public static double Skill(int level) => 1 + 0.05 * (level - 1);

    public static double Gear(int characterLevel) => 1 + 0.06 * (characterLevel - 1);
}
