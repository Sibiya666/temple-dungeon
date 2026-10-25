using System.Text.Json.Nodes;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Data;

public class ValidatorTests
{
    /// <summary>Copies tiny into a temp dir and edits one JSON array file.</summary>
    private static LoadResult LoadMutated(string file, Action<JsonArray> mutate)
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, file);
        var arr = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        mutate(arr);
        File.WriteAllText(path, arr.ToJsonString());
        return DataLoader.Load(dir);
    }

    private static JsonNode Node(JsonArray a, string id) => a.First(n => n!["id"]!.GetValue<string>() == id)!;

    [Fact]
    public void Tiny_fixture_passes_validation()
    {
        var d = DataLoader.Load(FixturePaths.Tiny);
        Assert.Empty(d.Errors);
        Assert.Empty(Validator.Validate(d.Data!, MechanicRegistry.Default));
    }

    [Fact]
    public void Validator_reports_disconnected_tree()
    {
        var r = LoadMutated("passives.json", a => a.Add(JsonNode.Parse(
            "{\"id\":\"island\",\"name\":\"Остров\",\"description\":\"\",\"kind\":\"small\",\"region\":\"test\",\"x\":0,\"y\":0,\"links\":[]}")));
        Assert.Null(r.Data);
        var e = Assert.Single(r.Errors);
        Assert.Equal("passives.json", e.File);
        Assert.Equal("island", e.Id);
        Assert.Contains("недостижим", e.Message);
    }

    [Fact]
    public void Validator_reports_two_starts_for_one_class()
    {
        var r = LoadMutated("passives.json", a => Node(a, "test_start_mage")["start_for_class"] = "test_warrior");
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "test_warrior" && e.Message.Contains("стартов"));
        Assert.Contains(r.Errors, e => e.File == "classes.json" && e.Id == "test_mage");
    }

    [Fact]
    public void Validator_reports_unknown_reference_with_file_and_id()
    {
        var r = LoadMutated("classes.json", a => Node(a, "test_warrior")["passive_start_node"] = "nope_node");
        Assert.Contains(r.Errors, e => e.File == "classes.json" && e.Id == "test_warrior" && e.Message.Contains("nope_node"));

        r = LoadMutated("classes.json", a => Node(a, "test_warrior")["ascendancies"] = new JsonArray("test_asc", "no_asc"));
        Assert.Contains(r.Errors, e => e.File == "classes.json" && e.Id == "test_warrior" && e.Message.Contains("no_asc"));

        r = LoadMutated("passives.json", a => Node(a, "small_c")["links"] = new JsonArray("ghost"));
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "small_c" && e.Message.Contains("ghost"));

        r = LoadMutated("ascendancies.json", a => Node(a, "test_asc")["class_id"] = "test_mage");
        Assert.Contains(r.Errors, e => e.File == "ascendancies.json" && e.Id == "test_asc");

        r = LoadMutated("passives.json", a => Node(a, "small_a")["effects"] = JsonNode.Parse(
            "[{\"type\":\"modifier\",\"stat\":\"no_such_stat\",\"kind\":\"flat\",\"value\":1}]"));
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "small_a" && e.Message.Contains("no_such_stat"));
    }

    [Fact]
    public void Validator_reports_per_stat_cycle()
    {
        // The primary-attribute rule makes damage depend on str; make str depend on damage.
        var r = LoadMutated("passives.json", a => Node(a, "small_a")["effects"] = JsonNode.Parse(
            "[{\"type\":\"modifier\",\"stat\":\"str\",\"kind\":\"flat\",\"value\":1,\"per\":{\"stat\":\"damage\",\"every\":1}}]"));
        Assert.Null(r.Data);
        var e = Assert.Single(r.Errors);
        Assert.Contains("Цикл", e.Message);
        Assert.Contains("damage", e.Message);
        Assert.Contains("str", e.Message);
    }
    [Fact]
    public void Validator_rejects_unknown_condition_id()
    {
        var r = LoadMutated("passives.json", a => Node(a, "small_a")["effects"] = JsonNode.Parse(
            "[{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"increased\",\"value\":1,\"conditions\":[\"target_dancing\"]}]"));
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "small_a" && e.Message.Contains("target_dancing"));

        var ok = LoadMutated("passives.json", a => Node(a, "small_a")["effects"] = JsonNode.Parse(
            "[{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"increased\",\"value\":1,\"conditions\":[\"target_ignited\"]}]"));
        Assert.Empty(ok.Errors);

        var skill = LoadMutated("skills.json", a => Node(a, "test_slash")["tree"]![0]!["effects"] = JsonNode.Parse(
            "[{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"increased\",\"value\":1,\"conditions\":[{\"id\":\"nope\"}]}]"));
        Assert.Contains(skill.Errors, e => e.File == "skills.json" && e.Id == "test_slash" && e.Message.Contains("nope"));
    }

    private static LoadResult SmallA(string effectsJson) =>
        LoadMutated("passives.json", a => Node(a, "small_a")["effects"] = JsonNode.Parse(effectsJson));

    [Fact]
    public void Mechanic_param_of_wrong_type_is_a_load_error()
    {
        var r = SmallA("[{\"type\":\"mechanic\",\"id\":\"blood_pact\",\"params\":{\"life_more\":\"-30\"}}]");
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "small_a" && e.Message.Contains("life_more") && e.Message.Contains("числом"));
    }

    [Fact]
    public void Mechanic_param_with_unknown_name_is_a_load_error()
    {
        var r = SmallA("[{\"type\":\"mechanic\",\"id\":\"blood_pact\",\"params\":{\"life_mor\":-30}}]");
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "small_a" && e.Message.Contains("неизвестный параметр «life_mor»"));
    }

    [Fact]
    public void Mechanic_param_references_are_checked()
    {
        var ailment = SmallA("[{\"type\":\"mechanic\",\"id\":\"flesh_rend\",\"params\":{\"ailment\":\"bleeding\"}}]");
        Assert.Contains(ailment.Errors, e => e.Id == "small_a" && e.Message.Contains("неизвестный недуг «bleeding»"));
        var type = SmallA("[{\"type\":\"mechanic\",\"id\":\"ground_type\",\"params\":{\"type\":\"lightnig\"}}]");
        Assert.Contains(type.Errors, e => e.Id == "small_a" && e.Message.Contains("lightnig"));
        var grant = SmallA("[{\"type\":\"mechanic\",\"id\":\"grant_skill_node\",\"params\":{\"skill\":\"test_slash\",\"node\":\"nope\"}}]");
        Assert.Contains(grant.Errors, e => e.Id == "small_a" && e.Message.Contains("нет узла «nope»"));
        var missing = SmallA("[{\"type\":\"mechanic\",\"id\":\"grant_skill_node\",\"params\":{\"skill\":\"test_slash\"}}]");
        Assert.Contains(missing.Errors, e => e.Id == "small_a" && e.Message.Contains("обязательный параметр «node»"));
        var ok = SmallA("[{\"type\":\"mechanic\",\"id\":\"flesh_rend\",\"params\":{\"ailment\":\"bleed\",\"extra_stacks\":2}}]");
        Assert.Empty(ok.Errors);
    }

    [Fact]
    public void Character_phase_mechanic_with_a_skill_scope_is_a_load_error()
    {
        var r = SmallA("[{\"type\":\"mechanic\",\"id\":\"shatter\",\"scope\":\"skill:test_slash\"}]");
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "small_a" && e.Message.Contains("shatter") && e.Message.Contains("global"));
        Assert.Empty(SmallA("[{\"type\":\"mechanic\",\"id\":\"flesh_rend\",\"scope\":\"skill:test_slash\"}]").Errors);
    }

    [Fact]
    public void Every_catalog_mechanic_declares_its_params()
    {
        foreach (var id in new[] { "blood_pact", "titan_will", "inevitability", "avatar_of_fire", "blade_dance", "stone_skin",
                     "berserk_frenzy", "insatiable", "reckless", "whirl_rage", "flesh_rend", "battle_trance", "blood_feast",
                     "static_charge", "grounding", "ground_type", "spread_ignite", "ashes", "eternal_fire", "soul_heat", "fire_armor",
                     "shatter", "ice_armor", "grant_skill_node", "ailment_swap", "brittle", "chill_slow" })
            Assert.NotNull(MechanicRegistry.Default.Get(id).ParamSchema);
    }

    private static string Mod(string extra) =>
        "[{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"increased\",\"value\":1" + extra + "}]";

    [Fact]
    public void More_below_minus_100_is_rejected()
    {
        var r = SmallA("[{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"more\",\"value\":-120}]");
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "small_a" && e.Message.Contains("−100"));
        Assert.Empty(SmallA("[{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"more\",\"value\":-100}]").Errors);

        // Per point: -30 x 5 points = -150.
        var tree = LoadMutated("skills.json", a =>
        {
            var node = Node(a, "test_slash")["tree"]![0]!;
            node["max_points"] = 5;
            node["effects"] = JsonNode.Parse("[{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"more\",\"value\":-30}]");
        });
        Assert.Contains(tree.Errors, e => e.File == "skills.json" && e.Id == "test_slash" && e.Message.Contains("−100"));
    }

    [Fact]
    public void Stat_min_max_and_default_are_checked()
    {
        var minMax = LoadMutated("stats.json", a => { Node(a, "armor")["min"] = 10; Node(a, "armor")["max"] = 5; });
        Assert.Contains(minMax.Errors, e => e.File == "stats.json" && e.Id == "armor" && e.Message.Contains("«min»"));
        var def = LoadMutated("stats.json", a => Node(a, "crit_chance")["default"] = 150);
        Assert.Contains(def.Errors, e => e.File == "stats.json" && e.Id == "crit_chance" && e.Message.Contains("«default»"));
    }

    [Fact]
    public void Missing_required_stat_is_a_load_error()
    {
        var r = LoadMutated("stats.json", a => a.Remove(Node(a, "resist_poison_max")));
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == "stats.json" && e.Id == "resist_poison_max" && e.Message.Contains("обязательный"));
        Assert.Contains("dodge_chance_max", RequiredStats.All);
        Assert.Contains("penetration_physical", RequiredStats.All);
    }

    [Fact]
    public void Tag_catalog_is_enforced_on_effect_tags_and_tag_scopes()
    {
        Assert.Contains(SmallA(Mod(",\"tags\":[\"fiery\"]")).Errors, e => e.Id == "small_a" && e.Message.Contains("неизвестный тег «fiery»"));
        Assert.Contains(SmallA(Mod(",\"scope\":{\"tags\":[\"magic\"]}")).Errors, e => e.Id == "small_a" && e.Message.Contains("«magic»"));
        Assert.Contains(SmallA("[{\"type\":\"add_tag\",\"tag\":\"wet\",\"scope\":\"skill:test_slash\"}]").Errors,
            e => e.Id == "small_a" && e.Message.Contains("«wet»"));
        Assert.Empty(SmallA(Mod(",\"tags\":[\"hit\",\"crit\",\"skill:test_slash\"]")).Errors); // engine tags and dynamic skill:*
    }

    [Fact]
    public void Skill_scope_must_name_an_existing_skill()
    {
        var r = SmallA(Mod(",\"scope\":\"skill:nope\""));
        Assert.Contains(r.Errors, e => e.Id == "small_a" && e.Message.Contains("skill:nope"));
    }

    [Fact]
    public void Behavior_params_must_be_known()
    {
        var effect = SmallA("[{\"type\":\"set_behavior\",\"param\":\"pierce\",\"value\":2,\"scope\":\"skill:test_bolt\"}]");
        Assert.Contains(effect.Errors, e => e.Id == "small_a" && e.Message.Contains("«pierce»"));
        var skill = LoadMutated("skills.json", a => Node(a, "test_slam")["behavior"] = JsonNode.Parse("{\"radius\":4}"));
        Assert.Contains(skill.Errors, e => e.File == "skills.json" && e.Id == "test_slam" && e.Message.Contains("«radius»"));
    }

    [Fact]
    public void Condition_refs_must_name_a_skill_or_a_buff()
    {
        var skill = SmallA(Mod(",\"conditions\":[{\"id\":\"recently_used_skill\",\"ref\":\"nope\"}]"));
        Assert.Contains(skill.Errors, e => e.Id == "small_a" && e.Message.Contains("recently_used_skill") && e.Message.Contains("nope"));
        Assert.Empty(SmallA(Mod(",\"conditions\":[{\"id\":\"recently_used_skill\",\"ref\":\"test_slam\"}]")).Errors);

        var buff = SmallA(Mod(",\"conditions\":[{\"id\":\"buff_active\",\"ref\":\"rage\"}]"));
        Assert.Contains(buff.Errors, e => e.Id == "small_a" && e.Message.Contains("buff_active") && e.Message.Contains("rage"));
        // A buff some apply_buff proc applies, or a mechanic's own buff (mechanic:<id>), is known.
        var proc = ",{\"type\":\"grant_proc\",\"scope\":\"skill:test_slash\",\"trigger\":\"on_use\",\"action\":{\"apply_buff\":{\"id\":\"rage\",\"duration\":3}}}";
        Assert.Empty(SmallA(Mod(",\"conditions\":[{\"id\":\"buff_active\",\"ref\":\"rage\"}]")[..^1] + proc + "]").Errors);
        Assert.Empty(SmallA(Mod(",\"conditions\":[{\"id\":\"buff_active\",\"ref\":\"mechanic:flesh_rend\"}]")[..^1]
                            + ",{\"type\":\"mechanic\",\"id\":\"flesh_rend\"}]").Errors);
    }

    [Fact]
    public void Dummy_resists_must_be_within_minus_100_and_100()
    {
        var r = LoadMutated("dummies.json", a => Node(a, "test_boss")["resists"] = JsonNode.Parse("{\"fire\":150}"));
        Assert.Contains(r.Errors, e => e.File == "dummies.json" && e.Id == "test_boss" && e.Message.Contains("fire"));
        Assert.Empty(LoadMutated("dummies.json", a => Node(a, "test_boss")["resists"] = JsonNode.Parse("{\"fire\":100,\"cold\":-100}")).Errors);
    }
}
