using System.Text.Json;

namespace Dialoid.Core.Data.Json;

internal sealed class SkillRequireJson
{
    public string? Node { get; set; }
    public int? Points { get; set; }
}

internal sealed class SkillNodeJson
{
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
