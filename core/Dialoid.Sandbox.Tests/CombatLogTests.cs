using System.Text.RegularExpressions;
using Dialoid.Core.Combat;
using Dialoid.Core.Simulation;
using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

/// <summary>The structured Russian combat log of the training room (spec 7).</summary>
public class CombatLogTests
{
    private static readonly Regex Latin = new("[A-Za-z]", RegexOptions.CultureInvariant);
    private static readonly LogKind[] DamageKinds = { LogKind.Hit, LogKind.Crit, LogKind.DotTick };

    private static void Near(double expected, double actual) =>
        Assert.InRange(actual, expected - 1e-6 * Math.Max(1, Math.Abs(expected)), expected + 1e-6 * Math.Max(1, Math.Abs(expected)));

    private static ISet<LogKind> Shown() => Enum.GetValues<LogKind>().Except(TrainingRoom.DefaultHiddenKinds).ToHashSet();

    /// <summary>Safety net: every reference build on every dummy, every filled slot pressed in reach, gives only Russian lines built
    /// from the structured fields (no English event text, no bare «механика»).</summary>
    [Theory]
    [MemberData(nameof(TrainingRoomTests.MainBuilds), MemberType = typeof(TrainingRoomTests))]
    public void Log_lines_come_from_structured_fields(string file)
    {
        var bareMechanic = new Regex(@"^\d+,\d с · механика$", RegexOptions.CultureInvariant);
        var s = Rooms.Load(file);
        var slots = Enumerable.Range(1, SandboxSession.SlotCount).Where(k => s.GetSlot(k - 1) != null).ToList();
        foreach (var mode in new[] { SimMode.Roll, SimMode.Average })
        foreach (var dummy in TestData.Data.Dummies.Keys.Order(StringComparer.Ordinal))
        {
            var room = new TrainingRoom(s);
            room.SetMode(mode, 1);
            room.SetDummy(dummy);
            var what = $"{file} {dummy} {mode}";
            Assert.True(room.Running, what);
            var hits = false;
            for (var i = 1; i <= 600; i++) // 30 s
            {
                foreach (var slot in slots) room.Press(slot, 1, true);
                room.Advance(LiveFight.StepSec);
                if (i % 40 != 0) continue; // check every 2 s, well inside the log window
                foreach (var e in room.Log)
                {
                    Assert.True(!Latin.IsMatch(e.Text), $"{what}: {e.Text}");
                    Assert.True(!bareMechanic.IsMatch(e.Text), $"{what}: {e.Text}");
                    hits |= e.Kind is LogKind.Hit or LogKind.Crit;
                }
            }
            Assert.True(hits, $"{what}: no hits logged");
            Assert.Equal(room.Log.Select(e => e.Text), room.LogLines);
        }
    }

    [Fact]
    public void Mechanic_line_names_the_mechanic_and_its_number()
    {
        // The thunderer's Cleave charges Static charge (lightning from «Грозовое оружие»); a Crush with 10 charges spends them.
        var room = new TrainingRoom(Rooms.Crush());
        Rooms.Hammer(room, Rooms.CleaveSlot, 15);
        Rooms.Hammer(room, Rooms.CrushSlot, 3);

        var spent = room.Log.Where(e => e.Kind == LogKind.Mechanic).ToList();
        Assert.NotEmpty(spent);
        Assert.All(spent, e => Assert.Matches(@"^\d+,\d с · Статический заряд · заряды потрачены · 10$", e.Text));
    }

    [Fact]
    public void Identical_refusals_collapse_with_count()
    {
        var room = new TrainingRoom(Rooms.Crush());
        for (var i = 0; i < 6; i++) room.Press(Rooms.CleaveSlot, 10, true);
        var cleave = room.Log[^1];
        Assert.Equal(LogKind.Refusal, cleave.Kind);
        Assert.Equal(6, cleave.Repeat);
        Assert.Equal("cleave", cleave.SkillId);
        Assert.Equal("0,0 с · Рассекающий удар · отказ: вне досягаемости ×6", cleave.Text);
        Assert.Single(room.Log, e => e.Kind == LogKind.Refusal);

        // A held key refused again and again (the game layer retries every frame) writes one line too.
        for (var i = 0; i < 5; i++) room.StartChannel(Rooms.WhirlwindSlot, 10, true);
        Assert.Equal("0,0 с · Вихрь · отказ: вне досягаемости ×5", room.Log[^1].Text);
        Assert.Equal(2, room.Log.Count(e => e.Kind == LogKind.Refusal));

        // Not in a row any more: a new line.
        room.Press(Rooms.CleaveSlot, 10, true);
        Assert.Equal("0,0 с · Рассекающий удар · отказ: вне досягаемости", room.Log[^1].Text);
        Assert.Equal(1, room.Log[^1].Repeat);

        // Cooldown refusals collapse whatever the seconds left.
        var sorc = new TrainingRoom(Rooms.Fireball());
        Assert.True(sorc.Press(Rooms.BlinkSlot, 1, true).Accepted);
        for (var i = 0; i < 100 && sorc.State!.CooldownLeftBySlot.Count == 0; i++) sorc.Advance(LiveFight.StepSec);
        for (var i = 0; i < 40 && sorc.State!.Busy; i++) sorc.Advance(LiveFight.StepSec);
        for (var i = 0; i < 6; i++) Assert.Equal(PressRefusal.Cooldown, sorc.Press(Rooms.BlinkSlot, 1, true).Refusal);
        Assert.EndsWith(" · Мерцание · отказ: перезарядка ×6", sorc.Log[^1].Text);
    }

    [Fact]
    public void Swarm_lines_carry_target_number()
    {
        var room = new TrainingRoom(Rooms.Fireball());
        room.SetDummy("swarm");
        Rooms.Hammer(room, Rooms.FireballSlot, 3);

        var hits = room.Log.Where(e => e.Kind is LogKind.Hit or LogKind.Crit).ToList();
        Assert.NotEmpty(hits);
        foreach (var h in hits)
        {
            Assert.NotNull(h.Target);
            Assert.Contains($" · цель {h.Target + 1} · ", h.Text);
        }
        Assert.Contains(hits, h => h.Target == 0); // the first figure is numbered too
        Assert.Contains(room.Log, e => e.Kind == LogKind.DotTick && e.Text.Contains(" · цель ", StringComparison.Ordinal));

        var single = new TrainingRoom(Rooms.Fireball());
        Rooms.Hammer(single, Rooms.FireballSlot, 3);
        Assert.Contains(single.Log, e => e.Kind is LogKind.Hit or LogKind.Crit);
        Assert.DoesNotContain(single.Log, e => e.Text.Contains("цель", StringComparison.Ordinal));
    }

    [Fact]
    public void Dot_ticks_aggregate_per_second_and_are_hidden_by_default()
    {
        var room = new TrainingRoom(Rooms.Fireball());
        Rooms.Hammer(room, Rooms.FireballSlot, 6);
        var seg = room.Current!;
        Assert.NotNull(seg);

        var ticks = room.Log.Where(e => e.Kind == LogKind.DotTick).ToList();
        var rawTicks = seg.Damage.Count(d => d.IsDot);
        Assert.NotEmpty(ticks);
        Assert.True(ticks.Count * 10 < rawTicks, $"{ticks.Count} lines for {rawTicks} ticks");
        Assert.Contains(ticks, t => t.Text.Contains(" · Огненный шар · урон поджога · ", StringComparison.Ordinal));
        // At most one line per (skill, ailment or zone, target) and second of the segment.
        foreach (var group in ticks.GroupBy(t => (t.SkillId, t.Target, What: t.Text.Split(" · ")[2])))
        {
            var seconds = group.Select(t => Math.Floor(t.Time + 1e-9)).ToList();
            Assert.Equal(seconds.Count, seconds.Distinct().Count());
        }
        Assert.All(ticks, t => Assert.True(t.Amount > 0));

        Assert.Contains(LogKind.DotTick, TrainingRoom.DefaultHiddenKinds);
        var shown = room.Filter(Shown(), null);
        Assert.DoesNotContain(shown, e => e.Kind == LogKind.DotTick);
        Assert.Contains(shown, e => e.Kind is LogKind.Hit or LogKind.Crit);
        Assert.Equal(ticks, room.Filter(new HashSet<LogKind> { LogKind.DotTick }, null));
        var fireballOnly = room.Filter(Enum.GetValues<LogKind>().ToHashSet(), "fireball");
        Assert.NotEmpty(fireballOnly);
        Assert.All(fireballOnly, e => Assert.Equal("fireball", e.SkillId));
    }

    [Fact]
    public void Segment_time_starts_at_zero()
    {
        var room = new TrainingRoom(Rooms.Fireball());
        Rooms.Steps(room, 40); // 2 s of fight before the first hit
        for (var i = 0; i < 200 && room.Current == null; i++)
        {
            room.Press(Rooms.FireballSlot, 1, true);
            room.Advance(LiveFight.StepSec);
        }
        Rooms.Hammer(room, Rooms.FireballSlot, 2);
        var seg = room.Current!;
        Assert.True(seg.Start > 2 - 1e-9);

        var inSegment = room.Log.Where(e => e.Segment == seg.Index).ToList();
        var first = inSegment.First(e => e.Kind is LogKind.Hit or LogKind.Crit);
        Assert.Equal(0, first.Time, 9);
        Assert.StartsWith("0,0 с · Огненный шар · попадание · ", first.Text);
        Assert.All(inSegment, e => Assert.InRange(e.Time, 0, seg.End - seg.Start + 1e-9));
    }

    [Fact]
    public void Lines_outside_segment_have_no_segment()
    {
        var room = new TrainingRoom(Rooms.Crush());
        Rooms.Steps(room, 20); // 1 s
        room.Press(Rooms.CleaveSlot, 10, true);
        var before = room.Log[^1];
        Assert.Null(before.Segment);
        Assert.Equal(1, before.Time, 9);
        Assert.StartsWith("1,0 с · Рассекающий удар · отказ: ", before.Text);

        var seg = Rooms.HitAndWaitForClose(room, Rooms.CleaveSlot);
        Assert.Contains(room.Log, e => e.Segment == seg.Index && e.Kind is LogKind.Hit or LogKind.Crit);
        // A use before the first hit has no segment (the hit lands later in the same step or after).
        Assert.All(room.Log.Where(e => e.Kind == LogKind.Use && e.Segment == null), e => Assert.True(e.Time <= seg.Start + 1e-9));

        var time = room.State!.Time;
        room.Press(Rooms.CleaveSlot, 10, true);
        var after = room.Log[^1];
        Assert.Null(after.Segment);
        Assert.Equal(time, after.Time, 9);
    }

    [Fact]
    public void Log_amounts_sum_to_meter_on_short_segment()
    {
        var room = new TrainingRoom(Rooms.Fireball());
        for (var i = 0; i < 60; i++)
        {
            room.Press(Rooms.MeteorSlot, 3, true);
            room.Press(Rooms.FireballSlot, 3, true);
            room.Advance(LiveFight.StepSec);
        }
        for (var i = 0; i < 2000 && room.Current != null; i++) room.Advance(LiveFight.StepSec);
        var seg = Assert.Single(room.Segments);
        Assert.False(seg.IsOpen);
        Assert.True(room.Log.Count < TrainingRoom.MaxLogLines);

        var lines = room.Log.Where(e => e.Segment == seg.Index && DamageKinds.Contains(e.Kind)).ToList();
        Assert.Contains(lines, e => e.Kind == LogKind.DotTick);
        Assert.All(lines, e => Assert.NotNull(e.Amount));
        Near(seg.TotalDamage, lines.Sum(e => e.Amount!.Value));
        foreach (var row in seg.DamageBySkill())
            Near(row.Value, lines.Where(e => e.SkillId == row.Key).Sum(e => e.Amount!.Value));
    }

    [Fact]
    public void Numbers_have_a_space_between_digit_groups()
    {
        var text = new CombatLogText(TestData.Data);
        var fire = new DamagePart(DamageType.Fire, 20000, 0, 7655, 0, 0, 12345);
        Assert.Equal("12,4 с · Огненный шар · попадание · 12 345 огнем (крит)",
            text.Line(new DamageEvent(12.4, "fireball", 0, false, true, new[] { fire })));
    }
}
