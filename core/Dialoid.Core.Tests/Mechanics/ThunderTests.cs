using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;
using static Dialoid.Core.Tests.Mechanics.MechanicTestData;

namespace Dialoid.Core.Tests.Mechanics;

/// <summary>Spec 10.4, Громовержец: one test per mechanic and one per "simple" node, through real compile and sim.
/// Nodes sit on the ascendancy start (level 20 warrior). Грозовое оружие (25% of attack physical as lightning) is the
/// start node in the game, so the other tests include it where lightning damage matters.</summary>
public class ThunderTests
{
    private const string StormWeapon =
        "{\"type\":\"add_conversion\",\"from\":\"physical\",\"to\":\"lightning\",\"percent\":25,\"scope\":{\"tags\":[\"attack\"]}}";

    private static CompiledCharacter Compile(GameData d, Build b) => new CharacterCompiler(d).Compile(b);

    private static double SkillStat(CompiledCharacter ch, string skill, string stat)
    {
        var s = ch.Skills.Single(x => x.SkillId == skill);
        return ch.Evaluator.WithExtra(s.Modifiers).Evaluate(stat, s.Tags).Value;
    }

    private static string Mech(string id, string paramsJson = "{}", string? scope = null) =>
        $"{{\"type\":\"mechanic\",\"id\":\"{id}\",\"params\":{paramsJson}{(scope == null ? "" : $",\"scope\":\"{scope}\"")}}}";

    private static double Dmg(SimResult r, DamageType t) => r.DamageByType.GetValueOrDefault(t);

    [Fact]
    public void Default_registry_has_all_thunder_mechanics()
    {
        foreach (var id in new[] { "static_charge", "grounding", "ground_type" })
            Assert.True(MechanicRegistry.Default.Contains(id), id);
    }

    [Fact]
    public void Static_charge_charges_per_lightning_use_and_the_11th_spender_deals_double_but_copies_never_charge()
    {
        // Slam (spender, free here) converts 25% to lightning, so every use charges; each use also casts a triggered
        // slash copy that deals lightning too. Uses 1-10 charge, use 11 deals +100% More and resets, uses 12-21 charge, use 22 again.
        var proc = "{\"type\":\"grant_proc\",\"trigger\":\"on_use\",\"action\":{\"cast_skill\":{\"id\":\"test_slash\",\"damage_percent\":100}},\"scope\":\"skill:test_slam\"}";
        void Mutate(string dir) => Edit(dir, "skills.json", s => Find(s, "test_slam")["resource_cost"] = 0);
        var plainData = Asc(StormWeapon + "," + proc, Mutate);
        var d = Asc(StormWeapon + "," + proc + "," + Mech("static_charge"), Mutate);

        var plain = Run(plainData, Warrior(true, "test_greataxe", "test_slam"), duration: 25);
        var r = Run(d, Warrior(true, "test_greataxe", "test_slam"), duration: 25);
        var plainHits = HitAmounts(plain, "test_slam");
        var hits = HitAmounts(r, "test_slam");
        Assert.Equal(23, hits.Length);
        Assert.True(Count(r, "hit", "test_slash") >= 22); // the copies hit (with lightning) but never charge
        for (var i = 0; i < hits.Length; i++)
            Assert.Equal(i is 10 or 21 ? 2 : 1, hits[i] / plainHits[i], 3);
        Assert.Equal(HitAmounts(plain, "test_slash"), HitAmounts(r, "test_slash")); // the copies are never empowered

        // Only the copies deal lightning (conversion on the slash alone): the slam never charges, so it is never empowered.
        var copyOnly = StormWeapon.Replace("{\"tags\":[\"attack\"]}", "\"skill:test_slash\"");
        var r2 = Run(Asc(copyOnly + "," + proc + "," + Mech("static_charge"), Mutate), Warrior(true, "test_greataxe", "test_slam"), duration: 25);
        Assert.True(r2.DamageByType.GetValueOrDefault(DamageType.Lightning) > 0);
        Assert.Equal(plainHits, HitAmounts(r2, "test_slam"));
    }

    [Fact]
    public void Static_charge_spent_event_carries_the_charges_before_the_reset()
    {
        var proc = "{\"type\":\"grant_proc\",\"trigger\":\"on_use\",\"action\":{\"cast_skill\":{\"id\":\"test_slash\",\"damage_percent\":100}},\"scope\":\"skill:test_slam\"}";
        var d = Asc(StormWeapon + "," + proc + "," + Mech("static_charge"),
            dir => Edit(dir, "skills.json", s => Find(s, "test_slam")["resource_cost"] = 0));

        var r = Run(d, Warrior(true, "test_greataxe", "test_slam"), duration: 25);

        var spent = r.Log.Where(e => e.Kind == "mechanic").ToList();
        Assert.Equal(2, spent.Count);
        Assert.All(spent, e =>
        {
            Assert.Equal("static_charge", e.MechanicKey);
            Assert.Equal(10, e.Value);
        });
    }

    [Fact]
    public void Grounding_gives_shocks_at_least_20_effect_and_5_seconds()
    {
        GameData Data(string extra) => Asc(StormWeapon + extra,
            dir => Edit(dir, "skills.json", s => Find(s, "test_slash")["ailment_chances"] = JsonNode.Parse("{\"shock\":100}")));
        AilmentInstance FirstShock(GameData d)
        {
            AilmentInstance? shock = null;
            Run(d, Warrior(true, "test_sword", "test_slash"), duration: 1, observe: s =>
                shock ??= s.Targets[0].Ailments.SingleOrDefault(a => a.AilmentId == "shock"));
            return shock!;
        }

        var plain = FirstShock(Data(""));
        Assert.Equal((10.0, 3.0), (plain.Magnitude, plain.Remaining));
        var grounded = FirstShock(Data("," + Mech("grounding")));
        Assert.Equal((20.0, 5.0), (grounded.Magnitude, grounded.Remaining));
        // A stronger shock keeps its effect: 10 + 15 = 25.
        var strong = FirstShock(Data(",{\"type\":\"modifier\",\"stat\":\"shock_effect\",\"kind\":\"flat\",\"value\":15}," + Mech("grounding")));
        Assert.Equal((25.0, 5.0), (strong.Magnitude, strong.Remaining));
    }

    [Fact]
    public void Storm_weapon_is_a_conversion_of_25_percent_attack_physical_to_lightning()
    {
        var plain = Run(Asc(""), Warrior(true, "test_sword", "test_slash"), duration: 3);
        var r = Run(Asc(StormWeapon), Warrior(true, "test_sword", "test_slash"), duration: 3);
        Assert.Equal(Count(plain, "use"), Count(r, "use"));
        Assert.Equal(plain.HitDamage, r.HitDamage, 9); // no armor, no resists: the total is unchanged
        Assert.Equal(0.25 * plain.HitDamage, Dmg(r, DamageType.Lightning), 9);
        Assert.Equal(0.75 * plain.HitDamage, r.HitDamage - Dmg(r, DamageType.Lightning), 9); // (physical also holds bleed DoT)
    }

    [Fact]
    public void Chain_strike_is_one_more_chain_for_melee_attacks()
    {
        // With the start node every attack deals lightning, so "melee attacks dealing lightning" is the melee tag scope.
        var chain = "{\"type\":\"add_behavior\",\"param\":\"chain_count\",\"delta\":1,\"scope\":{\"tags\":[\"melee\"]}}";
        var plain = Run(Asc(StormWeapon), Warrior(true, "test_sword", "test_slash"), "test_swarm", duration: 3);
        var r = Run(Asc(StormWeapon + "," + chain), Warrior(true, "test_sword", "test_slash"), "test_swarm", duration: 3);
        Assert.Equal(Count(plain, "use"), Count(plain, "hit"));
        Assert.Equal(2 * Count(r, "use"), Count(r, "hit"));
    }

    [Fact]
    public void Storm_in_veins_is_attack_lightning_penetration_15_and_resist_cap_plus_10()
    {
        var storm = "{\"type\":\"modifier\",\"stat\":\"penetration_lightning\",\"kind\":\"flat\",\"value\":15,\"tags\":[\"attack\"]}," +
                    "{\"type\":\"modifier\",\"stat\":\"resist_lightning_max\",\"kind\":\"flat\",\"value\":10}";
        var plainData = Asc(StormWeapon);
        var d = Asc(StormWeapon + "," + storm);

        var ch = Compile(d, Warrior(true, "test_sword", "test_slash"));
        Assert.Equal(15, SkillStat(ch, "test_slash", "penetration_lightning"));
        Assert.Equal(0, ch.Evaluator.Evaluate("penetration_lightning", TagSet.Of("spell")).Value); // attacks only
        Assert.Equal(85, ch.Evaluator.Evaluate("resist_lightning_max", default).Value);

        var plain = Run(plainData, Warrior(true, "test_sword", "test_slash"), duration: 3);
        var r = Run(d, Warrior(true, "test_sword", "test_slash"), duration: 3);
        Assert.Equal(1.15 * Dmg(plain, DamageType.Lightning), Dmg(r, DamageType.Lightning), 9); // 0 resist - 15 penetration
        Assert.Equal(Dmg(plain, DamageType.Physical), Dmg(r, DamageType.Physical), 9);
    }

    [Fact]
    public void Thunder_axe_is_set_damage_type_lightning_plus_chains_on_the_axe_skill()
    {
        // The slash stands in for the throwing axe.
        var axe = "{\"type\":\"set_damage_type\",\"to\":\"lightning\",\"scope\":\"skill:test_slash\"}," +
                  "{\"type\":\"add_behavior\",\"param\":\"chain_count\",\"delta\":2,\"scope\":\"skill:test_slash\"}";
        var ch = Compile(Asc(axe), Warrior(true, "test_sword", "test_slash"));
        var slash = ch.Skills.Single();
        Assert.Equal(DamageType.Lightning, slash.DamageTypeOverride);
        Assert.True(slash.Tags.Contains("lightning") && !slash.Tags.Contains("physical"));

        var r = Run(Asc(axe), Warrior(true, "test_sword", "test_slash"), "test_swarm", duration: 3);
        Assert.Equal(new[] { DamageType.Lightning }, r.DamageByType.Where(kv => kv.Value > 0).Select(kv => kv.Key));
        Assert.Equal(3 * Count(r, "use"), Count(r, "hit"));
    }

    [Fact]
    public void Discharge_is_a_ground_zone_with_shock_and_needs_ground_type_for_lightning_damage()
    {
        // Разряд: Сокрушение (slam stand-in) leaves a 3 s zone dealing 20% of the hit per second, and shocks what it hits
        // (shock lasts 3 s, as long as the zone, on the same targets). A physical skill's zone would be physical: the zone
        // takes the largest share of the hit, so ground_type (new mechanic) makes it lightning ("грозовая земля").
        var zone = "{\"type\":\"set_behavior\",\"param\":\"ground_duration\",\"value\":3,\"scope\":\"skill:test_slam\"}," +
                   "{\"type\":\"set_behavior\",\"param\":\"ground_fraction\",\"value\":0.2,\"scope\":\"skill:test_slam\"}," +
                   "{\"type\":\"modifier\",\"stat\":\"shock_chance\",\"kind\":\"flat\",\"value\":100,\"scope\":\"skill:test_slam\"}";
        void Mutate(string dir) => Edit(dir, "skills.json", s => Find(s, "test_slam")["resource_cost"] = 0);
        var physical = Run(Asc(StormWeapon + "," + zone, Mutate), Warrior(true, "test_greataxe", "test_slam"), duration: 4);
        var r = Run(Asc(StormWeapon + "," + zone + "," + Mech("ground_type", "{\"type\":\"lightning\"}", "skill:test_slam"), Mutate),
            Warrior(true, "test_greataxe", "test_slam"), duration: 4);

        Assert.True(Count(r, "ailment", "shock") > 0);
        Assert.True(physical.DotDamage > 0);
        Assert.Equal(physical.DotDamage, r.DotDamage, 9); // no armor or resists: same amount, other type
        Assert.Equal(Dmg(physical, DamageType.Lightning) + physical.DotDamage, Dmg(r, DamageType.Lightning), 9);
        Assert.Equal(Dmg(physical, DamageType.Physical) - physical.DotDamage, Dmg(r, DamageType.Physical), 9);
    }
}
