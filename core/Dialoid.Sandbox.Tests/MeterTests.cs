using Dialoid.Core.Simulation;
using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

public class MeterTests
{
    private static void Near(double expected, double actual) =>
        Assert.InRange(actual, expected - 1e-6 * Math.Max(1, Math.Abs(expected)), expected + 1e-6 * Math.Max(1, Math.Abs(expected)));

    [Fact]
    public void Meter_sums_equal_log_sums_for_segment()
    {
        var room = new TrainingRoom(Rooms.Fireball());
        for (var i = 0; i < 120; i++)
        {
            room.Press(Rooms.MeteorSlot, 3, true);
            room.Press(Rooms.FireballSlot, 3, true);
            room.Advance(LiveFight.StepSec);
        }
        var seg = room.Current!;
        Assert.NotNull(seg);
        Assert.Contains(seg.Damage, d => d.IsDot);
        Assert.True(seg.Damage.Count < FightSegment.MaxStoredDamage);

        var logTotal = Rooms.FinalSum(seg.Damage);
        Assert.True(logTotal > 0);

        var bySkill = seg.DamageBySkill();
        Near(logTotal, bySkill.Sum(r => r.Value));
        foreach (var row in bySkill)
            Near(Rooms.FinalSum(seg.Damage.Where(d => d.SkillId == row.Key)), row.Value);
        Assert.Equal("Огненный шар", bySkill.Single(r => r.Key == "fireball").Label);
        Near(1, bySkill.Sum(r => r.Share));
        Assert.Equal(bySkill.Select(r => r.Value).OrderByDescending(v => v), bySkill.Select(r => r.Value));

        var byType = seg.DamageByType();
        Near(logTotal, byType.Sum(r => r.Value));
        Assert.Contains(byType, r => r.Label == "огонь");

        var dps = seg.DpsBySkill();
        Near(logTotal, dps.Sum(r => r.Value) * seg.Duration);
        Near(seg.Damage[^1].Time - seg.Damage[0].Time, seg.Duration);

        var series = seg.DamageOverTime(1);
        Near(logTotal, series.Sum(p => p.Damage));
        Assert.Equal(seg.Start, series[0].Time, 9);
        Assert.Equal(series.Count, (int)Math.Floor(seg.End - seg.Start) + 1);

        var mit = seg.MitigationBySkill();
        Near(logTotal, mit.Values.Sum(p => p.Final));
    }

    [Fact]
    public void Single_hit_segment_lasts_one_step()
    {
        var room = new TrainingRoom(Rooms.Crush());
        for (var i = 0; i < 40 && room.Current == null; i++)
        {
            room.Press(Rooms.CleaveSlot, 1, true);
            room.Advance(LiveFight.StepSec);
        }
        var seg = room.Current!;
        Assert.NotNull(seg);
        // Just opened: all damage so far came in one step, so the duration is the one-step minimum.
        Assert.Equal(seg.Start, seg.End, 9);
        Assert.Equal(LiveFight.StepSec, seg.Duration, 9);
        Near(seg.DamageBySkill().Sum(r => r.Value) / LiveFight.StepSec, seg.DpsBySkill().Sum(r => r.Value));
    }

    [Fact]
    public void Mitigation_rows_add_up_to_before_defense()
    {
        var room = new TrainingRoom(Rooms.Crush());
        room.SetDummy("boss");
        for (var i = 0; i < 100; i++)
        {
            room.Press(Rooms.CrushSlot, 1, true);
            room.Press(5, 1, true); // cleave
            room.Advance(LiveFight.StepSec);
        }
        var seg = room.Current!;
        Assert.NotNull(seg);

        var before = seg.Damage.Sum(d => d.Parts.Sum(p => p.BeforeDefense));
        var rows = seg.Mitigation();
        Assert.Equal(new[] { "дошло", "броня", "сопротивления", "множитель получаемого урона", "обнулено" }, rows.Select(r => r.Label));
        Near(before, rows.Sum(r => r.Value));
        Near(1, rows.Sum(r => r.Share));
        Near(Rooms.FinalSum(seg.Damage), rows[0].Value);
        Assert.True(rows[1].Value > 0, "boss armor takes some physical damage");
        Assert.True(rows[2].Value > 0, "boss resistances take some lightning damage");

        foreach (var (skill, p) in seg.MitigationBySkill())
        {
            Near(p.BeforeDefense - p.ByArmor - p.ByResist - p.ByTakenMultiplier - p.Nullified, p.Final);
            Near(seg.Damage.Where(d => d.SkillId == skill).Sum(d => d.Parts.Sum(x => x.BeforeDefense)), p.BeforeDefense);
        }
    }
}
