namespace Dialoid.Core.Stats;

public enum StatDisplay { Number, Percent }

public sealed record StatDefinition(string Id, double Default, double Min, double Max, StatDisplay Display);
