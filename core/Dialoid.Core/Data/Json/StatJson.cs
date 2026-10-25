using System.Text.Json;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Data.Json;

internal sealed class StatJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("id", "name", "default", "min", "max", "display");

    public static void CheckFields(JsonElement el, string file, string? id, List<DataError> errors)
    {
        StrictJson.RejectUnknown(el, Allowed, file, id, errors);
    }

    public string? Id { get; set; }
    public string? Name { get; set; }
    public double Default { get; set; }
    public double Min { get; set; } = double.NegativeInfinity;
    public double Max { get; set; } = double.PositiveInfinity;
    public StatDisplay Display { get; set; } = StatDisplay.Number;

    public StatDefinition ToDefinition() => new(Id!, Default, Min, Max, Display);
}
