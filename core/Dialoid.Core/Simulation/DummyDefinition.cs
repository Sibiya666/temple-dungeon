using Dialoid.Core.Combat;

namespace Dialoid.Core.Simulation;

/// <summary>Training dummy preset (spec 11.1). LifePerTarget is multiplied by G(level); +∞ means immortal.</summary>
public sealed record DummyDefinition(string Id, string Name, int PackSize, double PackRadius, double LifePerTarget,
    bool Respawn, double Armor, IReadOnlyDictionary<DamageType, double> Resists, double FreezeDurationMultiplier, DummyAttack? Attack)
{
    public const int MaxPackSize = 10;

    public bool Immortal => double.IsPositiveInfinity(LifePerTarget);
}

/// <summary>Dummy attack for the "dummy hits the character" mode (Task 15). DamagePerHit is multiplied by G(level).</summary>
public sealed record DummyAttack(IReadOnlyDictionary<DamageType, double> DamagePerHit, double IntervalSec);
