using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Content;

namespace Dialoid.Core.Tests.Simulation;

/// <summary>Live fight (training room plan, task 2): the shared step engine driven by key presses instead of the rotation.</summary>
public class LiveFightTests
{
    private const double Eps = 1e-9;

    // Slots of the reference builds used below (1-based, as the player sees them).
    private const int BattleCry = 1, Charge = 2, Crush = 3, Whirlwind = 4, ThrowingAxe = 5; // warrior_berserker_whirlwind
    private const int Meteor = 2, BallLightning = 3, Fireball = 4, Spark = 5;                // sorcerer_pyromancer_fireball

    public static IEnumerable<object[]> MainBuildsAndModes() =>
        new[]
        {
            "warrior_berserker_whirlwind", "warrior_thunderer_crush", "warrior_fire_cleave_crit",
            "sorcerer_pyromancer_fireball", "sorcerer_cryomancer_ice_lance", "sorcerer_spark_hybrid",
        }.SelectMany(b => new[] { new object[] { b, SimMode.Average }, new object[] { b, SimMode.Roll } });

    [Theory]
    [MemberData(nameof(MainBuildsAndModes))]
    public void Live_fight_driven_by_rotation_matches_simulator(string buildPath, SimMode mode)
    {
        var build = ContentHarness.LoadBuild(buildPath);
        var sim = new Simulator(ContentHarness.Data, MechanicRegistry.Default);
        foreach (var dummy in ContentHarness.Data.Dummies.Keys.Order(StringComparer.Ordinal))
        {
            var expected = sim.Run(build, new SimConfig(dummy, mode, Seed: 1));
            var fight = Start(build, new LiveFightConfig(dummy, mode, Seed: 1, InfiniteLife: false));
            var rotation = fight.Setup.Rotation();
            var steps = (int)Math.Round(new SimConfig(dummy, mode).DurationSec / LiveFight.StepSec);
            for (var i = 0; i < steps; i++) fight.Step(rotation);
            var actual = fight.Snapshot();

            var what = $"{buildPath} {dummy} {mode}";
            Assert.True(expected.Violations.Count == 0, what);
            Assert.True(expected.DpsSingleTarget == actual.DpsSingleTarget, $"{what}: dps {expected.DpsSingleTarget} vs {actual.DpsSingleTarget}");
            Assert.Equal(expected.DpsPack, actual.DpsPack);
            Assert.Equal(expected.HitDamage, actual.HitDamage);
            Assert.Equal(expected.DotDamage, actual.DotDamage);
            Assert.Equal(expected.TimeToKill, actual.TimeToKill);
            Assert.Equal(expected.TimeToDeath, actual.TimeToDeath);
            Assert.Equal(expected.DamageBySkill, actual.DamageBySkill);
            Assert.Equal(expected.DamageByType, actual.DamageByType);
            Assert.Equal(expected.ResourceSeries, actual.ResourceSeries);
            Assert.Equal(expected.Log, actual.Log);
            Assert.Equal(expected.Log, fight.DrainEvents()); // every event, once
            Assert.Empty(fight.DrainEvents());
        }
    }

    [Fact]
    public void Start_reports_the_simulator_violations()
    {
        var build = ContentHarness.LoadBuild("warrior_berserker_whirlwind");
        Assert.Null(LiveFight.Start(ContentHarness.Data, MechanicRegistry.Default, build, new LiveFightConfig("nope", SimMode.Average), out var v));
        Assert.Contains(v, x => x.Code == "unknown_dummy");
        Assert.Null(LiveFight.Start(ContentHarness.Data, MechanicRegistry.Default, build,
            new LiveFightConfig("training", SimMode.Average, PackSizeOverride: 0), out v));
        Assert.Contains(v, x => x.Code == "invalid_sim_config");

        var fight = LiveFight.Start(ContentHarness.Data, MechanicRegistry.Default, build, new LiveFightConfig("training", SimMode.Roll), out v);
        Assert.NotNull(fight);
        Assert.Empty(v);
    }

    [Fact]
    public void Compiled_tags_follow_the_slots()
    {
        var fight = Start("warrior_berserker_whirlwind");
        Assert.True(fight.CompiledTags(ThrowingAxe)!.Value.Contains("projectile"));
        Assert.True(fight.CompiledTags(Whirlwind)!.Value.Contains("channel"));
        Assert.True(fight.CompiledTags(BattleCry)!.Value.Contains("buff"));
        Assert.Null(fight.CompiledTags(0));
        Assert.Null(fight.CompiledTags(6));
    }

    [Fact]
    public void Press_on_cooldown_is_refused_with_time_left()
    {
        var fight = Start("warrior_berserker_whirlwind");
        var first = fight.Press(Charge);
        Assert.Equal(new PressResult(true, false, PressRefusal.None, null), first);
        StepUntilFree(fight);
        Assert.Contains(fight.DrainEvents(), e => e.Kind == "use" && e.Text == "charge");

        var again = fight.Press(Charge);
        Assert.False(again.Accepted);
        Assert.Equal(PressRefusal.Cooldown, again.Refusal);
        var left = fight.Sim.CooldownReadyAt["charge"] - fight.State.Time;
        Assert.True(left > 1, $"cooldown left {left}");
        Assert.Equal(left, again.CooldownLeft!.Value, 9);
        Assert.Equal(left, fight.State.CooldownLeftBySlot[Charge], 9);
        Assert.False(fight.State.CooldownLeftBySlot.ContainsKey(ThrowingAxe));
    }

    [Fact]
    public void Press_while_busy_within_window_is_queued_and_fires_on_release()
    {
        var fight = Start("sorcerer_pyromancer_fireball");
        Assert.True(fight.Press(Fireball).Accepted);
        fight.Step();
        Assert.True(fight.State.Busy);
        var busyUntil = fight.Sim.BusyUntil;
        while (busyUntil - fight.State.Time > LiveFight.QueueWindowSec + Eps) fight.Step();

        var queued = fight.Press(Spark);
        Assert.Equal(new PressResult(true, true, PressRefusal.None, null), queued);
        StepUntilFree(fight);
        fight.Step();
        var uses = fight.DrainEvents().Where(e => e.Kind == "use").ToList();
        Assert.Equal(new[] { "fireball", "spark" }, uses.Select(e => e.Text));
        // The engine acts at the first step at or after the release.
        var release = Math.Ceiling(busyUntil / LiveFight.StepSec - Eps) * LiveFight.StepSec;
        Assert.Equal(release, uses[1].Time, 9);
    }

    [Fact]
    public void Press_while_busy_beyond_window_is_refused_busy()
    {
        var fight = Start("sorcerer_pyromancer_fireball");
        Assert.True(fight.Press(Meteor).Accepted);
        fight.Step();
        Assert.True(fight.Sim.BusyUntil - fight.State.Time > LiveFight.QueueWindowSec);
        Assert.Equal(new PressResult(false, false, PressRefusal.Busy, null), fight.Press(Spark));
        StepUntilFree(fight);
        fight.Step();
        Assert.Equal(new[] { "meteor" }, fight.DrainEvents().Where(e => e.Kind == "use").Select(e => e.Text));
    }

    [Fact]
    public void Newer_queued_press_replaces_older()
    {
        var fight = Start("sorcerer_pyromancer_fireball");
        Assert.True(fight.Press(Fireball).Accepted);
        fight.Step();
        while (fight.Sim.BusyUntil - fight.State.Time > LiveFight.QueueWindowSec + Eps) fight.Step();
        Assert.True(fight.Press(Spark).Queued);
        Assert.True(fight.Press(BallLightning).Queued);
        StepUntilFree(fight);
        fight.Step();
        StepUntilFree(fight);
        fight.Step();
        Assert.Equal(new[] { "fireball", "ball_lightning" }, fight.DrainEvents().Where(e => e.Kind == "use").Select(e => e.Text));
    }

    [Fact]
    public void Buff_skill_with_active_buff_is_refused()
    {
        var fight = Start("warrior_berserker_whirlwind");
        Assert.True(fight.Press(BattleCry).Accepted);
        StepUntilFree(fight);
        Assert.True(fight.State.Buffs.ContainsKey("battle_cry"));
        Assert.Equal(PressRefusal.BuffActive, fight.Press(BattleCry).Refusal);
    }

    [Fact]
    public void Empty_slot_and_triggered_only_are_refused()
    {
        var fight = Start("warrior_berserker_whirlwind");
        Assert.Equal(new PressResult(false, false, PressRefusal.EmptySlot, null), fight.Press(0));
        Assert.Equal(PressRefusal.EmptySlot, fight.Press(6).Refusal);

        // The build rules keep triggered-only skills out of slots; the fight refuses one that got there anyway.
        var build = ContentHarness.LoadBuild("warrior_berserker_whirlwind");
        build.Slots[ThrowingAxe - 1] = new SkillSlot { SkillId = "returning_axe", Level = 20 };
        var setup = FightSetup.Unchecked(ContentHarness.Data, MechanicRegistry.Default, build, "training");
        var rigged = new LiveFight(setup, new LiveFightConfig("training", SimMode.Average));
        Assert.Equal(PressRefusal.TriggeredOnly, rigged.Press(ThrowingAxe).Refusal);
        rigged.StartChannel(ThrowingAxe);
        for (var i = 0; i < 20; i++) rigged.Step();
        Assert.DoesNotContain(rigged.DrainEvents(), e => e.Kind == "use");
    }

    [Fact]
    public void Hold_refusal_says_why_a_slot_cannot_be_held()
    {
        var fight = Start("warrior_berserker_whirlwind");
        Assert.Equal(PressRefusal.None, fight.HoldRefusal(Whirlwind));
        Assert.Equal(PressRefusal.EmptySlot, fight.HoldRefusal(0));
        Assert.Equal(PressRefusal.EmptySlot, fight.HoldRefusal(6));

        var build = ContentHarness.LoadBuild("warrior_berserker_whirlwind");
        build.Slots[ThrowingAxe - 1] = new SkillSlot { SkillId = "returning_axe", Level = 20 };
        build.WeaponId = null;
        var rigged = new LiveFight(FightSetup.Unchecked(ContentHarness.Data, MechanicRegistry.Default, build, "training"),
            new LiveFightConfig("training", SimMode.Average));
        Assert.Equal(PressRefusal.TriggeredOnly, rigged.HoldRefusal(ThrowingAxe));
        Assert.Equal(PressRefusal.WeaponMismatch, rigged.HoldRefusal(Whirlwind)); // an attack without a weapon
        Assert.Equal(PressRefusal.None, rigged.HoldRefusal(BattleCry));
    }

    [Fact]
    public void Not_enough_resource_is_refused()
    {
        var fight = Start("warrior_berserker_whirlwind"); // Fury starts empty
        Assert.Equal(0, fight.State.Resource);
        Assert.Equal(new PressResult(false, false, PressRefusal.NotEnoughResource, null), fight.Press(Crush));
        fight.Step();
        Assert.Empty(fight.DrainEvents());
    }

    [Fact]
    public void Channel_runs_while_held_and_stops_on_release()
    {
        var fight = Start("warrior_berserker_whirlwind");
        fight.Sim.Resource = fight.Sim.ResourceMax; // Fury starts empty
        fight.StartChannel(Whirlwind);
        for (var i = 0; i < 40; i++) fight.Step(); // 2 s
        Assert.Equal("whirlwind", fight.State.ChannelSkillId);
        var held = fight.DrainEvents().Count(e => e.Kind == "use" && e.Text == "whirlwind");
        Assert.True(held >= 3, $"whirlwind ticks while held: {held}");

        fight.StopChannel();
        StepUntilFree(fight);
        fight.Step();
        Assert.Null(fight.State.ChannelSkillId);
        Assert.False(fight.State.Busy);
        fight.DrainEvents();
        for (var i = 0; i < 40; i++) fight.Step();
        Assert.DoesNotContain(fight.DrainEvents(), e => e.Kind == "use");
    }

    [Fact]
    public void Infinite_life_fight_never_ends()
    {
        var build = ContentHarness.LoadBuild("sorcerer_pyromancer_fireball");
        var mortal = Start(build, new LiveFightConfig("boss", SimMode.Average, InfiniteLife: false));
        var endless = Start(build, new LiveFightConfig("boss", SimMode.Average));
        var mortalRotation = mortal.Setup.Rotation();
        var endlessRotation = endless.Setup.Rotation();
        const int steps = (int)(120 / LiveFight.StepSec);
        for (var i = 0; i < steps; i++)
        {
            mortal.Step(mortalRotation);
            endless.Step(endlessRotation);
        }

        var dead = mortal.Snapshot();
        Assert.NotNull(dead.TimeToKill);
        Assert.Equal(PressRefusal.FightOver, mortal.Press(Spark).Refusal);

        var full = endless.Snapshot();
        Assert.Equal(120, endless.State.Time, 9);
        Assert.Null(full.TimeToKill);
        Assert.DoesNotContain(full.Log, e => e.Kind == "kill");
        Assert.True(full.HitDamage + full.DotDamage > dead.HitDamage + dead.DotDamage); // more than the boss's whole life
        Assert.True(endless.Press(Spark).Refusal is PressRefusal.None or PressRefusal.Busy or PressRefusal.NotEnoughResource);
        Assert.NotEmpty(endless.DrainDamage()); // task 3: every hit and DoT tick of the endless fight is a damage event
    }

    [Fact]
    public void Live_log_keeps_flowing_past_the_simulator_cap_with_bounded_memory()
    {
        // About 6 500 events a minute on the 5-target swarm: the simulator's capped log would stop after 10 000.
        var fight = Start(ContentHarness.LoadBuild("sorcerer_spark_hybrid"), new LiveFightConfig("swarm", SimMode.Roll));
        var rotation = fight.Setup.Rotation();
        var drained = 0;
        for (var i = 0; i < 20_000 && drained <= SimLog.Cap + 2_000; i++)
        {
            fight.Step(rotation);
            var batch = fight.DrainEvents();
            Assert.DoesNotContain(batch, e => e.Kind == "log_truncated");
            drained += batch.Count;
        }
        Assert.True(drained > SimLog.Cap + 2_000, $"events drained: {drained}");

        for (var i = 0; i < 40; i++) fight.Step(rotation); // two more seconds
        var fresh = fight.DrainEvents();
        Assert.Contains(fresh, e => e.Kind == "use");
        Assert.Contains(fresh, e => e.Kind == "hit");
        Assert.True(fight.Snapshot().Log.Count < 2 * LiveFight.LogKeep);

        // Nobody drains for over three minutes: only the most recent events wait, still in order and up to now.
        for (var i = 0; i < 4_000; i++) fight.Step(rotation);
        var backlog = fight.DrainEvents();
        Assert.InRange(backlog.Count, LiveFight.LogKeep, 2 * LiveFight.LogKeep - 1);
        Assert.True(backlog.Zip(backlog.Skip(1)).All(p => p.First.Time <= p.Second.Time));
        Assert.True(backlog[^1].Time > fight.State.Time - 1);
        Assert.True(fight.Snapshot().Log.Count < 2 * LiveFight.LogKeep);
    }

    [Fact]
    public void State_shows_buffs_ailments_and_life()
    {
        var fight = Start("warrior_berserker_whirlwind");
        Assert.True(fight.Press(BattleCry).Accepted); // thunder_roar: shock on the primary target
        StepUntilFree(fight);
        var s = fight.State;
        Assert.True(s.Buffs["battle_cry"] > 0);
        Assert.Contains("shock", s.TargetAilments);
        Assert.True(s.Life > 0);
        Assert.True(s.ResourceMax > 0);
        Assert.Null(fight.Snapshot().TimeToDeath);
    }

    private static readonly string[] MainBuildPaths =
    {
        "warrior_berserker_whirlwind", "warrior_thunderer_crush", "warrior_fire_cleave_crit",
        "sorcerer_pyromancer_fireball", "sorcerer_cryomancer_ice_lance", "sorcerer_spark_hybrid",
    };

    [Fact]
    public void Structured_fields_are_set_on_use_hit_ailment_proc_and_mechanic_events()
    {
        var sim = new Simulator(ContentHarness.Data, MechanicRegistry.Default);
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in MainBuildPaths)
        foreach (var dummy in new[] { "training", "swarm" })
        {
            var log = sim.Run(ContentHarness.LoadBuild(path), new SimConfig(dummy, SimMode.Roll, Seed: 1)).Log;
            SimEvent? lastHit = null;
            foreach (var e in log)
            {
                var what = $"{path} {dummy} {e}";
                kinds.Add(e.Kind);
                switch (e.Kind)
                {
                    case "use":
                        Assert.True(e.SkillId == e.Text, what);
                        Assert.True(e.UseSec > 0, what);
                        break;
                    case "hit":
                        Assert.True(e.SkillId != null && e.Target != null, what);
                        Assert.True(e.Text.StartsWith($"{e.SkillId} -> #{e.Target}:", StringComparison.Ordinal), what);
                        // A hit with no damage part at all (Blink without damage nodes) has no damage event, so no sequence number.
                        Assert.True(e.DamageSeq != null || e.Text.EndsWith(": 0", StringComparison.Ordinal), what);
                        lastHit = e;
                        break;
                    case "crit":
                        Assert.True(e.SkillId != null && e.Target != null, what);
                        Assert.True(lastHit != null && lastHit.DamageSeq == e.DamageSeq && lastHit.Target == e.Target, what);
                        break;
                    case "ailment":
                        Assert.True(e.SkillId != null && e.AilmentId != null && e.Target != null, what);
                        Assert.True(e.Text.StartsWith($"{e.AilmentId} -> #{e.Target} ({e.SkillId}", StringComparison.Ordinal), what);
                        break;
                    case "proc":
                        Assert.True(e.SkillId != null && e.SourceSkillId != null, what);
                        Assert.True(e.Text.StartsWith(e.SourceSkillId + ": ", StringComparison.Ordinal), what);
                        break;
                    case "mechanic":
                        Assert.True(e.MechanicKey != null && e.Text.StartsWith(e.MechanicKey + ": ", StringComparison.Ordinal), what);
                        break;
                    case "kill":
                        Assert.True(e.Target != null && e.Text == $"#{e.Target}", what);
                        break;
                }
            }
        }
        foreach (var kind in new[] { "use", "hit", "crit", "ailment", "proc", "mechanic", "kill" }) Assert.Contains(kind, kinds);
    }

    [Fact]
    public void Every_hit_has_exactly_one_damage_event_with_same_seq()
    {
        foreach (var path in MainBuildPaths)
        foreach (var dummy in new[] { "training", "swarm" })
        {
            var fight = Start(ContentHarness.LoadBuild(path), new LiveFightConfig(dummy, SimMode.Roll));
            var rotation = fight.Setup.Rotation();
            var events = new List<SimEvent>();
            var damage = new List<DamageEvent>();
            for (var i = 0; i < 400; i++)
            {
                fight.Step(rotation);
                events.AddRange(fight.DrainEvents());
                damage.AddRange(fight.DrainDamage());
            }
            var what = $"{path} {dummy}";
            Assert.Equal(damage.Count, damage.Select(d => d.Seq).Distinct().Count());
            Assert.True(damage.Zip(damage.Skip(1)).All(p => p.First.Seq < p.Second.Seq), what);
            Assert.DoesNotContain(damage, d => d.Seq <= 0);

            var hits = damage.Where(d => !d.IsDot).ToDictionary(d => d.Seq);
            var hitEvents = events.Where(e => e.Kind == "hit" && e.DamageSeq != null).ToList();
            Assert.NotEmpty(hitEvents);
            Assert.Equal(hits.Count, hitEvents.Count);
            foreach (var e in hitEvents)
            {
                Assert.True(hits.TryGetValue(e.DamageSeq!.Value, out var d), $"{what}: {e}");
                Assert.True(d.SkillId == e.SkillId && d.Target == e.Target && d.Time == e.Time, $"{what}: {e} vs {d}");
            }
            // A crit on a hit without any damage part (no damage event) has no number either.
            var crits = events.Where(e => e.Kind == "crit" && e.DamageSeq != null).Select(e => e.DamageSeq!.Value).ToHashSet();
            Assert.Equal(crits.Order(), hits.Values.Where(d => d.IsCrit).Select(d => d.Seq).Order());
        }
    }

    [Fact]
    public void Dot_damage_events_carry_ailment_or_zone_id()
    {
        bool ailments = false, zones = false;
        foreach (var path in MainBuildPaths)
        {
            var fight = Start(ContentHarness.LoadBuild(path), new LiveFightConfig("swarm", SimMode.Average));
            var rotation = fight.Setup.Rotation();
            for (var i = 0; i < 400; i++)
            {
                fight.Step(rotation);
                foreach (var d in fight.DrainDamage())
                {
                    if (!d.IsDot)
                    {
                        Assert.True(d.AilmentId == null && d.ZoneId == null, $"{path}: {d}");
                        continue;
                    }
                    Assert.True((d.AilmentId == null) != (d.ZoneId == null), $"{path}: {d}");
                    if (d.AilmentId != null) Assert.Contains(d.AilmentId, ContentHarness.Data.Ailments.Keys);
                    if (d.ZoneId != null) Assert.Equal(d.SkillId, d.ZoneId); // one zone per skill
                    ailments |= d.AilmentId != null;
                    zones |= d.ZoneId != null;
                }
            }
        }
        Assert.True(ailments, "no ailment tick");
        Assert.True(zones, "no zone tick");
    }

    [Fact]
    public void Use_event_carries_the_busy_time_of_that_use()
    {
        var fight = Start("sorcerer_pyromancer_fireball");
        Assert.True(fight.Press(Fireball).Accepted);
        fight.Step();
        var use = fight.DrainEvents().Single(e => e.Kind == "use");
        Assert.Equal(fight.Sim.BusyUntil - use.Time, use.UseSec!.Value, 9);
        Assert.Equal(fight.Slots[Fireball - 1].UseTimeSec, use.UseSec!.Value, 9); // no conditional speed bonus at the start
    }

    private static LiveFight Start(string buildPath) =>
        Start(ContentHarness.LoadBuild(buildPath), new LiveFightConfig("training", SimMode.Average));

    private static LiveFight Start(Build build, LiveFightConfig config)
    {
        var fight = LiveFight.Start(ContentHarness.Data, MechanicRegistry.Default, build, config, out var violations);
        Assert.True(fight != null, string.Join("; ", violations.Select(v => v.Message)));
        return fight;
    }

    /// <summary>At least one step, then steps until the character is free: the next step acts.</summary>
    private static void StepUntilFree(LiveFight fight)
    {
        var i = 0;
        do fight.Step(); while (fight.State.Busy && ++i < 200);
        Assert.False(fight.State.Busy);
    }
}
