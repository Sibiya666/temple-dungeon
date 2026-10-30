using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

public class RewardsTests
{
    private static readonly TowerData D = TowerDataLoader.Load(FixturePaths.RealTowerData).Data!;

    private static FloorSpec Empty() => new(null, null, null, Array.Empty<ModInstance>());

    private static FloorSpec Floor(string type, string? variant = null, params ModInstance[] mods) =>
        new(type, variant, D.Biomes[0].Id, mods);

    private static TowerSpec Tower(IEnumerable<FloorSpec> floors, params ModInstance[] towerMods)
    {
        var list = floors.ToList();
        while (list.Count < 5) list.Add(Empty());
        return new TowerSpec(5, list, towerMods, null);
    }

    private static ModInstance Max(string id, int tier = 1)
    {
        var t = D.Mod(id).Tiers.First(x => x.Tier == tier);
        return new ModInstance(id, tier, t.Max);
    }

    [Fact]
    public void Diminish_is_increasing_and_concave()
    {
        var r = D.Rules;
        double prev = Rewards.Diminish(r, 0), prevDelta = double.MaxValue;
        Assert.Equal(1.0, prev, 12);
        for (double x = 0.5; x <= 20; x += 0.5)
        {
            double v = Rewards.Diminish(r, x), delta = v - prev;
            Assert.True(delta > 0);
            Assert.True(delta < prevDelta);
            prev = v;
            prevDelta = delta;
        }
    }

    [Fact]
    public void Fifth_mod_still_adds_quality()
    {
        var general = D.Mods.Where(m => m.Kind == ModKind.General && m.FloorTypes.Count == 0 && m.Tiers.Any(t => t.Quality > 0)).Take(5).ToList();
        Assert.Equal(5, general.Count);
        var mods4 = general.Take(4).Select(m => new ModInstance(m.Id, 1, m.Tiers[0].Max)).ToArray();
        var mods5 = general.Select(m => new ModInstance(m.Id, 1, m.Tiers[0].Max)).ToArray();
        double q4 = Rewards.FloorQuality(D, Tower(new[] { Floor("clear", null, mods4) }), 0, false, false).Total;
        double q5 = Rewards.FloorQuality(D, Tower(new[] { Floor("clear", null, mods5) }), 0, false, false).Total;
        Assert.True(q5 > q4);
    }

    [Fact]
    public void Numberless_mod_quality_is_tier_quality()
    {
        var m = new ModInstance("no_healing", 1, 0);
        Assert.Equal(D.Mod("no_healing").Tiers[0].Quality, Rewards.ModQuality(D, m), 12);
    }

    [Fact]
    public void Ranged_mod_quality_scales_with_value()
    {
        var def = D.Mods.First(x => x.Tiers[0].Min != x.Tiers[0].Max);
        var t = def.Tiers[0];
        Assert.Equal(t.Quality, Rewards.ModQuality(D, new ModInstance(def.Id, 1, (t.Min + t.Max) / 2)), 9);
        Assert.True(Rewards.ModQuality(D, new ModInstance(def.Id, 1, t.Max)) > Rewards.ModQuality(D, new ModInstance(def.Id, 1, t.Min)));
    }

    [Fact]
    public void Floor_mods_do_not_affect_summit()
    {
        var general = D.Mods.First(m => m.Kind == ModKind.General && m.FloorTypes.Count == 0 && m.Tiers[0].Quality > 0);
        var plain = Tower(new[] { Floor("clear") });
        var modded = Tower(new[] { Floor("clear", null, Max(general.Id)) });
        Assert.Equal(Rewards.SummitQuality(D, plain, false, false), Rewards.SummitQuality(D, modded, false, false));
        var s = Rewards.SummitQuality(D, modded, false, false);
        Assert.Equal(1.0, s.FloorMods);
        Assert.Equal(D.Rules.SummitBonus, s.Height);
    }

    [Fact]
    public void Only_arena_compatible_tower_mods_affect_summit()
    {
        var compatible = D.Mods.First(m => m.Kind == ModKind.Tower && m.FloorTypes.Count == 0);
        var incompatible = D.Mods.First(m => m.Kind == ModKind.Tower && m.FloorTypes.Count > 0 && !m.FloorTypes.Contains("bosses"));
        var none = Rewards.SummitQuality(D, Tower(Array.Empty<FloorSpec>()), false, false);
        var withBad = Rewards.SummitQuality(D, Tower(Array.Empty<FloorSpec>(), Max(incompatible.Id)), false, false);
        var withGood = Rewards.SummitQuality(D, Tower(Array.Empty<FloorSpec>(), Max(compatible.Id)), false, false);
        Assert.Equal(none.TowerMods, withBad.TowerMods);
        Assert.True(withGood.TowerMods > none.TowerMods);
        Assert.True(Rewards.FloorQuality(D, Tower(new[] { Floor("clear") }, Max(incompatible.Id)), 0, false, false).TowerMods > 1.0);
    }

    [Fact]
    public void Tower_mods_raise_quality_only_on_compatible_floors()
    {
        var notBosses = D.Mods.First(m => m.Kind == ModKind.Tower && m.FloorTypes.Count > 0 && !m.FloorTypes.Contains("bosses"));
        var everywhere = D.Mods.First(m => m.Kind == ModKind.Tower && m.FloorTypes.Count == 0);
        var floors = new[] { Floor("bosses"), Floor("clear") };
        var bare = Tower(floors);
        var withNotBosses = Tower(floors, Max(notBosses.Id));
        var withEverywhere = Tower(floors, Max(everywhere.Id));

        Assert.Equal(1.0, Rewards.FloorQuality(D, withNotBosses, 0, false, false).TowerMods);
        Assert.Equal(Rewards.FloorQuality(D, bare, 0, false, false), Rewards.FloorQuality(D, withNotBosses, 0, false, false));
        Assert.True(Rewards.FloorQuality(D, withNotBosses, 1, false, false).TowerMods > 1.0);
        Assert.True(Rewards.FloorQuality(D, withEverywhere, 0, false, false).TowerMods > 1.0);
        Assert.True(Rewards.FloorQuality(D, withEverywhere, 1, false, false).TowerMods > 1.0);
    }

    [Fact]
    public void Summit_rule_is_acts_on_summit()
    {
        foreach (var m in D.Mods)
            Assert.Equal(m.Kind == ModKind.Tower && (m.FloorTypes.Count == 0 || m.FloorTypes.Contains("bosses")), m.ActsOnSummit);
        Assert.Contains(D.Mods, m => m.ActsOnSummit);
        Assert.All(D.Mods.Where(m => m.ActsOnSummit), m => Assert.Equal(TowerDataLoader.SummitGroup, m.Group));
    }

    [Fact]
    public void Diversity_bonus_caps_at_3_types()
    {
        string[] ids = { "timed_run", "clear", "waves", "bosses", "trial" };
        double Div(int n) => Rewards.FloorQuality(D, Tower(ids.Take(n).Select(i => Floor(i, i == "trial" ? "escort" : null))), 0, false, false).Diversity;
        Assert.Equal(1.0, Div(1));
        Assert.Equal(1 + D.Rules.DiversityBonusPerType, Div(2), 12);
        Assert.Equal(Div(3), Div(5), 12);
        Assert.Equal(1 + 2 * D.Rules.DiversityBonusPerType, Div(3), 12);

        var trials = Tower(new[] { Floor("trial", "escort"), Floor("trial", "hold") });
        Assert.Equal(1, Rewards.DistinctTypes(trials));
        Assert.Equal(1.0, Rewards.FloorQuality(D, trials, 0, false, false).Diversity);
        Assert.Equal(0, Rewards.DistinctTypes(Tower(Array.Empty<FloorSpec>())));
    }

    [Fact]
    public void Breakdown_total_is_product_and_capped()
    {
        var general = D.Mods.First(m => m.Kind == ModKind.General && m.FloorTypes.Count == 0 && m.Tiers[0].Quality > 0);
        var t = Tower(new[] { Floor("clear", null, Max(general.Id)), Floor("waves") });
        var b = Rewards.FloorQuality(D, t, 0, true, true);
        Assert.Equal(1 + D.Rules.NoDeathBonus, b.NoDeath, 12);
        Assert.Equal(1 + D.Rules.ShuffleBonus, b.Shuffle, 12);
        Assert.Equal(b.Height * b.FloorMods * b.TowerMods * b.Diversity * b.Shuffle * b.NoDeath, b.Total, 12);
        var s = Rewards.SummitQuality(D, t, true, true);
        Assert.Equal(s.Height * s.FloorMods * s.TowerMods * s.Diversity * s.Shuffle * s.NoDeath, s.Total, 12);

        var tiny = new TowerData(D.Rules with { QualityCap = 1.0 }, D.FloorTypes, D.Biomes, D.Mods, D.Currencies);
        Assert.Equal(1.0, Rewards.FloorQuality(tiny, t, 0, true, true).Total);
    }

    [Fact]
    public void Next_mod_gain_is_positive_and_grows_with_quality()
    {
        var t = Tower(new[] { Floor("clear") });
        double g1 = Rewards.NextModGain(D, t, 0, 0.1);
        Assert.True(g1 > 0);
        Assert.True(Rewards.NextModGain(D, t, 0, 0.2) > g1);
        Assert.True(Rewards.NextModGain(D, t, null, 0.1) > 0);
        Assert.Equal(0.0, Rewards.NextModGain(D, t, 0, 0.0), 12);
    }

    [Fact]
    public void Currency_drop_ignores_mods()
    {
        var a = Rewards.ExpectedCurrencyDrop(D, 5, 2);
        var imbue = D.Currency("imbue");
        Assert.Equal(imbue.DropBase * Math.Pow(imbue.DropGrowth, 4) * D.Rules.HeightMultipliers[2], a["imbue"], 12);
        var s = Rewards.ExpectedCurrencyDrop(D, 5, null);
        Assert.Equal(imbue.DropBase * Math.Pow(imbue.DropGrowth, 4) * imbue.SummitMultiplier, s["imbue"], 12);
    }

    [Fact]
    public void Currency_drop_follows_file_order()
    {
        var drop = Rewards.ExpectedCurrencyDrop(D, D.Rules.Ranks.Count, 0);
        var expected = D.Currencies.Where(c => c.DropsFromRank <= D.Rules.Ranks.Count).Select(c => c.Id).ToList();
        Assert.Equal(expected, drop.Keys.ToList());
    }

    [Fact]
    public void Currency_does_not_drop_before_its_rank()
    {
        Assert.Equal(4, D.Currency("imbue_tower").DropsFromRank);
        Assert.False(Rewards.ExpectedCurrencyDrop(D, 3, 0).ContainsKey("imbue_tower"));
        Assert.True(Rewards.ExpectedCurrencyDrop(D, 4, 0).ContainsKey("imbue_tower"));
        var rng = RandomSources.Create(SimMode.Roll, 1);
        for (int i = 0; i < 200; i++)
            Assert.DoesNotContain(Rewards.RollCurrencyDrop(D, 3, null, rng), x => x.CurrencyId == "imbue_tower");
    }

    [Fact]
    public void Rank1_multiplier_scales_only_levelled_currency_on_rank_1()
    {
        Assert.True(D.Rules.Rank1DropMultiplier > 1);
        Assert.Contains(D.Currencies, c => c.DropsFromRank == 1 && !c.HasLevel);
        foreach (int? place in new int?[] { 0, 4, null })
        {
            foreach (var (id, value) in Rewards.ExpectedCurrencyDrop(D, 1, place))
            {
                var c = D.Currency(id);
                double mult = place is int i ? D.Rules.HeightMultipliers[i] : c.SummitMultiplier;
                double bonus = c.HasLevel ? D.Rules.Rank1DropMultiplier : 1; // levelless currency works on any rank: no bonus
                Assert.Equal(c.DropBase * mult * bonus, value, 12);
            }
            foreach (var (id, value) in Rewards.ExpectedCurrencyDrop(D, 2, place))
            {
                var c = D.Currency(id);
                double mult = place is int i ? D.Rules.HeightMultipliers[i] : c.SummitMultiplier;
                Assert.Equal(c.DropBase * c.DropGrowth * mult, value, 12);
            }
        }
    }

    [Fact]
    public void Summit_drops_more_than_any_floor()
    {
        var summit = Rewards.ExpectedCurrencyDrop(D, 6, null);
        for (int i = 0; i < D.Rules.Floors; i++)
            foreach (var kv in Rewards.ExpectedCurrencyDrop(D, 6, i)) Assert.True(summit[kv.Key] > kv.Value);
    }

    [Fact]
    public void Same_seed_same_drop()
    {
        var a = Rewards.RollCurrencyDrop(D, 8, 3, RandomSources.Create(SimMode.Roll, 42));
        var b = Rewards.RollCurrencyDrop(D, 8, 3, RandomSources.Create(SimMode.Roll, 42));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Roll_drop_levels_and_integer_part()
    {
        int rank = 9;
        var drops = Rewards.RollCurrencyDrop(D, rank, null, RandomSources.Create(SimMode.Roll, 7));
        var expected = Rewards.ExpectedCurrencyDrop(D, rank, null);
        foreach (var (id, level, count) in drops)
        {
            Assert.Equal(D.Currency(id).HasLevel ? rank : 0, level);
            Assert.True(count >= (int)Math.Floor(expected[id]));
            Assert.True(count <= (int)Math.Floor(expected[id]) + 1);
        }
        foreach (var kv in expected.Where(kv => kv.Value >= 1))
            Assert.Contains(drops, x => x.CurrencyId == kv.Key);
    }
}
