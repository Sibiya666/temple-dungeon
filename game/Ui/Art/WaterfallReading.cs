using Godot;
using Dialoid.Core.Explain;

namespace Dialoid.Game.Ui.Art;

/// <summary>One immutable gauge reading. Animation never changes its fraction or its text.</summary>
public sealed record WaterfallReading(string Name, double Value, double Max, Color Color, bool Alert = false)
{
    public bool Known => double.IsFinite(Value) && double.IsFinite(Max) && Max > 0 && Value >= 0;
    public float Fraction => Known ? (float)Math.Clamp(Value / Max, 0, 1) : 0;
    public string PercentText => Known ? $"{Math.Clamp(Math.Floor(Math.Round(Value / Max * 100d, 9)), 0, 100)}%" : "—";
    public string CurrentText => Known ? RuNumber.Grouped(Math.Floor(Value)) : "—";
    public string MaxText => Known ? RuNumber.Grouped(Math.Round(Max, MidpointRounding.AwayFromZero)) : "—";
}
