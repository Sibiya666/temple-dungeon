using System.Text.Json;
using System.Text.Json.Nodes;
using Dialoid.Core.Data;
using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

public class TowerDataLoaderTests
{
    private static readonly string[] Levelled = { "imbue", "imbue_type", "imbue_tower", "upgrade", "biased_rare_elite", "biased_rare_reward", "biased_epic_elite", "biased_epic_reward", "bias_value" };

    private static readonly Dictionary<string, int> DropsFrom = new()
    {
        ["imbue"] = 1, ["clear"] = 1, ["shuffle"] = 1, ["reroll"] = 1, ["reroll_variant"] = 1,
        ["imbue_type"] = 3, ["upgrade"] = 3,
        ["imbue_tower"] = 4, ["biased_rare_elite"] = 4, ["biased_rare_reward"] = 4,
        ["remove"] = 5,
        ["biased_epic_elite"] = 8, ["biased_epic_reward"] = 8,
        ["bias_value"] = 10,
    };

    private static TowerData LoadOk(string dir)
    {
        var (data, errors) = TowerDataLoader.Load(dir);
        Assert.Empty(errors);
        Assert.NotNull(data);
        return data!;
    }

    [Fact]
    public void Real_tower_data_loads_without_errors()
    {
        var d = LoadOk(FixturePaths.RealTowerData);
        Assert.Equal(5, d.FloorTypes.Count);
        Assert.Equal(15, d.Rules.Ranks.Count);
        Assert.Equal(5, d.Rules.Floors);
        Assert.Equal(5, d.Rules.FloorSlots);
        Assert.Equal(3, d.Rules.TowerSlots);
    }

    [Fact]
    public void Tower_fixture_loads_without_errors()
    {
        var d = LoadOk(FixturePaths.TowerFixture);
        Assert.Equal(15, d.Rules.Ranks.Count);
        Assert.Equal(d.Currencies.Count, LoadOk(FixturePaths.RealTowerData).Currencies.Count);
    }

    [Fact]
    public void Rank_table_matches_spec()
    {
        var ranks = LoadOk(FixturePaths.RealTowerData).Rules.Ranks;
        var r2 = ranks[1];
        Assert.Equal((2, 12, 24, 1), (r2.Rank, r2.LevelMin, r2.LevelMax, r2.MaxTier));
        Assert.True(r2.ModKinds.SetEquals(new[] { ModKind.General }));
        var r4 = ranks[3];
        Assert.True(r4.ModKinds.SetEquals(new[] { ModKind.General, ModKind.FloorType, ModKind.Tower }));
        Assert.Equal(2, r4.MaxTier);
        Assert.Equal(0, r4.ZoneLevel);
        Assert.Equal(4, ranks[7].MaxTier);
        Assert.Equal(63, ranks[7].ZoneLevel);
        Assert.Equal(5, ranks[14].MaxTier);
        Assert.Equal(70, ranks[14].ZoneLevel);
        int[] maxTier = { 1, 1, 2, 2, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5 };
        Assert.Equal(maxTier, ranks.Select(r => r.MaxTier));
    }

    [Fact]
    public void Currency_levels_and_ranks_match_spec()
    {
        foreach (var dir in new[] { FixturePaths.RealTowerData, FixturePaths.TowerFixture })
        {
            var d = LoadOk(dir);
            Assert.Equal(Levelled.OrderBy(x => x), d.Currencies.Where(c => c.HasLevel).Select(c => c.Id).OrderBy(x => x));
            Assert.Equal(DropsFrom.Keys.OrderBy(x => x), d.Currencies.Select(c => c.Id).OrderBy(x => x));
            foreach (var c in d.Currencies) Assert.Equal(DropsFrom[c.Id], c.DropsFromRank);
        }
    }

    [Fact]
    public void Mods_keep_file_order()
    {
        var d = LoadOk(FixturePaths.RealTowerData);
        var file = JsonNode.Parse(File.ReadAllText(Path.Combine(FixturePaths.RealTowerData, "floor_mods.json")))!.AsArray();
        Assert.Equal((string)file[0]!["id"]!, d.Mods[0].Id);
        Assert.Equal(file.Select(n => (string)n!["id"]!), d.Mods.Select(m => m.Id));
        Assert.Same(d.Mods[3], d.Mod(d.Mods[3].Id));
        Assert.True(d.TryGetMod(d.Mods[3].Id, out _));
        Assert.False(d.TryGetMod("nope", out _));
    }

    [Fact]
    public void Unknown_field_is_rejected()
    {
        var e = Broken("floor_mods.json", mods => mods[0]!["colour"] = "red");
        Assert.Contains(e, x => x.File == "floor_mods.json" && x.Message.Contains("colour"));
    }

    [Fact]
    public void Unknown_rules_field_is_rejected()
    {
        var dir = FixturePaths.CopyTowerFixture();
        Edit(dir, "rules.json", n => n["ranks"]![2]!["colour"] = 1);
        var (data, errors) = TowerDataLoader.Load(dir);
        Assert.Null(data);
        Assert.Contains(errors, x => x.File == "rules.json" && x.Message.Contains("colour"));
    }

    [Fact]
    public void Hard_counter_on_tower_mod_is_rejected()
    {
        string? id = null;
        var e = Broken("floor_mods.json", mods =>
        {
            var m = First(mods, "kind", "tower");
            id = (string)m["id"]!;
            m["hard_counter"] = "no_healing";
        });
        Assert.Contains(e, x => x.Id == id && x.Message.Contains("контр"));
    }

    [Fact]
    public void Floor_type_mod_needs_exactly_one_type()
    {
        string? id = null;
        var e = Broken("floor_mods.json", mods =>
        {
            var m = mods.First(n => (string)n!["kind"]! == "floor_type" && (string)n["floor_types"]![0]! != "trial")!;
            id = (string)m["id"]!;
            m["floor_types"] = new JsonArray("clear", "waves");
        });
        Assert.Contains(e, x => x.Id == id && x.Message.Contains("ровно к одному типу"));
    }

    [Fact]
    public void Trial_mod_needs_variant()
    {
        string? id = null;
        var e = Broken("floor_mods.json", mods =>
        {
            var m = mods.First(n => (string)n!["kind"]! == "floor_type" && (string)n["floor_types"]![0]! == "trial")!;
            id = (string)m["id"]!;
            m.AsObject().Remove("variant");
        });
        Assert.Contains(e, x => x.Id == id && x.Message.Contains("variant"));
    }

    [Fact]
    public void Tiers_must_be_1_to_5_in_order()
    {
        string? id = null;
        var e = Broken("floor_mods.json", mods =>
        {
            id = (string)mods[0]!["id"]!;
            var tiers = mods[0]!["tiers"]!.AsArray();
            tiers[0]!["tier"] = 2;
            tiers[1]!["tier"] = 1;
        });
        Assert.Contains(e, x => x.Id == id && x.Message.Contains("тир"));
    }

    [Fact]
    public void Min_above_max_is_rejected()
    {
        string? id = null;
        var e = Broken("floor_mods.json", mods =>
        {
            id = (string)mods[0]!["id"]!;
            var t = mods[0]!["tiers"]![0]!;
            t["min"] = 50;
            t["max"] = 10;
        });
        Assert.Contains(e, x => x.Id == id && x.Message.Contains("«min»"));
    }

    [Fact]
    public void Rank_bands_must_join()
    {
        var dir = FixturePaths.CopyTowerFixture();
        Edit(dir, "rules.json", n => n["ranks"]![2]!["level_min"] = 25);
        var (data, errors) = TowerDataLoader.Load(dir);
        Assert.Null(data);
        Assert.Contains(errors, x => x.File == "rules.json" && x.Message.Contains("Ранг 3"));
    }

    [Fact]
    public void Rank1_drop_multiplier_below_one_is_rejected()
    {
        var dir = FixturePaths.CopyTowerFixture();
        Edit(dir, "rules.json", n => n["rank1_drop_multiplier"] = 0.9);
        var (data, errors) = TowerDataLoader.Load(dir);
        Assert.Null(data);
        Assert.Contains(errors, x => x.File == "rules.json" && x.Message.Contains("rank1_drop_multiplier"));
    }

    [Fact]
    public void Rank1_drop_multiplier_is_required()
    {
        var dir = FixturePaths.CopyTowerFixture();
        Edit(dir, "rules.json", n => n.AsObject().Remove("rank1_drop_multiplier"));
        var (data, errors) = TowerDataLoader.Load(dir);
        Assert.Null(data);
        Assert.Contains(errors, x => x.File == "rules.json" && x.Message.Contains("rank1_drop_multiplier"));
    }

    [Fact]
    public void Height_multipliers_must_match_floors()
    {
        var dir = FixturePaths.CopyTowerFixture();
        Edit(dir, "rules.json", n => n["height_multipliers"] = new JsonArray(1.0, 1.1));
        var (data, errors) = TowerDataLoader.Load(dir);
        Assert.Null(data);
        Assert.Contains(errors, x => x.File == "rules.json" && x.Message.Contains("height_multipliers"));
    }

    [Fact]
    public void Levelled_currency_must_grow()
    {
        var e = Broken("currencies.json", cs => First(cs, "id", "imbue")["drop_growth"] = 1.0);
        Assert.Contains(e, x => x.File == "currencies.json" && x.Id == "imbue" && x.Message.Contains("drop_growth"));
    }

    [Fact]
    public void Clear_cannot_have_level()
    {
        var e = Broken("currencies.json", cs => First(cs, "id", "clear")["has_level"] = true);
        Assert.Contains(e, x => x.Id == "clear" && x.Message.Contains("уров"));
    }

    [Fact]
    public void Biased_currency_needs_direction_and_chance()
    {
        var e = Broken("currencies.json", cs =>
        {
            First(cs, "id", "biased_rare_elite")["bias_chance"] = 1.0;
            First(cs, "id", "biased_rare_reward")["bias_direction"] = null;
        });
        Assert.Contains(e, x => x.Id == "biased_rare_elite" && x.Message.Contains("bias_chance"));
        Assert.Contains(e, x => x.Id == "biased_rare_reward" && x.Message.Contains("bias_direction"));
    }

    [Fact]
    public void Unknown_floor_type_in_mod_is_rejected()
    {
        string? id = null;
        var e = Broken("floor_mods.json", mods =>
        {
            var m = First(mods, "kind", "general");
            id = (string)m["id"]!;
            m["floor_types"] = new JsonArray("lava");
        });
        Assert.Contains(e, x => x.Id == id && x.Message.Contains("lava"));
    }

    [Fact]
    public void Missing_shuffle_currency_is_rejected()
    {
        var e = Broken("currencies.json", cs => cs.Remove(First(cs, "id", "shuffle")));
        Assert.Contains(e, x => x.File == "currencies.json" && x.Message.Contains("обязательной валюты «shuffle»"));
    }

    [Fact]
    public void Required_currency_with_wrong_kind_is_rejected()
    {
        var e = Broken("currencies.json", cs => First(cs, "id", "imbue_tower")["mod_kind"] = "general");
        Assert.Contains(e, x => x.Id == "imbue_tower" && x.Message.Contains("tower"));
    }

    [Fact]
    public void First_shuffle_must_be_free()
    {
        var dir = FixturePaths.CopyTowerFixture();
        Edit(dir, "rules.json", n => n["shuffle_costs"] = new JsonArray(1, 2, 4));
        var (data, errors) = TowerDataLoader.Load(dir);
        Assert.Null(data);
        Assert.Contains(errors, x => x.File == "rules.json" && x.Message.Contains("shuffle_costs"));
    }

    [Fact]
    public void Level_cap_comes_from_build_rules() =>
        Assert.Equal(Dialoid.Core.Character.BuildRules.MaxLevel, TowerDataLoader.LevelCap);

    [Fact]
    public void Tower_random_pick_stays_in_range()
    {
        var rng = Dialoid.Core.Simulation.RandomSources.Create(Dialoid.Core.Simulation.SimMode.Roll, 7);
        var seen = new HashSet<int>();
        for (int i = 0; i < 1000; i++)
        {
            int p = TowerRandom.Pick(rng, 4);
            Assert.InRange(p, 0, 3);
            seen.Add(p);
        }
        Assert.Equal(4, seen.Count);
    }

    // --- helpers ---

    private static IReadOnlyList<DataError> Broken(string file, Action<JsonArray> edit)
    {
        var dir = FixturePaths.CopyTowerFixture();
        Edit(dir, file, n => edit(n.AsArray()));
        var (data, errors) = TowerDataLoader.Load(dir);
        Assert.Null(data);
        Assert.NotEmpty(errors);
        return errors;
    }

    private static JsonNode First(JsonArray arr, string field, string value) =>
        arr.First(n => n![field] is JsonValue v && v.TryGetValue<string>(out var s) && s == value)!;

    private static void Edit(string dir, string file, Action<JsonNode> edit)
    {
        var path = Path.Combine(dir, file);
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        edit(node);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
