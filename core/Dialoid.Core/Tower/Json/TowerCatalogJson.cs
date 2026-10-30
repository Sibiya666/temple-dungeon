using System.Text.Json;
using Dialoid.Core.Data;
using Dialoid.Core.Data.Json;

namespace Dialoid.Core.Tower.Json;

internal sealed class FloorTypeJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "has_end_boss", "variants");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors) =>
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);

    public string? Id { get; set; }
    public string? Name { get; set; }
    public bool HasEndBoss { get; set; }
    public List<string>? Variants { get; set; }
}

internal sealed class BiomeJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors) =>
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);

    public string? Id { get; set; }
    public string? Name { get; set; }
}

internal sealed class ModTierJson
{
    public static readonly HashSet<string> Allowed = StrictJson.Fields("tier", "min", "max", "quality");

    public int? Tier { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Quality { get; set; }
}

internal sealed class FloorModJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "kind", "floor_types", "variant", "group",
        "directions", "style_tags", "hard_counter", "tiers");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
        StrictJson.RejectUnknownIn(el, "tiers", ModTierJson.Allowed, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
    public ModKind? Kind { get; set; }
    public List<string>? FloorTypes { get; set; }
    public string? Variant { get; set; }
    public string? Group { get; set; }
    public List<string>? Directions { get; set; }
    public List<string>? StyleTags { get; set; }
    /// <summary>null = no hard counter.</summary>
    public HardCounter? HardCounter { get; set; }
    public List<ModTierJson>? Tiers { get; set; }
}

internal sealed class CurrencyJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "op", "mod_kind", "has_level", "drops_from_rank",
        "drop_base", "drop_growth", "summit_multiplier", "bias_direction", "bias_chance");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors) =>
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);

    public string? Id { get; set; }
    public string? Name { get; set; }
    public CurrencyOp? Op { get; set; }
    public ModKind? ModKind { get; set; }
    public bool HasLevel { get; set; }
    public int? DropsFromRank { get; set; }
    public double? DropBase { get; set; }
    public double? DropGrowth { get; set; }
    public double? SummitMultiplier { get; set; }
    public string? BiasDirection { get; set; }
    public double BiasChance { get; set; }
}
