using Dialoid.Core.Combat;

namespace Dialoid.Core.Data;

/// <summary>
/// Stats the engine reads by name. stats.json must define every one of them (checked at load), so the compiler, the
/// hit and ailment maths and the simulator never need a fallback for a missing stat.
/// </summary>
public static class RequiredStats
{
    private static readonly string[] Elements = { "fire", "cold", "lightning", "poison", "dark" };
    private static readonly string[] Ailments = { "bleed", "ignite", "poison", "shock", "chill", "freeze" };

    public static IReadOnlyList<string> All { get; } = Build();

    private static IReadOnlyList<string> Build()
    {
        var list = new List<string>
        {
            "life", "life_regen_pct", "armor", "dodge_chance", "dodge_chance_max", "damage", "damage_taken", "crit_chance",
            "crit_damage", "resource_max", "resource_regen", "resource_gen", "resource_cost", "cast_speed", "attack_speed",
            "cooldown_recovery", "area", "shock_effect", "freeze_duration", "str", "int", "will", "dex",
        };
        foreach (var e in Elements) list.AddRange(new[] { $"resist_{e}", $"resist_{e}_max" });
        foreach (var t in Enum.GetValues<DamageType>().Select(t => t.ToString().ToLowerInvariant()))
            list.AddRange(new[] { $"added_{t}_damage_min", $"added_{t}_damage_max", $"penetration_{t}" });
        foreach (var a in Ailments) list.AddRange(new[] { $"{a}_chance", $"{a}_max_stacks" });
        return list;
    }
}
