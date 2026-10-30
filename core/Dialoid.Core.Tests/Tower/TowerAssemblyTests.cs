using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

public class TowerAssemblyTests
{
    private static readonly TowerData D = TowerDataLoader.Load(FixturePaths.TowerFixture).Data!;

    private static IRandomSource Rng(int seed) => RandomSources.Create(SimMode.Roll, seed);

    private static FloorSpec Empty() => new(null, null, null, Array.Empty<ModInstance>());

    private static TowerSpec SpecWith(params FloorSpec[] floors)
    {
        var all = floors.Concat(Enumerable.Range(0, 5 - floors.Length).Select(_ => Empty())).ToArray();
        return new TowerSpec(5, all, Array.Empty<ModInstance>(), null);
    }

    [Fact]
    public void New_assembly_has_five_empty_slots()
    {
        var a = new TowerAssembly(D, 3, Rng(1));
        Assert.Equal(5, a.Spec.Floors.Count);
        Assert.All(a.Spec.Floors, f => Assert.Equal(Empty() with { Mods = f.Mods }, f));
        Assert.All(a.Spec.Floors, f => Assert.Empty(f.Mods));
        Assert.Empty(a.Spec.TowerMods);
        Assert.False(a.ShuffleBonusActive);
        Assert.Equal(0, a.ShufflesDone);
    }

    [Fact]
    public void Shuffle_never_places_same_type_twice_in_a_row()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var a = new TowerAssembly(D, 1, Rng(seed));
            Assert.Equal(TowerRefusal.None, a.Shuffle(new Wallet(D)));
            var floors = a.Spec.Floors;
            Assert.All(floors, f => Assert.NotNull(f.TypeId));
            Assert.All(floors, f => Assert.True(D.HasBiome(f.BiomeId!)));
            for (int i = 1; i < floors.Count; i++) Assert.NotEqual(floors[i - 1].TypeId, floors[i].TypeId);
            Assert.All(floors, f => Assert.Equal(f.TypeId == "trial", f.Variant != null));
        }
    }

    [Fact]
    public void First_shuffle_is_free_then_costs_grow()
    {
        var a = new TowerAssembly(D, 1, Rng(7));
        var w = new Wallet(D);
        w.Add("shuffle", 0, 3);
        var costs = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            costs.Add(a.NextShuffleCost);
            Assert.Equal(TowerRefusal.None, a.Shuffle(w));
        }
        costs.Add(a.NextShuffleCost);
        Assert.Equal(new[] { 0, 1, 2, 4 }, costs);
        Assert.Equal(0, w.CountUsable("shuffle", 1));

        var before = TowerCodec.ToJson(a.Spec);
        w.Add("shuffle", 0, 3);
        Assert.Equal(TowerRefusal.NotEnoughCurrency, a.Shuffle(w));
        Assert.Equal(before, TowerCodec.ToJson(a.Spec));
        Assert.Equal(3, a.ShufflesDone);
        Assert.Equal(3, w.CountUsable("shuffle", 1));
    }

    [Fact]
    public void Shuffle_cost_doubles_after_the_list_ends()
    {
        var a = new TowerAssembly(D, 1, Rng(7));
        var w = new Wallet(D);
        w.Add("shuffle", 0, 1000);
        for (int i = 0; i < 6; i++) a.Shuffle(w);
        Assert.Equal(32, a.NextShuffleCost);
        a.Shuffle(w);
        Assert.Equal(64, a.NextShuffleCost);
    }

    [Fact]
    public void Manual_edit_after_shuffle_drops_bonus_for_good()
    {
        var a = new TowerAssembly(D, 1, Rng(3));
        a.Shuffle(new Wallet(D));
        Assert.True(a.ShuffleBonusActive);

        a.SetBiome(0, a.Spec.Floors[0].BiomeId == "crypt" ? "swamp" : "crypt");
        Assert.True(a.ShuffleBonusActive);

        string original = a.Spec.Floors[0].TypeId!;
        Assert.Equal(TowerRefusal.None, a.SetFloorType(0, original));
        Assert.True(a.ShuffleBonusActive);

        string other = D.FloorTypes.First(t => t.Id != original).Id;
        Assert.Equal(TowerRefusal.None, a.SetFloorType(0, other));
        Assert.False(a.ShuffleBonusActive);
        Assert.Equal(TowerRefusal.None, a.SetFloorType(0, original));
        Assert.False(a.ShuffleBonusActive);
        Assert.Equal(1, a.NextShuffleCost);
    }

    [Fact]
    public void Shuffle_keeps_tower_mods_and_clears_floor_mods()
    {
        var floor = new FloorSpec("clear", null, "crypt", new[] { new ModInstance("monster_life", 1, 20) });
        var towerMods = new[] { new ModInstance("tower_gate_life", 1, 10) };
        var spec = SpecWith(floor) with { TowerMods = towerMods };
        var a = TowerAssembly.FromSpec(D, spec, Rng(5));
        Assert.Single(a.Spec.Floors[0].Mods);

        Assert.Equal(TowerRefusal.None, a.Shuffle(new Wallet(D)));
        Assert.All(a.Spec.Floors, f => Assert.Empty(f.Mods));
        Assert.Equal(towerMods, a.Spec.TowerMods);
    }

    [Fact]
    public void Trial_variant_is_remembered_by_slot()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var a = new TowerAssembly(D, 1, Rng(seed));
            Assert.Equal(TowerRefusal.None, a.SetFloorType(2, "trial"));
            string? variant = a.Spec.Floors[2].Variant;
            Assert.Contains(variant, D.FloorType("trial").Variants);
            string biome = a.Spec.Floors[2].BiomeId!;

            a.SetFloorType(2, "clear");
            Assert.Null(a.Spec.Floors[2].Variant);
            Assert.Equal(biome, a.Spec.Floors[2].BiomeId);
            a.SetFloorType(2, "trial");
            Assert.Equal(variant, a.Spec.Floors[2].Variant);
        }
    }

    [Fact]
    public void Shuffle_overwrites_slot_variant_memory()
    {
        var spec = SpecWith(new FloorSpec("trial", "escort", "crypt", Array.Empty<ModInstance>()));
        bool sawOther = false;
        for (int seed = 0; seed < 50 && !sawOther; seed++)
        {
            var a = TowerAssembly.FromSpec(D, spec, Rng(seed));
            a.Shuffle(new Wallet(D));
            var after = a.Spec.Floors[0];
            if (after.TypeId != "trial") a.SetFloorType(0, "trial");
            sawOther = a.Spec.Floors[0].Variant != "escort";
        }
        Assert.True(sawOther);
    }

    [Fact]
    public void Changing_type_removes_type_mods()
    {
        var mods = new[]
        {
            new ModInstance("extra_wave", 1, 1),
            new ModInstance("monster_life", 1, 20),
            new ModInstance("more_packs", 1, 12),
        };
        var a = TowerAssembly.FromSpec(D, SpecWith(new FloorSpec("waves", null, "swamp", mods)), Rng(1));

        Assert.Equal(TowerRefusal.None, a.SetFloorType(0, "clear"));
        Assert.Equal(new[] { "monster_life", "more_packs" }, a.Spec.Floors[0].Mods.Select(m => m.ModId));

        Assert.Equal(TowerRefusal.None, a.SetFloorType(0, "bosses"));
        Assert.Equal(new[] { "monster_life" }, a.Spec.Floors[0].Mods.Select(m => m.ModId));
        Assert.Equal("swamp", a.Spec.Floors[0].BiomeId);
    }

    [Fact]
    public void Set_floor_type_refuses_bad_slot_and_type()
    {
        var a = new TowerAssembly(D, 1, Rng(1));
        Assert.Equal(TowerRefusal.SlotOutOfRange, a.SetFloorType(5, "clear"));
        Assert.Equal(TowerRefusal.SlotOutOfRange, a.SetFloorType(-1, "clear"));
        Assert.Equal(TowerRefusal.UnknownType, a.SetFloorType(0, "nope"));
        Assert.Null(a.Spec.Floors[0].TypeId);
        Assert.Throws<ArgumentException>(() => a.SetBiome(0, "nope"));
        Assert.ThrowsAny<ArgumentException>(() => a.SetBiome(5, "crypt"));
    }

    [Fact]
    public void Spec_is_a_snapshot()
    {
        var a = new TowerAssembly(D, 1, Rng(1));
        var before = a.Spec;
        a.SetFloorType(0, "clear");
        a.SetUberBoss("baal");
        Assert.Null(before.Floors[0].TypeId);
        Assert.Null(before.UberBossId);
        Assert.Equal("baal", a.Spec.UberBossId);
    }

    [Fact]
    public void Crafting_mutators_keep_shuffle_bonus()
    {
        var a = new TowerAssembly(D, 3, Rng(2));
        a.Shuffle(new Wallet(D));
        int trial = Enumerable.Range(0, 5).FirstOrDefault(i => a.Spec.Floors[i].TypeId == "trial", -1);
        a.SetFloorMods(0, new[] { new ModInstance("monster_life", 1, 20) });
        a.SetTowerMods(new[] { new ModInstance("tower_gate_life", 1, 10) });
        if (trial >= 0) a.SetVariant(trial, "hold");
        Assert.True(a.ShuffleBonusActive);
        Assert.Single(a.Spec.Floors[0].Mods);
        Assert.Single(a.Spec.TowerMods);
    }

    [Fact]
    public void Every_refusal_has_russian_text()
    {
        var texts = Enum.GetValues<TowerRefusal>().Select(TowerTexts.Refusal).ToList();
        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.All(texts, t => Assert.Matches("[А-Яа-яЁё]", t));
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }
}
