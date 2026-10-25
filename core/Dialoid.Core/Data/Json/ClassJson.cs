using System.Text.Json;
using Dialoid.Core.Character;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Data.Json;

internal sealed class ResourceJson
{
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
    public string? Stat { get; set; }
    public double Every { get; set; }
    public double? Cap { get; set; }
}

internal sealed class ClassJson
{
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
    public Character.Attribute? Attribute { get; set; }
    public string? Stat { get; set; }
    public ModKind? Kind { get; set; }
    public double? PerPoint { get; set; }
}

internal sealed class AttributesFileJson
{
    public double PrimaryDamageMorePerPoint { get; set; }
    public List<AttributeBonusJson>? Bonuses { get; set; }
}
