using System.Text.Json;

namespace Dialoid.Core.Data.Json;

internal sealed class AscendancyNodeJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "description", "cost", "effects");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? Cost { get; set; }
    public JsonElement? Effects { get; set; }
}

internal sealed class AscendancyJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "class_id", "start", "notables");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
        if (StrictJson.TryObject(el, "start", out var start)) AscendancyNodeJson.CheckFields(start, file, id, errors);
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("notables", out var notables) && notables.ValueKind == JsonValueKind.Array)
            foreach (var n in notables.EnumerateArray()) AscendancyNodeJson.CheckFields(n, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? ClassId { get; set; }
    public AscendancyNodeJson? Start { get; set; }
    public List<AscendancyNodeJson>? Notables { get; set; }
}
