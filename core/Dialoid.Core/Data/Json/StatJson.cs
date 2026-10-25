using Dialoid.Core.Stats;

namespace Dialoid.Core.Data.Json;

internal sealed class StatJson
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public double Default { get; set; }
    public double Min { get; set; } = double.NegativeInfinity;
    public double Max { get; set; } = double.PositiveInfinity;
    public StatDisplay Display { get; set; } = StatDisplay.Number;

    public StatDefinition ToDefinition() => new(Id!, Default, Min, Max, Display);
}
