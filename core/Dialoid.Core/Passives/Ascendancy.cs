using Dialoid.Core.Effects;

namespace Dialoid.Core.Passives;

public sealed record AscendancyNode(string Id, string Name, string Description, int Cost, IReadOnlyList<Effect> Effects);

/// <summary>A class subclass tree: a free start node plus notables bought with ascendancy points.</summary>
public sealed record Ascendancy(string Id, string Name, string ClassId, AscendancyNode Start, IReadOnlyList<AscendancyNode> Notables)
{
    public const int DefaultNotableCost = 2;

    /// <summary>Unlocks at level 20: 0 points below, then +2 at levels 20, 30, 40 and 50 (8 in total).</summary>
    public static int PointsForLevel(int level) => level < 20 ? 0 : 2 * (Math.Min(level, 50) / 10 - 1);
}
