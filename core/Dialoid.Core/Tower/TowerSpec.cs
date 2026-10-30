using System.Text.Json;
using Dialoid.Core.Data;
using Dialoid.Core.Data.Json;

namespace Dialoid.Core.Tower;

/// <summary>A rolled mod: id, tier and the rolled number (0 for numberless mods).</summary>
public sealed record ModInstance(string ModId, int Tier, double Value);

/// <summary>One floor slot. All of <see cref="TypeId"/>, <see cref="Variant"/>, <see cref="BiomeId"/> null and no mods = empty slot.</summary>
public sealed record FloorSpec(string? TypeId, string? Variant, string? BiomeId, IReadOnlyList<ModInstance> Mods);

/// <summary>
/// Portable tower preset: pure data, no character level and no shuffle bonus. <see cref="UberBossId"/> is a free string
/// (there is no boss pool in the data yet). Floors are indexed from 0.
/// </summary>
public sealed record TowerSpec(int Rank, IReadOnlyList<FloorSpec> Floors, IReadOnlyList<ModInstance> TowerMods, string? UberBossId);

/// <summary>
/// JSON of a <see cref="TowerSpec"/> (snake_case). Reading is strict: unknown fields, unknown ids of types, variants, biomes and mods,
/// a mod in the wrong place, a tier outside 1..5 or a wrong floor count are errors (a Russian sentence).
/// Rank permissions (which kinds and tiers a rank allows) are not checked here: that is the entry check of a run.
/// </summary>
public static class TowerCodec
{
    private sealed class ModJson
    {
        public static readonly HashSet<string> Allowed = StrictJson.Fields("mod_id", "tier", "value");
        public string? ModId { get; set; }
        public int? Tier { get; set; }
        public double? Value { get; set; }
    }

    private sealed class FloorJson
    {
        public static readonly HashSet<string> Allowed = StrictJson.Fields("type_id", "variant", "biome_id", "mods");
        public string? TypeId { get; set; }
        public string? Variant { get; set; }
        public string? BiomeId { get; set; }
        public List<ModJson?>? Mods { get; set; }
    }

    private sealed class SpecJson
    {
        public static readonly HashSet<string> Allowed = StrictJson.Fields("rank", "floors", "tower_mods", "uber_boss_id");
        public int? Rank { get; set; }
        public List<FloorJson?>? Floors { get; set; }
        public List<ModJson?>? TowerMods { get; set; }
        public string? UberBossId { get; set; }
    }

    private const string Source = "tower";

    public static string ToJson(TowerSpec spec)
    {
        static List<ModJson?> Mods(IEnumerable<ModInstance> mods) =>
            mods.Select(m => (ModJson?)new ModJson { ModId = m.ModId, Tier = m.Tier, Value = m.Value }).ToList();
        var j = new SpecJson
        {
            Rank = spec.Rank,
            Floors = spec.Floors.Select(f => (FloorJson?)new FloorJson { TypeId = f.TypeId, Variant = f.Variant, BiomeId = f.BiomeId, Mods = Mods(f.Mods) }).ToList(),
            TowerMods = Mods(spec.TowerMods),
            UberBossId = spec.UberBossId,
        };
        return JsonSerializer.Serialize(j, DataLoader.Options);
    }

    public static bool TryFromJson(string json, TowerData d, out TowerSpec? spec, out string? error)
    {
        spec = null;
        error = Parse(json, d, out var result);
        if (error != null) return false;
        spec = result;
        return true;
    }

    private static string? Parse(string json, TowerData d, out TowerSpec? spec)
    {
        spec = null;
        SpecJson? j;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "Описание башни должно быть объектом";
            var unknown = new List<DataError>();
            StrictJson.RejectUnknown(root, SpecJson.Allowed, Source, null, unknown);
            StrictJson.RejectUnknownIn(root, "tower_mods", ModJson.Allowed, Source, null, unknown);
            StrictJson.RejectUnknownIn(root, "floors", FloorJson.Allowed, Source, null, unknown);
            if (root.TryGetProperty("floors", out var floorsEl) && floorsEl.ValueKind == JsonValueKind.Array)
                foreach (var f in floorsEl.EnumerateArray()) StrictJson.RejectUnknownIn(f, "mods", ModJson.Allowed, Source, null, unknown);
            if (unknown.Count > 0) return $"В описании башни: {unknown[0].Message}";
            j = root.Deserialize<SpecJson>(DataLoader.Options);
        }
        catch (JsonException ex) { return $"Некорректный JSON описания башни: {ex.Message}"; }
        if (j == null) return "Описание башни пусто";

        var r = d.Rules;
        if (j.Rank is not int rank) return "Не задан ранг башни";
        if (rank < 1 || rank > r.Ranks.Count) return $"Ранг башни должен быть от 1 до {r.Ranks.Count}, а не {rank}";
        if (j.Floors == null) return "Не заданы этажи башни";
        if (j.Floors.Count != r.Floors) return $"В башне должно быть {r.Floors} этажей, а не {j.Floors.Count}";

        var floors = new List<FloorSpec>();
        for (int i = 0; i < j.Floors.Count; i++)
        {
            string? err = ParseFloor(d, j.Floors[i], i + 1, out var floor);
            if (err != null) return err;
            floors.Add(floor!);
        }

        var towerMods = new List<ModInstance>();
        var tm = j.TowerMods ?? new();
        if (tm.Count > r.TowerSlots) return $"У башни не больше {r.TowerSlots} свойств, а задано {tm.Count}";
        foreach (var m in tm)
        {
            string? err = ParseMod(d, m, "башни", out var mod, out var def);
            if (err != null) return err;
            if (def!.Kind != ModKind.Tower) return $"Свойство «{def.Id}» не относится ко всей башне";
            towerMods.Add(mod!);
        }

        if (j.UberBossId != null && string.IsNullOrWhiteSpace(j.UberBossId)) return "Пустой id убер-босса";
        spec = new TowerSpec(rank, floors.AsReadOnly(), towerMods.AsReadOnly(), j.UberBossId);
        return null;
    }

    private static string? ParseFloor(TowerData d, FloorJson? f, int number, out FloorSpec? floor)
    {
        floor = null;
        if (f == null) return $"Этаж {number}: пустая запись";
        var modsJson = f.Mods ?? new();
        if (f.TypeId == null)
        {
            if (f.Variant != null || f.BiomeId != null || modsJson.Count > 0)
                return $"Этаж {number}: у пустого этажа не может быть разновидности, биома или свойств";
            floor = new FloorSpec(null, null, null, Array.Empty<ModInstance>());
            return null;
        }
        if (!d.TryGetFloorType(f.TypeId, out var type)) return $"Этаж {number}: неизвестный тип этажа «{f.TypeId}»";
        if (type.Variants.Count == 0 && f.Variant != null) return $"Этаж {number}: у типа «{type.Id}» нет разновидностей, а задана «{f.Variant}»";
        if (type.Variants.Count > 0 && (f.Variant == null || !type.Variants.Contains(f.Variant, StringComparer.Ordinal)))
            return $"Этаж {number}: у типа «{type.Id}» нет разновидности «{f.Variant}»";
        if (f.BiomeId == null) return $"Этаж {number}: не задан биом";
        if (!d.HasBiome(f.BiomeId)) return $"Этаж {number}: неизвестный биом «{f.BiomeId}»";
        if (modsJson.Count > d.Rules.FloorSlots) return $"Этаж {number}: не больше {d.Rules.FloorSlots} свойств, а задано {modsJson.Count}";

        var mods = new List<ModInstance>();
        foreach (var m in modsJson)
        {
            string? err = ParseMod(d, m, $"этажа {number}", out var mod, out var def);
            if (err != null) return err;
            if (def!.Kind == ModKind.Tower) return $"Этаж {number}: свойство «{def.Id}» ставится только на всю башню";
            if (def.FloorTypes.Count > 0 && !def.FloorTypes.Contains(type.Id, StringComparer.Ordinal))
                return $"Этаж {number}: свойство «{def.Id}» не подходит к типу «{type.Id}»";
            if (def.Variant != null && def.Variant != f.Variant)
                return $"Этаж {number}: свойство «{def.Id}» не подходит к разновидности «{f.Variant}»";
            mods.Add(mod!);
        }
        floor = new FloorSpec(type.Id, f.Variant, f.BiomeId, mods.AsReadOnly());
        return null;
    }

    private static string? ParseMod(TowerData d, ModJson? m, string where, out ModInstance? mod, out FloorModDef? def)
    {
        mod = null;
        def = null;
        if (m == null) return $"Свойства {where}: пустая запись";
        if (m.ModId == null) return $"Свойства {where}: не задан id свойства";
        if (!d.TryGetMod(m.ModId, out def)) return $"Свойства {where}: неизвестное свойство «{m.ModId}»";
        if (m.Tier is not int tier) return $"Свойство «{def.Id}»: не задан тир";
        if (tier < 1 || tier > TowerDataLoader.TierCount || def.Tiers.All(t => t.Tier != tier))
            return $"Свойство «{def.Id}»: тир должен быть от 1 до {TowerDataLoader.TierCount}, а не {tier}";
        if (m.Value is not double value) return $"Свойство «{def.Id}»: не задано число";
        if (!double.IsFinite(value) || value < 0) return $"Свойство «{def.Id}»: число должно быть неотрицательным, а не {value}";
        mod = new ModInstance(def.Id, tier, value);
        return null;
    }
}
