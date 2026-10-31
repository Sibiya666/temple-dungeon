using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Explain;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Content;

namespace Dialoid.Core.Tests.Simulation;

/// <summary>Training room plan, task 6: what the combat screen reads from a live fight (slots, timers, queue, the dummy's life).</summary>
public class LiveStateTests
{
    private const double Eps = 1e-9;

    [Fact]
    public void Hero_life_max_exports_current_sim_value_and_survives_slot_remapping()
    {
        var fight = Start("sorcerer_pyromancer_fireball");
        var sim = fight.Sim;
        Assert.True(sim.LifeMax > 0);
        var initial = fight.State;
        Assert.Equal(sim.LifeMax, initial.LifeMax);

        // Distinct from both current life and the initial maximum: export, not a fallback.
        fight.Sim.LifeMax = initial.LifeMax + 137;
        sim.Life = initial.LifeMax / 2;
        var changed = fight.State;
        Assert.Equal(sim.LifeMax, changed.LifeMax);
        Assert.NotEqual(changed.Life, changed.LifeMax);
        Assert.Equal(initial.LifeMax + 137, changed.LifeMax);

        // TrainingRoom.State uses this record-copy path to remap player slots.
        var remapped = changed with
        {
            CooldownLeftBySlot = new Dictionary<int, double>(), QueuedSlot = null, HeldSlot = null,
            CostBySlot = new Dictionary<int, double>(), ResourceShortSlots = Array.Empty<int>(),
        };
        Assert.Equal(changed.LifeMax, remapped.LifeMax);

        fight.Step();
        Assert.Equal(sim.LifeMax, fight.State.LifeMax);
        Assert.Equal(initial.LifeMax, fight.State.LifeMax);
    }

    private const int BattleCry = 1, Charge = 2, Whirlwind = 4;                // warrior_berserker_whirlwind
    private const int Meteor = 2, Fireball = 4, Spark = 5;           // sorcerer_pyromancer_fireball

    private static readonly string[] MainBuilds =
    {
        "warrior_berserker_whirlwind", "warrior_thunderer_crush", "warrior_fire_cleave_crit",
        "sorcerer_pyromancer_fireball", "sorcerer_cryomancer_ice_lance", "sorcerer_spark_hybrid",
    };

    [Fact]
    public void State_is_cached_within_a_step()
    {
        var fight = Start("sorcerer_pyromancer_fireball");
        var first = fight.State;
        Assert.Same(first, fight.State);
        fight.Step();
        var second = fight.State;
        Assert.NotSame(first, second);
        Assert.Same(second, fight.State);
        Assert.Equal(LiveFight.StepSec, second.Time, 9);
    }

    [Fact]
    public void State_cache_is_dropped_by_commands()
    {
        var fight = Start("sorcerer_pyromancer_fireball");
        Assert.True(fight.Press(Fireball).Accepted);
        fight.Step();
        var busy = fight.State;
        Assert.True(busy.Busy);
        Assert.Null(busy.QueuedSlot);
        while (fight.Sim.BusyUntil - fight.State.Time > LiveFight.QueueWindowSec + Eps) fight.Step();

        var before = fight.State;
        Assert.True(fight.Press(Spark).Queued);
        Assert.NotSame(before, fight.State);
        Assert.Equal(Spark, fight.State.QueuedSlot); // no step in between

        fight.StartChannel(Meteor);
        Assert.Equal(Meteor, fight.State.HeldSlot);
        fight.StopChannel();
        Assert.Null(fight.State.HeldSlot);
    }

    [Fact]
    public void State_reports_queue_hold_busy_and_timers()
    {
        // Queue: a press while busy is the queued slot until it fires.
        var pyro = Start("sorcerer_pyromancer_fireball");
        Assert.Equal("mana", pyro.State.ResourceId);
        Assert.True(pyro.Press(Fireball).Accepted);
        Assert.Equal(Fireball, pyro.State.QueuedSlot); // accepted while free: fires at the next step
        pyro.Step();
        Assert.Null(pyro.State.QueuedSlot);
        Assert.Equal(pyro.Sim.BusyUntil - pyro.State.Time, pyro.State.BusyLeft, 9);
        Assert.True(pyro.State.BusyLeft > 0);
        while (pyro.Sim.BusyUntil - pyro.State.Time > LiveFight.QueueWindowSec + Eps) pyro.Step();
        Assert.True(pyro.Press(Spark).Queued);
        Assert.Equal(Spark, pyro.State.QueuedSlot);
        StepUntilFree(pyro);
        pyro.Step();
        Assert.Null(pyro.State.QueuedSlot);

        // Ignite on the target: stacks with a timer.
        var rotation = pyro.Setup.Rotation();
        for (var i = 0; i < 100; i++) pyro.Step(rotation);
        var target = Assert.Single(pyro.State.TargetEffects);
        var ignite = target.Single(e => e.Id == "ignite");
        Assert.Equal(TimedEffectKind.Ailment, ignite.Kind);
        Assert.Equal(pyro.Sim.Targets[0].Stacks("ignite"), ignite.Stacks);
        Assert.True(ignite.Stacks >= 2, $"ignite stacks {ignite.Stacks}");
        Assert.Equal(pyro.Sim.Targets[0].Ailments.Where(a => a.AilmentId == "ignite").Max(a => a.Remaining), ignite.TimeLeft!.Value, 9);
        Assert.Equal(target.Select(e => e.Id).Order(StringComparer.Ordinal), target.Select(e => e.Id));

        // Battle Cry: a buff with a timer; the held Whirlwind channel and the busy time.
        var fight = Start("warrior_berserker_whirlwind");
        Assert.Equal("fury", fight.State.ResourceId);
        Assert.True(fight.Press(BattleCry).Accepted);
        StepUntilFree(fight);
        var cry = fight.State.HeroEffects.Single(e => e.Id == "battle_cry");
        Assert.Equal(TimedEffectKind.Buff, cry.Kind);
        Assert.Equal(1, cry.Stacks);
        Assert.Equal(fight.Sim.Buffs["battle_cry"], cry.TimeLeft!.Value, 9);
        Assert.True(cry.TimeLeft > 0 && double.IsFinite(cry.TimeLeft.Value));

        fight.Sim.Resource = fight.Sim.ResourceMax; // Fury starts empty
        fight.StartChannel(Whirlwind);
        Assert.Equal(Whirlwind, fight.State.HeldSlot);
        fight.Step();
        var s = fight.State;
        Assert.Equal("whirlwind", s.ChannelSkillId);
        Assert.Equal(Whirlwind, s.HeldSlot);
        Assert.True(s.BusyLeft > 0);
        Assert.True(fight.State.HeroEffects.Single(e => e.Id == "battle_cry").TimeLeft < cry.TimeLeft);

        // Endless mechanic buffs carry no timer.
        foreach (var e in s.HeroEffects.Where(e => fight.Sim.Buffs.TryGetValue(e.Id, out var left) && double.IsPositiveInfinity(left)))
            Assert.Null(e.TimeLeft);
    }

    [Fact]
    public void Slot_info_matches_compiled_skill()
    {
        var build = ContentHarness.LoadBuild("sorcerer_pyromancer_fireball");
        var fight = Start(build, new LiveFightConfig("training", SimMode.Average));
        Assert.Equal(fight.Setup.Skills.Count, fight.Slots.Count);
        Assert.Equal(Enumerable.Range(1, fight.Slots.Count), fight.Slots.Select(s => s.Slot));

        var info = fight.Slots[Fireball - 1];
        Assert.Equal("fireball", info.SkillId);
        Assert.Equal(ContentHarness.Data.Skills["fireball"].Name, info.Name);
        Assert.Equal(fight.CompiledTags(Fireball), info.Tags);
        Assert.False(info.IsChannel);

        var sheet = Sheet(build, "fireball");
        Assert.Equal(Line(sheet, "resource_cost"), info.Cost, 9);
        Assert.Equal(0, info.CostPerSec);
        Assert.Equal(0, info.CooldownSec);
        Assert.Equal(1 / Line(sheet, "uses_per_sec"), info.UseTimeSec, 9);

        var warrior = ContentHarness.LoadBuild("warrior_berserker_whirlwind");
        var warriorFight = Start(warrior, new LiveFightConfig("training", SimMode.Average));
        var charge = warriorFight.Slots[Charge - 1];
        Assert.Equal("charge", charge.SkillId);
        Assert.True(charge.CooldownSec > 0);
        Assert.Equal(Line(Sheet(warrior, "charge"), "cooldown"), charge.CooldownSec, 9);

        var whirl = warriorFight.Slots[Whirlwind - 1];
        Assert.True(whirl.IsChannel);
        Assert.Equal(Line(Sheet(warrior, "whirlwind"), "resource_cost"), whirl.CostPerSec, 9);
        Assert.True(whirl.Cost > 0 && whirl.Cost < whirl.CostPerSec, $"tick {whirl.Cost}, per second {whirl.CostPerSec}, use {whirl.UseTimeSec}"); // one tick pays cost × interval
    }

    [Fact]
    public void Training_dummy_life_drops_and_refills_in_live_fight()
    {
        var data = ContentHarness.Data;
        var build = ContentHarness.LoadBuild("sorcerer_pyromancer_fireball");
        Assert.True(data.Dummies["training"].Immortal);
        var expectedMax = data.Dummies["boss"].LifePerTarget * Growth.Gear(build.Level);

        var fight = Start(build, new LiveFightConfig("training", SimMode.Average));
        var rotation = fight.Setup.Rotation();
        Assert.Equal(expectedMax, Assert.Single(fight.State.TargetLifeMax), 9);
        Assert.Equal(expectedMax, fight.State.TargetLife[0], 9);

        double previous = expectedMax, lowest = expectedMax;
        var refilled = false;
        for (var i = 0; i < (int)(300 / LiveFight.StepSec) && !refilled; i++)
        {
            fight.Step(rotation);
            var life = fight.State.TargetLife[0];
            Assert.InRange(life, 0, expectedMax);
            Assert.Equal(expectedMax, fight.State.TargetLifeMax[0], 9);
            if (life > previous + Eps) refilled = true;
            lowest = Math.Min(lowest, life);
            previous = life;
        }
        Assert.True(refilled, $"life never refilled; lowest {lowest} of {expectedMax}");
        Assert.True(lowest < expectedMax * 0.1, $"lowest life {lowest} of {expectedMax}");
        var snapshot = fight.Snapshot();
        Assert.Null(snapshot.TimeToKill);
        Assert.DoesNotContain(snapshot.Log, e => e.Kind == "kill");
        Assert.False(fight.Sim.Dead[0]);

        // Without the room's infinite life, and in the simulator, the preset stays immortal as in the data.
        var mortalRules = Start(build, new LiveFightConfig("training", SimMode.Average, InfiniteLife: false));
        Assert.True(double.IsPositiveInfinity(mortalRules.State.TargetLifeMax[0]));
        var sim = new Simulator(data, MechanicRegistry.Default).Run(build, new SimConfig("training", SimMode.Average));
        Assert.Null(sim.TimeToKill);
        Assert.DoesNotContain(sim.Log, e => e.Kind == "kill");

        // A preset with finite life keeps its own life in the room.
        var boss = Start(build, new LiveFightConfig("boss", SimMode.Average));
        Assert.Equal(expectedMax, boss.State.TargetLifeMax[0], 9);
        var swarm = Start(build, new LiveFightConfig("swarm", SimMode.Average));
        Assert.Equal(5, swarm.State.TargetLifeMax.Count);
        Assert.All(swarm.State.TargetLifeMax, m => Assert.Equal(data.Dummies["swarm"].LifePerTarget * Growth.Gear(build.Level), m, 9));
        Assert.Equal(5, swarm.State.TargetEffects.Count);
    }

    [Fact]
    public void Only_listed_mechanic_counters_are_shown_as_stacks()
    {
        var sawCharges = false;
        foreach (var path in MainBuilds)
        {
            var fight = Start(ContentHarness.LoadBuild(path), new LiveFightConfig("training", SimMode.Roll));
            var rotation = fight.Setup.Rotation();
            for (var i = 0; i < 600; i++)
            {
                fight.Step(rotation);
                foreach (var e in fight.State.HeroEffects.Where(e => e.Kind == TimedEffectKind.MechanicStack))
                {
                    Assert.Contains(e.Id, LiveFight.ShownCounters);
                    Assert.Null(e.TimeLeft);
                    Assert.True(e.Stacks > 0, $"{path}: {e.Id} {e.Stacks}");
                    Assert.Equal(fight.Sim.MechanicCounters[e.Id], e.Stacks);
                    sawCharges |= e.Id == "static_charge:charges";
                }
            }
            // Bookkeeping counters (soul_heat's use counter, static_charge's flags) exist but are never shown.
            Assert.DoesNotContain(fight.State.HeroEffects, e => e.Id.StartsWith("soul_heat", StringComparison.Ordinal));
        }
        Assert.True(sawCharges, "the thunderer's static charges were never shown");
    }

    private static IReadOnlyList<NumberLine> Sheet(Build build, string skillId)
    {
        var data = ContentHarness.Data;
        var ch = new CharacterCompiler(data, MechanicRegistry.Default).Compile(build);
        return SkillSheet.Describe(ch, ch.Skills.Single(s => s.SkillId == skillId), data,
            build.WeaponId != null ? data.Weapons[build.WeaponId] : null);
    }

    [Fact]
    public void Shown_counters_cannot_be_changed_and_target_not_visible_is_the_last_refusal()
    {
        Assert.False(LiveFight.ShownCounters is string[]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)LiveFight.ShownCounters)[0] = "x");
        Assert.Equal(PressRefusal.TargetNotVisible, Enum.GetValues<PressRefusal>()[^1]);
    }

    private static double Line(IReadOnlyList<NumberLine> lines, string key) => lines.Single(l => l.Key == key).Value;

    private static LiveFight Start(string buildPath) =>
        Start(ContentHarness.LoadBuild(buildPath), new LiveFightConfig("training", SimMode.Average));

    private static LiveFight Start(Build build, LiveFightConfig config)
    {
        var fight = LiveFight.Start(ContentHarness.Data, MechanicRegistry.Default, build, config, out var violations);
        Assert.True(fight != null, string.Join("; ", violations.Select(v => v.Message)));
        return fight;
    }

    private static void StepUntilFree(LiveFight fight)
    {
        var i = 0;
        do fight.Step(); while (fight.State.Busy && ++i < 200);
        Assert.False(fight.State.Busy);
    }
}
