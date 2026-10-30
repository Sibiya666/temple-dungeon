using Dialoid.Core.Combat;
using Dialoid.Core.Explain;
using Dialoid.Core.Simulation;

namespace Dialoid.Sandbox;

/// <summary>The meter's views of a segment (spec 8.1).</summary>
public enum MeterMode { DpsBySkill, DamageBySkill, DamageByType, Mitigation }

/// <summary>One meter bar: a stable key (skill id, damage type, mitigation step), its Russian label, the value and its share of
/// the mode's whole (0–1; 0 when the whole is 0). <paramref name="IsBonus"/>: in «Потеря на защите», damage the defense step
/// <b>added</b> (vulnerability, penetration below zero), drawn in another color with a positive value and the «+N от …» label.</summary>
public sealed record MeterRow(string Key, string Label, double Value, double Share, bool IsBonus = false);

/// <summary>
/// Where damage before defence went (spec 8.1, 8.3), with each hit part's resistance and damage-taken steps split by their sign:
/// a loss (positive) or a bonus (penetration below zero, vulnerability), both stored positive. Identity:
/// <see cref="BeforeDefense"/> − <see cref="Armor"/> − <see cref="ResistLoss"/> + <see cref="ResistBonus"/> − <see cref="TakenLoss"/>
/// + <see cref="TakenBonus"/> − <see cref="Nullified"/> = <see cref="Final"/>.
/// </summary>
public sealed record DefenceSplit(double BeforeDefense, double Armor, double ResistLoss, double ResistBonus, double TakenLoss,
    double TakenBonus, double Nullified, double Final);

/// <summary>
/// One fight of the training room (spec 6.4): from the first damage to the last one before <see cref="TrainingRoom.SegmentIdleSec"/>
/// without damage. The meter (<see cref="DamageBySkill"/> and the others) sums <b>every</b> damage event of the segment as it
/// arrives, so it stays exact however long the segment runs. The raw lists are bounded so an endless fight uses bounded memory:
/// <see cref="Damage"/> keeps the most recent <see cref="MaxStoredDamage"/>–2×<see cref="MaxStoredDamage"/> events and
/// <see cref="Events"/> the most recent <see cref="MaxStoredEvents"/>–2×<see cref="MaxStoredEvents"/>; while nothing was dropped
/// (<see cref="Trimmed"/> is false) the meter equals the sums over <see cref="Damage"/>. <see cref="DamageOverTime"/> uses per-step
/// series (the whole segment and one per skill) that halve their resolution when they grow past <see cref="MaxSeriesPoints"/>
/// (sums stay exact). Times are fight times of the fight the segment belongs to (a build edit restarts the fight at 0).
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
    private readonly Series _series;
    private readonly Dictionary<string, Series> _seriesBySkill = new(StringComparer.Ordinal);
    private readonly double? _previousDps;

    /// <param name="previousDps">DPS of the segment before this one (closed by then), for <see cref="DpsChangeVsPrevious"/>.</param>
    internal FightSegment(int index, double start, CombatLogText names, string dummyId = TrainingRoom.DefaultDummyId,
        bool afterBuildChange = false, double? previousDps = null)
    {
        Index = index;
        Start = End = start;
        _names = names;
        DummyId = dummyId;
        AfterBuildChange = afterBuildChange;
        _previousDps = previousDps;
        _series = new Series(start);
    }

    /// <summary>Running number of the segment in the room (1, 2, …; not reset by <see cref="TrainingRoom.ResetMeter"/>).</summary>
    public int Index { get; }

    /// <summary>The dummy preset the segment was fought against.</summary>
    public string DummyId { get; }

    /// <summary>The first segment after a build edit (spec 6.4: «после правки билда»).</summary>
    public bool AfterBuildChange { get; }

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

    /// <summary>Segment damage / <see cref="Duration"/>.</summary>
    public double Dps => TotalDamage / Duration;

    /// <summary>
    /// DPS against the segment before it, as a share (+0,12 is «DPS +12%», <see cref="CombatLogText.DpsChange"/>); null for the
    /// first segment (or the first after <see cref="TrainingRoom.ResetMeter"/>) or when the previous one dealt nothing.
    /// </summary>
    public double? DpsChangeVsPrevious => _previousDps is > 0 and var prev ? Dps / prev - 1 : null;

    /// <summary>«№4 · 0:42 · Бронированный · после правки билда»: number, duration (minutes:seconds), dummy, and the build edit.</summary>
    public string Label
    {
        get
        {
            var seconds = (int)Math.Floor(Duration + Eps);
            return Texts.SegmentLabel(Index, $"{seconds / 60}:{seconds % 60:00}", _names.DummyName(DummyId), AfterBuildChange);
        }
    }

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
        _series.Add(e.Time, final);
        if (!_seriesBySkill.TryGetValue(e.SkillId, out var series)) _seriesBySkill[e.SkillId] = series = new Series(Start);
        series.Add(e.Time, final);
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
    /// Where the damage before defense went (spec 8.1), losses in a fixed order: «дошло», «броня», «сопротивления», «множитель
    /// получаемого урона», «обнулено»; then the bonus rows (<see cref="MeterRow.IsBonus"/>), only when there is such damage:
    /// «+N от уязвимости» (the damage-taken multiplier above 1: shock, «Хрупкость») and «+N от пробивания» (penetration pushing a
    /// resistance below 0), both positive. Each hit's part counts as a loss or as a bonus by its own sign. Losses − bonuses = the
    /// damage before defense; shares are of it, so they do not add up to 1 when there are bonuses
    /// (<see cref="CombatLogText.MeterModeHint"/>).
    /// </summary>
    public IReadOnlyList<MeterRow> Mitigation()
    {
        var t = _total;
        var whole = t.BeforeDefense;
        MeterRow Row(string key, string label, double v, bool bonus = false) => new(key, label, v, whole == 0 ? 0 : v / whole, bonus);
        var rows = new List<MeterRow>
        {
            Row("final", Texts.MitigationFinal, t.Final),
            Row("armor", Texts.MitigationArmor, t.ByArmor),
            Row("resist", Texts.MitigationResist, t.ByResistLoss),
            Row("taken_multiplier", Texts.MitigationTaken, t.ByTakenLoss),
            Row("nullified", Texts.MitigationNullified, t.Nullified),
        };
        if (t.ByTakenBonus > 0)
            rows.Add(Row("vulnerability", Texts.MitigationVulnerability(RuNumber.Short(t.ByTakenBonus)), t.ByTakenBonus, bonus: true));
        if (t.ByResistBonus > 0)
            rows.Add(Row("penetration", Texts.MitigationPenetration(RuNumber.Short(t.ByResistBonus)), t.ByResistBonus, bonus: true));
        return rows;
    }

    /// <summary>The whole segment's defence split, each hit part counted as a loss or a bonus by its own sign.</summary>
    public DefenceSplit DefenceTotal => _total.ToSplit();

    /// <summary>Each skill's defence split, losses and bonuses summed per hit part (not netted over the skill), by skill id.</summary>
    public IReadOnlyDictionary<string, DefenceSplit> DefenceBySkill() =>
        new SortedDictionary<string, DefenceSplit>(_bySkill.ToDictionary(kv => kv.Key, kv => kv.Value.ToSplit()), StringComparer.Ordinal);

    /// <summary>The damage of each skill summed over types (Type carries no meaning), by skill id.</summary>
    public IReadOnlyDictionary<string, DamagePart> MitigationBySkill() =>
        new SortedDictionary<string, DamagePart>(_bySkill.ToDictionary(kv => kv.Key, kv => kv.Value.ToPart()), StringComparer.Ordinal);

    /// <summary>
    /// Final damage per bucket of <paramref name="bucketSec"/> from <see cref="Start"/>, of the whole segment or of one skill
    /// (spec 8.2, the lines of the chart); every bucket up to the segment's last damage is listed (empty ones with 0), each with
    /// its start time, so the series of all skills line up. The sum is the segment's (or the skill's) damage; an unknown skill
    /// gives zeros.
    /// </summary>
    public IReadOnlyList<(double Time, double Damage)> DamageOverTime(double bucketSec, string? skillId = null)
    {
        if (!(bucketSec > 0)) throw new ArgumentOutOfRangeException(nameof(bucketSec), "bucket must be positive");
        var count = (int)Math.Floor((End - Start) / bucketSec + Eps) + 1;
        var sums = new double[count];
        var series = skillId == null ? _series : _seriesBySkill.GetValueOrDefault(skillId);
        if (series != null)
            foreach (var (time, damage) in series.Points)
                sums[Math.Clamp((int)Math.Floor((time - Start) / bucketSec + Eps), 0, count - 1)] += damage;
        return sums.Select((d, i) => (Start + i * bucketSec, d)).ToList();
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

    /// <summary>Damage per engine step from the segment's start; past <see cref="MaxSeriesPoints"/> points it merges them into
    /// slots twice as wide (sums are kept), so its memory is bounded.</summary>
    private sealed class Series
    {
        private readonly double _start;
        private readonly List<(double Time, double Damage)> _points = new();
        private double _step = LiveFight.StepSec;

        public Series(double start) => _start = start;

        public IReadOnlyList<(double Time, double Damage)> Points => _points;

        public void Add(double time, double damage)
        {
            var slot = Slot(time);
            if (_points.Count > 0 && Math.Abs(_points[^1].Time - slot) < Eps)
            {
                _points[^1] = (slot, _points[^1].Damage + damage);
                return;
            }
            _points.Add((slot, damage));
            if (_points.Count <= MaxSeriesPoints) return;

            _step *= 2;
            var merged = new List<(double Time, double Damage)>(_points.Count / 2 + 1);
            foreach (var (t, d) in _points)
            {
                var s = Slot(t);
                if (merged.Count > 0 && Math.Abs(merged[^1].Time - s) < Eps) merged[^1] = (s, merged[^1].Damage + d);
                else merged.Add((s, d));
            }
            _points.Clear();
            _points.AddRange(merged);
        }

        private double Slot(double time) => _start + Math.Floor((time - _start) / _step + Eps) * _step;
    }

    private sealed class PartSum
    {
        public double BeforeDefense, ByArmor, ByResist, ByTakenMultiplier, Nullified, Final;
        // The same steps split by the sign of each part: a positive part is a loss, a negative one a bonus (stored positive).
        public double ByResistLoss, ByResistBonus, ByTakenLoss, ByTakenBonus;

        public void Add(DamagePart p)
        {
            BeforeDefense += p.BeforeDefense;
            ByArmor += p.ByArmor;
            ByResist += p.ByResist;
            ByTakenMultiplier += p.ByTakenMultiplier;
            Nullified += p.Nullified;
            Final += p.Final;
            if (p.ByResist >= 0) ByResistLoss += p.ByResist;
            else ByResistBonus -= p.ByResist;
            if (p.ByTakenMultiplier >= 0) ByTakenLoss += p.ByTakenMultiplier;
            else ByTakenBonus -= p.ByTakenMultiplier;
        }

        public DamagePart ToPart() => new(default, BeforeDefense, ByArmor, ByResist, ByTakenMultiplier, Nullified, Final);

        public DefenceSplit ToSplit() =>
            new(BeforeDefense, ByArmor, ByResistLoss, ByResistBonus, ByTakenLoss, ByTakenBonus, Nullified, Final);
    }
}
