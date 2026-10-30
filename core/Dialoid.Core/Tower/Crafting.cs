using Dialoid.Core.Simulation;

namespace Dialoid.Core.Tower;

/// <summary>
/// Applying tower currency to a floor or to the whole tower (spec 9.4–9.5). Checks run in a fixed order and the first failing one
/// is the answer; on any refusal nothing changes (neither the tower, nor the wallet, nor the assembly's random stream).
/// The currency is spent (one unit, at the tower's rank) only when the result is <see cref="TowerRefusal.None"/>.
/// All randomness comes from <see cref="TowerAssembly.Rng"/>; pools follow the file order of <see cref="TowerData.Mods"/>.
/// </summary>
public static class Crafting
{
    private const string BiasChannel = "tower.bias";

    /// <param name="floorSlot">Floor index from 0; null = the whole tower.</param>
    /// <param name="targetModId">Only for <see cref="CurrencyOp.RemoveOne"/>: the mod to remove.</param>
    public static TowerRefusal Apply(TowerData data, TowerAssembly tower, Wallet wallet, string currencyId, int? floorSlot, string? targetModId = null)
    {
        if (!data.TryGetCurrency(currencyId, out var cur)) return TowerRefusal.UnknownCurrency;
        if (tower.Rank < cur.DropsFromRank) return TowerRefusal.CurrencyLocked;

        var spec = tower.Spec;
        FloorSpec? floor = null;
        if (floorSlot is int slot)
        {
            if (slot < 0 || slot >= spec.Floors.Count) return TowerRefusal.SlotOutOfRange;
            floor = spec.Floors[slot];
            if (floor.TypeId == null) return TowerRefusal.EmptySlot;
        }

        var mods = floor?.Mods ?? spec.TowerMods;
        var rng = tower.Rng;
        int maxTier = RankRules.Def(data.Rules, tower.Rank).MaxTier;
        bool CanPay() => wallet.CountUsable(currencyId, tower.Rank) >= 1;

        switch (cur.Op)
        {
            case CurrencyOp.AddMod:
            case CurrencyOp.AddModBiased:
            {
                var kind = cur.ModKind ?? throw new InvalidOperationException($"У валюты «{cur.Id}» не задан вид свойства");
                if ((kind == ModKind.Tower) != (floor == null)) return TowerRefusal.WrongTarget;
                if (!RankRules.CanApply(data.Rules, tower.Rank, kind, 1)) return TowerRefusal.KindNotAllowed;
                int slots = floor == null ? data.Rules.TowerSlots : data.Rules.FloorSlots;
                if (mods.Count >= slots) return TowerRefusal.SlotFull;
                var pool = Pool(data, kind, floor, mods, tower.Rank);
                if (pool.Count == 0) return TowerRefusal.PoolEmpty;
                if (!CanPay()) return TowerRefusal.NotEnoughCurrency;

                if (cur.Op == CurrencyOp.AddModBiased && rng.Chance(BiasChannel, cur.BiasChance))
                {
                    var biased = pool.Where(m => m.Directions.Contains(cur.BiasDirection!, StringComparer.Ordinal)).ToList();
                    if (biased.Count > 0) pool = biased;
                }
                var added = Roll(rng, pool, maxTier);
                return Commit(tower, wallet, cur, floorSlot, mods.Append(added).ToArray());
            }

            case CurrencyOp.RerollOne:
            {
                if (mods.Count == 0) return TowerRefusal.NoMods;
                if (!CanPay()) return TowerRefusal.NotEnoughCurrency;
                int i = TowerRandom.Pick(rng, mods.Count);
                var removed = mods[i];
                var others = mods.Where((_, j) => j != i).ToList();
                var pool = Pool(data, data.Mod(removed.ModId).Kind, floor, others, tower.Rank);
                if (pool.Any(m => m.Id != removed.ModId)) pool = pool.Where(m => m.Id != removed.ModId).ToList();
                if (pool.Count == 0) return TowerRefusal.PoolEmpty; // unreachable for valid data: the removed mod still fits
                var result = mods.ToArray();
                result[i] = Roll(rng, pool, maxTier);
                return Commit(tower, wallet, cur, floorSlot, result);
            }

            case CurrencyOp.RerollValueHigh:
            {
                if (mods.Count == 0) return TowerRefusal.NoMods;
                if (!CanPay()) return TowerRefusal.NotEnoughCurrency;
                int i = TowerRandom.Pick(rng, mods.Count);
                var t = TierOf(data, mods[i].ModId, mods[i].Tier);
                var result = mods.ToArray();
                result[i] = mods[i] with { Value = rng.InRange((t.Min + t.Max) / 2, t.Max) };
                return Commit(tower, wallet, cur, floorSlot, result);
            }

            case CurrencyOp.UpgradeTier:
            {
                if (mods.Count == 0) return TowerRefusal.NoMods;
                var candidates = Enumerable.Range(0, mods.Count).Where(j => mods[j].Tier < maxTier).ToList();
                if (candidates.Count == 0) return TowerRefusal.NothingToUpgrade;
                if (!CanPay()) return TowerRefusal.NotEnoughCurrency;
                int i = candidates[TowerRandom.Pick(rng, candidates.Count)];
                int tier = mods[i].Tier + 1;
                var t = TierOf(data, mods[i].ModId, tier);
                var result = mods.ToArray();
                result[i] = mods[i] with { Tier = tier, Value = rng.InRange(t.Min, t.Max) };
                return Commit(tower, wallet, cur, floorSlot, result);
            }

            case CurrencyOp.Clear:
                if (mods.Count == 0) return TowerRefusal.NoMods;
                if (!CanPay()) return TowerRefusal.NotEnoughCurrency;
                return Commit(tower, wallet, cur, floorSlot, Array.Empty<ModInstance>());

            case CurrencyOp.RemoveOne:
            {
                int i = targetModId == null ? -1 : mods.ToList().FindIndex(m => m.ModId == targetModId);
                if (i < 0) return TowerRefusal.ModNotFound;
                if (!CanPay()) return TowerRefusal.NotEnoughCurrency;
                return Commit(tower, wallet, cur, floorSlot, mods.Where((_, j) => j != i).ToArray());
            }

            case CurrencyOp.RerollVariant:
            {
                if (floor == null) return TowerRefusal.WrongTarget;
                var type = data.FloorType(floor.TypeId!);
                if (type.Variants.Count == 0) return TowerRefusal.NotATrial;
                if (!CanPay()) return TowerRefusal.NotEnoughCurrency;
                var choices = type.Variants.Where(v => v != floor.Variant).ToList();
                if (choices.Count == 0) choices = type.Variants.ToList();
                var variant = choices[TowerRandom.Pick(rng, choices.Count)];
                var kept = mods.Where(m => data.Mod(m.ModId).Kind != ModKind.FloorType).ToArray();
                if (!wallet.TrySpend(cur.Id, tower.Rank, 1)) return TowerRefusal.NotEnoughCurrency;
                tower.SetVariant(floorSlot!.Value, variant);
                tower.SetFloorMods(floorSlot.Value, kept);
                return TowerRefusal.None;
            }

            default:
                // Shuffle is not applied to a target: it goes through TowerAssembly.Shuffle.
                return TowerRefusal.WrongTarget;
        }
    }

    /// <summary>
    /// Mods of <paramref name="kind"/> that fit the target: for a floor, its type (and variant); for the tower, tower mods.
    /// Mods whose group is already taken by <paramref name="present"/>, or that are already present, are left out; hard counters only from
    /// <see cref="TowerRules.HardCounterFromRank"/>. File order.
    /// </summary>
    private static List<FloorModDef> Pool(TowerData data, ModKind kind, FloorSpec? floor, IReadOnlyList<ModInstance> present, int rank)
    {
        var defs = present.Select(m => data.Mod(m.ModId)).ToList();
        var groups = defs.Where(d => d.Group != null).Select(d => d.Group!).ToHashSet(StringComparer.Ordinal);
        var ids = defs.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        return data.Mods.Where(m =>
            m.Kind == kind
            && !ids.Contains(m.Id)
            && (m.HardCounter == HardCounter.None || RankRules.AllowsHardCounter(data.Rules, rank))
            && (m.Group == null || !groups.Contains(m.Group))
            && (floor == null || FitsFloor(m, floor))).ToList();
    }

    private static bool FitsFloor(FloorModDef m, FloorSpec floor) =>
        (m.FloorTypes.Count == 0 || m.FloorTypes.Contains(floor.TypeId!, StringComparer.Ordinal))
        && (m.Variant == null || m.Variant == floor.Variant);

    private static ModInstance Roll(IRandomSource rng, IReadOnlyList<FloorModDef> pool, int maxTier)
    {
        var def = pool[TowerRandom.Pick(rng, pool.Count)];
        int tier = TowerRandom.Pick(rng, maxTier) + 1;
        var t = def.Tiers.First(x => x.Tier == tier);
        return new ModInstance(def.Id, tier, rng.InRange(t.Min, t.Max));
    }

    private static ModTier TierOf(TowerData data, string modId, int tier) => data.Mod(modId).Tiers.First(x => x.Tier == tier);

    private static TowerRefusal Commit(TowerAssembly tower, Wallet wallet, CurrencyDef cur, int? floorSlot, IReadOnlyList<ModInstance> mods)
    {
        if (!wallet.TrySpend(cur.Id, tower.Rank, 1)) return TowerRefusal.NotEnoughCurrency;
        if (floorSlot is int slot) tower.SetFloorMods(slot, mods);
        else tower.SetTowerMods(mods);
        return TowerRefusal.None;
    }
}
