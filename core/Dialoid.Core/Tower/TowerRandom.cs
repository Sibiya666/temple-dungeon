using Dialoid.Core.Simulation;

namespace Dialoid.Core.Tower;

/// <summary>The one way the tower module picks among n options (deterministic for a seeded Roll source).</summary>
internal static class TowerRandom
{
    public static int Pick(IRandomSource rng, int n) => Math.Min(n - 1, (int)Math.Floor(rng.InRange(0, n)));
}
