using Dialoid.Core.Data;
using Dialoid.Core.Tower.Json;

namespace Dialoid.Core.Tower;

/// <summary>Loads and validates data/tower/*.json. Any error → no data; every error is a Russian sentence with the file and id.</summary>
public static class TowerDataLoader
{
    public const int RankCount = 15;
    public const int TierCount = 5;
    /// <summary>Character level cap (core spec 5.3); ranks 6–15 play at it with zone level 60 + (rank − 5).</summary>
    public const int LevelCap = Dialoid.Core.Character.BuildRules.MaxLevel;
    /// <summary>Last levelling rank (spec 6.3).</summary>
    public const int LastLevellingRank = 5;
    /// <summary>Floor type whose arena hosts the summit (tower-mod compatibility with the uber boss).</summary>
    public const string BossesType = "bosses";

    private static readonly CurrencyOp[] LevellessOps = { CurrencyOp.Clear, CurrencyOp.RemoveOne, CurrencyOp.RerollOne, CurrencyOp.RerollVariant, CurrencyOp.Shuffle };

    public static (TowerData? Data, IReadOnlyList<DataError> Errors) Load(string towerDirectory)
    {
        var errors = new List<DataError>();
        var rules = LoadRules(towerDirectory, errors);
        var types = LoadFloorTypes(towerDirectory, errors);
        var biomes = LoadBiomes(towerDirectory, errors);
        var mods = LoadMods(towerDirectory, types, errors);
        var currencies = LoadCurrencies(towerDirectory, mods, errors);
        if (errors.Count > 0 || rules == null) return (null, errors);
        return (new TowerData(rules, types, biomes, mods, currencies), errors);
    }

    private static TowerRules? LoadRules(string dir, List<DataError> errors)
    {
        const string F = "rules.json";
        var j = RulesJson.Read(Path.Combine(dir, F), F, errors);
        if (j == null) return null;
        int before = errors.Count;
        void Err(string m) => errors.Add(new DataError(F, null, m));
        T Req<T>(T? v, string name) where T : struct
        {
            if (v is null) Err($"Не задано «{name}»");
            return v ?? default;
        }

        int floors = Req(j.Floors, "floors");
        int floorSlots = Req(j.FloorSlots, "floor_slots");
        int towerSlots = Req(j.TowerSlots, "tower_slots");
        double summit = Req(j.SummitBonus, "summit_bonus");
        double c = Req(j.DiminishingC, "diminishing_c");
        double divPer = Req(j.DiversityBonusPerType, "diversity_bonus_per_type");
        int divCap = Req(j.DiversityBonusTypesCap, "diversity_bonus_types_cap");
        double shuffleBonus = Req(j.ShuffleBonus, "shuffle_bonus");
        double noDeath = Req(j.NoDeathBonus, "no_death_bonus");
        double qualityCap = Req(j.QualityCap, "quality_cap");
        int ratio = Req(j.ExchangeRatio, "exchange_ratio");
        int minLevel = Req(j.ExchangeMinLevel, "exchange_min_level");
        double xpPer = Req(j.XpPenaltyPerLevel, "xp_penalty_per_level");
        double xpCap = Req(j.XpPenaltyCap, "xp_penalty_cap");
        if (errors.Count > before) return null;

        if (floors < 1) Err("«floors» должно быть положительным");
        if (floorSlots < 1) Err("«floor_slots» должно быть положительным");
        if (towerSlots < 1) Err("«tower_slots» должно быть положительным");
        var heights = j.HeightMultipliers ?? new();
        if (heights.Count != floors) Err($"В «height_multipliers» должно быть {floors} чисел (по числу этажей), а не {heights.Count}");
        if (heights.Any(h => !(h > 0))) Err("Все «height_multipliers» должны быть положительными");
        if (!(summit > 0)) Err("«summit_bonus» должен быть положительным");
        if (!(c > 0)) Err("«diminishing_c» должен быть положительным");
        if (divPer < 0) Err("«diversity_bonus_per_type» не может быть отрицательным");
        if (divCap < 0) Err("«diversity_bonus_types_cap» не может быть отрицательным");
        if (shuffleBonus < 0) Err("«shuffle_bonus» не может быть отрицательным");
        if (noDeath < 0) Err("«no_death_bonus» не может быть отрицательным");
        if (!(qualityCap > 0)) Err("«quality_cap» должен быть положительным");
        var shuffleCosts = j.ShuffleCosts ?? new();
        if (shuffleCosts.Count == 0) Err("Не задан список «shuffle_costs»");
        if (shuffleCosts.Any(x => x < 0)) Err("Цены в «shuffle_costs» не могут быть отрицательными");
        else if (shuffleCosts.Count > 0 && shuffleCosts[0] != 0) Err($"Первое перемешивание бесплатно: «shuffle_costs» должен начинаться с 0, а не с {shuffleCosts[0]}");
        if (ratio < 2) Err("«exchange_ratio» должен быть не меньше 2: обмен вверх всегда с потерей");
        if (minLevel < 2) Err("«exchange_min_level» должен быть не меньше 2: валюта уровня 1 не обменивается");
        if (xpPer < 0) Err("«xp_penalty_per_level» не может быть отрицательным");
        if (xpCap is < 0 or > 1) Err("«xp_penalty_cap» должен быть от 0 до 1");

        var ranks = LoadRanks(j.Ranks, Err);
        if (errors.Count > before) return null;
        return new TowerRules(floors, floorSlots, towerSlots, heights, summit, c, divPer, divCap, shuffleBonus, noDeath, qualityCap,
            shuffleCosts, ratio, minLevel, xpPer, xpCap, ranks);
    }

    private static List<RankDef> LoadRanks(List<RankJson>? src, Action<string> err)
    {
        var result = new List<RankDef>();
        src ??= new();
        if (src.Count != RankCount) { err($"Рангов должно быть {RankCount}, а не {src.Count}"); return result; }
        for (int i = 0; i < src.Count; i++)
        {
            var r = src[i];
            int expected = i + 1;
            if (r.Rank != expected) { err($"Ранг №{expected}: «rank» должен быть {expected} (ранги идут по порядку)"); continue; }
            if (r.LevelMin is not { } min || r.LevelMax is not { } max) { err($"Ранг {expected}: не задана полоса уровней «level_min» / «level_max»"); continue; }
            if (min > max) err($"Ранг {expected}: «level_min» ({min}) больше «level_max» ({max})");
            if (i == 0 && min != 1) err($"Ранг 1 должен начинаться с 1-го уровня, а не с {min}");
            if (i > 0 && src[i - 1].LevelMax is { } prevMax && min != prevMax)
                err($"Ранг {expected}: полоса не стыкуется с рангом {i}: «level_min» должен быть {prevMax}, а не {min}");
            if (expected > LastLevellingRank && (min != LevelCap || max != LevelCap))
                err($"Ранг {expected}: ранги 6–15 играются на капе, полоса должна быть {LevelCap}–{LevelCap}");
            if (r.MaxTier is not (>= 1 and <= TierCount)) err($"Ранг {expected}: «max_tier» должен быть от 1 до {TierCount}");
            else if (i > 0 && src[i - 1].MaxTier is { } prevTier && r.MaxTier < prevTier) err($"Ранг {expected}: «max_tier» не может быть ниже, чем у ранга {i}");
            var kinds = new HashSet<ModKind>(r.ModKinds ?? new());
            if (!kinds.Contains(ModKind.General)) err($"Ранг {expected}: в «mod_kinds» должны быть общие моды («general»)");
            int zone = expected > LastLevellingRank ? LevelCap + (expected - LastLevellingRank) : 0;
            result.Add(new RankDef(expected, min, max, zone, kinds, r.MaxTier ?? 0));
        }
        return result;
    }

    private static List<FloorTypeDef> LoadFloorTypes(string dir, List<DataError> errors)
    {
        const string F = "floor_types.json";
        var result = new List<FloorTypeDef>();
        foreach (var (id, t) in DataLoader.ReadCatalog<FloorTypeJson>(dir, F, x => x.Id, errors, FloorTypeJson.CheckFields))
        {
            int before = errors.Count;
            void Err(string m) => errors.Add(new DataError(F, id, m));
            if (string.IsNullOrWhiteSpace(t.Name)) Err("Не задано «name»");
            var variants = t.Variants ?? new();
            if (variants.Any(string.IsNullOrWhiteSpace)) Err("Пустой id разновидности в «variants»");
            foreach (var dup in variants.GroupBy(v => v).Where(g => g.Count() > 1)) Err($"Разновидность «{dup.Key}» указана дважды");
            if (errors.Count == before) result.Add(new FloorTypeDef(id, t.Name!, t.HasEndBoss, variants));
        }
        return result;
    }

    private static List<string> LoadBiomes(string dir, List<DataError> errors)
    {
        const string F = "biomes.json";
        var result = new List<string>();
        foreach (var (id, b) in DataLoader.ReadCatalog<BiomeJson>(dir, F, x => x.Id, errors, BiomeJson.CheckFields))
        {
            if (string.IsNullOrWhiteSpace(b.Name)) errors.Add(new DataError(F, id, "Не задано «name»"));
            else result.Add(id);
        }
        return result;
    }

    private static List<FloorModDef> LoadMods(string dir, List<FloorTypeDef> types, List<DataError> errors)
    {
        const string F = "floor_mods.json";
        var byId = types.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var result = new List<FloorModDef>();
        foreach (var (id, m) in DataLoader.ReadCatalog<FloorModJson>(dir, F, x => x.Id, errors, FloorModJson.CheckFields))
        {
            int before = errors.Count;
            void Err(string msg) => errors.Add(new DataError(F, id, msg));
            if (string.IsNullOrWhiteSpace(m.Name)) Err("Не задано «name»");
            if (m.Kind is not { } kind) { Err("Не задан «kind» (general, floor_type или tower)"); continue; }
            var floorTypes = m.FloorTypes ?? new();
            foreach (var t in floorTypes.Where(t => !byId.ContainsKey(t))) Err($"Неизвестный тип этажа «{t}» в «floor_types»");

            if (kind == ModKind.FloorType)
            {
                if (floorTypes.Count != 1) Err($"Мод под тип этажа должен быть привязан ровно к одному типу, а указано {floorTypes.Count}");
                else if (byId.TryGetValue(floorTypes[0], out var type))
                {
                    if (type.Variants.Count > 0 && m.Variant == null)
                        Err($"Мод под тип «{type.Id}» должен указать разновидность в «variant» ({string.Join(", ", type.Variants)})");
                    else if (type.Variants.Count == 0 && m.Variant != null)
                        Err($"У типа «{type.Id}» нет разновидностей, «variant» не нужен");
                    else if (m.Variant != null && !type.Variants.Contains(m.Variant))
                        Err($"Неизвестная разновидность «{m.Variant}» у типа «{type.Id}»");
                }
            }
            else if (m.Variant != null) Err("«variant» бывает только у мода под тип этажа");

            var hard = m.HardCounter ?? HardCounter.None;
            if (kind == ModKind.Tower && hard != HardCounter.None) Err("Мод башни не может быть жестким контром: он ломал бы все этажи и вершину");
            if (m.AffectsSummit && kind != ModKind.Tower) Err("«affects_summit» бывает только у мода башни");
            if (m.AffectsSummit && floorTypes.Count > 0 && !floorTypes.Contains(BossesType))
                Err($"Мод с «affects_summit» должен подходить арене вершины: «floor_types» пуст или содержит «{BossesType}»");
            if (m.Group is { } g && string.IsNullOrWhiteSpace(g)) Err("Пустая «group»: уберите поле или задайте имя группы");
            if ((m.Directions ?? new()).Any(string.IsNullOrWhiteSpace)) Err("Пустое направление в «directions»");
            if ((m.StyleTags ?? new()).Any(string.IsNullOrWhiteSpace)) Err("Пустая метка в «style_tags»");

            var tiers = LoadTiers(m.Tiers, Err);
            if (errors.Count > before) continue;
            result.Add(new FloorModDef(id, m.Name!, kind, floorTypes, m.Variant, m.Group, m.Directions ?? new(), m.StyleTags ?? new(),
                hard, m.AffectsSummit, tiers));
        }
        return result;
    }

    private static List<ModTier> LoadTiers(List<ModTierJson>? src, Action<string> err)
    {
        var result = new List<ModTier>();
        src ??= new();
        if (src.Count != TierCount || src.Select(t => t.Tier).Where((t, i) => t != i + 1).Any())
        {
            err($"У мода должно быть {TierCount} тиров с номерами 1–{TierCount} по порядку");
            return result;
        }
        foreach (var t in src)
        {
            if (t.Min is not { } min || t.Max is not { } max || t.Quality is not { } q) { err($"Тир {t.Tier}: нужны «min», «max» и «quality»"); continue; }
            if (min < 0) err($"Тир {t.Tier}: «min» не может быть отрицательным (направление задает текст мода)");
            if (min > max) err($"Тир {t.Tier}: «min» ({min}) больше «max» ({max})");
            if (!(q > 0)) err($"Тир {t.Tier}: «quality» должно быть положительным");
            result.Add(new ModTier(t.Tier!.Value, min, max, q));
        }
        return result;
    }

    private static List<CurrencyDef> LoadCurrencies(string dir, List<FloorModDef> mods, List<DataError> errors)
    {
        const string F = "currencies.json";
        var directions = mods.SelectMany(m => m.Directions).ToHashSet(StringComparer.Ordinal);
        var result = new List<CurrencyDef>();
        foreach (var (id, c) in DataLoader.ReadCatalog<CurrencyJson>(dir, F, x => x.Id, errors, CurrencyJson.CheckFields))
        {
            int before = errors.Count;
            void Err(string m) => errors.Add(new DataError(F, id, m));
            if (string.IsNullOrWhiteSpace(c.Name)) Err("Не задано «name»");
            if (c.Op is not { } op) { Err("Не задана операция «op»"); continue; }
            bool adds = op is CurrencyOp.AddMod or CurrencyOp.AddModBiased;
            if (adds && c.ModKind == null) Err("Валюте наложения нужен вид мода «mod_kind» (general, floor_type или tower)");
            if (!adds && c.ModKind != null) Err("«mod_kind» бывает только у валюты наложения");

            if (op == CurrencyOp.AddModBiased)
            {
                if (string.IsNullOrWhiteSpace(c.BiasDirection)) Err("Направленной намазке нужно направление «bias_direction»");
                else if (!directions.Contains(c.BiasDirection)) Err($"Направление «{c.BiasDirection}» не встречается ни у одного мода");
                if (!(c.BiasChance > 0 && c.BiasChance < 1)) Err($"«bias_chance» должен быть строго между 0 и 1 (а задано {c.BiasChance}): стопроцентной гарантии нет");
            }
            else if (c.BiasDirection != null || c.BiasChance != 0) Err("«bias_direction» и «bias_chance» бывают только у направленной намазки");

            if (c.HasLevel && LevellessOps.Contains(op)) Err($"У валюты с операцией «{OpName(op)}» нет уровня: она не прибавляет награды");
            if (c.DropsFromRank is not (>= 1 and <= RankCount)) Err($"«drops_from_rank» должен быть от 1 до {RankCount}");
            if (c.DropBase is not { } dropBase) Err("Не задано «drop_base»");
            else if (dropBase < 0) Err("«drop_base» не может быть отрицательным");
            if (c.DropGrowth is not { } growth) Err("Не задано «drop_growth»");
            else if (c.HasLevel && !(growth > 1)) Err($"У валюты с уровнем «drop_growth» должен быть больше 1 (а задано {growth}): выше ранг — больше валюты");
            else if (!(growth >= 1)) Err($"«drop_growth» не может быть меньше 1 (а задано {growth}): выше ранг — не меньше валюты");
            if (c.SummitMultiplier is not { } summit) Err("Не задано «summit_multiplier»");
            else if (!(summit > 0)) Err("«summit_multiplier» должен быть положительным");
            if (errors.Count > before) continue;
            result.Add(new CurrencyDef(id, c.Name!, op, c.ModKind, c.HasLevel, c.DropsFromRank!.Value, c.DropBase!.Value, c.DropGrowth!.Value,
                c.SummitMultiplier!.Value, c.BiasDirection, c.BiasChance));
        }
        CheckRequiredCurrencies(result, errors, F);
        return result;
    }

    /// <summary>Currencies the tower code relies on by id: id, operation and mod kind.</summary>
    private static readonly (string Id, CurrencyOp Op, ModKind? Kind)[] RequiredCurrencies =
    {
        ("shuffle", CurrencyOp.Shuffle, null),
        ("imbue", CurrencyOp.AddMod, ModKind.General),
        ("imbue_type", CurrencyOp.AddMod, ModKind.FloorType),
        ("imbue_tower", CurrencyOp.AddMod, ModKind.Tower),
    };

    private static void CheckRequiredCurrencies(List<CurrencyDef> loaded, List<DataError> errors, string file)
    {
        foreach (var (id, op, kind) in RequiredCurrencies)
        {
            var c = loaded.FirstOrDefault(x => x.Id == id);
            if (c == null)
            {
                // A currency that is present but broken already has its own error.
                if (!errors.Any(e => e.File == file && e.Id == id))
                    errors.Add(new DataError(file, null, $"Нет обязательной валюты «{id}»: на нее опирается код башни"));
                continue;
            }
            if (c.Op != op || c.ModKind != kind)
                errors.Add(new DataError(file, id, $"Обязательная валюта «{id}» должна иметь операцию «{OpName(op)}»" +
                    (kind is { } k ? $" и вид мода «{System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(k.ToString())}»" : "")));
        }
    }

    private static string OpName(CurrencyOp op) => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(op.ToString());
}
