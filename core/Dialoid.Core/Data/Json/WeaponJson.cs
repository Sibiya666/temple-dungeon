using System.Text.Json;

namespace Dialoid.Core.Data.Json;

internal sealed class WeaponJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "type", "phys_min", "phys_max", "attacks_per_second", "crit_chance", "implicits");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public double PhysMin { get; set; }
    public double PhysMax { get; set; }
    public double AttacksPerSecond { get; set; }
    public double CritChance { get; set; }
    public List<JsonElement>? Implicits { get; set; }
}
