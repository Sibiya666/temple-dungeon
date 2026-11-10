using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

public class ActivityInventoryTests
{
    private static readonly TowerData Data = TowerDataLoader.Load(FixturePaths.RealTowerData).Data!;
    private static TowerAssembly Assembly(int rank = 5) => new(Data, rank, RandomSources.Create(SimMode.Roll, 42));
    private static Wallet Wallet()
    {
        var wallet = new Wallet(Data);
        foreach (var currency in Data.Currencies) wallet.Add(currency.Id, 15, 10);
        return wallet;
    }

    private static string Snapshot(object value) => System.Text.Json.JsonSerializer.Serialize(value);

    [Fact]
    public void Fill_preserves_installed_and_inventory_mods_and_owned_instances_roundtrip()
    {
        var inventory = new ActivityInventory(Data);
        inventory.Add("escort", 5, 5);
        inventory.Add("clear", 1, 9);
        var assembly = Assembly();
        var wallet = Wallet();
        Assert.All(assembly.Spec.Floors, f => Assert.Null(f.TypeId));
        var stack = Assert.Single(inventory.Groups(5)).Id;
        Assert.Equal(TowerRefusal.None, inventory.TryInstall(assembly, 2, stack));
        Assert.Equal(TowerRefusal.None, inventory.ApplyToFloor(assembly, 2, "imbue", wallet));
        var installed = Snapshot(assembly.Spec.Floors[2]);
        Assert.Equal(TowerRefusal.None, inventory.ApplyToInventory(stack, "imbue", wallet));
        var modified = inventory.Groups(5).Single(g => g.EverModified);
        var otherRank = Snapshot(inventory.Groups(1));
        Assert.Equal(TowerRefusal.None, inventory.FillRandom(assembly, wallet));
        Assert.Equal(installed, Snapshot(assembly.Spec.Floors[2]));
        Assert.Empty(inventory.Groups(5));
        Assert.Equal(otherRank, Snapshot(inventory.Groups(1)));
        Assert.Equal(5, assembly.Spec.Floors.Count(f => f.TypeId == "escort"));
        Assert.True(assembly.ShuffleBonusActive);
        Assert.Equal(1, assembly.ShufflesDone);
        Assert.Equal(10, wallet.CountUsable("shuffle", 5));
        for (int slot = 0; slot < 5; slot++) Assert.Equal(TowerRefusal.None, inventory.Remove(assembly, slot));
        Assert.Equal(5, inventory.Groups(5).Sum(g => g.Count));
        var returned = inventory.Groups(5).Single(g => g.Id == modified.Id);
        Assert.Equal(modified.Mods, returned.Mods);
        Assert.Equal(modified.Variant, returned.Variant);
        Assert.Equal(modified.BiomeId, returned.BiomeId);
        Assert.Equal(2, inventory.Groups(5).Count(g => g.EverModified));
    }

    [Theory]
    [InlineData("items")]
    [InlineData("currency")]
    [InlineData("full")]
    public void Fill_refusals_leave_all_state_and_random_stream_unchanged(string failure)
    {
        var inventory = new ActivityInventory(Data);
        var controlInventory = new ActivityInventory(Data);
        var assembly = Assembly();
        var control = Assembly();
        var wallet = new Wallet(Data);
        if (failure != "items")
        {
            inventory.Add("escort", 5, 5);
            controlInventory.Add("escort", 5, 5);
            Assert.Equal(TowerRefusal.None, inventory.FillRandom(assembly, wallet));
            Assert.Equal(TowerRefusal.None, controlInventory.FillRandom(control, new Wallet(Data)));
            if (failure == "currency")
                for (int slot = 0; slot < 5; slot++)
                {
                    inventory.Remove(assembly, slot);
                    controlInventory.Remove(control, slot);
                }
        }
        else
        {
            inventory.Add("escort", 5, 4);
            controlInventory.Add("escort", 5, 4);
            inventory.Add("clear", 1, 20);
            controlInventory.Add("clear", 1, 20);
            wallet.Add("shuffle", 15, 10);
        }
        var before = Snapshot(assembly.Spec);
        var groups = Snapshot(inventory.Groups(5));
        var others = Snapshot(inventory.Groups(1));
        var money = Snapshot(Data.Currencies.Select(c => wallet.Levels(c.Id)).ToArray());
        int count = assembly.ShufflesDone;
        bool bonus = assembly.ShuffleBonusActive;
        var expected = failure == "items" ? TowerRefusal.NotEnoughActivities : failure == "full" ? TowerRefusal.NoEmptyFloors : TowerRefusal.NotEnoughCurrency;
        Assert.Equal(expected, inventory.FillRandom(assembly, wallet));
        Assert.Equal(before, Snapshot(assembly.Spec));
        Assert.Equal(groups, Snapshot(inventory.Groups(5)));
        Assert.Equal(others, Snapshot(inventory.Groups(1)));
        Assert.Equal(money, Snapshot(Data.Currencies.Select(c => wallet.Levels(c.Id)).ToArray()));
        Assert.Equal(count, assembly.ShufflesDone);
        Assert.Equal(bonus, assembly.ShuffleBonusActive);
        for (int i = 0; i < 10; i++) Assert.Equal(control.Rng.InRange(0, 1), assembly.Rng.InRange(0, 1));
        if (failure == "items")
        {
            inventory.Add("clear", 5, 1);
            controlInventory.Add("clear", 5, 1);
        }
        if (failure == "full")
        {
            inventory.Remove(assembly, 0);
            controlInventory.Remove(control, 0);
        }
        Assert.Equal(TowerRefusal.None, inventory.FillRandom(assembly, Wallet()));
        Assert.Equal(TowerRefusal.None, controlInventory.FillRandom(control, Wallet()));
        Assert.Equal(Snapshot(control.Spec), Snapshot(assembly.Spec));
    }

    [Fact]
    public void Fill_pays_existing_cost_once_per_success_and_keeps_other_currencies()
    {
        var inventory = new ActivityInventory(Data);
        inventory.Add("clear", 5, 5);
        var assembly = Assembly();
        var wallet = Wallet();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int cost = new[] { 0, 1, 2, 4 }[attempt];
            int balance = wallet.CountUsable("shuffle", 5);
            Assert.Equal(cost, assembly.NextShuffleCost);
            Assert.Equal(TowerRefusal.None, inventory.FillRandom(assembly, wallet));
            Assert.Equal(balance - cost, wallet.CountUsable("shuffle", 5));
            Assert.Equal(attempt + 1, assembly.ShufflesDone);
            Assert.True(assembly.ShuffleBonusActive);
            Assert.Equal(10, wallet.CountUsable("imbue", 5));
            inventory.Remove(assembly, 3);
        }
        Assert.Equal(5, inventory.Groups(5).Sum(g => g.Count) + assembly.Spec.Floors.Count(f => f.TypeId != null));
    }

    [Fact]
    public void Fill_is_seeded_and_selects_instances_not_groups_without_replacement()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var inventory = new ActivityInventory(Data);
            var otherInventory = new ActivityInventory(Data);
            foreach (var target in new[] { inventory, otherInventory })
            {
                target.Add("clear", 5, 9);
                target.Add("escort", 5, 1);
            }
            var assembly = new TowerAssembly(Data, 5, RandomSources.Create(SimMode.Roll, seed));
            var other = new TowerAssembly(Data, 5, RandomSources.Create(SimMode.Roll, seed));
            // Nine of the ten individual tickets are clear, independently of stack count.
            bool firstIsClear = RandomSources.Create(SimMode.Roll, seed).InRange(0, 10) < 9;
            Assert.Equal(TowerRefusal.None, inventory.FillRandom(assembly, Wallet()));
            Assert.Equal(TowerRefusal.None, otherInventory.FillRandom(other, Wallet()));
            Assert.Equal(firstIsClear ? "clear" : "escort", assembly.Spec.Floors[0].TypeId);
            Assert.Equal(Snapshot(other.Spec), Snapshot(assembly.Spec));
            Assert.InRange(assembly.Spec.Floors.Count(f => f.TypeId == "escort"), 0, 1);
            Assert.Equal(5, inventory.Groups(5).Sum(g => g.Count));
            for (int slot = 0; slot < 5; slot++) inventory.Remove(assembly, slot);
            Assert.Equal(9, inventory.Groups(5).Single(g => g.TypeId == "clear").Count);
            Assert.Equal(1, inventory.Groups(5).Single(g => g.TypeId == "escort").Count);
        }
    }

    [Fact]
    public void Swapping_returns_prior_and_reuses_exact_variant_biome_and_mods()
    {
        var inventory = new ActivityInventory(Data);
        inventory.Add("escort", 5, 1);
        inventory.Add("clear", 5, 1);
        var assembly = Assembly();
        var wallet = Wallet();
        var escortId = inventory.Groups(5).Single(g => g.TypeId == "escort").Id;
        Assert.Equal(TowerRefusal.None, inventory.TryInstall(assembly, 0, escortId));
        Assert.Equal(TowerRefusal.None, inventory.ApplyToFloor(assembly, 0, "imbue", wallet));
        var before = assembly.Spec.Floors[0];
        var clearId = inventory.Groups(5).Single().Id;
        Assert.Equal(TowerRefusal.None, inventory.TryInstall(assembly, 0, clearId));
        Assert.Equal("clear", assembly.Spec.Floors[0].TypeId);
        var returned = Assert.Single(inventory.Groups(5));
        Assert.True(returned.EverModified);
        Assert.Equal(before.Mods, returned.Mods);
        Assert.Equal(TowerRefusal.None, inventory.TryInstall(assembly, 1, returned.Id));
        var after = assembly.Spec.Floors[1];
        Assert.Equal(before.TypeId, after.TypeId);
        Assert.Equal(before.Variant, after.Variant);
        Assert.Equal(before.BiomeId, after.BiomeId);
        Assert.Equal(before.Mods, after.Mods);
        Assert.Equal(TowerRefusal.None, inventory.Remove(assembly, 1));
        Assert.Null(assembly.Spec.Floors[1].TypeId);
        Assert.Empty(assembly.Spec.Floors[1].Mods);
        Assert.Equal(1, inventory.Groups(5).Sum(g => g.Count));
        Assert.Equal(TowerRefusal.None, inventory.Remove(assembly, 0));
        Assert.Equal(2, inventory.Groups(5).Sum(g => g.Count));
    }

    [Fact]
    public void Repeated_install_and_remove_preserve_each_modified_instance_of_the_same_type()
    {
        var inventory = new ActivityInventory(Data);
        inventory.Add("escort", 5, 2);
        var stack = Assert.Single(inventory.Groups(5)).Id;
        var wallet = Wallet();
        Assert.Equal(TowerRefusal.None, inventory.ApplyToInventory(stack, "imbue", wallet, out var first));
        Assert.Equal(TowerRefusal.None, inventory.ApplyToInventory(stack, "imbue", wallet, out var second));
        var original = inventory.Groups(5).ToDictionary(g => g.Id, g => Snapshot(g));
        var assembly = Assembly();
        Assert.Equal(TowerRefusal.None, inventory.TryInstall(assembly, 1, first!));
        Assert.Equal(TowerRefusal.None, inventory.TryInstall(assembly, 1, second!));
        var installed = Snapshot(assembly.Spec);

        Assert.Equal(TowerRefusal.EmptySlot, inventory.TryInstall(assembly, 1, second!));
        Assert.Equal(installed, Snapshot(assembly.Spec));
        Assert.Equal(original[first!], Snapshot(Assert.Single(inventory.Groups(5))));

        Assert.Equal(TowerRefusal.None, inventory.Remove(assembly, 1));
        Assert.Equal(TowerRefusal.EmptySlot, inventory.Remove(assembly, 1));
        Assert.Null(assembly.Spec.Floors[1].TypeId);
        var returned = inventory.Groups(5).ToDictionary(g => g.Id, g => Snapshot(g));
        Assert.Equal(original.Count, returned.Count);
        Assert.Equal(original[first!], returned[first!]);
        Assert.Equal(original[second!], returned[second!]);
    }

    [Fact]
    public void Successful_inventory_craft_splits_only_one_and_clear_never_restacks()
    {
        var inventory = new ActivityInventory(Data);
        inventory.Add("clear", 5, 3);
        var wallet = Wallet();
        var stack = Assert.Single(inventory.Groups(5));
        Assert.Equal(TowerRefusal.None, inventory.ApplyToInventory(stack.Id, "imbue", wallet));
        var modified = inventory.Groups(5).Single(g => g.EverModified);
        Assert.Equal(1, modified.Count);
        Assert.Equal(2, inventory.Groups(5).Single(g => !g.EverModified).Count);
        Assert.Equal(TowerRefusal.None, inventory.ApplyToInventory(modified.Id, "clear", wallet));
        var cleared = inventory.Groups(5).Single(g => g.Id == modified.Id);
        Assert.True(cleared.EverModified);
        Assert.Empty(cleared.Mods);
        var assembly = Assembly();
        Assert.Equal(TowerRefusal.None, inventory.TryInstall(assembly, 0, cleared.Id));
        Assert.Equal(TowerRefusal.None, inventory.Remove(assembly, 0));
        Assert.Equal(2, inventory.Groups(5).Count);
        Assert.True(inventory.Groups(5).Single(g => g.Id == cleared.Id).EverModified);
    }

    [Fact]
    public void Failed_crafting_is_atomic_including_subsequent_random_result()
    {
        var a = new ActivityInventory(Data);
        var b = new ActivityInventory(Data);
        a.Add("escort", 5, 2);
        b.Add("escort", 5, 2);
        var id = Assert.Single(a.Groups(5)).Id;
        var emptyWallet = new Wallet(Data);
        Assert.Equal(TowerRefusal.NotEnoughCurrency, a.ApplyToInventory(id, "imbue", emptyWallet));
        Assert.Equal(0, emptyWallet.CountUsable("imbue", 5));
        var unchanged = Assert.Single(a.Groups(5));
        Assert.Equal(2, unchanged.Count);
        Assert.False(unchanged.EverModified);
        Assert.Null(unchanged.Variant);
        Assert.Null(unchanged.BiomeId);
        Assert.Empty(unchanged.Mods);
        var wa = Wallet();
        var wb = Wallet();
        Assert.Equal(TowerRefusal.WrongTarget, a.ApplyToInventory(id, "imbue_tower", wa));
        Assert.Equal(10, wa.CountUsable("imbue_tower", 5));
        Assert.Equal(TowerRefusal.None, a.ApplyToInventory(id, "imbue", wa));
        Assert.Equal(TowerRefusal.None, b.ApplyToInventory(id, "imbue", wb));
        var ga = a.Groups(5).Single(g => g.EverModified);
        var gb = b.Groups(5).Single(g => g.EverModified);
        Assert.Equal(gb.Mods, ga.Mods);
        Assert.Equal(gb.Variant, ga.Variant);
        Assert.Equal(gb.BiomeId, ga.BiomeId);
    }

    [Fact]
    public void Unmodified_escort_keeps_exact_rolls_between_different_assemblies()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var inventory = new ActivityInventory(Data);
            inventory.Add("escort", 5, 2);
            var first = new TowerAssembly(Data, 5, RandomSources.Create(SimMode.Roll, seed));
            var second = new TowerAssembly(Data, 5, RandomSources.Create(SimMode.Roll, seed + 100));
            var id = Assert.Single(inventory.Groups(5)).Id;
            Assert.Equal(TowerRefusal.None, inventory.TryInstall(first, 0, id));
            var original = first.Spec.Floors[0];
            Assert.Null(original.Variant);
            Assert.NotNull(original.BiomeId);
            // Consume the unrolled member so the next install selects the returned exact specimen.
            Assert.Equal(TowerRefusal.None, inventory.TryInstall(first, 1, id));
            Assert.Equal(TowerRefusal.None, inventory.Remove(first, 0));
            Assert.Equal(TowerRefusal.None, inventory.TryInstall(second, 4, id));
            Assert.Equal(original.Variant, second.Spec.Floors[4].Variant);
            Assert.Equal(original.BiomeId, second.Spec.Floors[4].BiomeId);
            Assert.Empty(second.Spec.Floors[4].Mods);
        }
    }

    [Fact]
    public void Last_crafted_id_points_to_split_singleton_and_survives_refusal()
    {
        var inventory = new ActivityInventory(Data);
        inventory.Add("clear", 5, 2);
        var id = Assert.Single(inventory.Groups(5)).Id;
        Assert.Null(inventory.LastCraftedGroupId);
        Assert.Equal(TowerRefusal.None, inventory.ApplyToInventory(id, "imbue", Wallet(), out var resultingId));
        Assert.Equal(resultingId, inventory.LastCraftedGroupId);
        Assert.Equal(inventory.Groups(5).Single(g => g.EverModified).Id, resultingId);
        Assert.Equal(TowerRefusal.NoMods, inventory.ApplyToInventory(id, "clear", Wallet(), out var failedId));
        Assert.Null(failedId);
        Assert.Equal(resultingId, inventory.LastCraftedGroupId);
    }
    [Fact]
    public void Rank_queries_do_not_grant_items_and_invalid_transfers_change_nothing()
    {
        var inventory = new ActivityInventory(Data);
        inventory.Add("clear", 1, 2);
        var group = Assert.Single(inventory.Groups(1));
        Assert.Empty(inventory.Groups(5));
        var assembly = Assembly();
        Assert.Equal(TowerRefusal.SlotOutOfRange, inventory.TryInstall(assembly, -1, group.Id));
        Assert.Throws<ArgumentException>(() => inventory.TryInstall(assembly, 0, group.Id));
        Assert.Equal(TowerRefusal.EmptySlot, inventory.TryInstall(assembly, 0, "missing"));
        Assert.Equal(2, Assert.Single(inventory.Groups(1)).Count);
        Assert.All(assembly.Spec.Floors, f => Assert.Null(f.TypeId));
    }

    [Fact]
    public void Failed_floor_craft_does_not_mark_item_and_successful_clear_stays_single()
    {
        var inventory = new ActivityInventory(Data);
        inventory.Add("clear", 5, 2);
        var assembly = Assembly();
        var id = Assert.Single(inventory.Groups(5)).Id;
        inventory.TryInstall(assembly, 0, id);
        Assert.Equal(TowerRefusal.NoMods, inventory.ApplyToFloor(assembly, 0, "clear", Wallet()));
        inventory.Remove(assembly, 0);
        Assert.Equal(2, Assert.Single(inventory.Groups(5)).Count);
        inventory.TryInstall(assembly, 0, id);
        Assert.Equal(TowerRefusal.None, inventory.ApplyToFloor(assembly, 0, "imbue", Wallet()));
        Assert.Equal(TowerRefusal.None, inventory.ApplyToFloor(assembly, 0, "clear", Wallet()));
        inventory.Remove(assembly, 0);
        Assert.Equal(2, inventory.Groups(5).Count);
        Assert.Empty(inventory.Groups(5).Single(g => g.EverModified).Mods);
    }
}
