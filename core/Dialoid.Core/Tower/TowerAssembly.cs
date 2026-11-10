using Dialoid.Core.Simulation;

namespace Dialoid.Core.Tower;

/// <summary>
/// A tower being assembled before entry (spec 7): floor types, variants, biomes, mods, uber boss and the shuffle bonus.
/// <see cref="Rng"/> is the only randomness for assembling and for crafting on this tower. Floor slots are indexed from 0.
/// Each slot remembers the variant rolled for a type with variants, so switching away and back keeps it;
/// a shuffle overwrites that memory. Any real manual change of a floor type drops the shuffle bonus for this assembly.
/// </summary>
public sealed class TowerAssembly
{
    private const string ShuffleCurrency = "shuffle";

    private sealed class Slot
    {
        public string? TypeId;
        public string? Variant;
        public string? BiomeId;
        public IReadOnlyList<ModInstance> Mods = Array.Empty<ModInstance>();
        /// <summary>Type id → variant rolled for it in this slot.</summary>
        public readonly Dictionary<string, string> VariantMemory = new(StringComparer.Ordinal);
    }

    private readonly TowerData _data;
    private readonly Slot[] _slots;
    private IReadOnlyList<ModInstance> _towerMods = Array.Empty<ModInstance>();
    private string? _uberBossId;

    public TowerAssembly(TowerData data, int rank, IRandomSource rng)
    {
        RankRules.Def(data.Rules, rank);
        _data = data;
        Rank = rank;
        Rng = rng;
        _slots = Enumerable.Range(0, data.Rules.Floors).Select(_ => new Slot()).ToArray();
    }

    /// <summary>
    /// Rebuilds an assembly from a full spec, rolled mods included. Internal on purpose: a free copy of rolled mods would let a player
    /// skip spending currency. For tests and for restoring the engine's own state. Variants of the spec seed the slot memory.
    /// </summary>
    internal static TowerAssembly FromSpec(TowerData d, TowerSpec spec, IRandomSource rng)
    {
        if (spec.Floors.Count != d.Rules.Floors)
            throw new ArgumentException($"В описании башни должно быть {d.Rules.Floors} этажей, а не {spec.Floors.Count}", nameof(spec));
        var a = new TowerAssembly(d, spec.Rank, rng);
        for (int i = 0; i < spec.Floors.Count; i++)
        {
            var f = spec.Floors[i];
            var s = a._slots[i];
            s.TypeId = f.TypeId;
            s.Variant = f.Variant;
            s.BiomeId = f.BiomeId;
            s.Mods = f.Mods.ToArray();
            if (f.TypeId != null && f.Variant != null) s.VariantMemory[f.TypeId] = f.Variant;
        }
        a._towerMods = spec.TowerMods.ToArray();
        a._uberBossId = spec.UberBossId;
        return a;
    }

    /// <summary>
    /// "Repeat the last tower": copies the composition of a spec (rank, floor types, variants, biomes, uber boss) and drops every mod,
    /// floor and tower alike; the player re-applies currency. Variants seed the slot memory. The shuffle bonus is not carried over.
    /// </summary>
    public static TowerAssembly FromComposition(TowerData d, TowerSpec spec, IRandomSource rng) =>
        FromSpec(d, spec with
        {
            Floors = spec.Floors.Select(f => f with { Mods = Array.Empty<ModInstance>() }).ToArray(),
            TowerMods = Array.Empty<ModInstance>(),
        }, rng);

    internal IRandomSource Rng { get; }
    internal int Rank { get; }

    /// <summary>An immutable snapshot of the current assembly.</summary>
    public TowerSpec Spec => new(Rank,
        Array.AsReadOnly(_slots.Select(s => new FloorSpec(s.TypeId, s.Variant, s.BiomeId, Array.AsReadOnly(s.Mods.ToArray()))).ToArray()),
        Array.AsReadOnly(_towerMods.ToArray()), _uberBossId);

    public bool ShuffleBonusActive { get; private set; }
    public int ShufflesDone { get; private set; }

    /// <summary><see cref="TowerRules.ShuffleCosts"/>[ShufflesDone]; past the end of the list each next shuffle costs double the previous.</summary>
    public int NextShuffleCost
    {
        get
        {
            var costs = _data.Rules.ShuffleCosts;
            if (ShufflesDone < costs.Count) return costs[ShufflesDone];
            long cost = costs[^1];
            for (int i = costs.Count - 1; i < ShufflesDone && cost < int.MaxValue; i++) cost *= 2;
            return (int)Math.Min(cost, int.MaxValue);
        }
    }

    /// <summary>
    /// Sets the floor type. Same type → nothing changes. An empty biome is rolled; a variant comes from the slot memory or is rolled
    /// and remembered. Type mods and general mods incompatible with the new type are removed; the shuffle bonus is lost.
    /// </summary>
    public TowerRefusal SetFloorType(int slot, string typeId)
    {
        if (slot < 0 || slot >= _slots.Length) return TowerRefusal.SlotOutOfRange;
        if (!_data.TryGetFloorType(typeId, out var type)) return TowerRefusal.UnknownType;
        var s = _slots[slot];
        if (s.TypeId == type.Id) return TowerRefusal.None;

        s.TypeId = type.Id;
        if (type.Variants.Count == 0) s.Variant = null;
        else if (s.VariantMemory.TryGetValue(type.Id, out var remembered)) s.Variant = remembered;
        else s.VariantMemory[type.Id] = s.Variant = RollVariant(type);
        s.BiomeId ??= RollBiome();
        s.Mods = s.Mods.Where(m => KeepsOnTypeChange(m, type.Id)).ToArray();
        ShuffleBonusActive = false;
        return TowerRefusal.None;
    }

    /// <summary>Sets the biome; does not touch the shuffle bonus. Unknown biome or slot → <see cref="ArgumentException"/>.</summary>
    public void SetBiome(int slot, string biomeId)
    {
        CheckSlot(slot);
        if (!_data.HasBiome(biomeId)) throw new ArgumentException($"Неизвестный биом «{biomeId}»", nameof(biomeId));
        _slots[slot].BiomeId = biomeId;
    }

    public void SetUberBoss(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Пустой id убер-босса", nameof(id));
        _uberBossId = id;
    }

    /// <summary>
    /// Pays <see cref="NextShuffleCost"/> of "shuffle" (nothing when it is 0), then fills every slot with a random type (never the same
    /// type twice in a row), rolls biomes and variants anew (overwriting slot memory) and removes floor mods; tower mods stay.
    /// </summary>
    public TowerRefusal Shuffle(Wallet wallet)
    {
        int cost = NextShuffleCost;
        if (cost > 0 && !wallet.TrySpend(ShuffleCurrency, Rank, cost)) return TowerRefusal.NotEnoughCurrency;

        var types = _data.FloorTypes;
        string? previous = null;
        foreach (var s in _slots)
        {
            var choices = types.Count > 1 ? types.Where(t => t.Id != previous).ToList() : types;
            var type = choices[TowerRandom.Pick(Rng, choices.Count)];
            s.TypeId = previous = type.Id;
            s.VariantMemory.Clear();
            s.Variant = type.Variants.Count == 0 ? null : s.VariantMemory[type.Id] = RollVariant(type);
            s.BiomeId = RollBiome();
            s.Mods = Array.Empty<ModInstance>();
        }
        ShuffleBonusActive = true;
        ShufflesDone++;
        return TowerRefusal.None;
    }

    /// <summary>Marks a successful paid inventory fill after all owned floors have been transferred.</summary>
    internal void RecordInventoryShuffle()
    {
        ShuffleBonusActive = true;
        ShufflesDone++;
    }

    // ---- mutators for Crafting: they never touch the shuffle bonus ----

    /// <summary>Moves an inventory-owned floor without rerolling its contents. Not a public spec import.</summary>
    internal void ReplaceInventoryFloor(int slot, FloorSpec floor)
    {
        CheckSlot(slot);
        var target = new Slot
        {
            TypeId = floor.TypeId, Variant = floor.Variant, BiomeId = floor.BiomeId,
            Mods = floor.Mods.ToArray(),
        };
        if (floor.TypeId != null && floor.Variant != null)
            target.VariantMemory[floor.TypeId] = floor.Variant;
        _slots[slot] = target;
        ShuffleBonusActive = false;
    }

    internal void SetFloorMods(int slot, IReadOnlyList<ModInstance> mods)
    {
        CheckSlot(slot);
        _slots[slot].Mods = mods.ToArray();
    }

    internal void SetTowerMods(IReadOnlyList<ModInstance> mods) => _towerMods = mods.ToArray();

    /// <summary>Sets the variant of the slot's type and remembers it. Mods are left to the caller.</summary>
    internal void SetVariant(int slot, string variant)
    {
        CheckSlot(slot);
        var s = _slots[slot];
        if (s.TypeId == null || !_data.FloorType(s.TypeId).Variants.Contains(variant, StringComparer.Ordinal))
            throw new ArgumentException($"У этажа {slot + 1} нет разновидности «{variant}»", nameof(variant));
        s.VariantMemory[s.TypeId] = s.Variant = variant;
    }

    private bool KeepsOnTypeChange(ModInstance m, string typeId)
    {
        var def = _data.Mod(m.ModId);
        return def.Kind != ModKind.FloorType && (def.FloorTypes.Count == 0 || def.FloorTypes.Contains(typeId, StringComparer.Ordinal));
    }

    private string RollVariant(FloorTypeDef type) => type.Variants[TowerRandom.Pick(Rng, type.Variants.Count)];

    private string RollBiome() => _data.Biomes[TowerRandom.Pick(Rng, _data.Biomes.Count)].Id;

    private void CheckSlot(int slot)
    {
        if (slot < 0 || slot >= _slots.Length)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"Этаж должен быть от 0 до {_slots.Length - 1}");
    }
}
