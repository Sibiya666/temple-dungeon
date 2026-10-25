using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;
using static Dialoid.Core.Tests.Mechanics.MechanicTestData;

namespace Dialoid.Core.Tests.Mechanics;

/// <summary>Spec 10.4, Криомант: one test per mechanic and one per "simple" node, through real compile and sim.
/// The nodes sit on the start node of the test mage ascendancy (level 20 mage). test_frostbolt is a free cold bolt (0.75 s).</summary>
public class CryomancerTests
{
    private static CompiledCharacter Compile(GameData d, Build b) => new CharacterCompiler(d).Compile(b);

    private static string Mech(string id, string paramsJson = "{}", string? scope = null) =>
        $"{{\"type\":\"mechanic\",\"id\":\"{id}\",\"params\":{paramsJson}{(scope == null ? "" : $",\"scope\":\"{scope}\"")}}}";

    private static void Freeze100(string dir) => SetSkill(dir, "test_frostbolt", ("ailment_chances", "{\"freeze\":100}"));

    private static double SkillStat(CompiledCharacter ch, string skill, string stat, double? baseValue = null)
    {
        var s = ch.Skills.Single(x => x.SkillId == skill);
        return ch.Evaluator.WithExtra(s.Modifiers).Evaluate(stat, s.Tags, null, baseValue).Value;
    }

    /// <summary>Times (rounded) at which the primary target became frozen, and the freeze duration it got.</summary>
    private static List<(double Time, double Duration)> Freezes(GameData d, Build b, double duration, string dummy = "test_training")
    {
        var list = new List<(double, double)>();
        var was = false;
        Run(d, b, dummy, duration: duration, observe: s =>
        {
            var t = s.Targets[0];
            if (t.IsFrozen && !was) list.Add((Math.Round(s.Time, 6), t.FreezeRemaining));
            was = t.IsFrozen;
        });
        return list;
    }

    [Fact]
    public void Default_registry_has_all_cryomancer_mechanics()
    {
        foreach (var id in new[] { "shatter", "ice_armor", "grant_skill_node", "ailment_swap", "brittle", "chill_slow" })
            Assert.True(MechanicRegistry.Default.Contains(id), id);
    }

    [Fact]
    public void Shatter_removes_the_freeze_and_doubles_that_hit_and_starts_the_3_second_immunity()
    {
        // Bolts at 0, 0.75, 1.5, ...: the first freezes (1 s), the second hits the frozen target.
        var plain = Run(MageAsc("", Freeze100), Mage(true, "test_frostbolt"), duration: 4);
        (bool Frozen, double Immunity) at075 = default;
        var r = Run(MageAsc(Mech("shatter"), Freeze100), Mage(true, "test_frostbolt"), duration: 4, observe: s =>
        {
            if (Math.Abs(s.Time - 0.75) < 1e-9) at075 = (s.Targets[0].IsFrozen, s.Targets[0].FreezeImmunityRemaining);
        });
        var plainHits = HitAmounts(plain, "test_frostbolt");
        var hits = HitAmounts(r, "test_frostbolt");
        Assert.Equal(6, hits.Length);
        Assert.Equal(plainHits[0], hits[0], 9);
        Assert.Equal(2.0, hits[1] / plainHits[1], 9); // +100% More on the frozen target
        for (var i = 2; i < 5; i++) Assert.Equal(1.0, hits[i] / plainHits[i], 9); // immune until 3.75: no freeze, no bonus
        Assert.Equal((false, TargetState.FreezeImmunitySec), at075); // freeze removed by the hit, immunity started (spec 6.7)
    }

    [Fact]
    public void Ice_armor_gives_200_armor_for_4_seconds_when_a_freeze_lands()
    {
        // test_attacker (boss-like freeze x0.5): the bolt at 0 freezes it 0.5 s; immunity until 3.5, so the bolts at
        // 0.75..3.0 freeze nothing and the buff from t = 0 is still active at the 3.5 s attack.
        var buff = new List<(double Time, double Left)>();
        var d = MageAsc(Mech("ice_armor"), Freeze100);
        var r = Run(d, Mage(true, "test_frostbolt"), "test_attacker", attacks: true, cap: 3.6, observe: s =>
        {
            if (s.Buffs.TryGetValue("mechanic:ice_armor", out var left)) buff.Add((Math.Round(s.Time, 6), left));
        });
        Assert.Equal(0, buff[0].Time);
        Assert.Equal(4.0, buff[0].Left, 9);
        Assert.Equal(3.55, buff[^1].Time); // never refreshed: the freezes at 0.75..3.0 were refused (frozen or immune)
        Assert.Equal(4.0 - 3.55, buff[^1].Left, 6);

        var plain = Run(MageAsc("", Freeze100), Mage(true, "test_frostbolt"), "test_attacker", attacks: true, cap: 3.6);
        var armor = Compile(d, Mage(true, "test_frostbolt")).Evaluator.Evaluate("armor", default).Value;
        var hit = 14 * Growth.Gear(20);
        Assert.Equal(hit * (1 - Mitigation.ArmorReduction(armor, hit)), plain.DamageTaken![DamageType.Physical], 6);
        Assert.Equal(hit * (1 - Mitigation.ArmorReduction(armor + 200, hit)), r.DamageTaken![DamageType.Physical], 6);
    }

    [Fact]
    public void Grant_skill_node_is_a_registered_mechanic_and_grants_the_node_once()
    {
        // Морозный шар: the ascendancy grants fb_frost on the fireball; fork "orb" drops the paid fb_storm (its point returns).
        var grant = Mech("grant_skill_node", "{\"skill\":\"test_fireball\",\"node\":\"fb_frost\"}");
        var d = MageAsc(grant, registry: MechanicRegistry.Default);
        var b = Mage(true, "test_fireball");
        b.Slots[0] = new SkillSlot { SkillId = "test_fireball", Level = 4, NodePoints = { ["fb_heat"] = 2, ["fb_storm"] = 1 } };
        var fb = Compile(d, b).Skills.Single();
        Assert.Equal(1, fb.EffectiveNodePoints["fb_frost"]);
        Assert.False(fb.EffectiveNodePoints.ContainsKey("fb_storm"));
        Assert.Equal(2, fb.FreePoints); // level 4, paid 2 (fb_heat)
        Assert.Equal(DamageType.Cold, fb.DamageTypeOverride);
        Assert.Single(fb.Modifiers, m => m.Stat == "freeze_chance"); // the node's effects apply once
        Assert.Empty(fb.Mechanics); // the grant is consumed by the compiler, never a skill mechanic
        Assert.Equal(10, SkillStat(Compile(d, b), "test_fireball", "freeze_chance"));
    }

    [Fact]
    public void Ailment_swap_turns_ignite_chance_into_freeze_chance_and_ignite_damage_into_half_freeze_duration()
    {
        // fb_core 3 (+30 ignite chance) -> +30 freeze chance; fb_residual 3 (+45% ignite damage) -> +22.5% freeze duration.
        var grant = Mech("grant_skill_node", "{\"skill\":\"test_fireball\",\"node\":\"fb_frost\"}");
        var swap = Mech("ailment_swap", "{\"skill\":\"test_fireball\"}");
        Build B()
        {
            var b = Mage(true, "test_fireball");
            b.Slots[0] = new SkillSlot { SkillId = "test_fireball", Level = 9,
                NodePoints = { ["fb_heat"] = 2, ["fb_core"] = 3, ["fb_residual"] = 3, ["fb_storm"] = 1 } };
            return b;
        }

        var plain = Compile(MageAsc(grant), B());
        Assert.Equal(10, SkillStat(plain, "test_fireball", "freeze_chance"));
        Assert.Equal(30, SkillStat(plain, "test_fireball", "ignite_chance"));
        var d = MageAsc(grant + "," + swap);
        var ch = Compile(d, B());
        Assert.Equal(40, SkillStat(ch, "test_fireball", "freeze_chance"));
        Assert.Equal(0, SkillStat(ch, "test_fireball", "ignite_chance"));
        Assert.Equal(1.225, SkillStat(ch, "test_fireball", "freeze_duration", 1), 9);
        Assert.DoesNotContain(ch.Skills.Single().Modifiers, m => m.Stat == "damage" && m.RequiredTags.Contains("ailment_ignite"));

        // In the sim: 40% chance (accumulator: the 3rd cast at 1.5 s), 1.225 s freezes, and diminishing returns still hold.
        var freezes = Freezes(d, B(), 12);
        Assert.Equal((1.5, 1.225), (freezes[0].Time, Math.Round(freezes[0].Duration, 9)));
        Assert.All(freezes, f => Assert.Equal(1.225, f.Duration, 9));
        for (var i = 1; i < freezes.Count; i++)
            Assert.True(freezes[i].Time - freezes[i - 1].Time >= 1.225 + TargetState.FreezeImmunitySec - 1e-6);
        Assert.True(freezes.Count >= 2);
    }

    [Fact]
    public void Brittle_gives_crits_on_chilled_targets_15_more_damage()
    {
        // Crit chance 100: every bolt crits. The first bolt hits an unchilled target; later ones hit a chilled one.
        void Crit(string dir) => SetSkill(dir, "test_frostbolt", ("crit_chance", "100"));
        var plain = HitAmounts(Run(MageAsc("", Crit), Mage(true, "test_frostbolt"), duration: 3), "test_frostbolt");
        var brittle = HitAmounts(Run(MageAsc(Mech("brittle"), Crit), Mage(true, "test_frostbolt"), duration: 3), "test_frostbolt");
        Assert.Equal(4, brittle.Length);
        Assert.Equal(plain[0], brittle[0], 9);
        for (var i = 1; i < 4; i++) Assert.Equal(1.15, brittle[i] / plain[i], 9);

        // Crit chance 0: no crits, no bonus.
        const string NoCrit = "{\"type\":\"modifier\",\"stat\":\"crit_chance\",\"kind\":\"override\",\"value\":0}";
        var plain0 = HitAmounts(Run(MageAsc(NoCrit), Mage(true, "test_frostbolt"), duration: 3), "test_frostbolt");
        var brittle0 = HitAmounts(Run(MageAsc(NoCrit + "," + Mech("brittle")), Mage(true, "test_frostbolt"), duration: 3), "test_frostbolt");
        Assert.Equal(plain0, brittle0);

        // Average mode with the bolt's own crit chance c: expected hit (1 - c) + c x 1.5 x 1.15 instead of (1 - c) + c x 1.5.
        var ch = Compile(MageAsc(""), Mage(true, "test_frostbolt"));
        var c = SkillStat(ch, "test_frostbolt", "crit_chance", 6) / 100;
        var plainAvg = HitAmounts(Run(MageAsc(""), Mage(true, "test_frostbolt"), duration: 3), "test_frostbolt");
        var brittleAvg = HitAmounts(Run(MageAsc(Mech("brittle")), Mage(true, "test_frostbolt"), duration: 3), "test_frostbolt");
        Assert.Equal((1 - c + c * 1.5 * 1.15) / (1 - c + c * 1.5), brittleAvg[1] / plainAvg[1], 3); // log keeps 2 decimals
    }

    [Fact]
    public void Permafrost_is_chill_slow_25_plus_10_increased_cold_damage_against_chilled_targets()
    {
        // Вечная мерзлота: the slow (chill magnitude, hard-coded 15) needs the new chill_slow mechanic; the damage part is a
        // plain conditional modifier.
        const string ColdBonus =
            "{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"increased\",\"value\":10,\"tags\":[\"cold\"],\"conditions\":[{\"id\":\"target_chilled\"}]}";
        AilmentInstance? chill = null;
        var r = Run(MageAsc(ColdBonus + "," + Mech("chill_slow", "{\"slow\":25}")), Mage(true, "test_frostbolt"), duration: 3,
            observe: s => chill ??= s.Targets[0].Ailments.SingleOrDefault(a => a.AilmentId == "chill"));
        AilmentInstance? plainChill = null;
        var plain = Run(MageAsc(""), Mage(true, "test_frostbolt"), duration: 3,
            observe: s => plainChill ??= s.Targets[0].Ailments.SingleOrDefault(a => a.AilmentId == "chill"));
        Assert.Equal(15, plainChill!.Magnitude);
        Assert.Equal(25, chill!.Magnitude);

        var hits = HitAmounts(r, "test_frostbolt");
        var plainHits = HitAmounts(plain, "test_frostbolt");
        Assert.Equal(plainHits[0], hits[0], 9); // not chilled yet
        for (var i = 1; i < hits.Length; i++) Assert.Equal(1.1, hits[i] / plainHits[i], 2); // +10% Increased (no other Increased)
    }

    [Fact]
    public void Northern_lights_is_spell_freeze_chance_10_and_freezes_lasting_1_5_seconds()
    {
        const string Aurora = "{\"type\":\"modifier\",\"stat\":\"freeze_chance\",\"kind\":\"flat\",\"value\":10,\"tags\":[\"spell\"]}," +
                              "{\"type\":\"modifier\",\"stat\":\"freeze_duration\",\"kind\":\"increased\",\"value\":50}";
        var d = MageAsc(Aurora);
        var ch = Compile(d, Mage(true, "test_frostbolt"));
        Assert.Equal(10, SkillStat(ch, "test_frostbolt", "freeze_chance"));
        Assert.Equal(0, ch.Evaluator.Evaluate("freeze_chance", TagSet.Of("attack")).Value);
        Assert.Equal(1.5, SkillStat(ch, "test_frostbolt", "freeze_duration", 1), 9);

        var freezes = Freezes(d, Mage(true, "test_frostbolt"), 15);
        Assert.Equal(6.75, freezes[0].Time); // 10% chance: the 10th bolt
        Assert.All(freezes, f => Assert.Equal(1.5, f.Duration, 9));
        Assert.Empty(Freezes(MageAsc(""), Mage(true, "test_frostbolt"), 15));
    }

    [Fact]
    public void Glacier_is_an_ice_trail_zone_of_15_percent_per_second_for_3_seconds_through_plain_effects()
    {
        // Ледник: the spear (frostbolt stand-in) leaves a 3 s zone dealing 15% of the hit's ailment base per second as cold;
        // its hit chills the same targets. No mechanic needed: the zone takes the cold hit's type.
        const string Trail = "{\"type\":\"set_behavior\",\"param\":\"ground_duration\",\"value\":3,\"scope\":\"skill:test_frostbolt\"}," +
                             "{\"type\":\"set_behavior\",\"param\":\"ground_fraction\",\"value\":0.15,\"scope\":\"skill:test_frostbolt\"}";
        var r = Run(MageAsc(Trail, d => SetSkill(d, "test_frostbolt", ("cooldown", "100"))), Mage(true, "test_frostbolt"), duration: 4);
        var hit = Assert.Single(HitAmounts(r, "test_frostbolt"));
        Assert.Equal(0.15 * 3 * r.HitDamage, r.DotDamage, 6); // one hit, no defenses: zone dps = 0.15 x hit for 3 s
        Assert.Equal(r.HitDamage + r.DotDamage, r.DamageByType[DamageType.Cold], 6);
        Assert.True(Count(r, "ailment", "chill") > 0);
        Assert.Equal(r.HitDamage, hit, 2);
    }
}
