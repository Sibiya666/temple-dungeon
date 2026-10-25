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

    [Fact]
    public void Illegal_build_after_level_decrease_reports_all_violations()
    {
        // A fully legal L60 build: 5 slots, ascendancy with its points, passives, loot skills from equipped items.
        var b = TestBuilds.Warrior(60, "test_greataxe", new[] { "test_cloud_ring", "test_bolt_charm", "test_spark_charm" });
        b.PassiveNodes.AddRange(new[] { "small_a", "small_b", "small_c", "attr_node" });
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
        Assert.Empty(Codes(b));

        // Lowering the level makes it illegal in many ways at once, and nothing throws.
        // (The tiny passive tree has only a few nodes, so we drop to level 2 instead of 10 to overspend passive points.)
        b.Level = 2;
        var codes = Codes(b);
        Assert.Contains("too_many_passive_points", codes);
        Assert.Contains("slot_locked", codes);
        Assert.Contains("ascendancy_locked", codes);
        Assert.Contains("skill_level_above_cap", codes);
        Assert.Contains("skill_locked_by_level", codes);

        // At level 10 the slots, ascendancy and skill levels are already illegal.
        b.Level = 10;
        codes = Codes(b);
        Assert.Contains("slot_locked", codes);
        Assert.Contains("ascendancy_locked", codes);
        Assert.Contains("skill_level_above_cap", codes);
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
