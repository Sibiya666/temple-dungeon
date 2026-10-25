using Dialoid.Core.Combat;

namespace Dialoid.Core.Simulation;

/// <summary>Damage accounting by target, skill, type and hit vs damage over time. Sorted maps keep sums deterministic.</summary>
internal sealed class DamageLedger
{
    public readonly double[] ByTarget;
    public readonly SortedDictionary<string, double> BySkill = new(StringComparer.Ordinal);
    public readonly SortedDictionary<DamageType, double> ByType = new();
    public double Hit, Dot;

    public DamageLedger(int targets) => ByTarget = new double[targets];

    public void AddHit(string skillId, int target, DamageType type, double amount)
    {
        Hit += amount;
        Add(skillId, target, type, amount);
    }

    public void AddDot(string skillId, int target, DamageType type, double amount)
    {
        Dot += amount;
        Add(skillId, target, type, amount);
    }

    private void Add(string skillId, int target, DamageType type, double amount)
    {
        ByTarget[target] += amount;
        BySkill[skillId] = BySkill.GetValueOrDefault(skillId) + amount;
        ByType[type] = ByType.GetValueOrDefault(type) + amount;
    }
}
