using Dialoid.Core.Combat;
using Dialoid.Core.Simulation;

namespace Dialoid.Sandbox;

/// <summary>What a <see cref="VisualEvent"/> asks the scene to show.</summary>
public enum VisualKind
{
    /// <summary>A skill was used: play its animation stretched to <see cref="VisualEvent.UseSec"/>.</summary>
    SkillUsed,
    /// <summary>Damage on a target (a hit or DoT ticks): flash and a floating number.</summary>
    Hit,
    /// <summary>An ailment landed on a target.</summary>
    AilmentApplied,
    /// <summary>A key press was refused: the short text over the hero and the slot blinks.</summary>
    Refused,
}

/// <summary>
/// One thing the 3D scene shows for the training room (spec 4 «Экран боя», 5). Slots are the player's (keys 1–5); a skill not in
/// a slot (a triggered copy such as Fireball's explosion) has no slot.
/// </summary>
/// <param name="Time">Fight time of the event.</param>
/// <param name="Slot">The player slot of the skill (the pressed slot for a refusal, even an empty one); null for a skill in no slot.</param>
/// <param name="Target">The target index (0 is the first pack member) for hits and ailments.</param>
public sealed record VisualEvent(VisualKind Kind, double Time, int? Slot, string? SkillId, int? Target)
{
    /// <summary>Hit: the damage type with the largest final part (the flash color and the number's icon).</summary>
    public DamageType? DamageType { get; init; }

    /// <summary>Hit: final damage (several merged hits: their sum).</summary>
    public double Amount { get; init; }

    /// <summary>Hit: a rolled crit (merged: any of them).</summary>
    public bool IsCrit { get; init; }

    /// <summary>Hit: DoT ticks (only a flash, no hit reaction).</summary>
    public bool IsDot { get; init; }

    /// <summary>
    /// SkillUsed: how long the hero is busy with the use (<see cref="SimEvent.UseSec"/>). A use chained right after the previous
    /// one may have started up to one engine step before <see cref="VisualEvent.Time"/>; the scene does not need to make up for it.
    /// </summary>
    public double? UseSec { get; init; }

    /// <summary>AilmentApplied: the ailment id (freeze included).</summary>
    public string? AilmentId { get; init; }

    /// <summary>Refused: why.</summary>
    public PressRefusal Refusal { get; init; }

    /// <summary>Refused: the short Russian text over the hero («Перезарядка 2,1 с», «Не хватает маны», «Цель не видна»).</summary>
    public string? RefusalText { get; init; }
}

/// <summary>
/// The training room's events for the 3D scene, which takes them every frame with <see cref="DrainVisual"/>. The queue holds at most
/// <see cref="MaxQueued"/> events (the oldest are dropped when nobody drains: a minimized window does not pile up work). DoT ticks
/// of one step are summed per (target, skill, ailment or zone). One drain returns at most <see cref="MaxFloatingHits"/>
/// <see cref="VisualKind.Hit"/> events: past that, hits on one target are merged (amounts summed), so «Рой» with ailments does
/// not flood the screen with numbers. Single-threaded, like the room.
/// </summary>
public sealed class RoomFeed
{
    public const int MaxQueued = 512;
    public const int MaxFloatingHits = 30;

    private readonly Queue<VisualEvent> _queue = new();

    /// <summary>Events waiting for the next drain.</summary>
    public int Count => _queue.Count;

    /// <summary>The events since the previous call, oldest first, with floating hits capped (see the class summary).</summary>
    public IReadOnlyList<VisualEvent> DrainVisual()
    {
        if (_queue.Count == 0) return Array.Empty<VisualEvent>();
        var events = _queue.ToList();
        _queue.Clear();
        return CapHits(events);
    }

    internal void Clear() => _queue.Clear();

    internal void Push(VisualEvent e)
    {
        _queue.Enqueue(e);
        if (_queue.Count > MaxQueued) _queue.Dequeue();
    }

    /// <summary>One engine step: uses, hits (joined to their damage by <see cref="SimEvent.DamageSeq"/>), ailments, then the
    /// step's DoT ticks summed per (target, skill, ailment or zone).</summary>
    internal void Step(IReadOnlyList<SimEvent> events, IReadOnlyList<DamageEvent> damage, Func<string, int?> slotOf)
    {
        Dictionary<long, DamageEvent>? hits = null;
        Dictionary<(int Target, string SkillId, string? Id), (DamageEvent Last, double Amount, DamageType Type, double Largest)>? dots = null;
        foreach (var d in damage)
        {
            if (!d.IsDot)
            {
                (hits ??= new Dictionary<long, DamageEvent>())[d.Seq] = d;
                continue;
            }
            dots ??= new();
            var key = (d.Target, d.SkillId, d.AilmentId ?? d.ZoneId);
            var (type, amount) = Main(d);
            dots[key] = dots.TryGetValue(key, out var sum)
                ? (d, sum.Amount + amount, amount > sum.Largest ? type : sum.Type, Math.Max(sum.Largest, amount))
                : (d, amount, type, amount);
        }

        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case "use" when e.SkillId is { } skill:
                    Push(new VisualEvent(VisualKind.SkillUsed, e.Time, slotOf(skill), skill, null) { UseSec = e.UseSec });
                    break;
                case "hit" when e.DamageSeq is { } seq && hits != null && hits.Remove(seq, out var hit):
                    Push(Hit(hit, slotOf));
                    break;
                case "ailment" when e.SkillId is { } skill:
                    Push(new VisualEvent(VisualKind.AilmentApplied, e.Time, slotOf(skill), skill, e.Target) { AilmentId = e.AilmentId });
                    break;
            }
        }
        if (hits is { Count: > 0 })
            foreach (var hit in hits.Values.OrderBy(h => h.Seq)) Push(Hit(hit, slotOf));
        if (dots != null)
            foreach (var ((target, skill, _), sum) in dots)
                Push(new VisualEvent(VisualKind.Hit, sum.Last.Time, slotOf(skill), skill, target)
                    { DamageType = sum.Type, Amount = sum.Amount, IsDot = true });
    }

    private static VisualEvent Hit(DamageEvent d, Func<string, int?> slotOf)
    {
        var (type, amount) = Main(d);
        return new VisualEvent(VisualKind.Hit, d.Time, slotOf(d.SkillId), d.SkillId, d.Target)
            { DamageType = type, Amount = amount, IsCrit = d.IsCrit };
    }

    // The type with the largest final part (by damage before defense when nothing got through) and the event's final damage.
    private static (DamageType Type, double Amount) Main(DamageEvent d)
    {
        var main = d.Parts.Count == 0 ? null : d.Parts.MaxBy(p => (p.Final, p.BeforeDefense));
        return (main?.Type ?? default, d.Parts.Sum(p => p.Final));
    }

    // Past MaxFloatingHits: merge hits per (target, dot or not), then per target; as a last resort keep the largest.
    private static IReadOnlyList<VisualEvent> CapHits(List<VisualEvent> events)
    {
        var count = events.Count(e => e.Kind == VisualKind.Hit);
        if (count <= MaxFloatingHits) return events;
        var merged = Merge(events, e => (e.Target, e.IsDot));
        if (merged.Count(e => e.Kind == VisualKind.Hit) > MaxFloatingHits) merged = Merge(merged, e => (e.Target, false));
        if (merged.Count(e => e.Kind == VisualKind.Hit) <= MaxFloatingHits) return merged;
        var keep = merged.Where(e => e.Kind == VisualKind.Hit).OrderByDescending(e => e.Amount).Take(MaxFloatingHits).ToHashSet();
        return merged.Where(e => e.Kind != VisualKind.Hit || keep.Contains(e)).ToList();
    }

    // Each group of hits becomes one hit at the place of its last member: amounts summed, the largest member's slot, skill and type.
    private static List<VisualEvent> Merge(List<VisualEvent> events, Func<VisualEvent, (int?, bool)> key)
    {
        var groups = events.Where(e => e.Kind == VisualKind.Hit).GroupBy(key).ToDictionary(g => g.Key, g => g.ToList());
        var last = new Dictionary<VisualEvent, List<VisualEvent>>(ReferenceEqualityComparer.Instance);
        foreach (var g in groups.Values) last[g[^1]] = g;
        var result = new List<VisualEvent>(events.Count);
        foreach (var e in events)
        {
            if (e.Kind != VisualKind.Hit)
            {
                result.Add(e);
                continue;
            }
            if (!last.TryGetValue(e, out var group)) continue;
            var largest = group.MaxBy(h => h.Amount)!;
            result.Add(largest with
            {
                Time = e.Time,
                Target = e.Target,
                Amount = group.Sum(h => h.Amount),
                IsCrit = group.Any(h => h.IsCrit),
                IsDot = group.All(h => h.IsDot),
            });
        }
        return result;
    }
}
