using Dialoid.Core.Builds;
using Dialoid.Core.Character;
using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

public class ImportExportTests
{
    /// <summary>A small legal warrior build edited through the session.</summary>
    private static SandboxSession Sample()
    {
        var s = SandboxSession.Create(TestData.Data, "warrior");
        Assert.True(s.SetLevel(20).Ok);
        Assert.True(s.TogglePassive("will_attribute_1").Ok);
        Assert.True(s.SetWeapon("greataxe").Ok);
        Assert.True(s.SetSlotSkill(0, "cleave").Ok);
        Assert.True(s.AddSkillNodePoint(0, "heavy_hand").Ok);
        Assert.True(s.SetSlotSkill(1, "crush").Ok);
        Assert.True(s.SetRotation(new[] { "cleave", "crush" }).Ok);
        Assert.Empty(s.Violations);
        return s;
    }

    private static void AssertImportRefused(string text)
    {
        var s = Sample();
        var before = BuildCodec.ToJson(s.Build);
        var rev = s.Revision;
        var fired = 0;
        s.Changed += () => fired++;

        var r = s.Import(text);

        Assert.False(r.Ok);
        Assert.Matches("[а-яА-Я]", r.Error!);
        Assert.Equal(rev, s.Revision);
        Assert.Equal(0, fired);
        Assert.Equal(before, BuildCodec.ToJson(s.Build));
        Assert.Equal("cleave", s.GetSlot(0)!.SkillId);
    }

    [Fact]
    public void Empty_string_is_refused() => AssertImportRefused("");

    [Fact]
    public void Whitespace_is_refused() => AssertImportRefused("   \n");

    [Fact]
    public void Truncated_code_is_refused()
    {
        var code = Sample().ExportCode();
        AssertImportRefused(code[..(code.Length / 2)]);
        AssertImportRefused(BuildCodec.Prefix);
    }

    [Fact]
    public void Arbitrary_json_is_refused() => AssertImportRefused("{\"foo\":1}");

    [Fact]
    public void Broken_json_is_refused() => AssertImportRefused("{\"class_id\": \"warrior\", ");

    [Fact]
    public void Foreign_class_skill_without_item_is_refused()
    {
        var b = Sample().Build;
        b.ClassId = "sorcerer";
        b.PassiveNodes.Clear();
        b.AttributeChoices.Clear();
        AssertImportRefused(BuildCodec.ToJson(b));
        AssertImportRefused(BuildCodec.Export(b));
    }

    [Fact]
    public void Unknown_skill_node_is_refused()
    {
        var b = Sample().Build;
        b.Slots[0].NodePoints["no_such_node"] = 1;
        AssertImportRefused(BuildCodec.ToJson(b));
    }

    [Fact]
    public void Too_many_slots_are_refused()
    {
        var b = Sample().Build;
        foreach (var id in new[] { "battle_cry", "whirlwind", "charge", "throwing_axe" })
            b.Slots.Add(new SkillSlot { SkillId = id });
        b.Level = 60;
        AssertImportRefused(BuildCodec.ToJson(b));
    }

    [Fact]
    public void Export_then_import_round_trips()
    {
        var source = Sample();
        var json = BuildCodec.ToJson(source.Build);

        var byCode = SandboxSession.Create(TestData.Data, "sorcerer");
        var rev = byCode.Revision;
        Assert.True(byCode.Import(source.ExportCode()).Ok);
        Assert.Equal(rev + 1, byCode.Revision);
        Assert.Equal(json, BuildCodec.ToJson(byCode.Build));
        Assert.Equal("crush", byCode.GetSlot(1)!.SkillId);
        Assert.Empty(byCode.Violations);

        var byJson = SandboxSession.Create(TestData.Data, "sorcerer");
        Assert.True(byJson.Import(json).Ok);
        Assert.Equal(json, BuildCodec.ToJson(byJson.Build));
        Assert.Equal(source.CharacterLines.Count, byJson.CharacterLines.Count);
    }

    [Fact]
    public void Over_budget_build_imports_with_violations()
    {
        var b = Sample().Build;
        b.Level = 1; // the session itself can reach this state by lowering the level
        var s = SandboxSession.Create(TestData.Data, "warrior");
        Assert.True(s.Import(BuildCodec.ToJson(b)).Ok);
        Assert.NotEmpty(s.Violations);
    }

    [Fact]
    public void Foreign_ascendancy_below_unlock_level_is_refused()
    {
        var b = Sample().Build;
        b.Level = 10;
        b.AscendancyId = "pyromancer";
        b.AscendancyNodes.Add("spread_ignite");
        AssertImportRefused(BuildCodec.ToJson(b));

        b.AscendancyId = "berserker";
        b.AscendancyNodes.Clear();
        b.AscendancyNodes.Add("spread_ignite"); // pyromancer's node under the warrior's ascendancy
        AssertImportRefused(BuildCodec.ToJson(b));
    }

    [Fact]
    public void Own_ascendancy_below_unlock_level_imports_with_violation()
    {
        var b = Sample().Build;
        b.Level = 10;
        b.AscendancyId = "berserker";
        b.AscendancyNodes.Add("insatiable");
        var s = SandboxSession.Create(TestData.Data, "warrior");
        Assert.True(s.Import(BuildCodec.ToJson(b)).Ok);
        Assert.Contains(s.Violations, v => v.Code == "ascendancy_locked");
        Assert.NotNull(s.Compiled);
    }

    [Fact]
    public void Skill_level_outside_native_cap_is_refused()
    {
        var b = Sample().Build;
        b.Slots[0].Level = BuildRules.NativeSkillCap + 1;
        AssertImportRefused(BuildCodec.ToJson(b));
        b.Slots[0].Level = 0;
        AssertImportRefused(BuildCodec.ToJson(b));
    }

    [Fact]
    public void Slot_positions_survive_export_import_after_level_drop()
    {
        var s = SandboxSession.Create(TestData.Data, "warrior");
        Assert.True(s.SetLevel(20).Ok);
        Assert.True(s.SetSlotSkill(0, "cleave").Ok);
        Assert.True(s.SetSlotSkill(4, "crush").Ok);
        Assert.True(s.SetLevel(4).Ok);

        Assert.Equal("cleave", s.GetSlot(0)!.SkillId);
        Assert.Equal("crush", s.GetSlot(1)!.SkillId);
        Assert.Null(s.GetSlot(4));
        Assert.DoesNotContain(s.Violations, v => v.Code == "slot_locked");

        var copy = SandboxSession.Create(TestData.Data, "sorcerer");
        Assert.True(copy.Import(s.ExportCode()).Ok);
        for (var i = 0; i < SandboxSession.SlotCount; i++)
            Assert.Equal(s.GetSlot(i)?.SkillId, copy.GetSlot(i)?.SkillId);
    }
}
