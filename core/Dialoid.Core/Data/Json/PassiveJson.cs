using System.Text.Json;
using Dialoid.Core.Passives;

namespace Dialoid.Core.Data.Json;

internal sealed class PassiveJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "description", "kind", "region", "x", "y", "links", "effects", "start_for_class");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

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
