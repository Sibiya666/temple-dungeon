using System.Text.Json;
using Dialoid.Core.Passives;

namespace Dialoid.Core.Data.Json;

internal sealed class PassiveJson
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public PassiveKind? Kind { get; set; }
    public string? Region { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public List<string>? Links { get; set; }
    public JsonElement? Effects { get; set; }
    public string? StartForClass { get; set; }
}
