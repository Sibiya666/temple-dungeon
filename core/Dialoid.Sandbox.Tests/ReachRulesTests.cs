using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;
using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

public class ReachRulesTests
{
    private static TagSet Tags(SandboxSession s, int slot) => new TrainingRoom(s).CompiledTags(slot)!.Value;

    [Fact]
    public void Out_of_reach_melee_is_refused_and_logged()
    {
        var room = new TrainingRoom(Rooms.Crush());
        var cleave = room.CompiledTags(Rooms.CleaveSlot)!.Value;
        Assert.True(cleave.Contains("melee"));
        Assert.False(cleave.Contains("projectile"));

        var far = room.Press(Rooms.CleaveSlot, ReachRules.MeleeTiles + 0.5, true);
        Assert.False(far.Accepted);
        Assert.Equal(PressRefusal.OutOfReach, far.Refusal);
        Assert.Equal("0,0 с · Рассекающий удар · отказ: цель вне досягаемости", room.LogLines[^1]);

        var held = room.StartChannel(Rooms.WhirlwindSlot, 5, true);
        Assert.Equal(PressRefusal.OutOfReach, held.Refusal);
        Rooms.Steps(room, 10);
        Assert.Null(room.Current);

        var near = room.Press(Rooms.CleaveSlot, ReachRules.MeleeTiles, false);
        Assert.True(near.Accepted);
    }

    [Fact]
    public void Projectile_reaches_any_distance_even_off_screen()
    {
        var fireball = Tags(Rooms.Fireball(), Rooms.FireballSlot);
        Assert.True(fireball.Contains("projectile"));
        Assert.Equal(PressRefusal.None, ReachRules.Check(fireball, 100, false));
        // Projectile wins over melee.
        Assert.Equal(PressRefusal.None, ReachRules.Check(TagSet.Of("attack", "melee", "projectile"), 50, false));
        // Shouts and self buffs always go.
        Assert.Equal(PressRefusal.None, ReachRules.Check(TagSet.Of("shout", "buff"), 100, false));

        var room = new TrainingRoom(Rooms.Fireball());
        Assert.True(room.Press(Rooms.FireballSlot, 100, false).Accepted);
    }

    [Fact]
    public void Ground_spell_needs_target_on_screen()
    {
        var s = Rooms.Fireball();
        Assert.True(s.RemoveSkillNodePoint(Rooms.MeteorSlot - 1, "starfall").Ok);
        var meteor = Tags(s, Rooms.MeteorSlot);
        Assert.False(meteor.Contains("projectile"));

        Assert.Equal(PressRefusal.OutOfReach, ReachRules.Check(meteor, 5, false));
        Assert.Equal(PressRefusal.None, ReachRules.Check(meteor, 30, true));
        // Poison fog (loot skill): a ground spell too.
        Assert.Equal(PressRefusal.OutOfReach, ReachRules.Check(TagSet.Of("spell", "area", "poison", "dot"), 1, false));
    }

    [Fact]
    public void Meteor_with_starfall_becomes_projectile_and_ignores_screen()
    {
        var s = Rooms.Fireball();
        Assert.Equal(1, s.GetSlot(Rooms.MeteorSlot - 1)!.NodePoints["starfall"]);
        var meteor = Tags(s, Rooms.MeteorSlot);

        Assert.True(meteor.Contains("projectile"));
        Assert.Equal(PressRefusal.None, ReachRules.Check(meteor, 50, false));
    }

    [Fact]
    public void Shockwave_crush_counts_as_projectile()
    {
        var s = Rooms.Crush();
        var slot = Rooms.CrushSlot - 1;
        Assert.Equal(PressRefusal.OutOfReach, ReachRules.Check(Tags(s, Rooms.CrushSlot), 5, true));

        Assert.True(s.RemoveSkillNodePoint(slot, "deep_tremors").Ok);
        Assert.True(s.RemoveSkillNodePoint(slot, "deep_tremors").Ok);
        Assert.True(s.RemoveSkillNodePoint(slot, "earthquake").Ok);
        Assert.True(s.AddSkillNodePoint(slot, "shockwave").Ok);
        var crush = Tags(s, Rooms.CrushSlot);

        Assert.True(crush.Contains("melee"));
        Assert.True(crush.Contains("projectile"));
        Assert.Equal(PressRefusal.None, ReachRules.Check(crush, 20, false));
    }
}
