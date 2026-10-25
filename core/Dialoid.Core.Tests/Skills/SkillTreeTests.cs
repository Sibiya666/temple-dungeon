using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;
using Dialoid.Core.Tests.Character;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Skills;

public class SkillTreeTests
{
    private static readonly GameData Data = DataLoader.Load(FixturePaths.Tiny).Data!;

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
    public void Fixture_skills_load_with_expected_shape()
    {
        var slash = Data.Skills["test_slash"];
        Assert.Equal("test_warrior", slash.ClassId);
        Assert.Equal(110, slash.Base.WeaponEffectiveness);
        Assert.Equal(12, slash.Base.ResourceGain);
        Assert.Equal(10, slash.Base.AilmentChances["bleed"]);
        Assert.Equal(4, slash.Tree.Count);
        Assert.Equal(3, slash.Tree.Single(n => n.Id == "slash_depth").MaxPoints);
        Assert.True(slash.Tags.Contains("generator"));
        Assert.True(slash.IsAttack);
        Assert.Equal(35, Data.Skills["test_slam"].Base.ResourceCost);
        var bolt = Data.Skills["test_bolt"];
        Assert.Equal((14d, 20d), bolt.Base.SpellDamage[DamageType.Fire]);
        Assert.Equal(0.75, bolt.Base.CastTime);
        Assert.Equal(2, Data.Skills["test_spark"].Base.Behavior["chain_count"]);
        Assert.Null(Data.Skills["test_cloud"].ClassId);
        Assert.Equal(1, Data.Skills["test_cloud"].Base.AttackTimeMultiplier);
        Assert.Equal(100, Data.Skills["test_cloud"].Base.AddedDamageEffectiveness);
    }

    [Fact]
    public void Skill_tree_points_equal_skill_level()
    {
        Assert.Equal(7, SkillTree.PointsForLevel(7));
        var ok = TestBuilds.Warrior(weapon: "test_sword", slot: new SkillSlot { SkillId = "test_slash", Level = 7, NodePoints = { ["slash_depth"] = 3, ["slash_rend"] = 1, ["slash_edge"] = 1 } });
        Assert.Empty(BuildRules.Validate(ok, Data));
        var over = TestBuilds.Warrior(slot: new SkillSlot { SkillId = "test_slash", Level = 2, NodePoints = { ["slash_depth"] = 3 } });
        Assert.Contains(BuildRules.Validate(over, Data), x => x.Code == "too_many_skill_points");
    }

    [Fact]
    public void Node_requires_points_in_parent()
    {
        var bad = TestBuilds.Warrior(slot: new SkillSlot { SkillId = "test_slash", Level = 5, NodePoints = { ["slash_rend"] = 1 } });
        Assert.Contains(BuildRules.Validate(bad, Data), x => x.Code == "skill_node_requirement_unmet");
        var good = TestBuilds.Warrior(weapon: "test_sword", slot: new SkillSlot { SkillId = "test_slash", Level = 5, NodePoints = { ["slash_depth"] = 1, ["slash_rend"] = 1 } });
        Assert.Empty(BuildRules.Validate(good, Data));
    }

    [Fact]
    public void Node_over_max_points_is_violation()
    {
        var bad = TestBuilds.Warrior(slot: new SkillSlot { SkillId = "test_slash", Level = 10, NodePoints = { ["slash_depth"] = 4 } });
        Assert.Contains(BuildRules.Validate(bad, Data), x => x.Code == "skill_node_over_max");
    }

    [Fact]
    public void Exclusive_group_allows_only_one()
    {
        var bad = TestBuilds.Warrior(slot: new SkillSlot { SkillId = "test_slash", Level = 6, NodePoints = { ["slash_depth"] = 1, ["slash_rend"] = 1, ["slash_fury"] = 1 } });
        Assert.Single(BuildRules.Validate(bad, Data), x => x.Code == "exclusive_group_violation");
    }

    [Fact]
    public void Slots_unlock_at_1_4_8_14_20()
    {
        Assert.Equal(new[] { 1, 1, 1, 2, 2, 3, 4, 5, 5 }, new[] { 1, 3, 3, 4, 7, 8, 14, 20, 60 }.Select(BuildRules.SlotsUnlocked));
        var b = TestBuilds.Warrior(level: 3, slots: new[]
        {
            new SkillSlot { SkillId = "test_slash" }, new SkillSlot { SkillId = "test_slam" },
        });
        Assert.Contains(BuildRules.Validate(b, Data), x => x.Code == "slot_locked");
    }

    [Fact]
    public void Native_skill_cap_20_loot_cap_10_item_bonus_up_to_14()
    {
        var native = TestBuilds.Warrior(level: 30);
        Assert.Equal(20, BuildRules.SkillLevelCap(native, "test_slash", Data));

        var noItem = TestBuilds.Warrior(level: 30);
        Assert.Equal(0, BuildRules.SkillLevelCap(noItem, "test_cloud", Data));

        var ring = TestBuilds.Warrior(level: 30, items: "test_cloud_ring");
        Assert.Equal(12, BuildRules.SkillLevelCap(ring, "test_cloud", Data)); // 10 + 2

        var big = TestBuilds.Warrior(level: 30, items: "test_big_ring");
        Assert.Equal(14, BuildRules.SkillLevelCap(big, "test_cloud", Data));  // 10 + 9 clamped

        var tooHigh = TestBuilds.Warrior(level: 30, items: "test_cloud_ring", slot: new SkillSlot { SkillId = "test_cloud", Level = 13 });
        Assert.Contains(BuildRules.Validate(tooHigh, Data), x => x.Code == "skill_level_above_cap");
        var atCap = TestBuilds.Warrior(level: 30, items: "test_cloud_ring", slot: new SkillSlot { SkillId = "test_cloud", Level = 12 });
        Assert.Empty(BuildRules.Validate(atCap, Data));
    }

    [Fact]
    public void Loot_skill_requires_item_equipped()
    {
        var without = TestBuilds.Warrior(slot: new SkillSlot { SkillId = "test_cloud" });
        Assert.Contains(BuildRules.Validate(without, Data), x => x.Code == "skill_not_available");
        var with = TestBuilds.Warrior(items: "test_cloud_ring", slot: new SkillSlot { SkillId = "test_cloud" });
        Assert.Empty(BuildRules.Validate(with, Data));
    }

    [Fact]
    public void Other_class_skill_without_item_is_violation()
    {
        var b = TestBuilds.Warrior(slot: new SkillSlot { SkillId = "test_bolt" });
        Assert.Contains(BuildRules.Validate(b, Data), x => x.Code == "skill_not_available");
        var unknown = TestBuilds.Warrior(slot: new SkillSlot { SkillId = "no_such_skill" });
        Assert.Contains(BuildRules.Validate(unknown, Data), x => x.Code == "skill_not_available");
    }

    [Fact]
    public void Attack_skill_requires_matching_weapon_type()
    {
        var sword = TestBuilds.Warrior(weapon: "test_sword", slot: new SkillSlot { SkillId = "test_slam" });
        Assert.Contains(BuildRules.Validate(sword, Data), x => x.Code == "weapon_type_mismatch");
        var none = TestBuilds.Warrior(level: 8, slot: new SkillSlot { SkillId = "test_slash" });
        Assert.Contains(BuildRules.Validate(none, Data), x => x.Code == "weapon_type_mismatch");
        var ok = TestBuilds.Warrior(weapon: "test_sword", slot: new SkillSlot { SkillId = "test_slash" });
        Assert.Empty(BuildRules.Validate(ok, Data));
        // spells do not care about the weapon
        var spell = new Build { ClassId = "test_mage", Slots = { new SkillSlot { SkillId = "test_bolt" } } };
        Assert.Empty(BuildRules.Validate(spell, Data));
    }

    [Fact]
    public void Skill_locked_by_unlock_level_is_violation()
    {
        var b = TestBuilds.Warrior(level: 3, weapon: "test_greataxe", slot: new SkillSlot { SkillId = "test_slam" });
        Assert.Contains(BuildRules.Validate(b, Data), x => x.Code == "skill_locked_by_level");
        var ok = TestBuilds.Warrior(level: 4, weapon: "test_greataxe", slot: new SkillSlot { SkillId = "test_slam" });
        Assert.Empty(BuildRules.Validate(ok, Data));
    }

    [Fact]
    public void Duplicate_skill_rotation_and_unknown_item_weapon_are_reported()
    {
        var b = TestBuilds.Warrior(level: 10, weapon: "no_weapon", items: "no_item", slots: new[]
        {
            new SkillSlot { SkillId = "test_slash" }, new SkillSlot { SkillId = "test_slash" },
        });
        b.Rotation.Add("test_slam");
        var codes = BuildRules.Validate(b, Data).Select(x => x.Code).ToList();
        Assert.Contains("duplicate_skill_in_slots", codes);
        Assert.Contains("rotation_unknown_skill", codes);
        Assert.Contains("unknown_weapon", codes);
        Assert.Contains("unknown_item", codes);
        Assert.DoesNotContain("weapon_type_mismatch", codes);
    }

    [Fact]
    public void Validator_reports_skill_tree_requirement_cycle()
    {
        var r = LoadMutated("skills.json", a =>
        {
            var tree = Node(a, "test_slash")["tree"]!.AsArray();
            Node(tree, "slash_depth")["requires"] = JsonNode.Parse("[{\"node\":\"slash_rend\",\"points\":1}]");
        });
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == "skills.json" && e.Id == "test_slash" && e.Message.Contains("Цикл"));
    }

    [Fact]
    public void Validator_reports_unknown_requirement_and_bad_references()
    {
        var r = LoadMutated("skills.json", a =>
        {
            var tree = Node(a, "test_slash")["tree"]!.AsArray();
            Node(tree, "slash_rend")["requires"] = JsonNode.Parse("[{\"node\":\"ghost\",\"points\":1}]");
            Node(a, "test_bolt")["class_id"] = "no_class";
            Node(a, "test_slam")["weapon_types"] = new JsonArray("club");
        });
        Assert.Contains(r.Errors, e => e.Id == "test_slash" && e.Message.Contains("ghost"));
        Assert.Contains(r.Errors, e => e.Id == "test_bolt" && e.Message.Contains("no_class"));
        Assert.Contains(r.Errors, e => e.Id == "test_slam" && e.Message.Contains("club"));

        r = LoadMutated("classes.json", a => Node(a, "test_warrior")["skills"] = new JsonArray("test_slash", "test_bolt", "nope"));
        Assert.Contains(r.Errors, e => e.File == "classes.json" && e.Message.Contains("nope"));
        Assert.Contains(r.Errors, e => e.File == "classes.json" && e.Message.Contains("test_bolt"));

        r = LoadMutated("items.json", a => Node(a, "test_cloud_ring")["grants_skill"] = "nope");
        Assert.Contains(r.Errors, e => e.File == "items.json" && e.Message.Contains("nope"));

        r = LoadMutated("weapons.json", a => Node(a, "test_sword")["type"] = "club");
        Assert.Contains(r.Errors, e => e.File == "weapons.json" && e.Id == "test_sword" && e.Message.Contains("club"));
    }

    [Fact]
    public void Weapon_implicits_become_global_modifiers()
    {
        var b = TestBuilds.Warrior(weapon: "test_staff");
        var c = new CharacterCompiler(Data).Compile(b);
        var mod = Assert.Single(c.GlobalModifiers, m => m.Stat == "cast_speed");
        Assert.Equal(10, mod.Value);
        Assert.Equal("weapon", mod.Source.Kind);
        Assert.Equal("test_staff", mod.Source.Id);
        var line = Assert.Single(c.Evaluator.Evaluate("cast_speed", TagSet.Empty).Lines, l => l.Source.Kind == "weapon");
        Assert.Equal(10, line.Value);
    }

    [Fact]
    public void Skill_level_from_xp_table()
    {
        var table = Data.Progression.SkillLevels;
        Assert.Equal(20, table.Count);
        Assert.Equal(60, Data.Progression.CharacterLevels.Count);
        Assert.Equal(1, SkillProgression.LevelForXp(0, table, 20));
        Assert.Equal(2, SkillProgression.LevelForXp(table[1], table, 20));
        Assert.Equal(1, SkillProgression.LevelForXp(table[1] - 1, table, 20));
        Assert.Equal(20, SkillProgression.LevelForXp(1e18, table, 20));
        Assert.Equal(10, SkillProgression.LevelForXp(1e18, table, 10));
    }

    [Fact]
    public void Progression_must_strictly_increase()
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, "progression.json");
        var o = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        o["skill_levels"]![3] = 0;
        File.WriteAllText(path, o.ToJsonString());
        var r = DataLoader.Load(dir);
        Assert.Contains(r.Errors, e => e.File == "progression.json" && e.Message.Contains("возрастать"));
    }
}
