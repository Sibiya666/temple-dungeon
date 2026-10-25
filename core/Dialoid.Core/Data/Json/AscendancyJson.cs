using System.Text.Json;

namespace Dialoid.Core.Data.Json;

internal sealed class AscendancyNodeJson
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? Cost { get; set; }
    public JsonElement? Effects { get; set; }
}

internal sealed class AscendancyJson
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? ClassId { get; set; }
    public AscendancyNodeJson? Start { get; set; }
    public List<AscendancyNodeJson>? Notables { get; set; }
}
