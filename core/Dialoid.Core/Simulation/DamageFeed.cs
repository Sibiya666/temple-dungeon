using Dialoid.Core.Combat;

namespace Dialoid.Core.Simulation;

/// <summary>
/// Where the engine reports every hit and DoT tick (<see cref="DamageEvent"/>). Always sums the parts into
/// <see cref="MitigationTotals"/> without storing the events (the offline simulator). With a positive keep (live fights) it also
/// queues the events for <see cref="Drain"/>; if nobody drains, the oldest are dropped so the queue holds keep to 2 × keep events
/// and an endless fight uses bounded memory.
/// </summary>
internal sealed class DamageFeed
{
    private readonly SortedDictionary<string, DamagePartSum> _bySkill = new(StringComparer.Ordinal);
    private readonly DamagePartSum _total = new();
    private readonly List<DamageEvent>? _pending;
    private readonly int _keep;

    /// <param name="keep">0: totals only; otherwise how many undrained events are kept (see the class summary).</param>
    public DamageFeed(int keep = 0)
    {
        if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep), "keep must not be negative");
        _keep = keep;
        if (keep > 0) _pending = new List<DamageEvent>();
    }

    /// <summary>One hit or tick. A hit with no damage part at all (a skill that deals no damage) is not an event.</summary>
    public void Add(double time, string skillId, int target, bool isDot, bool isCrit, IReadOnlyList<DamagePart> parts)
    {
        if (parts.Count == 0) return;
        if (!_bySkill.TryGetValue(skillId, out var sum)) _bySkill[skillId] = sum = new DamagePartSum();
        foreach (var p in parts)
        {
            sum.Add(p);
            _total.Add(p);
        }
        if (_pending == null) return;
        _pending.Add(new DamageEvent(time, skillId, target, isDot, isCrit, parts));
        if (_pending.Count >= 2 * _keep) _pending.RemoveRange(0, _pending.Count - _keep); // in batches: amortized O(1)
    }

    /// <summary>The events since the previous call (oldest first); always empty without a keep.</summary>
    public IReadOnlyList<DamageEvent> Drain()
    {
        if (_pending == null || _pending.Count == 0) return Array.Empty<DamageEvent>();
        var fresh = _pending.ToArray();
        _pending.Clear();
        return fresh;
    }

    /// <summary>A copy of the sums so far.</summary>
    public MitigationTotals Totals() =>
        new(new SortedDictionary<string, DamagePart>(_bySkill.ToDictionary(kv => kv.Key, kv => kv.Value.ToPart(default)), StringComparer.Ordinal),
            _total.ToPart(default));
}
