using System.Text.Json;
using Dialoid.Core.Data;
using Dialoid.Core.Data.Json;

namespace Dialoid.Core.Tower.Json;

internal sealed class RankJson
{
    public static readonly HashSet<string> Allowed = StrictJson.Fields("rank", "level_min", "level_max", "mod_kinds", "max_tier");

    public int? Rank { get; set; }
    public int? LevelMin { get; set; }
    public int? LevelMax { get; set; }
    public List<ModKind>? ModKinds { get; set; }
    public int? MaxTier { get; set; }
}

/// <summary>data/tower/rules.json — a single object (DataLoader.ReadCatalog reads arrays only).</summary>
internal sealed class RulesJson
{
    private static readonly HashSet<string> Allowed = StrictJson.Fields("floors", "floor_slots", "tower_slots", "height_multipliers",
        "summit_bonus", "diminishing_c", "diversity_bonus_per_type", "diversity_bonus_types_cap", "shuffle_bonus", "no_death_bonus",
        "quality_cap", "shuffle_costs", "exchange_ratio", "exchange_min_level", "xp_penalty_per_level", "xp_penalty_cap", "ranks");

    public int? Floors { get; set; }
    public int? FloorSlots { get; set; }
    public int? TowerSlots { get; set; }
    public List<double>? HeightMultipliers { get; set; }
    public double? SummitBonus { get; set; }
    public double? DiminishingC { get; set; }
    public double? DiversityBonusPerType { get; set; }
    public int? DiversityBonusTypesCap { get; set; }
    public double? ShuffleBonus { get; set; }
    public double? NoDeathBonus { get; set; }
    public double? QualityCap { get; set; }
    public List<int>? ShuffleCosts { get; set; }
    public int? ExchangeRatio { get; set; }
    public int? ExchangeMinLevel { get; set; }
    public double? XpPenaltyPerLevel { get; set; }
    public double? XpPenaltyCap { get; set; }
    public List<RankJson>? Ranks { get; set; }

    /// <summary>Reads the file; unknown fields of the root and of each rank are reported. Returns null after reporting a problem.</summary>
    public static RulesJson? Read(string path, string file, List<DataError> errors)
    {
        if (!File.Exists(path)) { errors.Add(new DataError(file, null, "Файл не найден")); return null; }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new DataError(file, null, "Корень файла должен быть объектом"));
                return null;
            }
            StrictJson.RejectUnknown(doc.RootElement, Allowed, file, null, errors);
            StrictJson.RejectUnknownIn(doc.RootElement, "ranks", RankJson.Allowed, file, null, errors);
            var rules = doc.RootElement.Deserialize<RulesJson>(DataLoader.Options);
            if (rules == null) errors.Add(new DataError(file, null, "Файл пуст"));
            return rules;
        }
        catch (JsonException ex) { errors.Add(new DataError(file, null, $"Некорректный JSON: {ex.Message}")); return null; }
        catch (IOException ex) { errors.Add(new DataError(file, null, $"Не удалось прочитать файл: {ex.Message}")); return null; }
    }
}
