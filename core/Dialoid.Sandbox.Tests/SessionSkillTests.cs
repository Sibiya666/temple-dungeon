using Dialoid.Core.Character;
using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

public class SessionSkillTests
{
    private static SandboxSession NewSorcerer(int level = 60)
    {
        var s = SandboxSession.Create(TestData.Data, "sorcerer");
        if (level != 1) Assert.True(s.SetLevel(level).Ok);
        return s;
    }

    private static void AssertRefused(SandboxSession s, CommandResult r, int revision)
    {
        Assert.False(r.Ok);
        Assert.Matches("[а-яА-Я]", r.Error!);
        Assert.Equal(revision, s.Revision);
    }

    [Fact]
    public void Slot_locked_by_level_is_refused()
    {
        var s = NewSorcerer(level: 1);
        Assert.Equal(1, s.SlotsOpen);
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        var rev = s.Revision;

        AssertRefused(s, s.SetSlotSkill(1, "spark"), rev);
        AssertRefused(s, s.SetSlotSkill(SandboxSession.SlotCount, "spark"), rev);
        Assert.Single(s.Build.Slots);

        Assert.True(s.SetLevel(4).Ok);
        Assert.True(s.SetSlotSkill(1, "spark").Ok);
        Assert.Equal(new[] { "fireball", "spark" }, s.Build.Slots.Select(x => x.SkillId));
    }

    [Fact]
    public void Same_skill_in_two_slots_is_refused()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        var rev = s.Revision;
        AssertRefused(s, s.SetSlotSkill(1, "fireball"), rev);
    }

    [Fact]
    public void Skill_node_point_respects_requirements_and_exclusive_group()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        Assert.True(s.SetSkillLevel(0, 20).Ok);
        var rev = s.Revision;

        // frost_orb needs 2 points in heat.
        var r = s.AddSkillNodePoint(0, "frost_orb");
        AssertRefused(s, r, rev);
        Assert.Contains("Морозный шар", r.Error);

        Assert.True(s.AddSkillNodePoint(0, "heat").Ok);
        Assert.True(s.AddSkillNodePoint(0, "heat").Ok);
        Assert.True(s.AddSkillNodePoint(0, "frost_orb").Ok);

        // storm_orb shares fireball_element with frost_orb.
        rev = s.Revision;
        AssertRefused(s, s.AddSkillNodePoint(0, "storm_orb"), rev);
        // frost_orb has max 1 point; unknown node is refused.
        AssertRefused(s, s.AddSkillNodePoint(0, "frost_orb"), rev);
        AssertRefused(s, s.AddSkillNodePoint(0, "nope"), rev);

        Assert.Equal(new Dictionary<string, int> { ["heat"] = 2, ["frost_orb"] = 1 }, s.Build.Slots[0].NodePoints);
        Assert.Equal(20, s.SkillPointsTotal(0));
        Assert.Equal(3, s.SkillPointsSpent(0));
        Assert.Empty(s.Violations);
    }

    [Fact]
    public void Skill_points_are_limited_by_skill_level()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        Assert.True(s.SetSkillLevel(0, 1).Ok);
        Assert.True(s.AddSkillNodePoint(0, "heat").Ok);
        var rev = s.Revision;
        AssertRefused(s, s.AddSkillNodePoint(0, "heat"), rev);
    }

    [Fact]
    public void Removing_required_node_point_is_refused()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        Assert.True(s.SetSkillLevel(0, 20).Ok);
        Assert.True(s.AddSkillNodePoint(0, "heat").Ok);
        Assert.True(s.AddSkillNodePoint(0, "heat").Ok);
        Assert.True(s.AddSkillNodePoint(0, "heat").Ok);
        Assert.True(s.AddSkillNodePoint(0, "frost_orb").Ok);

        // 3 → 2 in heat keeps frost_orb's requirement (2) met.
        Assert.True(s.RemoveSkillNodePoint(0, "heat").Ok);
        var rev = s.Revision;
        var r = s.RemoveSkillNodePoint(0, "heat");
        AssertRefused(s, r, rev);
        Assert.Contains("Морозный шар", r.Error);
        AssertRefused(s, s.RemoveSkillNodePoint(0, "wide_blast"), rev);

        Assert.True(s.RemoveSkillNodePoint(0, "frost_orb").Ok);
        Assert.True(s.RemoveSkillNodePoint(0, "heat").Ok);
        Assert.True(s.RemoveSkillNodePoint(0, "heat").Ok);
        Assert.Empty(s.Build.Slots[0].NodePoints);
    }

    [Fact]
    public void Skill_level_capped_by_item_for_loot_skill_and_refused_without_item()
    {
        var s = NewSorcerer();
        var rev = s.Revision;
        var r = s.SetSlotSkill(0, "poison_cloud");
        AssertRefused(s, r, rev);
        Assert.Contains("Ядовитый туман", r.Error);

        Assert.True(s.ToggleItem("ring_of_mist").Ok);
        Assert.True(s.SetSlotSkill(0, "poison_cloud").Ok);
        var cap = BuildRules.SkillLevelCap(s.Build, "poison_cloud", TestData.Data);
        Assert.Equal(BuildRules.LootSkillBaseCap + TestData.Data.Items["ring_of_mist"].SkillLevelBonus, cap);
        Assert.Equal(cap, s.SkillLevelCap(0));
        Assert.Equal(cap, s.Build.Slots[0].Level);

        rev = s.Revision;
        AssertRefused(s, s.SetSkillLevel(0, cap + 1), rev);
        AssertRefused(s, s.SetSkillLevel(0, 0), rev);
        Assert.True(s.SetSkillLevel(0, cap - 1).Ok);

        // The item cannot be taken off while its skill is slotted.
        rev = s.Revision;
        AssertRefused(s, s.ToggleItem("ring_of_mist"), rev);
        Assert.True(s.SetSlotSkill(0, null).Ok);
        Assert.True(s.ToggleItem("ring_of_mist").Ok);
        Assert.Empty(s.Build.ItemIds);
        Assert.Empty(s.Violations);
    }

    [Fact]
    public void Replacing_skill_then_undo_restores_level_and_nodes()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        Assert.True(s.SetSkillLevel(0, 7).Ok);
        Assert.True(s.AddSkillNodePoint(0, "heat").Ok);
        Assert.True(s.AddSkillNodePoint(0, "mana_focus").Ok);

        Assert.True(s.SetSlotSkill(0, "spark").Ok);
        Assert.Equal("spark", s.Build.Slots[0].SkillId);
        Assert.Empty(s.Build.Slots[0].NodePoints);

        Assert.True(s.UndoSlot(0).Ok);
        var slot = s.Build.Slots[0];
        Assert.Equal("fireball", slot.SkillId);
        Assert.Equal(7, slot.Level);
        Assert.Equal(new Dictionary<string, int> { ["heat"] = 1, ["mana_focus"] = 1 }, slot.NodePoints);

        // Undo again brings back what the undo replaced.
        Assert.True(s.UndoSlot(0).Ok);
        Assert.Equal("spark", s.Build.Slots[0].SkillId);

        var rev = s.Revision;
        AssertRefused(s, s.UndoSlot(1), rev);
    }

    [Fact]
    public void Clearing_a_middle_slot_keeps_other_positions()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        Assert.True(s.SetSlotSkill(1, "spark").Ok);
        Assert.True(s.SetSlotSkill(2, "ice_lance").Ok);
        Assert.True(s.SetSlotSkill(1, null).Ok);

        Assert.Equal("fireball", s.GetSlot(0)!.SkillId);
        Assert.Null(s.GetSlot(1));
        Assert.Equal("ice_lance", s.GetSlot(2)!.SkillId);
        Assert.Equal(new[] { "fireball", "ice_lance" }, s.Build.Slots.Select(x => x.SkillId));
        Assert.Empty(s.SkillLines(1));
        Assert.NotEmpty(s.SkillLines(2));
        Assert.Contains(s.SkillLines(2), l => l.Value > 0);
    }

    [Fact]
    public void Rotation_rejects_unslotted_skill()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        Assert.True(s.SetSlotSkill(1, "spark").Ok);
        var rev = s.Revision;

        var r = s.SetRotation(new[] { "fireball", "ice_lance" });
        AssertRefused(s, r, rev);
        Assert.Contains("Ледяное копье", r.Error);

        Assert.True(s.SetRotation(new[] { "spark", "fireball" }).Ok);
        Assert.Equal(new[] { "spark", "fireball" }, s.Build.Rotation);

        // Taking a skill out of its slot drops it from the rotation.
        Assert.True(s.SetSlotSkill(0, null).Ok);
        Assert.Equal(new[] { "spark" }, s.Build.Rotation);
        Assert.Empty(s.Violations);
    }

    [Fact]
    public void Weapon_and_items_validate_ids()
    {
        var s = SandboxSession.Create(TestData.Data, "warrior");
        var rev = s.Revision;
        AssertRefused(s, s.SetWeapon("laser"), rev);
        AssertRefused(s, s.ToggleItem("nothing"), rev);

        Assert.True(s.SetWeapon("greataxe").Ok);
        Assert.Equal("greataxe", s.Build.WeaponId);
        Assert.True(s.SetWeapon(null).Ok);
        Assert.Null(s.Build.WeaponId);
    }

    [Fact]
    public void SetClass_clears_slots_and_rotation()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        Assert.True(s.SetRotation(new[] { "fireball" }).Ok);
        Assert.True(s.SetClass("warrior").Ok);
        Assert.Empty(s.Build.Slots);
        Assert.Empty(s.Build.Rotation);
        Assert.Empty(s.Violations);
        var rev = s.Revision;
        AssertRefused(s, s.UndoSlot(0), rev);
    }

    [Fact]
    public void Read_only_views_cannot_change_the_session_cache()
    {
        var s = SandboxSession.Create(TestData.Data, "warrior");
        if (s.AllocatablePassives is ISet<string> set) set.Clear();
        Assert.NotEmpty(s.AllocatablePassives);

        Assert.True(s.SetLevel(10).Ok);
        Assert.True(s.SetSlotSkill(0, "cleave").Ok);
        Assert.True(s.SetLevel(1).Ok); // skill level above the character level → violation
        Assert.NotEmpty(s.Violations);
        if (s.Violations is IList<RuleViolation> list && !list.IsReadOnly) list.Clear();
        Assert.NotEmpty(s.Violations);
    }

    [Fact]
    public void Clear_then_undo_then_undo_empties_the_slot_again()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        Assert.True(s.SetSkillLevel(0, 5).Ok);
        Assert.True(s.SetSlotSkill(0, null).Ok);

        Assert.True(s.UndoSlot(0).Ok);
        Assert.Equal("fireball", s.GetSlot(0)!.SkillId);
        Assert.Equal(5, s.GetSlot(0)!.Level);

        var rev = s.Revision;
        var fired = 0;
        s.Changed += () => fired++;
        Assert.True(s.UndoSlot(0).Ok);
        Assert.Null(s.GetSlot(0));
        Assert.Empty(s.Build.Slots);
        Assert.Equal(rev + 1, s.Revision);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Edits_after_undo_do_not_change_undo_memory()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        Assert.True(s.SetSkillLevel(0, 5).Ok);
        Assert.True(s.SetSlotSkill(0, "spark").Ok);
        Assert.True(s.UndoSlot(0).Ok); // fireball back, spark remembered
        Assert.True(s.SetSkillLevel(0, 9).Ok);
        Assert.True(s.AddSkillNodePoint(0, "heat").Ok);

        Assert.True(s.UndoSlot(0).Ok); // spark back, fireball (level 9, heat 1) remembered
        Assert.Equal("spark", s.GetSlot(0)!.SkillId);
        Assert.True(s.UndoSlot(0).Ok);
        Assert.Equal(9, s.GetSlot(0)!.Level);
        Assert.Equal(new Dictionary<string, int> { ["heat"] = 1 }, s.GetSlot(0)!.NodePoints);

        // Editing the live slot leaves the remembered spark untouched, and vice versa.
        Assert.True(s.SetSlotSkill(0, "ice_lance").Ok);
        Assert.True(s.SetSkillLevel(0, 3).Ok);
        Assert.True(s.UndoSlot(0).Ok);
        Assert.Equal("fireball", s.GetSlot(0)!.SkillId);
        Assert.Equal(9, s.GetSlot(0)!.Level);
    }

    [Fact]
    public void Level_drop_compacts_slots_and_keeps_undo_with_them()
    {
        var s = NewSorcerer(level: 20);
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        Assert.True(s.SetSlotSkill(2, "spark").Ok);
        Assert.True(s.SetSlotSkill(4, "ice_lance").Ok);
        Assert.True(s.SetSlotSkill(4, "meteor").Ok);

        Assert.True(s.SetLevel(4).Ok);
        Assert.Equal(new[] { "fireball", "spark", "meteor" },
            Enumerable.Range(0, 3).Select(i => s.GetSlot(i)!.SkillId));
        Assert.Null(s.GetSlot(4));
        Assert.Contains(s.Violations, v => v.Code == "slot_locked"); // 3 skills, 2 open slots

        Assert.True(s.SetLevel(20).Ok);
        Assert.True(s.UndoSlot(2).Ok); // meteor's undo moved with it
        Assert.Equal("ice_lance", s.GetSlot(2)!.SkillId);
        var rev = s.Revision;
        AssertRefused(s, s.UndoSlot(4), rev);
    }

    [Fact]
    public void Rotation_with_null_id_is_refused()
    {
        var s = NewSorcerer();
        Assert.True(s.SetSlotSkill(0, "fireball").Ok);
        var rev = s.Revision;
        AssertRefused(s, s.SetRotation(new[] { "fireball", null! }), rev);
        AssertRefused(s, s.SetRotation(new string[] { null! }), rev);
    }
}
