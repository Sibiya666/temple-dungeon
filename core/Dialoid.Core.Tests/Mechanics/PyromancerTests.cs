using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;
using static Dialoid.Core.Tests.Mechanics.MechanicTestData;

namespace Dialoid.Core.Tests.Mechanics;

/// <summary>Spec 10.4, Пироман: one test per mechanic and one per "simple" node, through real compile and sim.
/// The nodes sit on the start node of the test mage ascendancy (level 20 mage); test_pack is an immortal pack of 5.</summary>
public class PyromancerTests
{
    private static CompiledCharacter Compile(GameData d, Build b) => new CharacterCompiler(d).Compile(b);

    private static string Mech(string id, string paramsJson = "{}", string? scope = null) =>
        $"{{\"type\":\"mechanic\",\"id\":\"{id}\",\"params\":{paramsJson}{(scope == null ? "" : $",\"scope\":\"{scope}\"")}}}";

    private static void Ignite100(string dir, string skill) => SetSkill(dir, skill, ("ailment_chances", "{\"ignite\":100}"));

    private static AilmentInstance Ignite(double remaining = 100) => new("ignite", DamageType.Fire, 0, remaining, "test_bolt");

    [Fact]
    public void Default_registry_has_all_pyromancer_mechanics()
    {
        foreach (var id in new[] { "spread_ignite", "ashes", "eternal_fire", "soul_heat", "fire_armor" })
            Assert.True(MechanicRegistry.Default.Contains(id), id);
    }

    [Fact]
    public void Spread_ignite_puts_the_ignite_on_every_pack_target_one_second_after_it_lands()
    {
        // Sandbox rule (spec 10.4): on a pack, an ignite counts as applied to all pack targets 1 s after it lands on one.
        void Mutate(string dir) => Ignite100(dir, "test_bolt");
        var spreadAt = new double?[5];
        AilmentInstance? source = null;
        var copies = new List<AilmentInstance>();
        Run(MageAsc(Mech("spread_ignite"), Mutate), Mage(true, "test_bolt"), "test_pack", duration: 2, observe: s =>
        {
            source ??= s.Targets[0].Ailments.SingleOrDefault(a => a.AilmentId == "ignite");
            for (var i = 1; i < 5; i++)
                if (spreadAt[i] == null && s.Targets[i].HasAilment("ignite"))
                {
                    spreadAt[i] = Math.Round(s.Time, 6);
                    copies.Add(s.Targets[i].Ailments.Single(a => a.AilmentId == "ignite"));
                }
        });
        Assert.NotNull(source);
        Assert.All(spreadAt.Skip(1), t => Assert.Equal(1.0, t)); // landed at 0, seen from the 1.0 s step on
        Assert.All(copies, c =>
        {
            Assert.Equal(source!.DamagePerSec, c.DamagePerSec, 9);
            Assert.Equal(3.0, c.Remaining, 6); // the same ignite, 1 s older
        });

        // A use whose ignites already landed on every target spreads nothing more (no extra stacks, even with room for them).
        void Fireball(string dir)
        {
            Ignite100(dir, "test_fireball");
            SetSkill(dir, "test_fireball", ("cooldown", "100")); // one cast
        }
        const string Stacks = "{\"type\":\"modifier\",\"stat\":\"ignite_max_stacks\",\"kind\":\"flat\",\"value\":4}";
        var maxStacks = 0;
        Run(MageAsc(Stacks + "," + Mech("spread_ignite"), Fireball), Mage(true, "test_fireball"), "test_pack", duration: 1.5,
            observe: s => maxStacks = Math.Max(maxStacks, Enumerable.Range(0, 5).Max(i => s.Targets[i].Stacks("ignite"))));
        Assert.Equal(1, maxStacks);

        var plainSpread = false;
        Run(MageAsc("", Mutate), Mage(true, "test_bolt"), "test_pack", duration: 2,
            observe: s => plainSpread |= Enumerable.Range(1, 4).Any(i => s.Targets[i].HasAilment("ignite")));
        Assert.False(plainSpread);
    }

    [Fact]
    public void Ashes_gives_hits_on_ignited_targets_1_more_per_pack_ignite_stack_capped_at_25()
    {
        // Ignites are seeded on the immortal pack after the first bolt (t = 0); the bolt hits the primary target only.
        double[] Hits(string effects, int primaryStacks, int otherStacks)
        {
            var r = Run(MageAsc(effects), Mage(true, "test_bolt"), "test_pack", duration: 2, observe: s =>
            {
                if (s.Time != 0) return;
                for (var i = 0; i < 5; i++)
                    for (var k = 0; k < (i == 0 ? primaryStacks : otherStacks); k++)
                        s.Targets[i].Apply(Ignite(), 99, StackRule.ReplaceWeakest);
            });
            return HitAmounts(r, "test_bolt");
        }

        var plain = Hits("", 3, 2);
        var eleven = Hits(Mech("ashes"), 3, 2);   // 3 + 4 x 2 = 11 stacks in the pack
        var capped = Hits(Mech("ashes"), 6, 6);   // 30 stacks: cap 25
        var unignited = Hits(Mech("ashes"), 0, 5); // the hit target is not ignited: no bonus
        Assert.Equal(3, plain.Length);
        Assert.Equal(plain[0], eleven[0], 9); // first hit before the ignites
        for (var i = 1; i < plain.Length; i++)
        {
            Assert.Equal(1.11, eleven[i] / plain[i], 3);
            Assert.Equal(1.25, capped[i] / plain[i], 3);
            Assert.Equal(1.0, unignited[i] / plain[i], 3);
        }

        // target_ignited is the hit target: a fireball hits the whole pack, only #3 is ignited (4 stacks): only its hits gain 4%.
        double[] TargetHits(string effects, int target)
        {
            var r = Run(MageAsc(effects), Mage(true, "test_fireball"), "test_pack", duration: 2, observe: s =>
            {
                if (s.Time != 0) return;
                for (var k = 0; k < 4; k++) s.Targets[3].Apply(Ignite(), 99, StackRule.ReplaceWeakest);
            });
            return HitAmounts(r, "test_fireball").Where((_, i) => r.Log.Where(e => e.Kind == "hit").ElementAt(i).Text.Contains($"#{target}:")).ToArray();
        }
        var plain3 = TargetHits("", 3);
        var ashes3 = TargetHits(Mech("ashes"), 3);
        Assert.Equal(1.04, ashes3[1] / plain3[1], 3);
        Assert.Equal(TargetHits("", 1)[1], TargetHits(Mech("ashes"), 1)[1], 9);
    }

    [Fact]
    public void Eternal_fire_makes_ignites_last_50_percent_longer_at_the_same_damage_per_second()
    {
        void Mutate(string dir)
        {
            Ignite100(dir, "test_bolt");
            SetSkill(dir, "test_bolt", ("cooldown", "100")); // one cast
        }
        AilmentInstance? First(GameData d, out SimResult r)
        {
            AilmentInstance? first = null;
            r = Run(d, Mage(true, "test_bolt"), duration: 8, observe: s => first ??= s.Targets[0].Ailments.SingleOrDefault());
            return first;
        }

        var plain = First(MageAsc("", Mutate), out var plainRun)!;
        var eternal = First(MageAsc(Mech("eternal_fire"), Mutate), out var run)!;
        Assert.Equal(4.0, plain.Remaining, 9);
        Assert.Equal(6.0, eternal.Remaining, 9);
        Assert.Equal(plain.DamagePerSec, eternal.DamagePerSec, 9);
        Assert.Equal(1.5 * plainRun.DotDamage, run.DotDamage, 6); // more in total
        Assert.Equal(plainRun.HitDamage, run.HitDamage, 9);
    }

    [Fact]
    public void Soul_heat_restores_1_percent_mana_per_ignited_target_hit_up_to_5_percent_per_use()
    {
        // The first fireball (t = 0) ignites every pack target it hits (explosion covers the whole pack).
        void Mutate(string dir)
        {
            Ignite100(dir, "test_fireball");
            Ignite100(dir, "test_bolt");
        }
        var soul = Mech("soul_heat", "{\"skills\":[\"test_fireball\",\"test_meteor\"]}");
        (double Mana, double Max) AfterFirstUse(string effects, string skill, int pack)
        {
            (double, double) v = default;
            Run(MageAsc(effects, Mutate), Mage(true, skill), "test_pack", duration: 0.1, pack: pack,
                observe: s => { if (s.Time == 0) v = (s.Resource, s.ResourceMax); });
            return v;
        }

        var (plain3, max) = AfterFirstUse("", "test_fireball", 3);
        Assert.Equal(max - 12, plain3, 9);
        Assert.Equal(plain3 + 0.03 * max, AfterFirstUse(soul, "test_fireball", 3).Mana, 9); // 3 ignited targets: 3%
        Assert.Equal(plain3 + 0.05 * max, AfterFirstUse(soul, "test_fireball", 7).Mana, 9); // 7 ignited targets: cap 5%
        Assert.Equal(AfterFirstUse("", "test_bolt", 3).Mana, AfterFirstUse(soul, "test_bolt", 3).Mana, 9); // not a listed skill

        // A target already ignited counts once per use: the second cast also restores 5% (not 10%) on 7 targets.
        var mana = new List<double>();
        Run(MageAsc(soul, Mutate), Mage(true, "test_fireball"), "test_pack", duration: 0.8, pack: 7,
            observe: s => { if (s.LastUseTime.TryGetValue("test_fireball", out var t) && t == s.Time) mana.Add(s.Resource); });
        var plainMana = new List<double>();
        Run(MageAsc("", Mutate), Mage(true, "test_fireball"), "test_pack", duration: 0.8, pack: 7,
            observe: s => { if (s.LastUseTime.TryGetValue("test_fireball", out var t) && t == s.Time) plainMana.Add(s.Resource); });
        Assert.Equal(2, mana.Count);
        Assert.Equal(plainMana[1] + 0.10 * max, mana[1], 6); // 5% per cast, two casts (regen is identical in both runs)
    }

    [Fact]
    public void Fire_armor_gives_30_increased_armor_and_10_fire_resist_cap_when_a_skill_makes_burning_ground()
    {
        // Пламенное кольцо stand-in: the fireball leaves burning ground (2 s, 25% of the ailment base per second).
        const string Ring = "{\"type\":\"set_behavior\",\"param\":\"ground_duration\",\"value\":2,\"scope\":\"skill:test_fireball\"}," +
                            "{\"type\":\"set_behavior\",\"param\":\"ground_fraction\",\"value\":0.25,\"scope\":\"skill:test_fireball\"}";
        var armor = Mech("fire_armor");
        double Stat(string effects, string stat, params string[] skills) =>
            Compile(MageAsc(effects), Mage(true, skills)).Evaluator.Evaluate(stat, default).Value;

        var plainArmor = Stat(Ring, "armor", "test_fireball");
        Assert.True(plainArmor > 0);
        Assert.Equal(1.3 * plainArmor, Stat(Ring + "," + armor, "armor", "test_fireball", "test_bolt"), 9);
        Assert.Equal(85, Stat(Ring + "," + armor, "resist_fire_max", "test_bolt", "test_fireball"));
        // No burning ground: no bonus (the fireball has no ring, or the ring skill is not slotted).
        Assert.Equal(plainArmor, Stat(armor, "armor", "test_fireball"), 9);
        Assert.Equal(75, Stat(Ring + "," + armor, "resist_fire_max", "test_bolt"));
        // A cold ground zone is not burning ground.
        var iceTrail = Ring.Replace("test_fireball", "test_frostbolt");
        Assert.Equal(plainArmor, Stat(iceTrail + "," + armor, "armor", "test_frostbolt"), 9);
    }

    [Fact]
    public void Meteor_shower_is_three_falls_at_50_percent_through_plain_effects()
    {
        // Метеоритный дождь: damage More -50 on the meteor plus two on_use casts of the meteor itself (triggered copies keep
        // the meteor's modifiers, so each fall deals 50%). No mechanic needed.
        const string Shower =
            "{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"more\",\"value\":-50,\"scope\":\"skill:test_meteor\"}," +
            "{\"type\":\"grant_proc\",\"trigger\":\"on_use\",\"action\":{\"cast_skill\":{\"id\":\"test_meteor\",\"damage_percent\":100}},\"scope\":\"skill:test_meteor\"}," +
            "{\"type\":\"grant_proc\",\"trigger\":\"on_use\",\"action\":{\"cast_skill\":{\"id\":\"test_meteor\",\"damage_percent\":100}},\"scope\":\"skill:test_meteor\"}";
        var plain = Run(MageAsc(""), Mage(true, "test_meteor"), duration: 3);
        var r = Run(MageAsc(Shower), Mage(true, "test_meteor"), duration: 3);
        var plainHits = HitAmounts(plain, "test_meteor");
        var hits = HitAmounts(r, "test_meteor");
        Assert.Equal(Count(plain, "use"), Count(r, "use"));
        Assert.Equal(3 * plainHits.Length, hits.Length);
        Assert.All(hits, h => Assert.Equal(0.5 * plainHits[0], h, 0.006)); // the log keeps 2 decimals
        Assert.Equal(1.5 * plain.HitDamage, r.HitDamage, 6);
    }
}
