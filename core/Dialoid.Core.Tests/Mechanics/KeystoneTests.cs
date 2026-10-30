using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;
using static Dialoid.Core.Tests.Mechanics.MechanicTestData;

namespace Dialoid.Core.Tests.Mechanics;

/// <summary>Spec 7.4: one test per keystone with the numbers of the task 16 table.</summary>
public class KeystoneTests
{
    private static CompiledCharacter Compile(GameData d, Build b) => new CharacterCompiler(d).Compile(b);

    private static double Stat(CompiledCharacter ch, string stat) => ch.Evaluator.Evaluate(stat, default).Value;

    private static double SkillStat(CompiledCharacter ch, string skill, string stat, double? baseValue = null)
    {
        var s = ch.Skills.Single(x => x.SkillId == skill);
        return ch.Evaluator.WithExtra(s.Modifiers).Evaluate(stat, s.Tags, null, baseValue).Value;
    }

    [Fact]
    public void Default_registry_has_all_keystone_mechanics()
    {
        foreach (var id in new[] { "blood_pact", "titan_will", "inevitability", "avatar_of_fire", "blade_dance", "stone_skin" })
            Assert.True(MechanicRegistry.Default.Contains(id), id);
    }

    [Fact]
    public void Blood_pact_removes_resource_turns_cost_into_cooldown_and_costs_life()
    {
        var d = Keystone("blood_pact");
        var plain = Compile(d, Make("test_mage", 2, false, null, "test_bolt", "test_beam", "test_spark"));
        var ch = Compile(d, Make("test_mage", 2, true, null, "test_bolt", "test_beam", "test_spark"));

        Assert.Equal(0, Stat(ch, "resource_max"));
        Assert.Equal(Stat(plain, "life") * 0.7, Stat(ch, "life"), 9); // 122 x 0.7 (Less)
        Assert.Equal(85.4, Stat(ch, "life"), 9);
        Assert.Equal(0, Stat(ch, "life_regen_pct"));

        var bolt = ch.Skills.Single(s => s.SkillId == "test_bolt");
        Assert.Equal(1.8, bolt.Base.Cooldown!.Value, 9); // cost 12 -> 1.5 s per 10 cost
        Assert.Equal(0, bolt.ResourceCost);
        Assert.Equal(0, SkillStat(ch, "test_bolt", "resource_cost", bolt.ResourceCost));
        Assert.Equal(new ChannelLimit(2, 3), ch.Skills.Single(s => s.SkillId == "test_beam").ChannelLimit);
        Assert.Null(plain.Skills.Single(s => s.SkillId == "test_beam").ChannelLimit);

        // Sim: the bolt is cast every 1.8 s although it would cost 12 mana.
        var r = Run(d, Make("test_mage", 2, true, null, "test_bolt"), duration: 6);
        Assert.Equal(new[] { 0, 1.8, 3.6, 5.4 }, UseTimes(r, "test_bolt"));

        // The channel lasts at most 2 s (8 ticks of 0.25 s), then the beam is on cooldown for 3 s.
        var beam = Run(d, Make("test_mage", 2, true, null, "test_beam"), duration: 6);
        Assert.Equal(new[] { 0, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 5, 5.25, 5.5, 5.75 }, UseTimes(beam, "test_beam"));

        // Generators generate nothing: the resource stays at 0.
        double maxResource = -1, resourceMax = -1;
        var spark = Run(d, Make("test_mage", 2, true, null, "test_spark"), duration: 3, observe: s =>
        {
            maxResource = Math.Max(maxResource, s.Resource);
            resourceMax = Math.Max(resourceMax, s.ResourceMax);
        });
        Assert.True(Count(spark, "use") > 0);
        Assert.Equal(0, maxResource);
        Assert.Equal(0, resourceMax);
    }

    [Fact]
    public void Blood_pact_channel_limit_counts_cumulative_channel_time_across_interleaved_skills()
    {
        var d = Keystone("blood_pact");
        var r = Run(d, Make("test_mage", 4, true, null, "test_bolt", "test_beam"), duration: 8);

        // bolt 0-0.75 (cd 1.8); beam 0.75-2.0 (1.25 s); bolt 2.0-2.75; beam 2.75-3.5 reaches 2 s in total -> cooldown until 6.5
        // (before the fix the bolt reset the channel and the beam never cooled down). The 6.5 channel holds the 7.4 bolt to 7.5.
        Assert.Equal(new[] { 0, 2, 3.8, 5.6, 7.5 }, UseTimes(r, "test_bolt"));
        Assert.Equal(new[] { 0.75, 1, 1.25, 1.5, 1.75, 2.75, 3, 3.25, 6.5, 6.75, 7, 7.25 }, UseTimes(r, "test_beam"));
    }

    [Fact]
    public void Titan_will_moves_the_damage_multiplier_from_primary_attribute_to_will()
    {
        var d = Keystone("titan_will");
        var plain = Compile(d, Make("test_mage", 60, false, null));
        var ch = Compile(d, Make("test_mage", 60, true, null));
        Assert.Equal(133, Stat(ch, "int"));
        Assert.Equal(69, Stat(ch, "will"));

        Assert.Equal(1.3325, plain.Evaluator.Evaluate("damage", default, null, 1).Value, 9); // Int 133 x 0.25 = +33.25% More
        var dmg = ch.Evaluator.Evaluate("damage", default, null, 1); // damage is a multiplier with base 1
        Assert.Equal(1.345, dmg.Value, 9); // Will 69 x 0.5 = +34.5% More
        Assert.DoesNotContain(dmg.Lines, l => l.Source.Kind == "attribute" && l.Kind == ModKind.More);
        var line = Assert.Single(dmg.Lines, l => l.Kind == ModKind.More);
        Assert.Equal(34.5, line.Value, 9);
        Assert.Equal(NodeId, line.Source.Id);
    }

    [Fact]
    public void Inevitability_removes_crits_adds_hit_more_and_lowers_ailment_chance()
    {
        var d = Keystone("inevitability", dir =>
        {
            Edit(dir, "skills.json", s =>
            {
                var bolt = Find(s, "test_bolt");
                bolt["resource_cost"] = 0;
                bolt["crit_chance"] = 50;
                bolt["ailment_chances"] = JsonNode.Parse("{\"ignite\":50}");
            });
            Edit(dir, "passives.json", p => Find(p, "small_c")["effects"]!.AsArray().Add(JsonNode.Parse(
                "{\"type\":\"grant_proc\",\"trigger\":\"on_crit\",\"action\":{\"apply_buff\":{\"id\":\"crit_buff\",\"duration\":1}}}")));
        });
        Build B(bool ks)
        {
            var b = Make("test_mage", 3, ks, null, "test_bolt");
            b.PassiveNodes.Add("small_c");
            return b;
        }

        var plainCh = Compile(d, B(false));
        var ch = Compile(d, B(true));
        Assert.Equal(0, SkillStat(ch, "test_bolt", "crit_chance", 50));
        var plainCrit = SkillStat(plainCh, "test_bolt", "crit_chance", 50);
        Assert.Equal(50.4, plainCrit, 9); // 50 + Dex 10 x 0.04

        // +18% More for hits only; DoT damage is unchanged.
        var bolt = ch.Skills.Single(s => s.SkillId == "test_bolt");
        var hitCtx = bolt.Tags.With("fire").With(HitCalculator.HitTag);
        Assert.Equal(1.18, ch.Evaluator.Evaluate("damage", hitCtx, null, 1).Value / plainCh.Evaluator.Evaluate("damage", hitCtx, null, 1).Value, 9);
        var dotCtx = TagSet.Of("dot", "fire", "skill:test_bolt", "ailment_ignite");
        Assert.Equal(plainCh.Evaluator.Evaluate("damage", dotCtx, null, 1).Value, ch.Evaluator.Evaluate("damage", dotCtx, null, 1).Value, 9);

        var plain = Run(d, B(false), duration: 6);
        var r = Run(d, B(true), duration: 6);
        var uses = Count(r, "use");
        Assert.Equal(8, uses);
        Assert.Equal(uses, Count(plain, "use"));

        // on_crit never fires (it does without the keystone).
        Assert.True(Count(plain, "proc") > 0);
        Assert.Equal(0, Count(r, "proc"));

        // Per hit: x1.18 instead of the expected crit multiplier 1 + 0.504 x 0.5.
        Assert.Equal(1.18 / (1 + plainCrit / 100 * 0.5), r.HitDamage / plain.HitDamage, 9);

        // Ignite chance 50% -> 35% (Less 30%): 4 of 8 hits vs 2 of 8.
        Assert.Equal(4, Count(plain, "ailment", "ignite"));
        Assert.Equal(2, Count(r, "ailment", "ignite"));
        Assert.Equal(35, SkillStat(ch, "test_bolt", "ignite_chance", 50), 9);
    }

    [Fact]
    public void Avatar_of_fire_converts_to_fire_drops_physical_and_fixes_ignite_stacks()
    {
        var d = Keystone("avatar_of_fire", dir => Edit(dir, "ascendancies.json", a =>
        {
            var effects = Find(a, "test_asc")["start"]!["effects"]!.AsArray();
            effects.Add(JsonNode.Parse("{\"type\":\"modifier\",\"stat\":\"ignite_max_stacks\",\"kind\":\"flat\",\"value\":2}"));
            effects.Add(JsonNode.Parse("{\"type\":\"modifier\",\"stat\":\"ignite_max_stacks\",\"kind\":\"override\",\"value\":3,\"priority\":100}"));
        }));

        Build Asc(bool ks)
        {
            var b = Make("test_warrior", 20, ks, "test_sword", "test_slash");
            b.AscendancyId = "test_asc";
            return b;
        }
        var plainCh = Compile(d, Asc(false));
        var ch = Compile(d, Asc(true));
        Assert.Equal(3, SkillStat(plainCh, "test_slash", "ignite_max_stacks"));
        Assert.Equal(1, SkillStat(ch, "test_slash", "ignite_max_stacks")); // Override 1 with the highest priority
        Assert.Equal(75, Stat(plainCh, "resist_fire_max"));
        Assert.Equal(60, Stat(ch, "resist_fire_max"));
        Assert.Equal(15, SkillStat(ch, "test_slash", "penetration_fire"));
        var conv = ch.Skills.Single(s => s.SkillId == "test_slash").Conversions
            .Select(c => (c.From, c.To, c.Percent)).OrderBy(c => c.From).ToArray();
        Assert.Equal(new[] { (DamageType.Physical, DamageType.Fire, 50.0), (DamageType.Lightning, DamageType.Fire, 100.0),
            (DamageType.Cold, DamageType.Fire, 100.0) }, conv);

        // Physical attack: 50% becomes fire (+15 penetration vs 0 resist: x1.15), the unconverted 50% deals nothing.
        var plainSlash = Run(d, Make("test_warrior", 2, false, "test_sword", "test_slash"), duration: 3);
        var slash = Run(d, Make("test_warrior", 2, true, "test_sword", "test_slash"), duration: 3);
        Assert.Equal(Count(plainSlash, "use"), Count(slash, "use"));
        Assert.Equal(0.5 * 1.15 * plainSlash.HitDamage, slash.HitDamage, 9);
        Assert.Equal(new[] { DamageType.Fire }, slash.DamageByType.Where(kv => kv.Value > 0).Select(kv => kv.Key));

        // Lightning spell: 100% becomes fire.
        var plainSpark = Run(d, Make("test_mage", 2, false, null, "test_spark"), duration: 3);
        var spark = Run(d, Make("test_mage", 2, true, null, "test_spark"), duration: 3);
        Assert.Equal(Count(plainSpark, "use"), Count(spark, "use"));
        Assert.Equal(1.15 * plainSpark.HitDamage, spark.HitDamage, 9);
        Assert.Equal(new[] { DamageType.Fire }, spark.DamageByType.Where(kv => kv.Value > 0).Select(kv => kv.Key));
    }

    /// <summary>Defensive fixture: an attacker hitting for <paramref name="phys"/> physical (and optionally fire) every <paramref name="interval"/> s.</summary>
    private static GameData Defensive(string keystone, double? phys = null, double? interval = null, double dodge = 0, double armor = 0) =>
        Keystone(keystone, dir =>
        {
            Edit(dir, "classes.json", n =>
            {
                var bonuses = Find(n, "test_warrior")["class_bonuses"]!.AsArray();
                foreach (var (stat, v) in new[] { ("dodge_chance", dodge), ("armor", armor) })
                    if (v != 0) bonuses.Add(JsonNode.Parse($"{{\"stat\":\"{stat}\",\"kind\":\"flat\",\"value\":{v}}}"));
            });
            Edit(dir, "dummies.json", n =>
            {
                var atk = Find(n, "test_attacker")["attack"]!;
                if (phys != null) atk["damage_per_hit"] = JsonNode.Parse($"{{\"physical\":{phys}}}");
                if (interval != null) atk["interval_sec"] = interval;
            });
        });

    [Fact]
    public void Blade_dance_triples_dodge_per_dex_raises_the_cap_to_60_and_zeroes_armor()
    {
        var d = Defensive("blade_dance", phys: 1, interval: 1, dodge: 80);
        var plain = Compile(d, Make("test_warrior", 2, false, null));
        var ch = Compile(d, Make("test_warrior", 2, true, null));
        Assert.Equal(11, Stat(ch, "dex"));
        Assert.Equal(80 + 11 * 0.05, Stat(plain, "dodge_chance"), 9);
        Assert.Equal(80 + 11 * 0.15, Stat(ch, "dodge_chance"), 9);
        Assert.Equal(60, Stat(ch, "dodge_chance_max"));
        Assert.Equal(34, Stat(plain, "armor")); // Str 17 x 2
        Assert.Equal(0, Stat(ch, "armor"));

        // 10 attacks of 1 x G(2) = 1.06 physical: 60% dodged (6), 4 land unmitigated.
        var plainRun = Run(d, Make("test_warrior", 2, false, null), "test_attacker", attacks: true, cap: 10.5);
        var r = Run(d, Make("test_warrior", 2, true, null), "test_attacker", attacks: true, cap: 10.5);
        Assert.Equal(10, Count(r, "dummy_attack"));
        Assert.Equal(5, Count(plainRun, "dodge"));
        Assert.Equal(6, Count(r, "dodge"));
        Assert.Equal(4 * 1.06, r.DamageTaken![DamageType.Physical], 9);
    }

    [Fact]
    public void Stone_skin_uses_half_armor_against_elements_removes_int_resists_and_dodge()
    {
        var d = Defensive("stone_skin", armor: 140);
        var plain = Compile(d, Make("test_warrior", 2, false, null));
        var ch = Compile(d, Make("test_warrior", 2, true, null));
        Assert.Equal(0.8, Stat(plain, "resist_fire"), 9); // Int 8 x 0.1
        Assert.Equal(0, Stat(ch, "resist_fire"));
        Assert.Equal(0, Stat(ch, "resist_cold"));
        Assert.Equal(0.55, Stat(plain, "dodge_chance"), 9);
        Assert.Equal(0, Stat(ch, "dodge_chance"));
        Assert.Equal(174, Stat(ch, "armor")); // 140 + Str 17 x 2
        Assert.Equal(0.5, ch.Rules[CharacterRules.ElementalArmorFraction]);

        // One attack at L2 (G = 1.06): physical 14.84 vs armor 174; fire 9.54 vs half armor 87 and no resist.
        var plainRun = Run(d, Make("test_warrior", 2, false, null), "test_attacker", attacks: true, cap: 4);
        var r = Run(d, Make("test_warrior", 2, true, null), "test_attacker", attacks: true, cap: 4);
        double phys = 14 * 1.06, fire = 9 * 1.06;
        Assert.Equal(phys * (1 - 174 / (174 + 10 * phys)), r.DamageTaken![DamageType.Physical], 9);
        Assert.Equal(fire * (1 - 87 / (87 + 10 * fire)), r.DamageTaken[DamageType.Fire], 9);
        Assert.Equal(fire * (1 - 0.008), plainRun.DamageTaken![DamageType.Fire], 9);
    }

    [Fact]
    public void Stone_skin_does_not_armor_dark_hits()
    {
        var d = Keystone("stone_skin", dir =>
        {
            Edit(dir, "classes.json", n => Find(n, "test_warrior")["class_bonuses"]!.AsArray()
                .Add(JsonNode.Parse("{\"stat\":\"armor\",\"kind\":\"flat\",\"value\":140}")));
            Edit(dir, "dummies.json", n =>
            {
                var atk = Find(n, "test_attacker")["attack"]!;
                atk["damage_per_hit"] = JsonNode.Parse("{\"dark\":100,\"fire\":100}");
            });
        });
        var plainRun = Run(d, Make("test_warrior", 2, false, null), "test_attacker", attacks: true, cap: 4);
        var r = Run(d, Make("test_warrior", 2, true, null), "test_attacker", attacks: true, cap: 4);
        double hit = 100 * 1.06;
        // Dark is not elemental: no armor against it, with or without the keystone; fire gets half of the armor (174 / 2).
        Assert.Equal(hit, plainRun.DamageTaken![DamageType.Dark], 9);
        Assert.Equal(hit, r.DamageTaken![DamageType.Dark], 9);
        Assert.Equal(hit * (1 - 87 / (87 + 10 * hit)), r.DamageTaken[DamageType.Fire], 9);
        Assert.True(r.DamageTaken[DamageType.Fire] < r.DamageTaken[DamageType.Dark]);
    }
}
