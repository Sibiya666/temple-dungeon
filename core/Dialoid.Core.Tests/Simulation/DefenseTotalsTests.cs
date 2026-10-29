using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;
using Dialoid.Core.Tests.Content;

namespace Dialoid.Core.Tests.Simulation;

public class DefenseTotalsTests
{
    [Fact]
    public void Defensive_run_reports_dodges_mitigation_and_regen()
    {
        // The pyromancer takes dodge nodes (dex_phantom_step) and has armor and resists.
        var r = ContentHarness.Survival(ContentHarness.LoadBuild("sorcerer_pyromancer_fireball"));

        var d = r.Defense;
        Assert.NotNull(d);
        Assert.True(d!.AttacksReceived > 0);
        Assert.InRange(d.AttacksDodged, 0, d.AttacksReceived);
        Assert.Equal(r.Log.Count(e => e.Kind == "dummy_attack"), d.AttacksReceived);
        Assert.Equal(r.Log.Count(e => e.Kind == "dodge"), d.AttacksDodged);
        Assert.True(d.MitigatedByArmor + d.MitigatedByResist > 0);
        Assert.True(d.LifeRegenerated >= 0);
        Assert.NotNull(r.DamageTaken);
        Assert.True(r.DamageTaken!.Values.Sum() <= d.IncomingBeforeMitigation.Values.Sum() + 1e-9);
    }

    [Fact]
    public void Dodged_attacks_add_nothing_to_incoming_damage()
    {
        // Roll mode, fixed seed: this run dodges 2 of the boss's 9 attacks (the pyromancer has about 9.6% dodge).
        var build = ContentHarness.LoadBuild("sorcerer_pyromancer_fireball");
        var r = new Simulator(ContentHarness.Data, MechanicRegistry.Default).Run(build,
            new SimConfig("attacking_boss", SimMode.Roll, Seed: 3, DummyAttacks: true));

        var d = r.Defense!;
        Assert.True(d.AttacksDodged > 0, "the seed should dodge at least once");
        Assert.True(d.AttacksDodged < d.AttacksReceived);
        Assert.Equal(r.Log.Count(e => e.Kind == "dodge"), d.AttacksDodged);

        // The boss hit is constant, so incoming = landed attacks × one hit (gear-scaled) for every type.
        var perHit = ContentHarness.Data.Dummies["attacking_boss"].Attack!.DamagePerHit;
        var gear = Growth.Gear(build.Level);
        var landed = d.AttacksReceived - d.AttacksDodged;
        Assert.Equal(perHit.Keys.Where(t => perHit[t] > 0).OrderBy(t => t), d.IncomingBeforeMitigation.Keys);
        foreach (var (type, raw) in perHit)
            Assert.Equal(landed * raw * gear, d.IncomingBeforeMitigation[type], 6);
    }

    [Fact]
    public void Resource_series_is_sampled_every_half_second()
    {
        var build = ContentHarness.LoadBuild("warrior_berserker_whirlwind");
        var r = ContentHarness.Run(build, "training");
        var max = new CharacterCompiler(ContentHarness.Data).Compile(build).Evaluator.Evaluate("resource_max", TagSet.Empty).Value;

        var s = r.ResourceSeries;
        Assert.True(s.Count > 2);
        Assert.Equal(0, s[0].Time);
        for (var i = 1; i < s.Count - 1; i++)
            Assert.Equal(0.5, s[i].Time - s[i - 1].Time, 6);
        var last = s[^1].Time - s[^2].Time;
        Assert.InRange(last, 1e-9, 0.5 + 1e-6);
        Assert.All(s, x => Assert.InRange(x.Resource, 0, max + 1e-9));
        Assert.Contains(s, x => x.Resource > 0);
    }

    [Fact]
    public void Offensive_run_has_no_defense_totals()
    {
        var r = ContentHarness.Run(ContentHarness.LoadBuild("warrior_berserker_whirlwind"), "training");
        Assert.Null(r.Defense);
    }
}
