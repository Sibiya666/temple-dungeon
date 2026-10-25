using Dialoid.Core.Character;
using Dialoid.Core.Effects;
using Dialoid.Core.Stats;
using static Dialoid.Core.Tests.Mechanics.MechanicTestData;

namespace Dialoid.Core.Tests.Mechanics;

/// <summary>The IMechanic hooks are called by the compiler and the simulator with working contexts.</summary>
public class MechanicHookTests
{
    private sealed class Recorder : IMechanic
    {
        public string Id => "test_recorder";
        public int CharacterPhase, SkillPhase, Uses, Hits, Kills, Ticks;
        public readonly List<string> SkillPhaseIds = new();
        public readonly List<string?> UseSkills = new();

        public void OnCompile(MechanicCompileContext ctx)
        {
            if (ctx.Skill == null)
            {
                CharacterPhase++;
                ctx.AddCharacterModifier(ctx.Mod("life", ModKind.Flat, ctx.Number("life", 0)));
            }
            else
            {
                SkillPhase++;
                SkillPhaseIds.Add(ctx.Skill.SkillId);
            }
        }

        public void OnUse(MechanicSimContext ctx)
        {
            Uses++;
            UseSkills.Add(ctx.SkillId);
            ctx.SetCounter("uses", ctx.Counter("uses") + 1);
            ctx.DamageSelf(ctx.Number("self_damage", 0));
        }

        public void OnHit(MechanicSimContext ctx)
        {
            Assert.NotNull(ctx.Hit);
            Assert.True(ctx.TargetIndex >= 0);
            Hits++;
        }

        public void OnKill(MechanicSimContext ctx)
        {
            Assert.True(ctx.TargetIndex >= 0);
            Kills++;
        }

        public void OnTick(MechanicSimContext ctx, double dt)
        {
            Assert.Equal(0.05, dt, 9);
            Ticks++;
            if (Ticks == 200) Assert.Equal(Uses, ctx.Counter("uses"));
        }
    }

    [Fact]
    public void Global_mechanic_gets_every_hook_with_its_params()
    {
        var rec = new Recorder();
        var reg = new MechanicRegistry();
        reg.Register(rec);
        var d = With("{\"type\":\"mechanic\",\"id\":\"test_recorder\",\"params\":{\"life\":50,\"self_damage\":5}}", registry: reg, kind: "small");

        double life = 0, lifeMax = 0;
        var b = Make("test_warrior", 2, true, "test_sword", "test_slash");
        var r = Run(d, b, "test_swarm", duration: 10, registry: reg, observe: s => { life = s.Life; lifeMax = s.LifeMax; });

        Assert.Equal(1, rec.CharacterPhase); // compiled once by the simulator
        Assert.Equal(new[] { "test_slash" }, rec.SkillPhaseIds);
        Assert.Equal((122 + 50) * 1.2, lifeMax, 9); // the character-phase modifier reached the life stat
        var uses = Count(r, "use");
        Assert.True(uses > 0);
        Assert.Equal(uses, rec.Uses);
        Assert.All(rec.UseSkills, s => Assert.Equal("test_slash", s));
        Assert.Equal(Count(r, "hit"), rec.Hits);
        Assert.True(rec.Kills > 0);
        Assert.Equal(Count(r, "kill"), rec.Kills);
        Assert.Equal(200, rec.Ticks);
        Assert.Equal(lifeMax - 5 * uses, life, 9); // DamageSelf
    }

    [Fact]
    public void Scoped_mechanic_fires_only_for_its_skill_and_never_twice()
    {
        var rec = new Recorder();
        var reg = new MechanicRegistry();
        reg.Register(rec);
        var d = With("{\"type\":\"mechanic\",\"id\":\"test_recorder\",\"scope\":\"skill:test_slash\"}", registry: reg, kind: "small");

        var b = Make("test_warrior", 4, true, "test_greataxe", "test_slam", "test_slash");
        var r = Run(d, b, "test_training", duration: 10, registry: reg);

        Assert.Equal(0, rec.CharacterPhase);
        Assert.Equal(new[] { "test_slash" }, rec.SkillPhaseIds);
        Assert.True(Count(r, "use", "test_slam") > 0);
        Assert.Equal(Count(r, "use", "test_slash"), rec.Uses);
        Assert.All(rec.UseSkills, s => Assert.Equal("test_slash", s));
        Assert.Equal(200, rec.Ticks); // one tick per step for the one skill carrying it
    }
}
