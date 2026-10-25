using Dialoid.Core.Combat;
using Dialoid.Core.Data;

namespace Dialoid.Core.Simulation;

public sealed record AilmentInstance(string AilmentId, DamageType Type, double DamagePerSec, double Remaining, string SourceSkillId)
{
    /// <summary>Shock: increased damage taken %; chill: slow %; otherwise 0.</summary>
    public double Magnitude { get; init; }
    /// <summary>Applied by a triggered copy: kills it causes fire no procs.</summary>
    public bool Triggered { get; init; }
}

/// <summary>Mutable state of a target: life, active ailments, freeze and freeze immunity (spec 6.7).</summary>
public sealed class TargetState
{
    public const double FreezeImmunitySec = 3;

    private readonly List<AilmentInstance> _ailments = new();
    private readonly SortedDictionary<string, double> _damageTakenSources = new(StringComparer.Ordinal);

    public double Life, MaxLife;
    public bool Immortal;
    public TargetDefense Defense = new(0, new Dictionary<DamageType, double>(), 0);
    public double FreezeRemaining, FreezeImmunityRemaining;
    /// <summary>Scales applied freeze durations; the simulator sets it from the dummy (boss = 0.5).</summary>
    public double FreezeDurationMultiplier = 1.0;

    public IReadOnlyList<AilmentInstance> Ailments => _ailments;
    public bool IsFrozen => FreezeRemaining > 0;

    /// <summary>Spec 6.6 additive bucket: strongest active shock plus the keyed contributions set by mechanics.</summary>
    public double DamageTakenIncreased =>
        _ailments.Where(a => a.AilmentId == "shock").Select(a => a.Magnitude).DefaultIfEmpty(0).Max() + _damageTakenSources.Values.Sum();

    /// <summary>Sets (0 removes) one keyed contribution to the damage-taken bucket; cleared on death/respawn.</summary>
    public void SetDamageTaken(string key, double increasedPercent)
    {
        if (increasedPercent == 0) _damageTakenSources.Remove(key);
        else _damageTakenSources[key] = increasedPercent;
    }

    /// <summary>Active instances of an ailment (freeze: 1 while frozen).</summary>
    public int Stacks(string ailmentId) =>
        ailmentId == "freeze" ? (IsFrozen ? 1 : 0) : _ailments.Count(a => a.AilmentId == ailmentId);

    /// <summary>Removes every instance of an ailment. Ending a freeze starts the usual freeze immunity.</summary>
    public void RemoveAilment(string ailmentId)
    {
        if (ailmentId == "freeze")
        {
            if (!IsFrozen) return;
            FreezeRemaining = 0;
            FreezeImmunityRemaining = FreezeImmunitySec;
            return;
        }
        _ailments.RemoveAll(a => a.AilmentId == ailmentId);
    }

    public bool HasAilment(string ailmentId) =>
        ailmentId == "freeze" ? IsFrozen : _ailments.Any(a => a.AilmentId == ailmentId);

    /// <summary>Applies an instance by its stacking rule. Returns false when it was refused (frozen or immune to freeze,
    /// weaker than the current one, or no free stack and not stronger than the weakest).</summary>
    public bool Apply(AilmentInstance a, int maxStacks, StackRule rule)
    {
        if (a.AilmentId == "freeze")
        {
            if (IsFrozen || FreezeImmunityRemaining > 0) return false;
            FreezeRemaining = a.Remaining * FreezeDurationMultiplier;
            return true;
        }

        switch (rule)
        {
            case StackRule.Unlimited:
                _ailments.Add(a);
                return true;
            case StackRule.StrongestOnly:
            {
                var idx = _ailments.FindIndex(x => x.AilmentId == a.AilmentId);
                if (idx < 0) _ailments.Add(a);
                else if (Strength(a) >= Strength(_ailments[idx])) _ailments[idx] = a;
                else return false;
                return true;
            }
            default: // ReplaceWeakest
            {
                var count = 0;
                var weakest = -1;
                for (var i = 0; i < _ailments.Count; i++)
                {
                    if (_ailments[i].AilmentId != a.AilmentId) continue;
                    count++;
                    if (weakest < 0 || _ailments[i].DamagePerSec < _ailments[weakest].DamagePerSec) weakest = i;
                }
                if (count < maxStacks) _ailments.Add(a);
                else if (weakest >= 0 && a.DamagePerSec > _ailments[weakest].DamagePerSec) _ailments[weakest] = a;
                else return false;
                return true;
            }
        }
    }

    /// <summary>Death or respawn: ailments, freeze and freeze immunity are cleared and life is restored.</summary>
    public void Reset()
    {
        _ailments.Clear();
        _damageTakenSources.Clear();
        FreezeRemaining = 0;
        FreezeImmunityRemaining = 0;
        Life = MaxLife;
    }

    private static double Strength(AilmentInstance a) => a.Magnitude != 0 ? a.Magnitude : a.DamagePerSec;

    /// <summary>Advances time; returns DoT damage dealt during dt. Expired ailments are removed.
    /// <paramref name="onDamage"/> receives each instance's share (for per-skill/type attribution).</summary>
    public double Tick(double dt, Action<AilmentInstance, double>? onDamage = null)
    {
        double dmg = 0;
        for (var i = _ailments.Count - 1; i >= 0; i--)
        {
            var a = _ailments[i];
            var share = a.DamagePerSec * Math.Min(dt, a.Remaining);
            dmg += share;
            if (share != 0) onDamage?.Invoke(a, share);
            var rem = a.Remaining - dt;
            if (rem <= 0) _ailments.RemoveAt(i);
            else _ailments[i] = a with { Remaining = rem };
        }

        if (FreezeRemaining > 0)
        {
            var left = FreezeRemaining - dt;
            if (left > 0) FreezeRemaining = left;
            else
            {
                FreezeRemaining = 0;
                FreezeImmunityRemaining = Math.Max(0, FreezeImmunitySec + left); // time past the end already counts toward immunity
            }
        }
        else if (FreezeImmunityRemaining > 0)
            FreezeImmunityRemaining = Math.Max(0, FreezeImmunityRemaining - dt);
        return dmg;
    }
}
