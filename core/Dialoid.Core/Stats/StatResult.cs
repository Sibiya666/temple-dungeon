namespace Dialoid.Core.Stats;

/// <summary>Value already includes the per-stat scaling.</summary>
public sealed record BreakdownLine(SourceRef Source, ModKind Kind, double Value);

public sealed record StatResult(string Stat, double Base, double Uncapped, double Value,
    IReadOnlyList<BreakdownLine> Lines, BreakdownLine? AppliedOverride);
