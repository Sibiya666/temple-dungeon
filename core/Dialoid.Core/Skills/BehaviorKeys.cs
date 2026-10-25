namespace Dialoid.Core.Skills;

/// <summary>Every skill behavior param the engine reads (skills.json «behavior», set_behavior / add_behavior «param»).</summary>
public static class BehaviorKeys
{
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "area_radius", "projectile_count", "pierce_count", "chain_count", "duration", "channel_interval",
        "explosion_radius", "ground_duration", "ground_fraction",
    };

    public static bool IsKnown(string key) => All.Contains(key);
}
