namespace Dialoid.Core.Combat;

/// <summary>A slice of raw damage of one type; Lineage holds every type it has been through, always including Type (spec 6.3).</summary>
public sealed record DamagePortion(DamageType Type, double Min, double Max, IReadOnlySet<DamageType> Lineage);
