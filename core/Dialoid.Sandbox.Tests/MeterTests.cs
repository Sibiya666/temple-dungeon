using Dialoid.Core.Combat;
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
        var all = seg.Mitigation();
        var rows = all.Where(r => !r.IsBonus).ToList();
        var bonus = all.Where(r => r.IsBonus).ToList();
        Assert.Equal(new[] { "дошло", "броня", "сопротивления", "множитель получаемого урона", "обнулено" }, rows.Select(r => r.Label));
        // Bonus rows (vulnerability, penetration below zero) come after the losses and add damage: losses − bonuses = before defense.
        Assert.Equal(all.Take(5), rows);
        Near(before, rows.Sum(r => r.Value) - bonus.Sum(r => r.Value));
        Near(1, rows.Sum(r => r.Share) - bonus.Sum(r => r.Share));
        Near(Rooms.FinalSum(seg.Damage), rows[0].Value);
        Assert.True(rows[1].Value > 0, "boss armor takes some physical damage");
        Assert.True(rows[2].Value > 0, "boss resistances take some lightning damage");

        foreach (var (skill, p) in seg.MitigationBySkill())
        {
            Near(p.BeforeDefense - p.ByArmor - p.ByResist - p.ByTakenMultiplier - p.Nullified, p.Final);
            Near(seg.Damage.Where(d => d.SkillId == skill).Sum(d => d.Parts.Sum(x => x.BeforeDefense)), p.BeforeDefense);
        }
    }

    private static FightSegment Segment(params DamagePart[] parts)
    {
        var seg = new FightSegment(1, 0, new CombatLogText(TestData.Data));
        foreach (var p in parts) seg.Add(new DamageEvent(0, "spark", 0, false, false, new[] { p }));
        return seg;
    }

    [Fact]
    public void Vulnerability_is_a_positive_bonus_row()
    {
        // 1000 lightning, shocked: the multiplier adds 250 (a negative loss); a chilled hit loses 100 to a weaker multiplier.
        var seg = Segment(
            new DamagePart(DamageType.Lightning, 1000, 0, 0, -250, 0, 1250),
            new DamagePart(DamageType.Lightning, 1000, 0, 0, 100, 0, 900));

        var rows = seg.Mitigation();
        Assert.Equal(100, rows.Single(r => r.Key == "taken_multiplier").Value, 9); // only the loss
        var bonus = rows.Single(r => r.IsBonus);
        Assert.Equal("vulnerability", bonus.Key);
        Assert.Equal(250, bonus.Value, 9);
        Assert.Equal("+250 от уязвимости", bonus.Label);
        Assert.Equal(0.125, bonus.Share, 9);
        Assert.Same(rows[^1], bonus);
        Near(2000, rows.Where(r => !r.IsBonus).Sum(r => r.Value) - bonus.Value);
        Assert.Contains("100", CombatLogText.MeterModeHint(MeterMode.Mitigation));
        Assert.Null(CombatLogText.MeterModeHint(MeterMode.DpsBySkill));

        // No vulnerability: no bonus row.
        Assert.DoesNotContain(Segment(new DamagePart(DamageType.Fire, 100, 0, 10, 0, 0, 90)).Mitigation(), r => r.IsBonus);
    }

    [Fact]
    public void Defence_split_per_skill_keeps_losses_and_bonuses_apart()
    {
        // One skill: a shocked hit (+250) and a chilled one (−100). Netted per skill it would read −150 loss; split it is both.
        var seg = Segment(
            new DamagePart(DamageType.Lightning, 1000, 0, 0, -250, 0, 1250),
            new DamagePart(DamageType.Lightning, 1000, 0, -50, 100, 0, 950));
        var split = seg.DefenceBySkill()["spark"];
        Assert.Equal(new DefenceSplit(2000, 0, 0, 50, 100, 250, 0, 2200), split);
        Assert.Equal(split, seg.DefenceTotal);
        Near(split.Final, split.BeforeDefense - split.Armor - split.ResistLoss + split.ResistBonus - split.TakenLoss + split.TakenBonus - split.Nullified);
        var rows = seg.Mitigation();
        Assert.Equal(split.TakenLoss, rows.Single(r => r.Key == "taken_multiplier").Value, 9);
        Assert.Equal(split.TakenBonus, rows.Single(r => r.Key == "vulnerability").Value, 9);
        Assert.Equal(split.ResistBonus, rows.Single(r => r.Key == "penetration").Value, 9);
    }

    [Fact]
    public void Negative_resist_loss_is_a_penetration_bonus_row()
    {
        // Penetration pushes the resistance below zero: −1500 "lost" is 1500 more damage, shown in the meter's short form.
        var seg = Segment(
            new DamagePart(DamageType.Fire, 10000, 0, -1500, 0, 0, 11500),
            new DamagePart(DamageType.Cold, 1000, 0, 300, 0, 0, 700));

        var rows = seg.Mitigation();
        Assert.Equal(300, rows.Single(r => r.Key == "resist").Value, 9);
        var bonus = rows.Single(r => r.IsBonus);
        Assert.Equal("penetration", bonus.Key);
        Assert.Equal(1500, bonus.Value, 9);
        Assert.Equal("+1,5 тыс. от пробивания", bonus.Label);
        Near(11000, rows.Where(r => !r.IsBonus).Sum(r => r.Value) - bonus.Value);
    }

    [Fact]
    public void Per_skill_damage_over_time_sums_to_skill_total()
    {
        var room = new TrainingRoom(Rooms.Fireball());
        Rooms.Hammer(room, Rooms.MeteorSlot, 1); // a newer press replaces a queued one: Meteor first, alone
        Rooms.Hammer(room, Rooms.FireballSlot, 5);
        var seg = room.Current!;
        var total = seg.DamageOverTime(1);
        var bySkill = seg.DamageBySkill();
        Assert.True(bySkill.Count >= 2);

        foreach (var row in bySkill)
        {
            var series = seg.DamageOverTime(1, row.Key);
            Assert.Equal(total.Select(p => p.Time), series.Select(p => p.Time));
            Near(row.Value, series.Sum(p => p.Damage));
        }
        for (var i = 0; i < total.Count; i++)
            Near(total[i].Damage, bySkill.Sum(r => seg.DamageOverTime(1, r.Key)[i].Damage));
        Assert.All(seg.DamageOverTime(1, "no_such_skill"), p => Assert.Equal(0, p.Damage));
    }
}
