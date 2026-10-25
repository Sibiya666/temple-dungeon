using Dialoid.Core.Stats;

namespace Dialoid.Core.Combat;

/// <summary>Converts Percent of a skill's From damage into To damage (spec 6.3); Source is kept for the breakdown.</summary>
public sealed record Conversion(DamageType From, DamageType To, double Percent, SourceRef Source);
