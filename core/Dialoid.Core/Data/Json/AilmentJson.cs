using System.Text.Json;
using Dialoid.Core.Combat;

namespace Dialoid.Core.Data.Json;

internal sealed class AilmentJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "source_type", "chance_stat", "damage_fraction", "duration_sec", "max_stacks_stat", "stack_rule", "tag");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

    public string? Id { get; set; }
    public DamageType? SourceType { get; set; }
    public string? ChanceStat { get; set; }
    public double DamageFraction { get; set; }
    public double? DurationSec { get; set; }
    public string? MaxStacksStat { get; set; }
    public StackRule? StackRule { get; set; }
    public string? Tag { get; set; }
}
