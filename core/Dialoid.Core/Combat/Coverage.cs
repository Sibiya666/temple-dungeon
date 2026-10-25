namespace Dialoid.Core.Combat;

public static class Coverage
{
    /// <summary>Expected targets hit by an area of the given radius: 1 + (pack-1) * min(1, (r/R)^2).</summary>
    public static double AreaTargets(int pack, double radius, double packRadius = 4.0)
    {
        if (pack <= 1) return 1;
        var ratio = radius / packRadius;
        var f = Math.Min(1.0, ratio * ratio);
        return 1 + (pack - 1) * f;
    }

    public static double DistinctTargets(int pack, int projectiles, int pierce, int chain,
        double? explosionRadius, double packRadius = 4.0)
    {
        var primary = Math.Max(1, projectiles) + Math.Max(0, pierce) + Math.Max(0, chain);
        var perHit = explosionRadius.HasValue ? AreaTargets(pack, explosionRadius.Value, packRadius) : 1.0;
        return Math.Max(1.0, Math.Min(Math.Max(pack, 1), primary * perHit));
    }
}
