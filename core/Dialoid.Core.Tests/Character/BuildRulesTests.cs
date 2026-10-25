using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Character;

public class BuildRulesTests
{
    private static readonly GameData Data = DataLoader.Load(FixturePaths.Tiny).Data!;

    private static List<string> Codes(Build b) => BuildRules.Validate(b, Data).Select(v => v.Code).ToList();

    [Fact]
    public void Empty_warrior_build_is_legal()
    {
        Assert.Empty(BuildRules.Validate(TestBuilds.Warrior(), Data));
    }

    [Fact]
    public void Unknown_class_and_level_range()
    {
        var b = new Build { ClassId = "nope", Level = 99 };
        var codes = Codes(b);
        Assert.Contains("unknown_class", codes);
        Assert.Contains("level_out_of_range", codes);
    }

    [Fact]
    public void Passive_rules()
    {
        var b = TestBuilds.Warrior(level: 2);
        b.PassiveNodes.AddRange(new[] { "small_a", "small_a", "ghost", "test_start_warrior", "small_c" });
        var codes = Codes(b);
        Assert.Contains("duplicate_passive_node", codes);
        Assert.Contains("unknown_passive_node", codes);
        Assert.Contains("start_node_in_build", codes);
        Assert.Contains("passive_disconnected", codes); // small_c without small_b
        Assert.Contains("too_many_passive_points", codes); // 2 nodes, 1 point

        var attr = TestBuilds.Warrior(level: 5);
        attr.PassiveNodes.Add("small_a");
        attr.PassiveNodes.Add("attr_node");
        Assert.Contains("attribute_choice_missing", Codes(attr));
        attr.AttributeChoices["attr_node"] = Dialoid.Core.Character.Attribute.Str;
        Assert.Empty(Codes(attr));
    }

    [Fact]
    public void Ascendancy_rules()
    {
        var b = TestBuilds.Warrior(level: 19);
        b.AscendancyId = "test_asc";
        Assert.Contains("ascendancy_locked", Codes(b));

        b.Level = 20;
        Assert.Empty(Codes(b));

        b.AscendancyId = "nope";
        Assert.Contains("ascendancy_wrong_class", Codes(b));

        var mage = new Build { ClassId = "test_mage", Level = 20, AscendancyId = "test_asc" };
        Assert.Contains("ascendancy_wrong_class", Codes(mage));

        b.AscendancyId = "test_asc";
        b.AscendancyNodes.AddRange(new[] { "asc_n1", "asc_n2" }); // 4 points at level 20 -> only 2
        Assert.Contains("too_many_ascendancy_points", Codes(b));
        b.AscendancyNodes.Clear();
        b.AscendancyNodes.Add("asc_ghost");
        Assert.Contains("unknown_ascendancy_node", Codes(b));
    }

    /// <summary>Tiny data plus a chain of 12 extra small passives off the warrior start, so L10 can be overspent.</summary>
    private static GameData LoadWithLongChain()
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, "passives.json");
        var arr = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        for (int i = 1; i <= 12; i++)
            arr.Add(System.Text.Json.Nodes.JsonNode.Parse(
                $"{{\"id\":\"chain_{i}\",\"name\":\"Цепь {i}\",\"description\":\"\",\"kind\":\"small\",\"region\":\"test\",\"x\":0,\"y\":{i},\"links\":[\"{(i == 1 ? "test_start_warrior" : "chain_" + (i - 1))}\"]}}"));
        File.WriteAllText(path, arr.ToJsonString());
        return DataLoader.Load(dir).Data!;
    }

    [Fact]
    public void Illegal_build_after_level_decrease_reports_all_violations()
    {
        var data = LoadWithLongChain();
        // A fully legal L60 build: 16 passives, 5 slots, ascendancy with its points, loot skills from equipped items.
        var b = TestBuilds.Warrior(60, "test_greataxe", new[] { "test_cloud_ring", "test_bolt_charm", "test_spark_charm" });
        b.PassiveNodes.AddRange(new[] { "small_a", "small_b", "small_c", "attr_node" });
        b.PassiveNodes.AddRange(Enumerable.Range(1, 12).Select(i => "chain_" + i));
        b.AttributeChoices["attr_node"] = Dialoid.Core.Character.Attribute.Dex;
        b.AscendancyId = "test_asc";
        b.AscendancyNodes.AddRange(new[] { "asc_n1", "asc_n2", "asc_n3" });
        b.Slots.AddRange(new[]
        {
            new SkillSlot { SkillId = "test_slash", Level = 20 },
            new SkillSlot { SkillId = "test_slam", Level = 20 },
            new SkillSlot { SkillId = "test_cloud", Level = 12 },
            new SkillSlot { SkillId = "test_bolt", Level = 10 },
            new SkillSlot { SkillId = "test_spark", Level = 10 },
        });
        Assert.Empty(BuildRules.Validate(b, data));

        // Lowering the level makes it illegal in many ways at once, and nothing throws.
        b.Level = 10;
        var codes = BuildRules.Validate(b, data).Select(v => v.Code).ToList();
        Assert.Contains("too_many_passive_points", codes); // 16 nodes, 9 points
        Assert.Contains("slot_locked", codes);             // 5 slots, 3 open
        Assert.Contains("ascendancy_locked", codes);
        Assert.Contains("skill_level_above_cap", codes);   // skill level 20 above character level 10
    }

    [Fact]
    public void Unknown_skill_node_has_its_own_code()
    {
        var b = TestBuilds.Warrior(weapon: "test_sword", slot: new SkillSlot { SkillId = "test_slash", Level = 5, NodePoints = { ["ghost_node"] = 1 } });
        Assert.Contains("unknown_skill_node", Codes(b));
        Assert.DoesNotContain("skill_node_requirement_unmet", Codes(b));
    }

    [Fact]
    public void Violations_have_russian_messages()
    {
        var b = new Build { ClassId = "nope" };
        var v = Assert.Single(BuildRules.Validate(b, Data));
        Assert.Equal("unknown_class", v.Code);
        Assert.Matches("[А-Яа-я]", v.Message);
    }
}
