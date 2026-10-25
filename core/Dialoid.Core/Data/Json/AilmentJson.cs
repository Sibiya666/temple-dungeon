using Dialoid.Core.Combat;

namespace Dialoid.Core.Data.Json;

internal sealed class AilmentJson
{
    public string? Id { get; set; }
    public DamageType? SourceType { get; set; }
    public string? ChanceStat { get; set; }
    public double DamageFraction { get; set; }
    public double? DurationSec { get; set; }
    public string? MaxStacksStat { get; set; }
    public StackRule? StackRule { get; set; }
    public string? Tag { get; set; }
}
