using System.Text.Json;
namespace Dialoid.Core.Data.Json;

internal sealed class ItemJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "grants_skill", "skill_level_bonus");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? GrantsSkill { get; set; }
    public int SkillLevelBonus { get; set; }
}

internal sealed class ProgressionJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("character_levels", "skill_levels");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

    public List<double>? CharacterLevels { get; set; }
    public List<double>? SkillLevels { get; set; }
}
