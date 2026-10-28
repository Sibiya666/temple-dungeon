using System.Text.Json;
namespace Dialoid.Core.Data.Json;

internal sealed class TagJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
}
