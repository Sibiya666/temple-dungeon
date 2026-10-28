using System.Text.Json;

namespace Dialoid.Core.Data.Json;

internal sealed class SkillRequireJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("node", "points");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

    public string? Node { get; set; }
    public int? Points { get; set; }
}

internal sealed class SkillNodeJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "description", "max_points", "requires", "exclusive_group", "effects");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("requires", out var requires) && requires.ValueKind == JsonValueKind.Array)
            foreach (var r in requires.EnumerateArray()) SkillRequireJson.CheckFields(r, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? MaxPoints { get; set; }
    public List<SkillRequireJson>? Requires { get; set; }
    public string? ExclusiveGroup { get; set; }
    public JsonElement? Effects { get; set; }
}

internal sealed class SkillJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "description", "class_id", "unlock_level", "tags", "is_triggered_only", "spell_damage", "weapon_effectiveness", "added_damage_effectiveness", "cast_time", "attack_time_multiplier", "cooldown", "resource_cost", "resource_gain", "crit_chance", "hits_per_use", "behavior", "ailment_chances", "weapon_types", "tree");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("tree", out var tree) && tree.ValueKind == JsonValueKind.Array)
            foreach (var n in tree.EnumerateArray()) SkillNodeJson.CheckFields(n, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ClassId { get; set; }
    public int UnlockLevel { get; set; } = 1;
    public List<string>? Tags { get; set; }
    public bool IsTriggeredOnly { get; set; }
    public Dictionary<string, List<double>>? SpellDamage { get; set; }
    public double? WeaponEffectiveness { get; set; }
    public double AddedDamageEffectiveness { get; set; } = 100;
    public double CastTime { get; set; }
    public double AttackTimeMultiplier { get; set; } = 1;
    public double? Cooldown { get; set; }
    public double ResourceCost { get; set; }
    public double ResourceGain { get; set; }
    public double CritChance { get; set; }
    public int HitsPerUse { get; set; } = 1;
    public Dictionary<string, double>? Behavior { get; set; }
    public Dictionary<string, double>? AilmentChances { get; set; }
    public List<string>? WeaponTypes { get; set; }
    public List<SkillNodeJson>? Tree { get; set; }
}
