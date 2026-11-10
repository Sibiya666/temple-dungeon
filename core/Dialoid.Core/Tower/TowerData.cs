namespace Dialoid.Core.Tower;

/// <summary>Mod kind by specificity (spec 9.1): general, floor-type, whole tower.</summary>
public enum ModKind { General, FloorType, Tower }

/// <summary>Hard-counter label (spec 9.3). JSON: null, no_life_regen, no_resource_regen, no_healing.</summary>
public enum HardCounter { None, NoLifeRegen, NoResourceRegen, NoHealing }

/// <summary>What a currency does when applied (spec 9.4–9.5).</summary>
public enum CurrencyOp { AddMod, AddModBiased, RerollOne, RerollValueHigh, UpgradeTier, Clear, RemoveOne, RerollVariant, Shuffle }

/// <summary>Selectable activity and any variants declared by its catalog.</summary>
public sealed record FloorTypeDef(string Id, string Name, bool HasEndBoss, IReadOnlyList<string> Variants);

/// <summary>Tier of a mod: value range (always non-negative, direction is in the mod text) and reward quality bonus.</summary>
public sealed record ModTier(int Tier, double Min, double Max, double Quality);

/// <summary>
/// Floor or tower mod. Empty <see cref="FloorTypes"/> = compatible with every type. A FloorType mod has exactly one type
/// (and a <see cref="Variant"/> if that type defines variants). <see cref="Group"/> = incompatibility group, <see cref="Directions"/> = axes for biased imbues.
/// A tower mod acts on the summit (and counts for its reward) iff FloorTypes is empty or contains "bosses"; the loader
/// requires every such mod to be in group "gate", so at most one acts on the summit (spec 5.2).
/// </summary>
public sealed record FloorModDef(string Id, string Name, ModKind Kind, IReadOnlyList<string> FloorTypes, string? Variant, string? Group,
    IReadOnlyList<string> Directions, IReadOnlyList<string> StyleTags, HardCounter HardCounter, IReadOnlyList<ModTier> Tiers)
{
    /// <summary>FloorTypes is empty or contains <paramref name="typeId"/> (variants are not checked here).</summary>
    public bool FitsType(string typeId) => FloorTypes.Count == 0 || FloorTypes.Contains(typeId, StringComparer.Ordinal);

    /// <summary>The one rule for "acts on the summit": a tower mod that fits the summit arena (<see cref="TowerDataLoader.BossesType"/>).</summary>
    public bool ActsOnSummit => Kind == ModKind.Tower && FitsType(TowerDataLoader.BossesType);
}

/// <summary>Biome: decoration only, does not change numbers.</summary>
public sealed record BiomeDef(string Id, string Name);

/// <summary>Modification currency. <see cref="ModKind"/> only for AddMod/AddModBiased; bias fields only for AddModBiased.</summary>
public sealed record CurrencyDef(string Id, string Name, CurrencyOp Op, ModKind? ModKind, bool HasLevel, int DropsFromRank, double DropBase,
    double DropGrowth, double SummitMultiplier, string? BiasDirection, double BiasChance);

/// <summary>Rank row (spec 6.1, 6.3). ZoneLevel is 0 for ranks 1–5 (computed from the character level) and 60 + (rank − 5) for 6–15.</summary>
public sealed record RankDef(int Rank, int LevelMin, int LevelMax, int ZoneLevel, IReadOnlySet<ModKind> ModKinds, int MaxTier);

/// <summary>
/// Tower-wide numbers from data/tower/rules.json. <see cref="Rank1DropMultiplier"/> (≥ 1) scales drops of levelled currencies on rank 1 only
/// (spec 8.3: rank 1 is generous; its level-1 currency cannot be exchanged up, so the bonus does not leak to rank 2; levelless currencies
/// work on any rank, so they get no bonus).
/// <see cref="HardCounterFromRank"/>: hard-counter mods (spec 9.3) enter the mod pool and are accepted on entry only from this rank,
/// the rank where the targeted-removal currency starts dropping.
/// </summary>
public sealed record TowerRules(int Floors, int FloorSlots, int TowerSlots, IReadOnlyList<double> HeightMultipliers, double SummitBonus,
    double DiminishingC, double DiversityBonusPerType, int DiversityBonusTypesCap, double ShuffleBonus, double NoDeathBonus, double QualityCap,
    IReadOnlyList<int> ShuffleCosts, int ExchangeRatio, int ExchangeMinLevel, double XpPenaltyPerLevel, double XpPenaltyCap, IReadOnlyList<RankDef> Ranks,
    double Rank1DropMultiplier, int HardCounterFromRank);

/// <summary>Immutable tower catalogs. Lists keep file order (deterministic iteration); lookups are by id.</summary>
public sealed class TowerData
{
    private readonly Dictionary<string, FloorTypeDef> _types;
    private readonly Dictionary<string, FloorModDef> _mods;
    private readonly Dictionary<string, CurrencyDef> _currencies;

    internal TowerData(TowerRules rules, IReadOnlyList<FloorTypeDef> floorTypes, IReadOnlyList<BiomeDef> biomes,
        IReadOnlyList<FloorModDef> mods, IReadOnlyList<CurrencyDef> currencies)
    {
        Rules = rules;
        FloorTypes = floorTypes;
        Biomes = biomes;
        Mods = mods;
        Currencies = currencies;
        _types = floorTypes.ToDictionary(t => t.Id, StringComparer.Ordinal);
        _mods = mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        _currencies = currencies.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    public TowerRules Rules { get; }
    public IReadOnlyList<FloorTypeDef> FloorTypes { get; }
    public IReadOnlyList<BiomeDef> Biomes { get; }
    public IReadOnlyList<FloorModDef> Mods { get; }
    public IReadOnlyList<CurrencyDef> Currencies { get; }

    public FloorTypeDef FloorType(string id) => _types.TryGetValue(id, out var t) ? t : throw new KeyNotFoundException($"Неизвестный тип этажа «{id}»");
    public FloorModDef Mod(string id) => _mods.TryGetValue(id, out var m) ? m : throw new KeyNotFoundException($"Неизвестное свойство «{id}»");
    public CurrencyDef Currency(string id) => _currencies.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"Неизвестная валюта «{id}»");

    public bool TryGetFloorType(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FloorTypeDef? def) => _types.TryGetValue(id, out def);
    public bool TryGetMod(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FloorModDef? def) => _mods.TryGetValue(id, out def);
    public bool TryGetCurrency(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CurrencyDef? def) => _currencies.TryGetValue(id, out def);
    public bool HasBiome(string id) => Biomes.Any(b => string.Equals(b.Id, id, StringComparison.Ordinal));
}
