namespace Dialoid.Core.Tower;

/// <summary>
/// Tower currency by level (spec 8.2–8.4). A levelled currency of level L works on ranks ≤ L; levelless currency is stored
/// at level 0 and works everywhere. Spending takes the lowest usable level first and is all or nothing.
/// Exchange goes up only to the neighbour level at <see cref="TowerRules.ExchangeRatio"/>:1, never from level 1.
/// Iteration is by ascending level (deterministic). Programmer errors (unknown id, bad level, rank or count) throw.
/// </summary>
public sealed class Wallet
{
    private readonly TowerData _data;
    private readonly Dictionary<string, SortedDictionary<int, int>> _store = new(StringComparer.Ordinal);

    public Wallet(TowerData data) => _data = data;

    private int MaxLevel => _data.Rules.Ranks.Count;

    /// <summary>Adds <paramref name="count"/> units; for a levelless currency the level is ignored and stored as 0.</summary>
    public void Add(string currencyId, int level, int count)
    {
        var def = _data.Currency(currencyId);
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "Добавлять можно только положительное количество валюты");
        if (def.HasLevel && (level < 1 || level > MaxLevel))
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Уровень валюты «{currencyId}» должен быть от 1 до {MaxLevel}");
        int key = def.HasLevel ? level : 0;
        var levels = Bucket(currencyId);
        levels[key] = levels.GetValueOrDefault(key) + count;
    }

    /// <summary>Units usable on <paramref name="rank"/>: levels ≥ rank, or everything for a levelless currency.</summary>
    public int CountUsable(string currencyId, int rank) => Usable(currencyId, rank).Sum(p => p.Value);

    /// <summary>Spends from the lowest usable level up. Not enough → false and nothing is taken.</summary>
    public bool TrySpend(string currencyId, int rank, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Нельзя потратить отрицательное количество валюты");
        var usable = Usable(currencyId, rank).ToList();
        if (usable.Sum(p => p.Value) < count) return false;
        var levels = Bucket(currencyId);
        int left = count;
        foreach (var (level, have) in usable)
        {
            if (left == 0) break;
            int take = Math.Min(have, left);
            left -= take;
            if (have == take) levels.Remove(level);
            else levels[level] = have - take;
        }
        return true;
    }

    /// <summary>
    /// ExchangeRatio units of level L → 1 unit of level L + 1. Only for a levelled currency with ExchangeMinLevel ≤ L &lt; max level
    /// and enough units; otherwise false and nothing changes.
    /// </summary>
    public bool TryExchangeUp(string currencyId, int fromLevel)
    {
        var def = _data.Currency(currencyId);
        var r = _data.Rules;
        if (!def.HasLevel || fromLevel < r.ExchangeMinLevel || fromLevel >= MaxLevel) return false;
        var levels = Bucket(currencyId);
        int have = levels.GetValueOrDefault(fromLevel);
        if (have < r.ExchangeRatio) return false;
        if (have == r.ExchangeRatio) levels.Remove(fromLevel);
        else levels[fromLevel] = have - r.ExchangeRatio;
        levels[fromLevel + 1] = levels.GetValueOrDefault(fromLevel + 1) + 1;
        return true;
    }

    /// <summary>A copy of level → count (only non-zero counts, ascending level; 0 for a levelless currency).</summary>
    public IReadOnlyDictionary<int, int> Levels(string currencyId)
    {
        _data.Currency(currencyId);
        return _store.TryGetValue(currencyId, out var levels) ? new SortedDictionary<int, int>(levels) : new SortedDictionary<int, int>();
    }

    private IEnumerable<KeyValuePair<int, int>> Usable(string currencyId, int rank)
    {
        var def = _data.Currency(currencyId);
        RankRules.Def(_data.Rules, rank);
        if (!_store.TryGetValue(currencyId, out var levels)) return Array.Empty<KeyValuePair<int, int>>();
        return def.HasLevel ? levels.Where(p => p.Key >= rank) : levels;
    }

    private SortedDictionary<int, int> Bucket(string currencyId)
    {
        if (!_store.TryGetValue(currencyId, out var levels)) _store[currencyId] = levels = new SortedDictionary<int, int>();
        return levels;
    }
}
