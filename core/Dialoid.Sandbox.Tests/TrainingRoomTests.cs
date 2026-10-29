using System.Text.RegularExpressions;
using Dialoid.Core.Combat;
using Dialoid.Core.Simulation;
using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

/// <summary>Reference builds and fight helpers shared by the training room tests.</summary>
internal static class Rooms
{
    // Slots are 1-based in the room (keys 1–5), 0-based in the session.
    // sorcerer_pyromancer_fireball: blink, meteor (with «Звездопад»), ball_lightning, fireball, spark.
    public const int BlinkSlot = 1, MeteorSlot = 2, FireballSlot = 4, SparkSlot = 5;
    // warrior_thunderer_crush: battle_cry, charge, crush, whirlwind, cleave.
    public const int CrushSlot = 3, WhirlwindSlot = 4, CleaveSlot = 5;

    public static SandboxSession Load(string file)
    {
        var dir = DataLocator.Find(AppContext.BaseDirectory)!;
        var info = ReferenceBuilds.List(dir).Single(i => i.Group == ReferenceBuilds.MainGroup && Path.GetFileName(i.Path) == file);
        var s = SandboxSession.Create(TestData.Data, file.StartsWith("warrior", StringComparison.Ordinal) ? "warrior" : "sorcerer");
        Assert.True(ReferenceBuilds.Load(s, info).Ok);
        return s;
    }

    public static SandboxSession Fireball() => Load("sorcerer_pyromancer_fireball.json");
    public static SandboxSession Crush() => Load("warrior_thunderer_crush.json");

    public static void Steps(TrainingRoom room, int steps)
    {
        for (var i = 0; i < steps; i++) room.Advance(LiveFight.StepSec);
    }

    /// <summary>Presses a slot (in reach) every step for the given fight time.</summary>
    public static void Hammer(TrainingRoom room, int slot, double seconds)
    {
        var steps = (int)Math.Round(seconds / LiveFight.StepSec);
        for (var i = 0; i < steps; i++)
        {
            room.Press(slot, 1, true);
            room.Advance(LiveFight.StepSec);
        }
    }

    /// <summary>Hits until a segment is open, then waits (no input) until it closes. Returns the closed segment.</summary>
    public static FightSegment HitAndWaitForClose(TrainingRoom room, int slot)
    {
        for (var i = 0; i < 200 && room.Current == null; i++)
        {
            room.Press(slot, 1, true);
            room.Advance(LiveFight.StepSec);
        }
        var open = room.Current;
        Assert.NotNull(open);
        for (var i = 0; i < 2000 && room.Current != null; i++) room.Advance(LiveFight.StepSec);
        Assert.Null(room.Current);
        return open!;
    }

    public static double FinalSum(IEnumerable<Dialoid.Core.Combat.DamageEvent> damage) => damage.Sum(d => d.Parts.Sum(p => p.Final));
}

public class TrainingRoomTests
{
    private static readonly Regex Latin = new("[A-Za-z]", RegexOptions.CultureInvariant);
    private static readonly Regex LinePrefix = new(@"^\d+,\d с · ", RegexOptions.CultureInvariant);

    [Fact]
    public void Segment_closes_after_idle_and_new_one_starts_on_next_hit()
    {
        var room = new TrainingRoom(Rooms.Fireball());

        var first = Rooms.HitAndWaitForClose(room, Rooms.FireballSlot);
        var closedAt = room.State!.Time;

        Assert.Equal(1, first.Index);
        Assert.Single(room.Segments);
        Assert.Same(first, room.Segments[0]);
        Assert.NotEmpty(first.Damage);
        Assert.Equal(first.Damage[0].Time, first.Start, 9);
        Assert.Equal(first.Damage[^1].Time, first.End, 9);
        // Closed exactly at the first step boundary 5 s after the last damage.
        Assert.InRange(closedAt - first.End, TrainingRoom.SegmentIdleSec - 1e-9, TrainingRoom.SegmentIdleSec + LiveFight.StepSec + 1e-9);

        for (var i = 0; i < 200 && room.Current == null; i++)
        {
            room.Press(Rooms.FireballSlot, 1, true);
            room.Advance(LiveFight.StepSec);
        }
        Assert.NotNull(room.Current);
        Assert.Equal(2, room.Current!.Index);
        Assert.Equal(2, room.Segments.Count);
        Assert.Same(room.Current, room.Segments[^1]);
        Assert.True(room.Current.Start > first.End + TrainingRoom.SegmentIdleSec - 1e-9);
    }

    [Fact]
    public void Keeps_last_ten_segments_and_reset_clears()
    {
        var room = new TrainingRoom(Rooms.Fireball());

        for (var i = 0; i < TrainingRoom.MaxSegments + 2; i++) Rooms.HitAndWaitForClose(room, Rooms.SparkSlot);

        Assert.Equal(TrainingRoom.MaxSegments, room.Segments.Count);
        Assert.Equal(Enumerable.Range(3, TrainingRoom.MaxSegments), room.Segments.Select(s => s.Index));

        room.ResetMeter();
        Assert.Empty(room.Segments);
        Assert.Null(room.Current);

        var next = Rooms.HitAndWaitForClose(room, Rooms.SparkSlot);
        Assert.Same(next, Assert.Single(room.Segments));
    }

    [Fact]
    public void Build_change_restarts_fight_closes_segment_and_logs_line()
    {
        var s = Rooms.Fireball();
        var room = new TrainingRoom(s);
        room.Attach();
        var updates = 0;
        room.Updated += () => updates++;

        Rooms.Hammer(room, Rooms.FireballSlot, 2);
        var segment = room.Current;
        Assert.NotNull(segment);
        Assert.True(room.State!.Time > 1.9);

        var rotation = Enumerable.Reverse(s.Build.Rotation).ToList();
        Assert.True(s.SetRotation(rotation).Ok);

        Assert.Null(room.Current);
        Assert.Same(segment, Assert.Single(room.Segments));
        Assert.Equal(0, room.State!.Time, 9);
        Assert.Equal("Билд изменен — бой начат заново", room.LogLines[^1]);
        Assert.True(updates > 0);

        Rooms.Hammer(room, Rooms.FireballSlot, 1);
        Assert.Equal(2, room.Current!.Index);

        // Detached: edits no longer touch the room.
        room.Detach();
        var time = room.State!.Time;
        Assert.True(s.SetRotation(Enumerable.Reverse(rotation).ToList()).Ok);
        Assert.Equal(time, room.State!.Time, 9);
        Assert.NotNull(room.Current);
    }

    [Fact]
    public void Build_change_during_channel_and_queued_press_is_clean()
    {
        var s = Rooms.Crush();
        var room = new TrainingRoom(s);
        room.Attach();

        Rooms.Hammer(room, Rooms.CleaveSlot, 3); // whirlwind spends Fury: build some first
        var hold = room.StartChannel(Rooms.WhirlwindSlot, 1, true);
        Assert.True(hold.Accepted);
        Rooms.Steps(room, 20);
        Assert.NotNull(room.Current);
        Assert.Equal("whirlwind", room.State!.ChannelSkillId);

        // Queue Cleave behind a channel tick: press only while busy, step until the press is taken as queued.
        PressResult? queued = null;
        for (var i = 0; i < 40 && queued is not { Queued: true }; i++)
        {
            if (room.State!.Busy) queued = room.Press(Rooms.CleaveSlot, 1, true);
            if (queued is not { Queued: true }) room.Advance(LiveFight.StepSec);
        }
        Assert.NotNull(queued);
        Assert.True(queued!.Accepted);
        Assert.True(queued.Queued);

        Assert.True(s.SetRotation(Enumerable.Reverse(s.Build.Rotation).ToList()).Ok);
        var restartAt = room.LogLines.Count;

        var state = room.State!;
        Assert.Equal(0, state.Time, 9);
        Assert.Null(state.ChannelSkillId);
        Assert.False(state.Busy);
        Assert.Single(room.Segments);
        Assert.Null(room.Current);

        // The old hold and the queued press died with the old fight: no input, no damage.
        Rooms.Steps(room, 60);
        Assert.Null(room.Current);
        Assert.Single(room.Segments);
        Assert.Null(room.State!.ChannelSkillId);
        var after = room.LogLines.Skip(restartAt).ToList();
        Assert.DoesNotContain(after, l => l.Contains("Рассекающий удар", StringComparison.Ordinal));
        Assert.DoesNotContain(after, l => l.Contains("Вихрь", StringComparison.Ordinal));
        room.Detach();
    }

    [Fact]
    public void Held_whirlwind_stops_when_the_hero_walks_out_of_melee_reach()
    {
        var room = new TrainingRoom(Rooms.Crush());
        Rooms.Hammer(room, Rooms.CleaveSlot, 3); // Fury for Whirlwind
        Assert.True(room.StartChannel(Rooms.WhirlwindSlot, 1, true).Accepted);
        for (var i = 0; i < 20; i++)
        {
            room.UpdateReach(ReachRules.MeleeTiles, true); // still in reach: nothing happens
            room.Advance(LiveFight.StepSec);
        }
        Assert.Equal("whirlwind", room.State!.ChannelSkillId);
        Assert.DoesNotContain(room.LogLines, l => l.Contains("вне досягаемости", StringComparison.Ordinal));

        for (var i = 0; i < 60; i++)
        {
            room.UpdateReach(10, true);
            room.Advance(LiveFight.StepSec);
        }

        var refusals = room.LogLines.Select((l, i) => (l, i)).Where(x => x.l.Contains("Вихрь · отказ: цель вне досягаемости", StringComparison.Ordinal)).ToList();
        var single = Assert.Single(refusals); // logged once, not every step
        Assert.DoesNotContain(room.LogLines.Skip(single.i + 1), l => l.Contains("Вихрь · применение", StringComparison.Ordinal));
        Assert.Null(room.State!.ChannelSkillId);
    }

    [Fact]
    public void Held_ground_spell_stops_when_the_dummy_leaves_the_screen()
    {
        var s = Rooms.Fireball();
        Assert.True(s.RemoveSkillNodePoint(Rooms.MeteorSlot - 1, "starfall").Ok);
        var room = new TrainingRoom(s);

        Assert.True(room.StartChannel(Rooms.MeteorSlot, 20, true).Accepted);
        for (var i = 0; i < 400 && !room.LogLines.Any(l => l.Contains("Метеор · применение", StringComparison.Ordinal)); i++)
        {
            room.UpdateReach(20, true);
            room.Advance(LiveFight.StepSec);
        }
        Assert.Contains(room.LogLines, l => l.Contains("Метеор · применение", StringComparison.Ordinal));

        for (var i = 0; i < 400; i++) // 20 s: several Meteor cooldowns
        {
            room.UpdateReach(20, false);
            room.Advance(LiveFight.StepSec);
        }

        var refusals = room.LogLines.Select((l, i) => (l, i)).Where(x => x.l.Contains("Метеор · отказ: цель вне досягаемости", StringComparison.Ordinal)).ToList();
        var single = Assert.Single(refusals);
        Assert.DoesNotContain(room.LogLines.Skip(single.i + 1), l => l.Contains("Метеор · применение", StringComparison.Ordinal));
    }

    [Fact]
    public void Huge_dt_is_capped_to_max_catch_up_steps()
    {
        var room = new TrainingRoom(Rooms.Fireball());

        room.Advance(1000);
        Assert.Equal(TrainingRoom.MaxCatchUpSteps * LiveFight.StepSec, room.State!.Time, 9);

        // The rest of the huge dt was dropped, not carried over.
        room.Advance(0.04);
        Assert.Equal(TrainingRoom.MaxCatchUpSteps * LiveFight.StepSec, room.State!.Time, 9);

        // Small dts accumulate: 0,04 + 0,02 makes one step.
        room.Advance(0.02);
        Assert.Equal((TrainingRoom.MaxCatchUpSteps + 1) * LiveFight.StepSec, room.State!.Time, 9);
    }

    [Fact]
    public void Log_lines_are_russian_and_capped()
    {
        var room = new TrainingRoom(Rooms.Fireball());
        for (var i = 0; i < 200; i++)
        {
            room.Press(Rooms.FireballSlot, 1, true);
            room.Press(Rooms.MeteorSlot, 3, true);
            room.Press(Rooms.BlinkSlot, 3, true);
            room.Advance(LiveFight.StepSec);
        }

        Assert.NotEmpty(room.LogLines);
        Assert.Contains(room.LogLines, l => l.Contains(" · Огненный шар · попадание · ", StringComparison.Ordinal));
        Assert.Contains(room.LogLines, l => l.Contains(" · отказ: ", StringComparison.Ordinal));
        Assert.DoesNotContain(room.LogLines, l => l.EndsWith(" · попадание · 0", StringComparison.Ordinal)); // Blink deals no damage
        foreach (var line in room.LogLines)
        {
            Assert.Matches(LinePrefix, line);
            Assert.DoesNotMatch(Latin, line);
        }

        for (var i = 0; i < TrainingRoom.MaxLogLines + 50; i++) room.Press(Rooms.BlinkSlot, 3, false);
        Assert.Equal(TrainingRoom.MaxLogLines, room.LogLines.Count);
        Assert.Contains("отказ: цель вне досягаемости", room.LogLines[^1]);
    }

    [Fact]
    public void Combat_log_line_formats()
    {
        var text = new CombatLogText(TestData.Data);
        var fire = new DamagePart(DamageType.Fire, 1000, 0, 188, 0, 0, 812);
        var phys = new DamagePart(DamageType.Physical, 600, 100, 0, 0, 0, 500.4);

        Assert.Equal("12,4 с · Огненный шар · попадание · 812 огнем (крит)",
            text.Line(new DamageEvent(12.4, "fireball", 0, false, true, new[] { fire })));
        Assert.Equal("1,0 с · Рассекающий удар · попадание по цели 2 · 500 физическим + 812 огнем",
            text.Line(new DamageEvent(1, "cleave", 1, false, false, new[] { phys, fire })));
        Assert.Equal("12,4 с · Огненный шар · поджог", text.Line(new SimEvent(12.4, "ailment", "ignite -> #0 (fireball)")));
        Assert.Equal("12,4 с · Огненный шар · применение", text.Line(new SimEvent(12.4, "use", "fireball")));
        Assert.Equal("3,0 с · Метеор · прок · Огненный шар",
            text.Line(new SimEvent(3, "proc", "meteor: on_hit -> cast_skill fireball")));
        Assert.Equal("3,0 с · механика · заряды потрачены", text.Line(new SimEvent(3, "mechanic", "thunder: charges spent")));
        Assert.Equal("13,0 с · Метеор · отказ: перезарядка 2,1 с",
            text.Refusal(13, 2, "meteor", new PressResult(false, false, PressRefusal.Cooldown, 2.1)));
        Assert.Equal("13,0 с · слот 3 · отказ: пустой слот",
            text.Refusal(13, 3, null, new PressResult(false, false, PressRefusal.EmptySlot, null)));
    }

    [Fact]
    public void Keys_follow_the_player_slots_across_an_empty_slot()
    {
        // Slots 1 (Meteor) and 5 (Blink, cooldown 6 s) filled, 2–4 empty: the fight's build lists only the filled ones.
        var s = Rooms.Fireball();
        for (var i = 0; i <= 4; i++) Assert.True(s.SetSlotSkill(i, null).Ok);
        Assert.True(s.SetSlotSkill(0, "meteor").Ok);
        Assert.True(s.SetSlotSkill(4, "blink").Ok);
        Assert.Empty(s.Violations);
        var room = new TrainingRoom(s);
        Assert.True(room.Running);

        Assert.Null(room.CompiledTags(2));
        Assert.NotNull(room.CompiledTags(5));
        Assert.Equal(PressRefusal.EmptySlot, room.Press(2, 3, true).Refusal);
        Assert.Equal("0,0 с · слот 2 · отказ: пустой слот", room.LogLines[^1]);
        Assert.Equal(PressRefusal.EmptySlot, room.StartChannel(2, 3, true).Refusal);
        Assert.Equal("0,0 с · слот 2 · отказ: пустой слот", room.LogLines[^1]);

        Assert.True(room.Press(5, 3, true).Accepted);
        for (var i = 0; i < 100 && !room.LogLines.Any(l => l.Contains("Мерцание · применение", StringComparison.Ordinal)); i++)
            room.Advance(LiveFight.StepSec);
        Assert.Contains(room.LogLines, l => l.Contains("Мерцание · применение", StringComparison.Ordinal));
        Assert.DoesNotContain(room.LogLines, l => l.Contains("Метеор", StringComparison.Ordinal));

        var cooldowns = room.State!.CooldownLeftBySlot;
        Assert.Equal(new[] { 5 }, cooldowns.Keys); // Blink's cooldown is shown on key 5
        var refused = room.Press(5, 3, true);
        Assert.Equal(PressRefusal.Cooldown, refused.Refusal);
        Assert.Equal(cooldowns[5], refused.CooldownLeft!.Value, 9);
        Assert.Contains(" · Мерцание · отказ: перезарядка ", room.LogLines[^1]);
    }

    [Fact]
    public void Illegal_build_starts_no_fight_and_reports_violations()
    {
        var s = Rooms.Fireball();
        Assert.True(s.SetLevel(1).Ok);
        Assert.NotEmpty(s.Violations);

        var room = new TrainingRoom(s);

        Assert.False(room.Running);
        Assert.Null(room.State);
        Assert.NotEmpty(room.Violations);
        Assert.StartsWith("Бой не начат: ", room.LogLines[^1]);
        Assert.Contains(room.Violations[0].Message, room.LogLines[^1]);

        room.Advance(1);
        var press = room.Press(Rooms.FireballSlot, 1, true);
        Assert.False(press.Accepted);
        Assert.Equal(PressRefusal.FightOver, press.Refusal);
        Assert.Empty(room.Segments);

        room.SetDummy("no_such_dummy");
        Assert.False(room.Running);
    }

    [Fact]
    public void Unknown_dummy_starts_no_fight_and_a_known_one_restarts_it()
    {
        var room = new TrainingRoom(Rooms.Fireball());
        Assert.True(room.Running);

        room.SetDummy("no_such_dummy");
        Assert.False(room.Running);
        Assert.Contains(room.Violations, v => v.Code == "unknown_dummy");

        room.SetDummy("boss");
        Assert.True(room.Running);
        Assert.Equal("boss", room.DummyId);
        Assert.Empty(room.Violations);
        Assert.Equal("Манекен «Босс» — бой начат заново", room.LogLines[^1]);

        room.SetMode(SimMode.Roll, 7);
        Assert.Equal(SimMode.Roll, room.Mode);
        Assert.Equal(7, room.Seed);
        Assert.Equal("Режим «бросок» — бой начат заново", room.LogLines[^1]);
    }
}
