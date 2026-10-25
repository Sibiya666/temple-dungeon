using Dialoid.Core.Combat;

namespace Dialoid.Core.Simulation;

/// <summary>Mutable state of one simulation run: time, character resource and life, rotation memory, targets and zones.</summary>
internal sealed class SimState
{
    public double Time;
    public double Resource, ResourceMax;
    public double Life, LifeMax;
    public double? LastCritTime, LastHitTime;

    /// <summary>Absolute time the current cast/attack/channel tick ends.</summary>
    public double BusyUntil;

    /// <summary>Skill currently being channelled (null when not channelling) and when the channel began.</summary>
    public string? ChannelSkillId;
    public double ChannelStart;
    /// <summary>The current channel hit its <see cref="Skills.ChannelLimit"/>: the next use of the skill starts a new channel.</summary>
    public bool ChannelExhausted;

    public readonly Dictionary<string, double> CooldownReadyAt = new(StringComparer.Ordinal);
    public readonly Dictionary<string, double> LastUseTime = new(StringComparer.Ordinal);

    /// <summary>Active buffs: id → remaining seconds. Filled by procs (Task 14).</summary>
    public readonly Dictionary<string, double> Buffs = new(StringComparer.Ordinal);

    /// <summary>Mechanic charges/counters, keyed "mechanic_id:key" (see MechanicSimContext.Counter).</summary>
    public readonly Dictionary<string, double> MechanicCounters = new(StringComparer.Ordinal);

    public readonly List<TargetState> Targets = new();
    public readonly bool[] Dead;

    /// <summary>One ground zone per skill, keyed by skill id (ordinal order keeps ticking deterministic).</summary>
    public readonly SortedDictionary<string, GroundZone> Zones = new(StringComparer.Ordinal);

    public SimState(int targetCount, double maxLifePerTarget)
    {
        Dead = new bool[targetCount];
        for (var i = 0; i < targetCount; i++)
            Targets.Add(new TargetState
            {
                MaxLife = maxLifePerTarget,
                Life = maxLifePerTarget,
                Immortal = double.IsPositiveInfinity(maxLifePerTarget),
            });
    }

    /// <summary>Self-inflicted damage (life costs, blood magic): never lowers life below 1.</summary>
    public void DamageSelf(double amount)
    {
        if (amount <= 0 || Life <= 1) return;
        Life = Math.Max(1, Life - amount);
    }

    /// <summary>Pays a life cost. Refused (nothing paid) when current life is not above the cost, so the payer can never die from it.</summary>
    public bool TryPayLife(double cost)
    {
        if (cost <= 0) return true;
        if (Life <= cost) return false;
        Life -= cost;
        return true;
    }

    public bool IsAlive(int i) => !Dead[i];

    /// <summary>Lowest-index living target, or -1 when every target is dead.</summary>
    public int PrimaryIndex => Array.IndexOf(Dead, false);

    public TargetState? Primary => PrimaryIndex is var i and >= 0 ? Targets[i] : null;
}

/// <summary>A ground zone (burning ground etc.): ticks a fraction of the creating hit's ailment base per second on the targets it covered.</summary>
internal sealed record GroundZone(string SkillId, DamageType Type, double AilmentBase, double FractionPerSec, double Remaining,
    IReadOnlyList<int> TargetIndices)
{
    /// <summary>Created by a triggered copy: kills it causes fire no procs.</summary>
    public bool Triggered { get; init; }
}
