using Dialoid.Core.Simulation;

namespace Dialoid.Core.Tower;

/// <summary>A snapshot. Unmodified activities stack by type and rank; modified activities stay individual forever.</summary>
public sealed record ActivityInventoryGroup(string Id, string TypeId, int Rank, int Count,
    IReadOnlyList<ModInstance> Mods, bool EverModified, string? Variant, string? BiomeId);

/// <summary>
/// Owns activities moved through this API. Callers must not replace inventory-managed floors directly.
/// Add is a seed/loot grant, not a rank-change operation. Returned floors retain their exact rolled composition.
/// </summary>
public sealed class ActivityInventory
{
    private sealed class Item(string id, int rank, FloorSpec floor)
    {
        public string Id = id;
        public int Rank = rank;
        public FloorSpec Floor = floor;
        public bool EverModified;
    }

    private readonly TowerData _data;
    private readonly List<Item> _items = new();
    private readonly Dictionary<(TowerAssembly Assembly, int Slot), Item> _installed = new();
    private long _nextId;
    private int _craftSequence;

    /// <summary>The singleton produced by the last successful inventory craft; refusals leave it unchanged.</summary>
    public string? LastCraftedGroupId { get; private set; }

    public ActivityInventory(TowerData data) => _data = data ?? throw new ArgumentNullException(nameof(data));

    public void Add(string typeId, int rank, int count)
    {
        _data.FloorType(typeId);
        RankRules.Def(_data.Rules, rank);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = 0; i < count; i++)
            _items.Add(new Item(NewId(), rank, new FloorSpec(typeId, null, null, Array.Empty<ModInstance>())));
    }

    public IReadOnlyList<ActivityInventoryGroup> Groups(int rank)
    {
        RankRules.Def(_data.Rules, rank);
        return Array.AsReadOnly(_items.Where(i => i.Rank == rank).GroupBy(GroupId, StringComparer.Ordinal)
            .Select(g =>
            {
                var i = g.First();
                return new ActivityInventoryGroup(g.Key, i.Floor.TypeId!, rank, g.Count(),
                    Array.AsReadOnly(i.Floor.Mods.ToArray()), i.EverModified, i.Floor.Variant, i.Floor.BiomeId);
            }).ToArray());
    }

    public TowerRefusal TryInstall(TowerAssembly assembly, int slot, string groupId)
    {
        if (!ValidSlot(assembly, slot)) return TowerRefusal.SlotOutOfRange;
        var item = Find(groupId);
        if (item == null) return TowerRefusal.EmptySlot;
        if (item.Rank != assembly.Rank) throw new ArgumentException("Ранг активности не совпадает с рангом башни", nameof(groupId));
        var floor = item.Floor;
        if (floor.BiomeId == null)
        {
            // Roll only a newly seeded activity, on the destination's deterministic random stream.
            var temporary = new TowerAssembly(_data, item.Rank, assembly.Rng);
            temporary.SetFloorType(0, floor.TypeId!);
            floor = temporary.Spec.Floors[0];
        }
        ReturnPrior(assembly, slot);
        assembly.ReplaceInventoryFloor(slot, floor);
        item.Floor = floor;
        _items.Remove(item);
        _installed[(assembly, slot)] = item;
        return TowerRefusal.None;
    }

    /// <summary>
    /// Fills only empty floors from owned instances of this rank, uniformly without replacement.
    /// Refusals leave inventory, assembly, wallet and random stream untouched; success pays one shuffle cost.
    /// </summary>
    public TowerRefusal FillRandom(TowerAssembly assembly, Wallet wallet)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(wallet);
        var floors = assembly.Spec.Floors;
        var empty = Enumerable.Range(0, floors.Count).Where(slot => floors[slot].TypeId == null).ToArray();
        if (empty.Length == 0) return TowerRefusal.NoEmptyFloors;
        var available = _items.Where(item => item.Rank == assembly.Rank).ToList();
        if (available.Count < empty.Length) return TowerRefusal.NotEnoughActivities;
        int cost = assembly.NextShuffleCost;
        if (wallet.CountUsable("shuffle", assembly.Rank) < cost) return TowerRefusal.NotEnoughCurrency;

        if (cost > 0) wallet.TrySpend("shuffle", assembly.Rank, cost);
        foreach (int slot in empty)
        {
            int index = TowerRandom.Pick(assembly.Rng, available.Count);
            var item = available[index];
            available.RemoveAt(index);
            var floor = item.Floor;
            if (floor.BiomeId == null)
            {
                var temporary = new TowerAssembly(_data, item.Rank, assembly.Rng);
                temporary.SetFloorType(0, floor.TypeId!);
                floor = temporary.Spec.Floors[0];
            }
            assembly.ReplaceInventoryFloor(slot, floor);
            item.Floor = floor;
            _items.Remove(item);
            _installed[(assembly, slot)] = item;
        }
        assembly.RecordInventoryShuffle();
        return TowerRefusal.None;
    }

    public TowerRefusal Remove(TowerAssembly assembly, int slot)
    {
        if (!ValidSlot(assembly, slot)) return TowerRefusal.SlotOutOfRange;
        if (assembly.Spec.Floors[slot].TypeId == null) return TowerRefusal.EmptySlot;
        ReturnPrior(assembly, slot);
        assembly.ReplaceInventoryFloor(slot, new FloorSpec(null, null, null, Array.Empty<ModInstance>()));
        return TowerRefusal.None;
    }

    public TowerRefusal ApplyToInventory(string groupId, string currencyId, Wallet wallet, string? targetModId = null)
        => ApplyToInventory(groupId, currencyId, wallet, out _, targetModId);

    public TowerRefusal ApplyToInventory(string groupId, string currencyId, Wallet wallet, out string? resultingGroupId, string? targetModId = null)
    {
        resultingGroupId = null;
        var item = Find(groupId);
        if (item == null) return TowerRefusal.EmptySlot;
        // Failed attempts neither split the stack nor advance the inventory's craft sequence.
        var temporary = new TowerAssembly(_data, item.Rank, RandomSources.Create(SimMode.Roll, _craftSequence));
        var floor = item.Floor;
        if (floor.BiomeId == null)
        {
            temporary.SetFloorType(0, floor.TypeId!);
        }
        else temporary.ReplaceInventoryFloor(0, floor);
        var result = Crafting.Apply(_data, temporary, wallet, currencyId, 0, targetModId);
        if (result != TowerRefusal.None) return result;
        item.Floor = temporary.Spec.Floors[0];
        item.EverModified = true;
        resultingGroupId = LastCraftedGroupId = item.Id;
        _craftSequence = unchecked(_craftSequence + 1);
        return TowerRefusal.None;
    }

    public TowerRefusal ApplyToFloor(TowerAssembly assembly, int slot, string currencyId, Wallet wallet, string? targetModId = null)
    {
        var result = Crafting.Apply(_data, assembly, wallet, currencyId, slot, targetModId);
        if (result == TowerRefusal.None)
        {
            var item = InstalledItem(assembly, slot);
            item.EverModified = true;
            item.Floor = assembly.Spec.Floors[slot];
        }
        return result;
    }

    private string NewId() => "activity:" + (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string GroupId(Item item) => item.EverModified ? item.Id : $"stack:{item.Rank}:{item.Floor.TypeId}";
    private Item? Find(string groupId) => _items.FirstOrDefault(i => GroupId(i) == groupId);
    private static bool ValidSlot(TowerAssembly assembly, int slot) => slot >= 0 && slot < assembly.Spec.Floors.Count;

    private Item InstalledItem(TowerAssembly assembly, int slot)
    {
        if (_installed.TryGetValue((assembly, slot), out var item)) return item;
        var floor = assembly.Spec.Floors[slot];
        item = new Item(NewId(), assembly.Rank, floor) { EverModified = floor.Mods.Count > 0 };
        _installed.Add((assembly, slot), item);
        return item;
    }

    private void ReturnPrior(TowerAssembly assembly, int slot)
    {
        var floor = assembly.Spec.Floors[slot];
        if (floor.TypeId == null) return;
        var prior = InstalledItem(assembly, slot);
        prior.Floor = floor;
        prior.EverModified |= floor.Mods.Count > 0;
        _installed.Remove((assembly, slot));
        _items.Add(prior);
    }
}


