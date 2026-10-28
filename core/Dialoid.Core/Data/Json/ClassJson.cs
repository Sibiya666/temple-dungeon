using System.Text.Json;
using Dialoid.Core.Character;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Data.Json;

internal sealed class ResourceJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "base_max", "regen_per_sec", "decay_per_sec", "decay_delay_sec", "max_per");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
        if (StrictJson.TryObject(el, "max_per", out var maxPer)) PerJson.CheckFields(maxPer, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
    public double? BaseMax { get; set; }
    public double RegenPerSec { get; set; }
    public double DecayPerSec { get; set; }
    public double DecayDelaySec { get; set; }
    public PerJson? MaxPer { get; set; }
}

internal sealed class PerJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("stat", "every", "cap");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

    public string? Stat { get; set; }
    public double Every { get; set; }
    public double? Cap { get; set; }
}

internal sealed class ClassJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "primary", "base_attributes", "life_multiplier", "resource", "class_bonuses", "passive_start_node", "skills", "ascendancies");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
        if (StrictJson.TryObject(el, "resource", out var resource)) ResourceJson.CheckFields(resource, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
    public Character.Attribute? Primary { get; set; }
    public Dictionary<string, int>? BaseAttributes { get; set; }
    public double LifeMultiplier { get; set; } = 1;
    public ResourceJson? Resource { get; set; }
    public List<JsonElement>? ClassBonuses { get; set; }
    public string? PassiveStartNode { get; set; }
    public List<string>? Skills { get; set; }
    public List<string>? Ascendancies { get; set; }
}

internal sealed class AttributeBonusJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("attribute", "stat", "kind", "per_point");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

    public Character.Attribute? Attribute { get; set; }
    public string? Stat { get; set; }
    public ModKind? Kind { get; set; }
    public double? PerPoint { get; set; }
}

internal sealed class AttributesFileJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("primary_damage_more_per_point", "bonuses");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("bonuses", out var bonuses) && bonuses.ValueKind == JsonValueKind.Array)
            foreach (var b in bonuses.EnumerateArray()) AttributeBonusJson.CheckFields(b, file, id, errors);
    }

    public double PrimaryDamageMorePerPoint { get; set; }
    public List<AttributeBonusJson>? Bonuses { get; set; }
}
