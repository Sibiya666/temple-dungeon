using System.Collections;
using Dialoid.Core.Simulation;

namespace Dialoid.Core.Tower;

/// <summary>Multipliers of the reward quality of one floor or of the summit; <see cref="Total"/> = their product, capped by QualityCap.</summary>
public sealed record QualityBreakdown(double Height, double FloorMods, double TowerMods, double Diversity, double Shuffle, double NoDeath, double Total);

/// <summary>Reward quality and currency drops (spec 10). Pure functions; floor indices are 0-based, null floor index = summit.</summary>
public static class Rewards
{
    private const string SummitArena = "bosses";

    /// <summary>1 + c·x / (c + x): increasing and concave, 1 at x = 0.</summary>
    public static double Diminish(TowerRules r, double x) => 1 + r.DiminishingC * x / (r.DiminishingC + x);

    /// <summary>Tier quality; for ranged mods scaled by the rolled value against the middle of the range.</summary>
    public static double ModQuality(TowerData d, ModInstance m)
    {
        var tier = d.Mod(m.ModId).Tiers.First(t => t.Tier == m.Tier);
        return tier.Min == tier.Max ? tier.Quality : tier.Quality * m.Value / ((tier.Min + tier.Max) / 2);
    }

    /// <summary>Distinct non-null floor types; trial variants are one type.</summary>
    public static int DistinctTypes(TowerSpec t) =>
        t.Floors.Where(f => f.TypeId != null).Select(f => f.TypeId!).Distinct(StringComparer.Ordinal).Count();

    private static double Sum(TowerData d, IEnumerable<ModInstance> mods) => mods.Sum(m => ModQuality(d, m));

    private static bool ArenaCompatible(TowerData d, ModInstance m)
    {
        var types = d.Mod(m.ModId).FloorTypes;
        return types.Count == 0 || types.Contains(SummitArena, StringComparer.Ordinal);
    }

    private static QualityBreakdown Make(TowerRules r, double height, double floorMods, double towerMods, double diversity, bool noDeaths, bool shuffleBonus)
    {
        double shuffle = shuffleBonus ? 1 + r.ShuffleBonus : 1;
        double noDeath = noDeaths ? 1 + r.NoDeathBonus : 1;
        double total = Math.Min(r.QualityCap, height * floorMods * towerMods * diversity * shuffle * noDeath);
        return new QualityBreakdown(height, floorMods, towerMods, diversity, shuffle, noDeath, total);
    }

    private static double Diversity(TowerRules r, TowerSpec t)
    {
        int types = DistinctTypes(t);
        return types <= 1 ? 1 : 1 + r.DiversityBonusPerType * Math.Min(r.DiversityBonusTypesCap, types - 1);
    }

    private static QualityBreakdown FloorCore(TowerData d, TowerSpec t, int floorIndex, bool noDeaths, bool shuffleBonus, double addFloor, double addTower)
    {
        var r = d.Rules;
        double floorSum = Sum(d, t.Floors[floorIndex].Mods) + addFloor;
        double towerSum = Sum(d, t.TowerMods) + addTower;
        return Make(r, r.HeightMultipliers[floorIndex], Diminish(r, floorSum), Diminish(r, towerSum), Diversity(r, t), noDeaths, shuffleBonus);
    }

    public static QualityBreakdown FloorQuality(TowerData d, TowerSpec t, int floorIndex, bool noDeaths, bool shuffleBonus) =>
        FloorCore(d, t, floorIndex, noDeaths, shuffleBonus, 0, 0);

    private static QualityBreakdown SummitCore(TowerData d, TowerSpec t, bool noDeaths, bool shuffleBonus, double addTower)
    {
        var r = d.Rules;
        double towerSum = Sum(d, t.TowerMods.Where(m => ArenaCompatible(d, m))) + addTower;
        return Make(r, r.SummitBonus, 1, Diminish(r, towerSum), Diversity(r, t), noDeaths, shuffleBonus);
    }

    public static QualityBreakdown SummitQuality(TowerData d, TowerSpec t, bool noDeaths, bool shuffleBonus) =>
        SummitCore(d, t, noDeaths, shuffleBonus, 0);

    /// <summary>
    /// Total gained by one more mod of the given quality on that floor (floorIndex) or on the tower (null; compared at floor 0).
    /// Without the no-death and shuffle bonuses.
    /// </summary>
    public static double NextModGain(TowerData d, TowerSpec t, int? floorIndex, double addedQuality)
    {
        int i = floorIndex ?? 0;
        double now = FloorCore(d, t, i, false, false, 0, 0).Total;
        double next = floorIndex.HasValue
            ? FloorCore(d, t, i, false, false, addedQuality, 0).Total
            : FloorCore(d, t, i, false, false, 0, addedQuality).Total;
        return next - now;
    }

    private sealed class OrderedMap : IReadOnlyDictionary<string, double>
    {
        private readonly List<KeyValuePair<string, double>> _items = new();
        private readonly Dictionary<string, double> _index = new(StringComparer.Ordinal);

        public void Add(string key, double value)
        {
            _items.Add(new(key, value));
            _index.Add(key, value);
        }

        public double this[string key] => _index[key];
        public IEnumerable<string> Keys => _items.Select(p => p.Key);
        public IEnumerable<double> Values => _items.Select(p => p.Value);
        public int Count => _items.Count;
        public bool ContainsKey(string key) => _index.ContainsKey(key);
        public bool TryGetValue(string key, out double value) => _index.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, double>> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Expected drop per currency in data file order; currencies not yet dropping at this rank are absent. Mods do not matter.
    /// On rank 1 everything is scaled by <see cref="TowerRules.Rank1DropMultiplier"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, double> ExpectedCurrencyDrop(TowerData d, int rank, int? floorIndex)
    {
        double height = floorIndex.HasValue ? d.Rules.HeightMultipliers[floorIndex.Value] : 0;
        double rankBonus = rank == 1 ? d.Rules.Rank1DropMultiplier : 1;
        var map = new OrderedMap();
        foreach (var c in d.Currencies)
        {
            if (c.DropsFromRank > rank) continue;
            double mult = floorIndex.HasValue ? height : c.SummitMultiplier;
            map.Add(c.Id, c.DropBase * Math.Pow(c.DropGrowth, rank - 1) * mult * rankBonus);
        }
        return map;
    }

    /// <summary>Integer part of the expectation plus one more with the probability of the fractional part. Zero counts are omitted.</summary>
    public static IReadOnlyList<(string CurrencyId, int Level, int Count)> RollCurrencyDrop(TowerData d, int rank, int? floorIndex, IRandomSource rng)
    {
        var result = new List<(string, int, int)>();
        foreach (var (id, expected) in ExpectedCurrencyDrop(d, rank, floorIndex))
        {
            double whole = Math.Floor(expected);
            int count = (int)whole;
            if (rng.Chance($"tower.drop.{id}", expected - whole)) count++;
            if (count > 0) result.Add((id, d.Currency(id).HasLevel ? rank : 0, count));
        }
        return result;
    }
}
