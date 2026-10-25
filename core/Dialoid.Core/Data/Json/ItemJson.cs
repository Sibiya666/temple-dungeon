namespace Dialoid.Core.Data.Json;

internal sealed class ItemJson
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? GrantsSkill { get; set; }
    public int SkillLevelBonus { get; set; }
}

internal sealed class ProgressionJson
{
    public List<double>? CharacterLevels { get; set; }
    public List<double>? SkillLevels { get; set; }
}
