using Dialoid.Core.Combat;
using Dialoid.Core.Simulation;

namespace Dialoid.Sandbox;

/// <summary>
/// Builds the training room's combat log (spec 7) from the live fight's events, one engine step at a time. Single-threaded.
/// <list type="bullet">
/// <item>A "hit" event is shown as its <see cref="DamageEvent"/>, joined by <see cref="SimEvent.DamageSeq"/> (the damage event has
/// the damage types and the crit); "crit" is folded into it; a hit with no damage part at all has no damage event and is not shown.</item>
/// <item>DoT ticks (one per ailment instance or zone and step) are summed per (skill, ailment or zone, target) over
/// <see cref="DotWindowSec"/> of the segment into one <see cref="LogKind.DotTick"/> line; the window's line is written when the
/// window is over or the segment closes, so the lines' amounts add up to the meter.</item>
/// <item>The same refusal again (same segment, slot, skill and reason) with only DoT lines in between updates the previous line's
/// <see cref="CombatLogEntry.Repeat"/> instead of writing a new one.</item>
/// <item>"no_resource" (a held key waiting for its skill) and "proc_blocked" (engine bookkeeping) are not shown.</item>
/// </list>
/// Lines inside a segment carry its index and the time from its start; lines outside carry fight time and no segment.
/// </summary>
internal sealed class CombatLog
{
    public const double DotWindowSec = 1;

    private const double Eps = 1e-9;

    private readonly CombatLogText _text;
    private readonly int _keep;
    private readonly List<CombatLogEntry> _entries = new(); // up to 2 × _keep; trimmed in batches, shown through the tails
    private readonly Dictionary<DotKey, DotSum> _dots = new();
    private (CombatLogEntry Entry, int Slot, PressResult Result)? _lastRefusal;
    private bool _showTargets;
    private long _dotOrder;

    public CombatLog(CombatLogText text, int keep)
    {
        _text = text;
        _keep = keep;
        Entries = new Tail<CombatLogEntry>(_entries, keep, e => e);
        Lines = new Tail<string>(_entries, keep, e => e.Text);
    }

    /// <summary>The newest lines, oldest first, at most the keep.</summary>
    public IReadOnlyList<CombatLogEntry> Entries { get; }

    /// <summary>The texts of <see cref="Entries"/>.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>A new fight: whether lines name the target (a pack).</summary>
    public void Restart(bool showTargets) => _showTargets = showTargets;

    /// <summary>A line of the room itself (restart, fight not started), without time.</summary>
    public void System(double fightTime, string text) => Add(new CombatLogEntry(null, fightTime, LogKind.System, null, null, null, text, 1));

    public void Refusal(FightSegment? segment, double fightTime, int slot, string? skillId, PressResult r)
    {
        var (index, time) = Place(segment, fightTime);
        if (_lastRefusal is { } last && last.Entry.Segment == index && last.Slot == slot && last.Entry.SkillId == skillId
            && last.Result.Refusal == r.Refusal && FindBack(last.Entry) is var at and >= 0)
        {
            var repeat = last.Entry.Repeat + 1;
            var merged = last.Entry with { Repeat = repeat, Text = _text.Refusal(last.Entry.Time, slot, skillId, last.Result, repeat) };
            _entries[at] = merged;
            _lastRefusal = (merged, slot, last.Result);
            return;
        }
        var entry = new CombatLogEntry(index, time, LogKind.Refusal, skillId, null, null, _text.Refusal(time, slot, skillId, r), 1);
        Add(entry);
        _lastRefusal = (entry, slot, r);
    }

    /// <summary>One engine step's events and damage; <paramref name="segment"/> is the room's open segment after the step's damage
    /// (null when none), <paramref name="now"/> the fight time at the end of the step.</summary>
    public void Step(FightSegment? segment, double now, IReadOnlyList<SimEvent> events, IReadOnlyList<DamageEvent> damage)
    {
        Dictionary<long, DamageEvent>? hits = null;
        foreach (var d in damage)
        {
            if (d.IsDot)
            {
                if (segment != null) AddDot(segment, d);
                continue;
            }
            (hits ??= new Dictionary<long, DamageEvent>())[d.Seq] = d;
        }

        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case "hit":
                    if (e.DamageSeq is { } seq && hits != null && hits.Remove(seq, out var hit)) AddHit(segment, hit);
                    break;
                case "crit":
                case "no_resource":
                case "proc_blocked":
                    break;
                default:
                    AddEvent(segment, e);
                    break;
            }
        }
        if (hits is { Count: > 0 })
            foreach (var hit in hits.Values.OrderBy(h => h.Seq)) AddHit(segment, hit);

        if (segment != null) FlushDots(segment, now);
    }

    /// <summary>The segment closed: its open DoT windows are written now.</summary>
    public void SegmentClosed(FightSegment segment) => FlushDots(segment, double.PositiveInfinity);

    public IReadOnlyList<CombatLogEntry> Filter(ISet<LogKind> kinds, string? skillId) =>
        Entries.Where(e => kinds.Contains(e.Kind) && (skillId == null || e.SkillId == skillId)).ToList();

    private void AddHit(FightSegment? segment, DamageEvent d)
    {
        var (index, time) = Place(segment, d.Time);
        Add(new CombatLogEntry(index, time, d.IsCrit ? LogKind.Crit : LogKind.Hit, d.SkillId, d.Target, d.Parts.Sum(p => p.Final),
            _text.Hit(time, d, _showTargets), 1));
    }

    private void AddEvent(FightSegment? segment, SimEvent e)
    {
        var (index, time) = Place(segment, e.Time);
        var (kind, skillId) = e.Kind switch
        {
            "use" => (LogKind.Use, e.SkillId),
            "ailment" => (LogKind.Ailment, e.SkillId),
            "proc" => (LogKind.Proc, e.SourceSkillId ?? e.SkillId),
            "mechanic" => (LogKind.Mechanic, e.SkillId),
            _ => (LogKind.System, (string?)null),
        };
        Add(new CombatLogEntry(index, time, kind, skillId, e.Target, null, _text.Line(time, e, _showTargets), 1));
    }

    private void AddDot(FightSegment segment, DamageEvent d)
    {
        var window = (long)Math.Floor((d.Time - segment.Start) / DotWindowSec + Eps);
        var key = new DotKey(d.SkillId, d.AilmentId, d.ZoneId, d.Target);
        if (_dots.TryGetValue(key, out var sum) && sum.Window != window)
        {
            Write(segment, key, sum);
            sum = null;
        }
        if (sum == null) _dots[key] = sum = new DotSum(window, ++_dotOrder);
        sum.LastTime = d.Time;
        foreach (var p in d.Parts) sum.ByType[p.Type] = sum.ByType.GetValueOrDefault(p.Type) + p.Final;
    }

    // Writes (in the order the windows started) every DoT window that ended by `now`.
    private void FlushDots(FightSegment segment, double now)
    {
        if (_dots.Count == 0) return;
        var due = _dots.Where(kv => segment.Start + (kv.Value.Window + 1) * DotWindowSec <= now + Eps)
            .OrderBy(kv => kv.Value.Order).ToList();
        foreach (var (key, sum) in due)
        {
            _dots.Remove(key);
            Write(segment, key, sum);
        }
    }

    private void Write(FightSegment segment, DotKey key, DotSum sum)
    {
        var time = sum.LastTime - segment.Start;
        Add(new CombatLogEntry(segment.Index, time, LogKind.DotTick, key.SkillId, key.Target, sum.ByType.Values.Sum(),
            _text.DotTicks(time, key.SkillId, key.AilmentId, key.Target, _showTargets, sum.ByType.Select(kv => (kv.Key, kv.Value))), 1));
    }

    private static (int? Index, double Time) Place(FightSegment? segment, double fightTime) =>
        segment != null && fightTime >= segment.Start - Eps ? (segment.Index, Math.Max(0, fightTime - segment.Start)) : (null, fightTime);

    // Where the last refusal still is: only DoT lines may follow it (anything else ends the run of identical refusals).
    private int FindBack(CombatLogEntry entry)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_entries[i], entry)) return i;
            if (_entries[i].Kind != LogKind.DotTick) return -1;
        }
        return -1;
    }

    private void Add(CombatLogEntry entry)
    {
        if (entry.Kind != LogKind.DotTick) _lastRefusal = null;
        _entries.Add(entry);
        if (_entries.Count >= 2 * _keep) _entries.RemoveRange(0, _entries.Count - _keep); // in batches: amortized O(1)
    }

    private readonly record struct DotKey(string SkillId, string? AilmentId, string? ZoneId, int Target);

    private sealed class DotSum
    {
        public DotSum(long window, long order)
        {
            Window = window;
            Order = order;
        }

        public long Window { get; }
        public long Order { get; }
        public double LastTime { get; set; }
        public SortedDictionary<DamageType, double> ByType { get; } = new();
    }

    /// <summary>The newest <c>keep</c> entries of the list (which holds up to twice as many), oldest first, projected.</summary>
    private sealed class Tail<T> : IReadOnlyList<T>
    {
        private readonly List<CombatLogEntry> _list;
        private readonly int _keep;
        private readonly Func<CombatLogEntry, T> _project;

        public Tail(List<CombatLogEntry> list, int keep, Func<CombatLogEntry, T> project)
        {
            _list = list;
            _keep = keep;
            _project = project;
        }

        public int Count => Math.Min(_list.Count, _keep);

        public T this[int index] =>
            (uint)index < (uint)Count ? _project(_list[_list.Count - Count + index]) : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<T> GetEnumerator()
        {
            for (var i = _list.Count - Count; i < _list.Count; i++) yield return _project(_list[i]);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
