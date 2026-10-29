using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Content;
using Dialoid.Core.Tests.Simulation;

namespace Dialoid.Core.Tests.Combat;

/// <summary>
/// Per-hit defense breakdown (training room plan, task 3): every hit and DoT tick is a <see cref="DamageEvent"/> whose parts say how
/// much of the damage the target's armor, resistances and damage-taken multiplier stopped, and how much a keystone nullified.
/// </summary>
public class DamageEventTests
{
    private static readonly string[] ReferenceBuilds =
    {
        "warrior_berserker_whirlwind", "warrior_thunderer_crush", "warrior_fire_cleave_crit",
        "sorcerer_pyromancer_fireball", "sorcerer_cryomancer_ice_lance", "sorcerer_spark_hybrid",
        "keystones/avatar_of_fire_fit",
    };

    private static IEnumerable<string> Presets => ContentHarness.Data.Dummies.Keys.Order(StringComparer.Ordinal);

    public static IEnumerable<object[]> ReferenceBuildsAndModes() =>
        ReferenceBuilds.SelectMany(b => new[] { new object[] { b, SimMode.Average }, new object[] { b, SimMode.Roll } });

    [Theory]
    [MemberData(nameof(ReferenceBuildsAndModes))]
    public void Damage_parts_satisfy_identity_on_every_reference_and_preset(string buildPath, SimMode mode)
    {
        var build = ContentHarness.LoadBuild(buildPath);
        foreach (var dummy in Presets)
        {
            var (events, result) = Fight(build, dummy, mode);
            Assert.NotEmpty(events);
            foreach (var e in events)
                foreach (var p in e.Parts)
                    AssertIdentity(p, $"{buildPath} {dummy} {mode} {e.SkillId} t={e.Time}");
            foreach (var (skill, p) in result.Mitigation.BySkill) AssertIdentity(p, $"{buildPath} {dummy} {mode} total {skill}");
            AssertIdentity(result.Mitigation.Total, $"{buildPath} {dummy} {mode} total");
        }
    }

    [Theory]
    [MemberData(nameof(ReferenceBuildsAndModes))]
    public void Final_sum_of_events_equals_damage_by_skill(string buildPath, SimMode mode)
    {
        var build = ContentHarness.LoadBuild(buildPath);
        foreach (var dummy in Presets)
        {
            var (events, result) = Fight(build, dummy, mode);
            var sums = events.GroupBy(e => e.SkillId).ToDictionary(g => g.Key, g => g.Sum(e => e.Parts.Sum(p => p.Final)));
            // A skill whose damage is all nullified has events but no damage.
            Assert.All(sums.Where(kv => !result.DamageBySkill.ContainsKey(kv.Key)), kv => Assert.Equal(0, kv.Value));
            foreach (var (skill, damage) in result.DamageBySkill)
            {
                AssertClose(damage, sums[skill], 1e-6, $"{buildPath} {dummy} {mode} events {skill}");
                AssertClose(damage, result.Mitigation.BySkill[skill].Final, 1e-6, $"{buildPath} {dummy} {mode} totals {skill}");
            }
            AssertClose(result.HitDamage + result.DotDamage, result.Mitigation.Total.Final, 1e-6, $"{buildPath} {dummy} {mode} total");
        }
    }

    [Fact]
    public void Armor_cuts_physical_resist_cuts_elemental()
    {
        // Бронированный: armor 2000, no resists. The physical warrior's hits lose damage to armor only.
        var (armored, _) = Fight(ContentHarness.LoadBuild("warrior_berserker_whirlwind"), "armored", SimMode.Average);
        var physHits = armored.Where(e => !e.IsDot).SelectMany(e => e.Parts).Where(p => p.Type == DamageType.Physical).ToList();
        Assert.NotEmpty(physHits);
        Assert.All(physHits, p =>
        {
            Assert.True(p.ByArmor > 0, $"armor cut {p.ByArmor}");
            Assert.Equal(0, p.ByResist);
            Assert.Equal(p.BeforeDefense * Mitigation.ArmorReduction(2000, p.BeforeDefense), p.ByArmor, 9);
        });

        // Стихийный: resists 40, no armor. The pyromancer's fire loses damage to resistance only.
        var (elemental, _) = Fight(ContentHarness.LoadBuild("sorcerer_pyromancer_fireball"), "elemental", SimMode.Average);
        var fire = elemental.SelectMany(e => e.Parts).Where(p => p.Type == DamageType.Fire).ToList();
        Assert.NotEmpty(fire);
        Assert.All(fire, p =>
        {
            Assert.True(p.ByResist > 0, $"resist cut {p.ByResist}");
            Assert.Equal(0, p.ByArmor);
        });

        // The training dummy (no armor, no resists) stops nothing from the physical warrior.
        var (training, _) = Fight(ContentHarness.LoadBuild("warrior_berserker_whirlwind"), "training", SimMode.Average);
        Assert.All(training.Where(e => !e.IsDot).SelectMany(e => e.Parts).Where(p => p.Type == DamageType.Physical),
            p => Assert.Equal(0, p.ByArmor));
    }

    [Fact]
    public void Shock_shows_negative_taken_multiplier_part()
    {
        var (events, _) = Fight(ContentHarness.LoadBuild("sorcerer_spark_hybrid"), "training", SimMode.Average, collectLog: true,
            out var log);
        Assert.Contains(log, e => e.Kind == "ailment" && e.Text.StartsWith("shock", StringComparison.Ordinal));
        var parts = events.SelectMany(e => e.Parts).ToList();
        var boosted = parts.Where(p => p.ByTakenMultiplier < 0).ToList();
        Assert.NotEmpty(boosted);
        // The damage-taken multiplier is above 1 on those parts: Final exceeds what got past armor and resistance.
        Assert.All(boosted, p => Assert.True(p.Final > p.BeforeDefense - p.ByArmor - p.ByResist));
        Assert.DoesNotContain(parts, p => p.ByTakenMultiplier > 0);
    }

    [Fact]
    public void Avatar_of_fire_reports_nullified_physical()
    {
        // The keystone build files are sorcerers, whose spells deal no physical damage at all: nothing to nullify, nothing reported.
        var build = ContentHarness.LoadBuild("keystones/avatar_of_fire_fit");
        Assert.Contains("keystone_avatar_of_fire", build.PassiveNodes);
        var (sorcerer, _) = Fight(build, "training", SimMode.Average);
        Assert.NotEmpty(sorcerer);
        Assert.DoesNotContain(sorcerer.SelectMany(e => e.Parts), p => p.Nullified != 0);

        // A physical warrior under the keystone (hand-made: the tree rules may reject the node): half its weapon damage turns to
        // fire, the unconverted half is reported as nullified physical with nothing dealt.
        var warrior = ContentHarness.Clone(ContentHarness.LoadBuild("warrior_berserker_whirlwind"));
        warrior.PassiveNodes.Add("keystone_avatar_of_fire");
        var setup = FightSetup.Unchecked(ContentHarness.Data, MechanicRegistry.Default, warrior, "training");
        var (events, result) = Fight(new LiveFight(setup, new LiveFightConfig("training", SimMode.Average)), "training", SimMode.Average,
            collectLog: false, out _);
        var physical = events.Where(e => !e.IsDot).SelectMany(e => e.Parts).Where(p => p.Type == DamageType.Physical).ToList();
        Assert.Contains(physical, p => p.Nullified > 0);
        Assert.All(physical, p =>
        {
            Assert.Equal(0, p.Final);
            Assert.Equal(p.BeforeDefense, p.Nullified);
        });
        Assert.True(result.Mitigation.Total.Nullified > 0);
        Assert.False(result.DamageByType.TryGetValue(DamageType.Physical, out var phys) && phys > 0);
    }

    [Fact]
    public void Simulator_numbers_unchanged()
    {
        // Task 1's reference results (engine-baseline.json) for the reference builds × every preset × mode, bit for bit, log
        // included. CombatEngineTests compares every other build file too.
        CombatEngineTests.AssertMatchesBaseline(name => ReferenceBuilds.Contains(name[..^".json".Length]));
    }

    [Fact]
    public void Simulator_result_carries_mitigation_totals()
    {
        var build = ContentHarness.LoadBuild("warrior_berserker_whirlwind");
        var r = ContentHarness.Run(build, "armored");
        Assert.True(r.Mitigation.Total.ByArmor > 0);
        Assert.Equal(r.DamageBySkill.Keys, r.Mitigation.BySkill.Keys); // damage-less skills (battle_cry) report nothing
        Assert.Same(MitigationTotals.Empty, SimResult.Rejected(Array.Empty<RuleViolation>()).Mitigation);
    }

    [Fact]
    public void Undrained_damage_events_stay_bounded_and_never_stop()
    {
        var fight = LiveFight.Start(ContentHarness.Data, MechanicRegistry.Default, ContentHarness.LoadBuild("sorcerer_pyromancer_fireball"),
            new LiveFightConfig("training", SimMode.Average, InfiniteLife: true), out _)!;
        var rotation = fight.Setup.Rotation();
        var steps = (int)Math.Round(3600 / LiveFight.StepSec); // an hour nobody drains
        for (var i = 0; i < steps; i++) fight.Step(rotation);
        var events = fight.DrainDamage();
        Assert.InRange(events.Count, 1, 2 * LiveFight.DamageKeep);
        Assert.True(events[^1].Time > 3600 - 1, $"last event at {events[^1].Time}");
        Assert.Empty(fight.DrainDamage());
        for (var i = 0; i < 40; i++) fight.Step(rotation);
        Assert.NotEmpty(fight.DrainDamage());
    }

    private static (List<DamageEvent> Events, SimResult Result) Fight(Build build, string dummy, SimMode mode) =>
        Fight(build, dummy, mode, collectLog: false, out _);

    /// <summary>A live fight driven by the build's rotation for the simulator's default duration, draining damage every step.</summary>
    private static (List<DamageEvent> Events, SimResult Result) Fight(Build build, string dummy, SimMode mode, bool collectLog,
        out List<SimEvent> log)
    {
        var fight = LiveFight.Start(ContentHarness.Data, MechanicRegistry.Default, build, new LiveFightConfig(dummy, mode, Seed: 1), out var v);
        Assert.True(fight != null, string.Join("; ", v.Select(x => x.Message)));
        return Fight(fight, dummy, mode, collectLog, out log);
    }

    private static (List<DamageEvent> Events, SimResult Result) Fight(LiveFight fight, string dummy, SimMode mode, bool collectLog,
        out List<SimEvent> log)
    {
        var rotation = fight.Setup.Rotation();
        var events = new List<DamageEvent>();
        log = new List<SimEvent>();
        var steps = (int)Math.Round(new SimConfig(dummy, mode).DurationSec / LiveFight.StepSec);
        for (var i = 0; i < steps; i++)
        {
            fight.Step(rotation);
            events.AddRange(fight.DrainDamage());
            if (collectLog) log.AddRange(fight.DrainEvents());
        }
        return (events, fight.Snapshot());
    }

    private static void AssertIdentity(DamagePart p, string what)
    {
        var rest = p.BeforeDefense - p.ByArmor - p.ByResist - p.ByTakenMultiplier - p.Nullified;
        Assert.True(Math.Abs(rest - p.Final) <= 1e-9 * Math.Max(1, Math.Abs(p.BeforeDefense)),
            $"{what} {p.Type}: {p.BeforeDefense} − {p.ByArmor} − {p.ByResist} − {p.ByTakenMultiplier} − {p.Nullified} = {rest} ≠ {p.Final}");
    }

    private static void AssertClose(double expected, double actual, double relative, string what) =>
        Assert.True(Math.Abs(expected - actual) <= relative * Math.Max(1, Math.Abs(expected)), $"{what}: {expected} vs {actual}");
}
