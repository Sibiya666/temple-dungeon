using System.Text.Json;

namespace Dialoid.Core.Data.Json;

internal sealed class WeaponJson
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public double PhysMin { get; set; }
    public double PhysMax { get; set; }
    public double AttacksPerSecond { get; set; }
    public double CritChance { get; set; }
    public List<JsonElement>? Implicits { get; set; }
}
