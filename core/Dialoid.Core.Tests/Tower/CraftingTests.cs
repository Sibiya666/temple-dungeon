using System.Text.Json;
using System.Text.Json.Nodes;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

public class CraftingTests
{
    private static readonly TowerData D = TowerDataLoader.Load(FixturePaths.RealTowerData).Data!;

    private static IRandomSource Rng(int seed) => RandomSources.Create(SimMode.Roll, seed);

    private static Wallet Rich(TowerData d, int count = 100)
    {
        var w = new Wallet(d);
        foreach (var c in d.Currencies) w.Add(c.Id, d.Rules.Ranks.Count, count);
        return w;
    }

    private static TowerAssembly WithFloor(int rank, int seed, string typeId, TowerData? d = null)
    {
        var a = new TowerAssembly(d ?? D, rank, Rng(seed));
        Assert.Equal(TowerRefusal.None, a.SetFloorType(0, typeId));
        return a;
    }

    private static FloorSpec Empty() => new(null, null, null, Array.Empty<ModInstance>());

    private static TowerAssembly FromFloor(int rank, int seed, FloorSpec floor, TowerData? data = null)
    {
        var floors = new[] { floor }.Concat(Enumerable.Range(0, 4).Select(_ => Empty())).ToArray();
        return TowerAssembly.FromSpec(data ?? D, new TowerSpec(rank, floors, Array.Empty<ModInstance>(), null), Rng(seed));
    }

    private static TowerData WithDropRank(string currencyId, int rank)
    {
        var dir = FixturePaths.CopyTowerFixture();
        var path = Path.Combine(dir, "currencies.json");
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        node.First(c => (string)c!["id"]! == currencyId)!["drops_from_rank"] = rank;
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        var (data, errors) = TowerDataLoader.Load(dir);
        Assert.True(data != null, string.Join("; ", errors.Select(e => e.Message)));
        return data!;
    }

    [Fact]
    public void Sixth_floor_mod_is_refused_and_wallet_untouched()
    {
        var a = WithFloor(1, 3, "clear");
        var w = new Wallet(D);
        w.Add("imbue", 1, 10);
        for (int i = 0; i < 5; i++) Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "imbue", 0));
        Assert.Equal(5, w.CountUsable("imbue", 1));
        var before = TowerCodec.ToJson(a.Spec);

        Assert.Equal(TowerRefusal.SlotFull, Crafting.Apply(D, a, w, "imbue", 0));
        Assert.Equal(5, w.CountUsable("imbue", 1));
        Assert.Equal(before, TowerCodec.ToJson(a.Spec));
        Assert.Equal(5, a.Spec.Floors[0].Mods.Count);
        Assert.Equal(5, a.Spec.Floors[0].Mods.Select(m => m.ModId).Distinct().Count());
    }

    [Fact]
    public void Fourth_tower_mod_is_refused()
    {
        var a = new TowerAssembly(D, 4, Rng(5));
        var w = Rich(D);
        for (int i = 0; i < 3; i++) Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "imbue_tower", null));
        Assert.Equal(TowerRefusal.SlotFull, Crafting.Apply(D, a, w, "imbue_tower", null));
        Assert.Equal(3, a.Spec.TowerMods.Count);
        Assert.Equal(97, w.CountUsable("imbue_tower", 4));
    }

    [Fact]
    public void Tower_currency_on_floor_is_wrong_target()
    {
        var a = WithFloor(4, 1, "clear");
        var w = Rich(D);
        Assert.Equal(TowerRefusal.WrongTarget, Crafting.Apply(D, a, w, "imbue_tower", 0));
        Assert.Empty(a.Spec.Floors[0].Mods);
        Assert.Equal(100, w.CountUsable("imbue_tower", 4));
    }

    [Fact]
    public void General_currency_on_tower_is_wrong_target()
    {
        var a = new TowerAssembly(D, 4, Rng(1));
        var w = Rich(D);
        Assert.Equal(TowerRefusal.WrongTarget, Crafting.Apply(D, a, w, "imbue", null));
        Assert.Equal(TowerRefusal.WrongTarget, Crafting.Apply(D, a, w, "imbue_type", null));
        Assert.Equal(TowerRefusal.UnknownCurrency, Crafting.Apply(D, a, w, "reroll_variant", null));
        Assert.Empty(a.Spec.TowerMods);
        Assert.Equal(100, w.CountUsable("imbue", 4));
    }

    [Fact]
    public void Floor_type_mod_not_allowed_on_rank_2()
    {
        // Real data: the currency itself is locked before rank 3.
        var w = Rich(D);
        Assert.Equal(TowerRefusal.CurrencyLocked, Crafting.Apply(D, WithFloor(2, 1, "clear"), w, "imbue_type", 0));
        // Even if it dropped earlier, the rank does not allow the kind.
        var d = WithDropRank("imbue_type", 1);
        var a = WithFloor(2, 1, "clear", d);
        var w2 = Rich(d);
        Assert.Equal(TowerRefusal.KindNotAllowed, Crafting.Apply(d, a, w2, "imbue_type", 0));
        Assert.Empty(a.Spec.Floors[0].Mods);
        Assert.Equal(100, w2.CountUsable("imbue_type", 2));
        Assert.Equal(TowerRefusal.None, Crafting.Apply(D, WithFloor(3, 1, "clear"), w, "imbue_type", 0));
    }

    [Fact]
    public void Tower_mod_not_allowed_on_rank_3()
    {
        var w = Rich(D);
        Assert.Equal(TowerRefusal.CurrencyLocked, Crafting.Apply(D, new TowerAssembly(D, 3, Rng(1)), w, "imbue_tower", null));
        var d = WithDropRank("imbue_tower", 1);
        var a = new TowerAssembly(d, 3, Rng(1));
        Assert.Equal(TowerRefusal.KindNotAllowed, Crafting.Apply(d, a, Rich(d), "imbue_tower", null));
        Assert.Empty(a.Spec.TowerMods);
        Assert.Equal(TowerRefusal.None, Crafting.Apply(D, new TowerAssembly(D, 4, Rng(1)), w, "imbue_tower", null));
    }

    [Fact]
    public void Remove_locked_before_rank_5()
    {
        var floor = new FloorSpec("clear", null, "swamp", new[] { new ModInstance("monster_life", 1, 20) });
        var w = Rich(D);
        var a4 = FromFloor(4, 1, floor);
        Assert.Equal(TowerRefusal.CurrencyLocked, Crafting.Apply(D, a4, w, "remove", 0, "monster_life"));
        Assert.Single(a4.Spec.Floors[0].Mods);
        Assert.Equal(100, w.CountUsable("remove", 4));
        var a5 = FromFloor(5, 1, floor);
        Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a5, w, "remove", 0, "monster_life"));
        Assert.Empty(a5.Spec.Floors[0].Mods);
        Assert.Equal(99, w.CountUsable("remove", 5));
    }

    [Fact]
    public void Common_checks_come_first()
    {
        var a = new TowerAssembly(D, 5, Rng(1));
        var w = Rich(D);
        Assert.Equal(TowerRefusal.UnknownCurrency, Crafting.Apply(D, a, w, "nope", 0));
        Assert.Equal(TowerRefusal.SlotOutOfRange, Crafting.Apply(D, a, w, "imbue", 5));
        Assert.Equal(TowerRefusal.SlotOutOfRange, Crafting.Apply(D, a, w, "imbue", -1));
        Assert.Equal(TowerRefusal.EmptySlot, Crafting.Apply(D, a, w, "imbue", 0));
        Assert.Equal(TowerRefusal.NoMods, Crafting.Apply(D, a, w, "clear", null));
        Assert.Equal(TowerRefusal.ModNotFound, Crafting.Apply(D, a, w, "remove", null, "tower_reward"));
    }

    [Fact]
    public void Not_enough_currency_changes_nothing()
    {
        var a = WithFloor(1, 2, "clear");
        var w = new Wallet(D);
        var before = TowerCodec.ToJson(a.Spec);
        Assert.Equal(TowerRefusal.NotEnoughCurrency, Crafting.Apply(D, a, w, "imbue", 0));
        Assert.Equal(before, TowerCodec.ToJson(a.Spec));
    }

    [Fact]
    public void Tower_pool_never_contains_hard_counters()
    {
        for (int seed = 0; seed < 1000; seed++)
        {
            var a = new TowerAssembly(D, 4, Rng(seed));
            var w = Rich(D);
            for (int i = 0; i < 3; i++) Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "imbue_tower", null));
            Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "reroll", null));
            Assert.All(a.Spec.TowerMods, m =>
            {
                var def = D.Mod(m.ModId);
                Assert.Equal(ModKind.Tower, def.Kind);
                Assert.Equal(HardCounter.None, def.HardCounter);
            });
        }
    }

    [Fact]
    public void Group_allows_one_mod_per_floor()
    {
        for (int seed = 0; seed < 1000; seed++)
        {
            var a = WithFloor(3, seed, "timed_run");
            var w = Rich(D);
            Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "imbue_type", 0));
            for (int i = 0; i < 4; i++) Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "imbue", 0));
            for (int i = 0; i < 3; i++) Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "reroll", 0));
            var groups = a.Spec.Floors[0].Mods.Select(m => D.Mod(m.ModId).Group).Where(g => g != null).ToList();
            Assert.True(groups.Count(g => g == "timer") <= 1, $"seed {seed}");
            Assert.Equal(groups.Count, groups.Distinct().Count());
        }
        // Both timer mods would fit timed_run, but the second imbue_type finds no free group.
        var b = WithFloor(3, 1, "timed_run");
        var wb = Rich(D);
        Assert.Equal(TowerRefusal.None, Crafting.Apply(D, b, wb, "imbue_type", 0));
        Assert.Equal(TowerRefusal.PoolEmpty, Crafting.Apply(D, b, wb, "imbue_type", 0));
        Assert.Equal(99, wb.CountUsable("imbue_type", 3));
    }

    [Fact]
    public void Tier_never_exceeds_rank_max()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            var a = WithFloor(2, seed, "clear");
            var w = Rich(D);
            for (int i = 0; i < 3; i++) Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "imbue", 0));
            Assert.All(a.Spec.Floors[0].Mods, m =>
            {
                Assert.Equal(1, m.Tier);
                var t = D.Mod(m.ModId).Tiers[0];
                Assert.InRange(m.Value, t.Min, t.Max);
            });
        }
    }

    [Fact]
    public void Upgrade_rerolls_value_in_new_tier_range()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var a = FromFloor(5, seed, new FloorSpec("clear", null, "swamp", new[] { new ModInstance("monster_life", 1, 20) }));
            var w = Rich(D);
            Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "upgrade", 0));
            var m = Assert.Single(a.Spec.Floors[0].Mods);
            Assert.Equal(2, m.Tier);
            Assert.InRange(m.Value, 30, 40);
            Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "upgrade", 0));
            Assert.Equal(3, a.Spec.Floors[0].Mods[0].Tier);
            Assert.Equal(TowerRefusal.NothingToUpgrade, Crafting.Apply(D, a, w, "upgrade", 0));
            Assert.Equal(98, w.CountUsable("upgrade", 5));
        }
    }

    [Fact]
    public void Bias_value_rolls_in_upper_half()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var a = FromFloor(10, seed, new FloorSpec("clear", null, "swamp", new[] { new ModInstance("monster_life", 4, 60) }));
            Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, Rich(D), "bias_value", 0));
            var m = Assert.Single(a.Spec.Floors[0].Mods);
            Assert.Equal(4, m.Tier);
            Assert.InRange(m.Value, 65, 70);
        }
    }

    [Fact]
    public void Remove_one_removes_exactly_target()
    {
        var mods = new[] { new ModInstance("monster_life", 1, 20), new ModInstance("monster_damage", 1, 21), new ModInstance("reward_quantity", 1, 6) };
        var a = FromFloor(5, 1, new FloorSpec("clear", null, "swamp", mods));
        var w = Rich(D);
        Assert.Equal(TowerRefusal.ModNotFound, Crafting.Apply(D, a, w, "remove", 0, "elite_explode"));
        Assert.Equal(TowerRefusal.ModNotFound, Crafting.Apply(D, a, w, "remove", 0));
        Assert.Equal(100, w.CountUsable("remove", 5));
        Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "remove", 0, "monster_damage"));
        Assert.Equal(new[] { mods[0], mods[2] }, a.Spec.Floors[0].Mods);
        Assert.Equal(99, w.CountUsable("remove", 5));
    }

    [Fact]
    public void Clear_removes_all_mods_of_target()
    {
        var a = FromFloor(1, 1, new FloorSpec("clear", null, "swamp", new[] { new ModInstance("monster_life", 1, 20), new ModInstance("monster_damage", 1, 21) }));
        Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, Rich(D), "clear", 0));
        Assert.Empty(a.Spec.Floors[0].Mods);
    }

    [Fact]
    public void Reroll_variant_changes_variant_and_drops_type_mods()
    {
        var legacy = TowerDataLoader.Load(FixturePaths.TowerFixture).Data!;
        for (int seed = 0; seed < 100; seed++)
        {
            var mods = new[] { new ModInstance("escort_hunters", 1, 12), new ModInstance("monster_life", 1, 20) };
            var a = FromFloor(1, seed, new FloorSpec("trial", "escort", "swamp", mods), legacy);
            var w = Rich(legacy);
            Assert.Equal(TowerRefusal.None, Crafting.Apply(legacy, a, w, "reroll_variant", 0));
            var f = a.Spec.Floors[0];
            Assert.NotEqual("escort", f.Variant);
            Assert.Contains(f.Variant, legacy.FloorType("trial").Variants);
            Assert.Equal(new[] { mods[1] }, f.Mods);
            Assert.False(a.ShuffleBonusActive);
            // The slot remembers the new variant.
            var variant = f.Variant;
            Assert.Equal(TowerRefusal.None, a.SetFloorType(0, "clear"));
            Assert.Equal(TowerRefusal.None, a.SetFloorType(0, "trial"));
            Assert.Equal(variant, a.Spec.Floors[0].Variant);
        }
        var c = WithFloor(1, 1, "clear", legacy);
        Assert.Equal(TowerRefusal.NotATrial, Crafting.Apply(legacy, c, Rich(legacy), "reroll_variant", 0));
    }

    [Fact]
    public void Crafting_keeps_shuffle_bonus()
    {
        var a = new TowerAssembly(D, 5, Rng(9));
        var w = Rich(D);
        Assert.Equal(TowerRefusal.None, a.Shuffle(w));
        Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "imbue", 0));
        Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "reroll", 0));
        Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, w, "imbue_tower", null));
        Assert.True(a.ShuffleBonusActive);
    }

    [Fact]
    public void Reroll_one_replaces_with_another_mod_in_same_position()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var mods = new[] { new ModInstance("more_kill_threshold", 1, 12), new ModInstance("monster_life", 1, 20) };
            var a = FromFloor(3, seed, new FloorSpec("clear", null, "swamp", mods));
            Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, Rich(D), "reroll", 0));
            var after = a.Spec.Floors[0].Mods;
            Assert.Equal(2, after.Count);
            bool firstKept = after[0] == mods[0];
            bool secondKept = after[1] == mods[1];
            Assert.True(firstKept ^ secondKept, $"seed {seed}");
            int changed = firstKept ? 1 : 0;
            Assert.NotEqual(mods[changed].ModId, after[changed].ModId);
            Assert.Equal(D.Mod(mods[changed].ModId).Kind, D.Mod(after[changed].ModId).Kind);
        }
    }

    [Fact]
    public void Biased_currency_hits_direction_about_half()
    {
        double Share(string currency)
        {
            int hits = 0;
            for (int seed = 0; seed < 2000; seed++)
            {
                var a = WithFloor(4, seed, "clear");
                Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, Rich(D, 1), currency, 0));
                if (D.Mod(a.Spec.Floors[0].Mods.Single().ModId).Directions.Contains("elite")) hits++;
            }
            return hits / 2000.0;
        }
        Assert.InRange(Share("biased_rare_elite"), 0.45, 0.65);
        Assert.True(Share("imbue") < 0.3);
    }

    [Fact]
    public void Same_seed_same_result()
    {
        string Run(int seed)
        {
            var a = new TowerAssembly(D, 5, Rng(seed));
            var w = Rich(D);
            Assert.Equal(TowerRefusal.None, a.Shuffle(w));
            for (int f = 0; f < 5; f++)
                for (int i = 0; i < 3; i++) Crafting.Apply(D, a, w, "imbue", f);
            Crafting.Apply(D, a, w, "imbue_type", 1);
            Crafting.Apply(D, a, w, "imbue_tower", null);
            Crafting.Apply(D, a, w, "imbue_tower", null);
            Crafting.Apply(D, a, w, "reroll", 2);
            Crafting.Apply(D, a, w, "upgrade", 3);
            Crafting.Apply(D, a, w, "biased_rare_reward", 4);
            for (int f = 0; f < 5; f++) Crafting.Apply(D, a, w, "reroll_variant", f);
            return TowerCodec.ToJson(a.Spec);
        }
        Assert.Equal(Run(42), Run(42));
        Assert.NotEqual(Run(42), Run(43));
    }

    private static bool HasHardCounter(TowerAssembly a) =>
        a.Spec.Floors[0].Mods.Any(m => D.Mod(m.ModId).HardCounter != HardCounter.None);

    [Fact]
    public void Imbue_never_yields_hard_counter_on_rank_4()
    {
        for (int seed = 0; seed < 1000; seed++)
        {
            var a = WithFloor(4, seed, "clear");
            Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, Rich(D), "imbue", 0));
            Assert.False(HasHardCounter(a), $"seed {seed}");
        }
    }

    [Fact]
    public void Imbue_can_yield_hard_counter_on_rank_5()
    {
        bool seen = false;
        for (int seed = 0; seed < 1000 && !seen; seed++)
        {
            var a = WithFloor(5, seed, "clear");
            Assert.Equal(TowerRefusal.None, Crafting.Apply(D, a, Rich(D), "imbue", 0));
            seen = HasHardCounter(a);
        }
        Assert.True(seen);
    }
}
