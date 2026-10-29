using Dialoid.Core.Character;
using Dialoid.Sandbox;
using Attribute = Dialoid.Core.Character.Attribute;

namespace Dialoid.Sandbox.Tests;

public class SessionPassiveTests
{
    // Warrior start → will_attribute_1 (attribute) → will_sacrifice_1 (cooldown) → will_sacrifice_2 (life).
    private const string WillAttr = "will_attribute_1";
    private const string Sacrifice1 = "will_sacrifice_1";
    private const string LifeNode = "will_sacrifice_2";
    private const string FarNode = "will_wellspring";

    private static SandboxSession NewWarrior(int level = 10)
    {
        var s = SandboxSession.Create(TestData.Data, "warrior");
        if (level != 1) Assert.True(s.SetLevel(level).Ok);
        return s;
    }

    private static double Line(SandboxSession s, string key) => s.CharacterLines.Single(l => l.Key == key).Value;

    [Fact]
    public void New_session_has_only_class_start_and_zero_spent()
    {
        var s = SandboxSession.Create(TestData.Data, "warrior");
        Assert.Equal("warrior", s.Build.ClassId);
        Assert.Equal(1, s.Build.Level);
        Assert.Empty(s.Build.PassiveNodes);
        Assert.Null(s.Build.AscendancyId);
        Assert.Equal(0, s.PassivePointsTotal);
        Assert.Equal(0, s.PassivePointsSpent);
        Assert.Equal(0, s.AscendancyPointsSpent);
        Assert.Empty(s.Violations);
        Assert.NotNull(s.Compiled);
        Assert.Contains(WillAttr, s.AllocatablePassives);
        Assert.DoesNotContain(LifeNode, s.AllocatablePassives);
    }

    [Fact]
    public void Build_is_a_copy()
    {
        var s = NewWarrior();
        s.Build.PassiveNodes.Add(FarNode);
        s.Build.Level = 55;
        Assert.Empty(s.Build.PassiveNodes);
        Assert.Equal(10, s.Build.Level);
    }

    [Fact]
    public void Unknown_class_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SandboxSession.Create(TestData.Data, "nobody"));
        var s = NewWarrior();
        var rev = s.Revision;
        var r = s.SetClass("nobody");
        Assert.False(r.Ok);
        Assert.Equal(rev, s.Revision);
    }

    [Fact]
    public void Toggle_unreachable_node_fails_in_russian_and_keeps_revision()
    {
        var s = NewWarrior();
        var rev = s.Revision;
        var fired = 0;
        s.Changed += () => fired++;

        var r = s.TogglePassive(FarNode);

        Assert.False(r.Ok);
        Assert.Contains(TestData.Data.Passives.Nodes[FarNode].Name, r.Error);
        Assert.Matches("[а-яА-Я]", r.Error!);
        Assert.Equal(rev, s.Revision);
        Assert.Equal(0, fired);
        Assert.Empty(s.Build.PassiveNodes);
    }

    [Fact]
    public void Toggle_reachable_node_allocates_and_bumps_revision()
    {
        var s = NewWarrior();
        var rev = s.Revision;
        var fired = 0;
        s.Changed += () => fired++;

        var r = s.TogglePassive(WillAttr);

        Assert.True(r.Ok, r.Error);
        Assert.Null(r.Error);
        Assert.Equal(rev + 1, s.Revision);
        Assert.Equal(1, fired);
        Assert.Equal(new[] { WillAttr }, s.Build.PassiveNodes);
        Assert.Equal(1, s.PassivePointsSpent);
        Assert.Equal(9, s.PassivePointsTotal);
        Assert.Contains(Sacrifice1, s.AllocatablePassives);
        Assert.DoesNotContain(WillAttr, s.AllocatablePassives);
        // An attribute node gets the class primary by default, so the build stays legal and compilable.
        Assert.Equal(Attribute.Str, s.Build.AttributeChoices[WillAttr]);
        Assert.Empty(s.Violations);

        Assert.True(s.TogglePassive(WillAttr).Ok);
        Assert.Empty(s.Build.PassiveNodes);
        Assert.Empty(s.Build.AttributeChoices);
        Assert.Equal(rev + 2, s.Revision);
    }

    [Fact]
    public void Attribute_choice_changes_attributes()
    {
        var s = NewWarrior();
        Assert.False(s.SetAttributeChoice(WillAttr, Attribute.Will).Ok);
        Assert.True(s.TogglePassive(WillAttr).Ok);
        Assert.False(s.SetAttributeChoice(Sacrifice1, Attribute.Will).Ok);
        var will = Line(s, "will");

        var r = s.SetAttributeChoice(WillAttr, Attribute.Will);

        Assert.True(r.Ok, r.Error);
        Assert.Equal(Attribute.Will, s.Build.AttributeChoices[WillAttr]);
        Assert.Equal(will + 5, Line(s, "will"));
    }

    [Fact]
    public void Removing_middle_of_path_is_refused()
    {
        var s = NewWarrior();
        Assert.True(s.TogglePassive(WillAttr).Ok);
        Assert.True(s.TogglePassive(Sacrifice1).Ok);
        Assert.True(s.TogglePassive(LifeNode).Ok);
        var rev = s.Revision;

        var r = s.TogglePassive(Sacrifice1);

        Assert.False(r.Ok);
        Assert.Contains(TestData.Data.Passives.Nodes[Sacrifice1].Name, r.Error);
        Assert.Equal(rev, s.Revision);
        Assert.Equal(3, s.Build.PassiveNodes.Count);

        Assert.False(s.TogglePassive("start_warrior").Ok);
        Assert.False(s.TogglePassive("no_such_node").Ok);
    }

    [Fact]
    public void Lowering_level_keeps_nodes_and_reports_points_violation()
    {
        var s = NewWarrior(4);
        Assert.True(s.TogglePassive(WillAttr).Ok);
        Assert.True(s.TogglePassive(Sacrifice1).Ok);
        Assert.True(s.TogglePassive(LifeNode).Ok);
        Assert.Empty(s.Violations);

        Assert.True(s.SetLevel(2).Ok);

        Assert.Equal(3, s.Build.PassiveNodes.Count);
        Assert.Equal(1, s.PassivePointsTotal);
        Assert.Equal(3, s.PassivePointsSpent);
        Assert.Contains(s.Violations, v => v.Code == "too_many_passive_points");
        Assert.NotNull(s.Compiled);

        Assert.False(s.SetLevel(0).Ok);
        Assert.False(s.SetLevel(BuildRules.MaxLevel + 1).Ok);
        Assert.True(s.SetLevel(BuildRules.MaxLevel).Ok);
    }

    [Fact]
    public void SetClass_resets_build()
    {
        var s = NewWarrior(30);
        Assert.True(s.TogglePassive(WillAttr).Ok);
        Assert.True(s.SetAscendancy("berserker").Ok);
        var rev = s.Revision;

        var r = s.SetClass("sorcerer");

        Assert.True(r.Ok, r.Error);
        Assert.Equal(rev + 1, s.Revision);
        var b = s.Build;
        Assert.Equal("sorcerer", b.ClassId);
        Assert.Empty(b.PassiveNodes);
        Assert.Empty(b.AttributeChoices);
        Assert.Null(b.AscendancyId);
        Assert.Empty(b.AscendancyNodes);
        Assert.Equal(0, s.PassivePointsSpent);
        Assert.Contains("will_attribute_2", s.AllocatablePassives);
        Assert.DoesNotContain(WillAttr, s.AllocatablePassives);
    }

    [Fact]
    public void Ascendancy_needs_level_20()
    {
        var s = NewWarrior(19);
        Assert.Equal(0, s.AscendancyPointsTotal);
        var at19 = s.SetAscendancy("berserker");
        Assert.True(!at19.Ok || s.Violations.Any(v => v.Code == "ascendancy_locked"));

        Assert.True(s.SetLevel(20).Ok);
        Assert.True(s.SetAscendancy("berserker").Ok);
        Assert.Equal(2, s.AscendancyPointsTotal);
        Assert.DoesNotContain(s.Violations, v => v.Code == "ascendancy_locked");

        var r = s.ToggleAscendancyNode("insatiable");
        Assert.True(r.Ok, r.Error);
        Assert.Equal(2, s.AscendancyPointsSpent);
        Assert.Empty(s.Violations);

        Assert.False(s.SetAscendancy("pyromancer").Ok);
        Assert.False(s.ToggleAscendancyNode("shatter").Ok);
        Assert.True(s.ToggleAscendancyNode("insatiable").Ok);
        Assert.Equal(0, s.AscendancyPointsSpent);
    }

    [Fact]
    public void Changing_ascendancy_clears_its_nodes()
    {
        var s = NewWarrior(30);
        Assert.True(s.SetAscendancy("berserker").Ok);
        Assert.True(s.ToggleAscendancyNode("insatiable").Ok);
        Assert.Single(s.Build.AscendancyNodes);

        Assert.True(s.SetAscendancy("thunderer").Ok);
        Assert.Equal("thunderer", s.Build.AscendancyId);
        Assert.Empty(s.Build.AscendancyNodes);

        Assert.True(s.ToggleAscendancyNode("discharge").Ok);
        Assert.True(s.SetAscendancy(null).Ok);
        Assert.Null(s.Build.AscendancyId);
        Assert.Empty(s.Build.AscendancyNodes);
        Assert.False(s.ToggleAscendancyNode("discharge").Ok);
    }

    [Fact]
    public void Character_lines_follow_revision()
    {
        var s = NewWarrior();
        Assert.True(s.TogglePassive(WillAttr).Ok);
        Assert.True(s.TogglePassive(Sacrifice1).Ok);
        var lines = s.CharacterLines;
        Assert.Same(lines, s.CharacterLines);
        var life = Line(s, "life");

        Assert.True(s.TogglePassive(LifeNode).Ok);

        Assert.NotSame(lines, s.CharacterLines);
        Assert.True(Line(s, "life") > life);
    }
}
