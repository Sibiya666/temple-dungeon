using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

/// <summary>
/// Analytical model of the currency economy (spec 8.3). Two pools: floors (imbue + imbue_type over 25 floor slots)
/// and tower (imbue_tower over 3 tower slots, checked from rank 4). Mods do not change currency drops.
/// </summary>
internal static class EconomyModel
{
    public enum Pool { Floors, Tower }

    public const int TowerPoolFromRank = 4;

    public static IReadOnlyList<string> Currencies(Pool p) =>
        p == Pool.Floors ? new[] { "imbue", "imbue_type" } : new[] { "imbue_tower" };

    /// <summary>Every slot allowed for the pool filled with one ordinary imbue; does not depend on rank.</summary>
    public static double FullFill(TowerData d, int rank, Pool p) =>
        p == Pool.Floors ? d.Rules.Floors * d.Rules.FloorSlots : d.Rules.TowerSlots;

    /// <summary>Expected drop of the pool's currencies over floors 0..Floors−1 plus the summit.</summary>
    public static double Income(TowerData d, int rank, Pool p)
    {
        var ids = Currencies(p);
        double sum = 0;
        var places = Enumerable.Range(0, d.Rules.Floors).Select(i => (int?)i).Append(null);
        foreach (var place in places)
            foreach (var (id, expected) in Rewards.ExpectedCurrencyDrop(d, rank, place))
                if (ids.Contains(id)) sum += expected;
        return sum;
    }

    /// <summary>
    /// Pool currency per hour usable on <paramref name="targetRank"/> when farming <paramref name="farmRank"/> at the given speed
    /// (towers per hour relative to the own rank). Level L reaches level T by exchange at ExchangeRatio^(T − L);
    /// levels below ExchangeMinLevel cannot be exchanged. Currency already usable (farm ≥ target) is taken as is.
    /// </summary>
    public static double UsablePerHour(TowerData d, int farmRank, int targetRank, Pool p, double speed)
    {
        double income = Income(d, farmRank, p) * speed;
        if (farmRank >= targetRank) return income;
        if (farmRank < d.Rules.ExchangeMinLevel) return 0;
        return income / Math.Pow(d.Rules.ExchangeRatio, targetRank - farmRank);
    }

    /// <summary>Ranks where the pool is checked by equation 1 (60–80%).</summary>
    public static IEnumerable<int> BandRanks(Pool p, int maxRank) => Enumerable.Range(FirstChecked(p), maxRank - FirstChecked(p) + 1);

    public static int FirstChecked(Pool p) => p == Pool.Floors ? 2 : TowerPoolFromRank;

    public static readonly Pool[] All = { Pool.Floors, Pool.Tower };
}

public class EconomyAcceptanceTests
{
    private static readonly TowerData D = TowerDataLoader.Load(FixturePaths.RealTowerData).Data!;
    private const int MaxRank = 15;
    private const double FastSpeed = 3.5;

    private static double Ratio(int rank, EconomyModel.Pool p) => EconomyModel.Income(D, rank, p) / EconomyModel.FullFill(D, rank, p);

    [Fact]
    public void Ranks_in_data_match_the_model()
    {
        Assert.Equal(MaxRank, D.Rules.Ranks.Count);
        Assert.Equal(EconomyModel.TowerPoolFromRank, D.Currency("imbue_tower").DropsFromRank);
    }

    [Fact]
    public void Equation1_rank1_covers_full_fill()
    {
        double r = Ratio(1, EconomyModel.Pool.Floors);
        Assert.True(r >= 1.0, $"ранг 1: {r:F3}");
    }

    [Fact]
    public void Equation1_ranks_2_to_15_cover_60_to_80_percent()
    {
        foreach (var p in EconomyModel.All)
            foreach (int rank in EconomyModel.BandRanks(p, MaxRank))
            {
                double r = Ratio(rank, p);
                Assert.True(r >= 0.6 && r <= 0.8, $"{p}, ранг {rank}: {r:F3}");
            }
    }

    [Fact]
    public void Bare_tower_income_at_least_40_percent()
    {
        // The model has no mod input: income is the bare tower's income (see Mods_do_not_change_currency_income).
        foreach (var p in EconomyModel.All)
            foreach (int rank in EconomyModel.BandRanks(p, MaxRank))
            {
                double r = Ratio(rank, p);
                Assert.True(r >= 0.4, $"{p}, ранг {rank}: {r:F3}");
            }
    }

    [Fact]
    public void Income_grows_with_rank()
    {
        // from rank 2: rank 1 is boosted by Rank1DropMultiplier on purpose (spec 8.3, "rank 1 is generous")
        for (int n = 2; n < MaxRank; n++)
            Assert.True(EconomyModel.Income(D, n + 1, EconomyModel.Pool.Floors) > EconomyModel.Income(D, n, EconomyModel.Pool.Floors), $"ранг {n} → {n + 1}");
    }

    [Fact]
    public void Equation2_own_rank_is_best_per_hour()
    {
        foreach (var p in EconomyModel.All)
            for (int n = EconomyModel.FirstChecked(p); n <= MaxRank; n++)
            {
                double own = EconomyModel.UsablePerHour(D, n, n, p, 1.0);
                Assert.True(own > 0, $"{p}, ранг {n}: на своем ранге нет дохода");
                foreach (int farm in new[] { n - 1, n - 2 }.Where(f => f >= 1))
                {
                    double low = EconomyModel.UsablePerHour(D, farm, n, p, FastSpeed);
                    Assert.True(own > low, $"{p}, цель {n}: свой ранг {own:F3}, фарм ранга {farm} ×{FastSpeed} дает {low:F3}");
                }
            }
    }

    [Fact]
    public void Level_1_currency_is_not_exchanged()
    {
        // this is why the rank 1 bonus does not leak to rank 2
        Assert.Equal(0, EconomyModel.UsablePerHour(D, 1, 2, EconomyModel.Pool.Floors, FastSpeed));
        Assert.True(EconomyModel.UsablePerHour(D, 2, 3, EconomyModel.Pool.Floors, FastSpeed) > 0);
    }

    // --- mods do not change currency income ---

    private const int ModRank = 6;

    private static IRandomSource Rng(int seed) => RandomSources.Create(SimMode.Roll, seed);

    private static TowerAssembly Bare()
    {
        var a = new TowerAssembly(D, ModRank, Rng(3));
        for (int i = 0; i < D.Rules.Floors; i++) Assert.Equal(TowerRefusal.None, a.SetFloorType(i, D.FloorTypes[i % D.FloorTypes.Count].Id));
        a.SetUberBoss("boss");
        return a;
    }

    private static TowerAssembly Crafted()
    {
        var a = Bare();
        var w = new Wallet(D);
        foreach (var id in new[] { "imbue", "imbue_type", "imbue_tower" }) w.Add(id, ModRank, 100);
        for (int i = 0; i < D.Rules.Floors; i++)
        {
            // fill type mods first, then general ones; stop when the slot is full or the pool is empty
            foreach (var id in new[] { "imbue_type", "imbue" })
                while (Crafting.Apply(D, a, w, id, i) == TowerRefusal.None) { }
        }
        while (Crafting.Apply(D, a, w, "imbue_tower", null) == TowerRefusal.None) { }
        return a;
    }

    private static TowerProgress Unlocked(int rank)
    {
        var p = new TowerProgress();
        for (int r = 1; r < rank; r++) p.RecordUberKill(r);
        Assert.True(p.TryUnlock(D.Rules, 60));
        return p;
    }

    private static List<(string, int, int)> RunCurrency(TowerAssembly a, int seed)
    {
        var progress = Unlocked(ModRank);
        Assert.Equal(TowerRefusal.None, TowerRun.TryEnter(D, a, progress, 60, Rng(seed), out var run));
        var wallet = new Wallet(D);
        for (int i = 0; i < D.Rules.Floors; i++) run!.WinFloor(wallet);
        run!.WinSummit(wallet, progress);
        return run.Rewards.SelectMany(r => r.Currency).ToList();
    }

    [Fact]
    public void Mods_do_not_change_currency_income()
    {
        var bare = Bare();
        var crafted = Crafted();
        Assert.All(bare.Spec.Floors, f => Assert.Empty(f.Mods));
        Assert.All(crafted.Spec.Floors, f => Assert.Equal(D.Rules.FloorSlots, f.Mods.Count));
        Assert.Equal(D.Rules.TowerSlots, crafted.Spec.TowerMods.Count);

        // quality differs, so the mods are really in effect
        Assert.True(Rewards.FloorQuality(D, crafted.Spec, 0, true, false).Total > Rewards.FloorQuality(D, bare.Spec, 0, true, false).Total);

        foreach (int seed in new[] { 1, 2, 3, 42 })
            Assert.Equal(RunCurrency(bare, seed), RunCurrency(crafted, seed));
    }
}
