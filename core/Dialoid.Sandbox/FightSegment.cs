using Dialoid.Core.Combat;
using Dialoid.Core.Simulation;

namespace Dialoid.Sandbox;

/// <summary>The meter's views of a segment (spec 8.1).</summary>
public enum MeterMode { DpsBySkill, DamageBySkill, DamageByType, Mitigation }

/// <summary>One meter bar: a stable key (skill id, damage type, mitigation step), its Russian label, the value and its share of
/// the mode's whole (0–1; 0 when the whole is 0).</summary>
public sealed record MeterRow(string Key, string Label, double Value, double Share);

/// <summary>
/// One fight of the training room (spec 6.4): from the first damage to the last one before <see cref="TrainingRoom.SegmentIdleSec"/>
/// without damage. The meter (<see cref="DamageBySkill"/> and the others) sums <b>every</b> damage event of the segment as it
/// arrives, so it stays exact however long the segment runs. The raw lists are bounded so an endless fight uses bounded memory:
/// <see cref="Damage"/> keeps the most recent <see cref="MaxStoredDamage"/>–2×<see cref="MaxStoredDamage"/> events and
/// <see cref="Events"/> the most recent <see cref="MaxStoredEvents"/>–2×<see cref="MaxStoredEvents"/>; while nothing was dropped
/// (<see cref="Trimmed"/> is false) the meter equals the sums over <see cref="Damage"/>. <see cref="DamageOverTime"/> uses a
/// per-step series that halves its resolution when it grows past <see cref="MaxSeriesPoints"/> (sums stay exact).
/// Times are fight times of the fight the segment belongs to (a build edit restarts the fight at 0).
/// </summary>
public sealed class FightSegment
{
    public const int MaxStoredDamage = 20_000;
    public const int MaxStoredEvents = 5_000;
    public const int MaxSeriesPoints = 8_192;

    private const double Eps = 1e-9;

    private readonly CombatLogText _names;
    private readonly List<SimEvent> _events = new();
    private readonly List<DamageEvent> _damage = new();
    private readonly SortedDictionary<string, PartSum> _bySkill = new(StringComparer.Ordinal);
    private readonly SortedDictionary<DamageType, double> _byType = new();
    private readonly PartSum _total = new();
    private readonly List<(double Time, double Damage)> _series = new();
    private double _seriesStep = LiveFight.StepSec;

    internal FightSegment(int index, double start, CombatLogText names)
    {
        Index = index;
        Start = End = start;
        _names = names;
    }

    /// <summary>Running number of the segment in the room (1, 2, …; not reset by <see cref="TrainingRoom.ResetMeter"/>).</summary>
    public int Index { get; }

    /// <summary>Time of the first damage.</summary>
    public double Start { get; }

    /// <summary>Time of the last damage so far.</summary>
    public double End { get; private set; }

    /// <summary>Last damage − first damage, at least one engine step: the DPS divisor.</summary>
    public double Duration => Math.Max(End - Start, LiveFight.StepSec);

    /// <summary>Still collecting damage (the room's <see cref="TrainingRoom.Current"/>).</summary>
    public bool IsOpen { get; private set; } = true;

    /// <summary>Some raw events were dropped from <see cref="Damage"/> or <see cref="Events"/> (the meter is still exact).</summary>
    public bool Trimmed { get; private set; }

    /// <summary>Core log events of the segment, oldest first (bounded, see the class summary).</summary>
    public IReadOnlyList<SimEvent> Events => _events;

    /// <summary>Hits and DoT ticks of the segment, oldest first (bounded, see the class summary).</summary>
    public IReadOnlyList<DamageEvent> Damage => _damage;

    /// <summary>Total final damage of the segment.</summary>
    public double TotalDamage => _total.Final;

    internal void Add(DamageEvent e)
    {
        End = Math.Max(End, e.Time);
        if (!_bySkill.TryGetValue(e.SkillId, out var skill)) _bySkill[e.SkillId] = skill = new PartSum();
        var final = 0.0;
        foreach (var p in e.Parts)
        {
            skill.Add(p);
            _total.Add(p);
            _byType[p.Type] = _byType.GetValueOrDefault(p.Type) + p.Final;
            final += p.Final;
        }
        AddToSeries(e.Time, final);
        Trimmed |= Keep(_damage, e, MaxStoredDamage);
    }

    internal void Add(SimEvent e) => Trimmed |= Keep(_events, e, MaxStoredEvents);

    internal void Close() => IsOpen = false;

    public IReadOnlyList<MeterRow> Rows(MeterMode mode) => mode switch
    {
        MeterMode.DpsBySkill => DpsBySkill(),
        MeterMode.DamageBySkill => DamageBySkill(),
        MeterMode.DamageByType => DamageByType(),
        MeterMode.Mitigation => Mitigation(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Segment damage of each skill / <see cref="Duration"/>, highest first.</summary>
    public IReadOnlyList<MeterRow> DpsBySkill()
    {
        var d = Duration;
        return Ranked(_bySkill.Select(kv => (kv.Key, _names.SkillName(kv.Key), kv.Value.Final / d)));
    }

    /// <summary>Final damage of each skill (triggered copies count for the skill they copy), highest first.</summary>
    public IReadOnlyList<MeterRow> DamageBySkill() => Ranked(_bySkill.Select(kv => (kv.Key, _names.SkillName(kv.Key), kv.Value.Final)));

    /// <summary>Final damage of each damage type, highest first.</summary>
    public IReadOnlyList<MeterRow> DamageByType() =>
        Ranked(_byType.Select(kv => (kv.Key.ToString().ToLowerInvariant(), Texts.DamageTypeName(kv.Key), kv.Value)));

    /// <summary>
    /// Where the damage before defense went, in a fixed order: «дошло», «броня», «сопротивления», «множитель получаемого урона»
    /// (negative when the target takes more), «обнулено». The values add up to the damage before defense; shares are of it.
    /// </summary>
    public IReadOnlyList<MeterRow> Mitigation()
    {
        var t = _total;
        var whole = t.BeforeDefense;
        MeterRow Row(string key, string label, double v) => new(key, label, v, whole == 0 ? 0 : v / whole);
        return new[]
        {
            Row("final", Texts.MitigationFinal, t.Final),
            Row("armor", Texts.MitigationArmor, t.ByArmor),
            Row("resist", Texts.MitigationResist, t.ByResist),
            Row("taken_multiplier", Texts.MitigationTaken, t.ByTakenMultiplier),
            Row("nullified", Texts.MitigationNullified, t.Nullified),
        };
    }

    /// <summary>The damage of each skill summed over types (Type carries no meaning), by skill id.</summary>
    public IReadOnlyDictionary<string, DamagePart> MitigationBySkill() =>
        new SortedDictionary<string, DamagePart>(_bySkill.ToDictionary(kv => kv.Key, kv => kv.Value.ToPart()), StringComparer.Ordinal);

    /// <summary>
    /// Final damage per bucket of <paramref name="bucketSec"/> from <see cref="Start"/>; every bucket up to the last damage is
    /// listed (empty ones with 0), each with its start time. The sum is the segment's damage.
    /// </summary>
    public IReadOnlyList<(double Time, double Damage)> DamageOverTime(double bucketSec)
    {
        if (!(bucketSec > 0)) throw new ArgumentOutOfRangeException(nameof(bucketSec), "bucket must be positive");
        var count = (int)Math.Floor((End - Start) / bucketSec + Eps) + 1;
        var sums = new double[count];
        foreach (var (time, damage) in _series)
            sums[Math.Clamp((int)Math.Floor((time - Start) / bucketSec + Eps), 0, count - 1)] += damage;
        return sums.Select((d, i) => (Start + i * bucketSec, d)).ToList();
    }

    private void AddToSeries(double time, double damage)
    {
        var slot = Start + Math.Floor((time - Start) / _seriesStep + Eps) * _seriesStep;
        if (_series.Count > 0 && Math.Abs(_series[^1].Time - slot) < Eps)
        {
            _series[^1] = (slot, _series[^1].Damage + damage);
            return;
        }
        _series.Add((slot, damage));
        if (_series.Count <= MaxSeriesPoints) return;

        // Halve the resolution: merge points into slots twice as wide (sums are kept).
        _seriesStep *= 2;
        var merged = new List<(double Time, double Damage)>(_series.Count / 2 + 1);
        foreach (var (t, d) in _series)
        {
            var s = Start + Math.Floor((t - Start) / _seriesStep + Eps) * _seriesStep;
            if (merged.Count > 0 && Math.Abs(merged[^1].Time - s) < Eps) merged[^1] = (s, merged[^1].Damage + d);
            else merged.Add((s, d));
        }
        _series.Clear();
        _series.AddRange(merged);
    }

    // Trimmed in batches, so adding stays amortized O(1). Returns whether something was dropped.
    private static bool Keep<T>(List<T> list, T item, int keep)
    {
        list.Add(item);
        if (list.Count < 2 * keep) return false;
        list.RemoveRange(0, list.Count - keep);
        return true;
    }

    private static IReadOnlyList<MeterRow> Ranked(IEnumerable<(string Key, string Label, double Value)> rows)
    {
        var list = rows.ToList();
        var whole = list.Sum(r => r.Value);
        return list
            .OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => new MeterRow(r.Key, r.Label, r.Value, whole == 0 ? 0 : r.Value / whole))
            .ToList();
    }

    private sealed class PartSum
    {
        public double BeforeDefense, ByArmor, ByResist, ByTakenMultiplier, Nullified, Final;

        public void Add(DamagePart p)
        {
            BeforeDefense += p.BeforeDefense;
            ByArmor += p.ByArmor;
            ByResist += p.ByResist;
            ByTakenMultiplier += p.ByTakenMultiplier;
            Nullified += p.Nullified;
            Final += p.Final;
        }

        public DamagePart ToPart() => new(default, BeforeDefense, ByArmor, ByResist, ByTakenMultiplier, Nullified, Final);
    }
}
