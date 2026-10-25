using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;
using static Dialoid.Core.Tests.Mechanics.MechanicTestData;

namespace Dialoid.Core.Tests.Mechanics;

/// <summary>Spec 10.4, Берсерк: one test per mechanic with the numbers of the task 16 table, through real compile and sim.
/// The mechanics sit on the ascendancy start node (source kind "ascendancy"); the level 20 warrior has 384 life,
/// Will 29 (+11.6% resource generation: a slash gives 12 x 1.116 = 13.392 Fury) and a sword at 1.4 attacks per second.</summary>
public class BerserkerTests
{
    private const double SlashGain = 12 * 1.116;
    private const double SlashTime = 1 / 1.4;

    private static CompiledCharacter Compile(GameData d, Build b) => new CharacterCompiler(d).Compile(b);

    /// <summary>Uses chain at their exact start time, but the log stamps the 0.05 s step in which the use started.</summary>
    private static double Logged(double start) => Math.Round(Math.Ceiling(start / 0.05 - 1e-6) * 0.05, 6);

    private static SimConditionContext Fury(double fury) =>
        new(new SimState(1, double.PositiveInfinity) { Resource = fury, ResourceMax = 100 });

    private static string Mech(string id, string paramsJson = "{}") => $"{{\"type\":\"mechanic\",\"id\":\"{id}\",\"params\":{paramsJson}}}";

    [Fact]
    public void Default_registry_has_all_berserker_mechanics()
    {
        foreach (var id in new[] { "berserk_frenzy", "insatiable", "reckless", "whirl_rage", "flesh_rend", "battle_trance", "blood_feast" })
            Assert.True(MechanicRegistry.Default.Contains(id), id);
    }

    [Fact]
    public void Berserk_frenzy_at_100_fury_gives_attacks_20_more_and_10_damage_taken_and_fury_never_decays()
    {
        var d = Asc(Mech("berserk_frenzy"));
        var ch = Compile(d, Warrior(true, "test_sword", "test_slash"));
        var attack = TagSet.Of("attack", "physical", "hit");
        var spell = TagSet.Of("spell", "fire", "hit");
        double Damage(TagSet tags, double fury) => ch.Evaluator.Evaluate("damage", tags, Fury(fury), 1).Value;

        Assert.Equal(1.2, Damage(attack, 100) / Damage(attack, 99), 9);
        Assert.Equal(Damage(spell, 99), Damage(spell, 100), 9);
        Assert.Equal(10, ch.Evaluator.Evaluate("damage_taken", default, Fury(100)).Value, 9);
        Assert.Equal(0, ch.Evaluator.Evaluate("damage_taken", default, Fury(99)).Value, 9);

        // Fury decays without the node (5/s, no hits ever), never with it.
        double plainFury = 0, fury = 0;
        Run(d, Warrior(false, "test_sword"), observe: s => { if (s.Time == 0) s.Resource = 50; plainFury = s.Resource; });
        Run(d, Warrior(true, "test_sword"), observe: s => { if (s.Time == 0) s.Resource = 50; fury = s.Resource; });
        Assert.True(plainFury < 25, $"plain fury {plainFury}");
        Assert.Equal(50, fury);

        // Slash: the 8th use fills Fury (7 x 13.392 = 93.7, then 100), so hits 9+ deal x1.2.
        var plain = HitAmounts(Run(d, Warrior(false, "test_sword", "test_slash"), duration: 8), "test_slash");
        var frenzy = HitAmounts(Run(d, Warrior(true, "test_sword", "test_slash"), duration: 8), "test_slash");
        Assert.Equal(12, frenzy.Length);
        for (var i = 0; i < frenzy.Length; i++)
            Assert.Equal(i < 8 ? 1 : 1.2, frenzy[i] / plain[i], 3);
    }

    [Fact]
    public void Insatiable_at_100_fury_pays_2_percent_life_per_10_fury_and_falls_back_to_fury()
    {
        var d = Asc(Mech("insatiable"));
        var slams = new List<(double Fury, double Life)>();
        void Observe(SimState s)
        {
            if (s.Time == 0) s.Resource = 100; // the slash at 0 gave 13.4 Fury; start the slams at 100
            if (s.LastUseTime.TryGetValue("test_slam", out var t) && t == s.Time) slams.Add((s.Resource, s.Life));
        }

        // Slam costs 35: 2% x 384 x 3.5 = 26.88 life, Fury stays 100 (life must be above the cost, else Fury pays).
        // Life regenerates in DPS runs too: life_regen_pct 0.5 x (1 + Will 29 x 0.2%) = 0.529% of 384 = 2.03136 life/s,
        // applied once per 0.05 s step, so between two observed slams life gains 2.03136 x (their time difference).
        // Slash at 0 (0.9 aps greataxe), then slam k at (k + 1) / 0.9: life 384 pays 15 times (regen buys one extra).
        const double regen = 0.529 / 100 * 384;
        var r = Run(d, Warrior(true, "test_greataxe", "test_slam", "test_slash"), duration: 19.5, observe: Observe);
        var times = UseTimes(r, "test_slam");
        Assert.Equal(Enumerable.Range(1, times.Length).Select(k => Logged(k / 0.9)).ToArray(), times);
        Assert.True(slams.Count >= 17, $"{slams.Count} slams");
        double life = 384, fury = 100, prev = 0;
        int lifePaid = 0;
        for (var k = 0; k < slams.Count; k++)
        {
            life = Math.Min(384, life + regen * (times[k] - prev));
            prev = times[k];
            if (fury >= 100 && life > 26.88) { life -= 26.88; lifePaid++; }
            else fury -= 35;
            Assert.Equal(fury, slams[k].Fury, 9);
            Assert.Equal(life, slams[k].Life, 9);
        }
        Assert.Equal(15, lifePaid);
        Assert.Equal(65, slams[15].Fury, 9); // life is no longer above the cost: Fury pays
        Assert.Equal(30, slams[16].Fury, 9); // below 100 Fury: normal cost

        // Without the node the slam always pays Fury.
        var plain = new List<(double Fury, double Life)>();
        Run(d, Warrior(false, "test_greataxe", "test_slam", "test_slash"), duration: 3, observe: s =>
        {
            if (s.Time == 0) s.Resource = 100;
            if (s.LastUseTime.TryGetValue("test_slam", out var t) && t == s.Time) plain.Add((s.Resource, s.Life));
        });
        Assert.Equal((65.0, 384.0), plain[0]);
    }

    [Fact]
    public void Reckless_zeroes_resists_and_gives_1_more_per_missing_life_percent_capped_at_40()
    {
        var d = Asc(Mech("reckless"));
        var plainCh = Compile(d, Warrior(false, "test_sword"));
        var ch = Compile(d, Warrior(true, "test_sword"));
        Assert.Equal(2.6, plainCh.Evaluator.Evaluate("resist_fire", default).Value, 9); // Int 26 x 0.1
        foreach (var r in new[] { "resist_fire", "resist_cold", "resist_lightning", "resist_poison" })
            Assert.Equal(0, ch.Evaluator.Evaluate(r, default).Value);

        double[] Hits(bool node, double lifeFraction) =>
            HitAmounts(Run(d, Warrior(node, "test_sword", "test_slash"), duration: 3,
                observe: s => s.Life = s.LifeMax * lifeFraction), "test_slash"); // held each step: life regenerates in DPS runs

        var plain = Hits(false, 0.8);
        var missing20 = Hits(true, 0.8);
        var missing70 = Hits(true, 0.3);
        Assert.Equal(1, missing20[0] / plain[0], 3); // the first hit lands before life drops
        for (var i = 1; i < plain.Length; i++)
        {
            Assert.Equal(1.2, missing20[i] / plain[i], 3);
            Assert.Equal(1.4, missing70[i] / plain[i], 3); // capped at +40%
        }
    }

    /// <summary>Whirlwind stand-in: attack channel, 10 Fury per second, a hit every 0.4 s. Fury starts full (no decay).</summary>
    private static void AddWhirl(string dir)
    {
        AddSkill(dir, "test_warrior", """
            {"id":"test_whirl","name":"Тестовый вихрь","description":"Канал","class_id":"test_warrior","unlock_level":1,
             "tags":["attack","melee","area","channel","physical","spender"],"weapon_effectiveness":50,"resource_cost":10,
             "weapon_types":["one_hand","two_hand"],"behavior":{"channel_interval":0.4,"area_radius":3}}
            """);
        Edit(dir, "classes.json", c => Find(c, "test_warrior")["resource"]!["decay_per_sec"] = 0);
    }

    [Fact]
    public void Whirl_rage_channels_free_above_half_fury_and_hits_10_percent_faster_per_second_up_to_50()
    {
        var d = Asc(Mech("whirl_rage", "{\"skill\":\"test_whirl\"}"), AddWhirl);

        // Tick k starts at t_k; the interval is 0.4 / (1 + min(50, 10 x whole seconds channelled) / 100).
        var expected = new List<double>();
        for (double t = 0; t <= 7.95 + 1e-9; t += 0.4 / (1 + Math.Min(50, 10 * Math.Floor(t + 1e-9)) / 100))
            expected.Add(t);

        double minFury = 100;
        var r = Run(d, Warrior(true, "test_sword", "test_whirl"), duration: 8, observe: s => minFury = Math.Min(minFury, s.Resource));
        Assert.Equal(expected.Select(Logged).ToArray(), UseTimes(r, "test_whirl"));
        Assert.Equal(100, minFury); // costs nothing while Fury > 50%

        // Without the node: a tick every 0.4 s for 4 Fury (10/s).
        double plainFury = 0;
        var plain = Run(d, Warrior(false, "test_sword", "test_whirl"), duration: 4, observe: s => plainFury = s.Resource);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => Math.Round(i * 0.4, 6)).ToArray(), UseTimes(plain, "test_whirl"));
        Assert.Equal(60, plainFury, 9);

        // At 50% Fury it costs again: 10/s, i.e. 4 x interval / 0.4 per (faster) tick.
        double fury = 0;
        expected = expected.Where(t => t <= 3.95 + 1e-9).ToList();
        r = Run(d, Warrior(true, "test_sword", "test_whirl"), duration: 4, observe: s =>
        {
            if (Math.Abs(s.Time - 2) < 1e-9) s.Resource = 50;
            fury = s.Resource;
        });
        Assert.Equal(expected.Select(Logged).ToArray(), UseTimes(r, "test_whirl")); // the ramp is unaffected
        var paid = expected.Where(t => t > 2).Sum(t => 4 / (1 + Math.Min(50, 10 * Math.Floor(t + 1e-9)) / 100));
        Assert.Equal(50 - paid, fury, 6);
    }

    [Fact]
    public void Flesh_rend_crit_on_a_bleeding_target_applies_one_more_bleed_stack()
    {
        GameData Data(bool node, double weaponCrit) => Asc(node ? Mech("flesh_rend") : "", dir =>
        {
            Edit(dir, "weapons.json", w => Find(w, "test_sword")["crit_chance"] = weaponCrit);
            Edit(dir, "skills.json", s => Find(s, "test_slash")["ailment_chances"] = JsonNode.Parse("{\"bleed\":100}"));
        });
        (int Applied, int Stacks) Slash(GameData d)
        {
            var stacks = 0;
            var r = Run(d, Warrior(true, "test_sword", "test_slash"), duration: 2, observe: s => stacks = s.Targets[0].Stacks("bleed"));
            Assert.Equal(3, Count(r, "use"));
            return (Count(r, "ailment", "bleed"), stacks);
        }

        // Every hit crits: hit 1 finds no bleed (1 stack), hits 2 and 3 add an extra stack each (5 stacks, not 3).
        Assert.Equal((3, 3), Slash(Data(false, 100)));
        Assert.Equal((5, 5), Slash(Data(true, 100)));
        // Crit chance 1.16% (weapon 0 + Dex 29 x 0.04): three hits make no crit event, so no extra stack.
        Assert.Equal((3, 3), Slash(Data(true, 0)));
    }

    /// <summary>Battle cry stand-in: a 5 s buff "battle_cry" with +30% increased damage, cooldown 20 s.</summary>
    private static void AddCry(string dir)
    {
        AddTags(dir, "shout", "buff");
        AddSkill(dir, "test_warrior", """
            {"id":"test_cry","name":"Тестовый клич","description":"Бафф","class_id":"test_warrior","unlock_level":1,
             "tags":["shout","buff"],"cast_time":0.5,"cooldown":20,
             "tree":[{"id":"cry_node","name":"Клич","description":"Бафф урона","max_points":1,"effects":[
               {"type":"grant_proc","trigger":"on_use","action":{"apply_buff":{"id":"battle_cry","duration":5,
                 "modifiers":[{"stat":"damage","kind":"increased","value":30}]}}}]}]}
            """);
    }

    [Fact]
    public void Battle_trance_turns_the_cry_damage_buff_into_5_fury_per_attack()
    {
        var d = Asc(Mech("battle_trance", "{\"buff\":\"battle_cry\"}"), AddCry);
        Build B(bool node)
        {
            var b = Warrior(node, "test_sword", "test_cry", "test_slash");
            b.Slots[0].NodePoints["cry_node"] = 1;
            return b;
        }

        // The cry's buff keeps its id and duration but loses its damage modifier.
        var cry = Compile(d, B(true)).Skills.Single(s => s.SkillId == "test_cry");
        var buff = Assert.IsType<ProcAction.ApplyBuff>(Assert.Single(cry.Procs).Action);
        Assert.Equal(("battle_cry", 5.0), (buff.BuffId, buff.DurationSec));
        Assert.Empty(buff.Mods);

        // Cry at 0 (0.5 s), then slashes at 0.5, 1.21, ... 4.79 under the buff (until 5.0) and 5.5+ after it.
        double plainFirst = 0, first = 0;
        var plain = Run(d, B(false), duration: 7, observe: s => { if (Math.Abs(s.Time - 0.5) < 1e-9) plainFirst = s.Resource; });
        var r = Run(d, B(true), duration: 7, observe: s => { if (Math.Abs(s.Time - 0.5) < 1e-9) first = s.Resource; });
        Assert.Equal(SlashGain, plainFirst, 9);
        Assert.Equal((12 + 5) * 1.116, first, 9); // +5 Fury per attack while the cry is active

        var plainHits = HitAmounts(plain, "test_slash");
        var hits = HitAmounts(r, "test_slash");
        Assert.Equal(10, hits.Length);
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(1.3, plainHits[i] / plainHits[8], 3); // the cry's +30% without the node
            Assert.Equal(1, hits[i] / hits[8], 3); // no damage buff with it
        }
    }

    [Fact]
    public void Blood_feast_is_expressed_as_bleed_stacks_flat_and_a_mechanic_for_attack_speed_per_stack()
    {
        // Кровавый пир: bleed_max_stacks +7 is a plain modifier; "+1% attack speed per bleed stack on the primary target,
        // cap 15%" needs the blood_feast mechanic (no stat or condition counts stacks).
        GameData Data(string mech) => Asc("{\"type\":\"modifier\",\"stat\":\"bleed_max_stacks\",\"kind\":\"flat\",\"value\":7}," + mech,
            dir => Edit(dir, "skills.json", s => Find(s, "test_slash")["ailment_chances"] = JsonNode.Parse("{\"bleed\":100}")));

        var d = Data(Mech("blood_feast"));
        var ch = Compile(d, Warrior(true, "test_sword", "test_slash"));
        var slash = ch.Skills.Single();
        Assert.Equal(15, ch.Evaluator.WithExtra(slash.Modifiers).Evaluate("bleed_max_stacks", slash.Tags).Value);

        // Use k (k = 1..) sees the k-1 bleeds of the earlier uses (all younger than 5 s): interval 1 / (1.4 x (1 + (k-1)%)).
        double[] Expected(double perStack)
        {
            var times = new List<double>();
            double t = 0;
            for (var k = 1; k <= 6; k++)
            {
                times.Add(Logged(t));
                t += SlashTime / (1 + Math.Min(15, perStack * (k - 1)) / 100);
            }
            return times.ToArray();
        }
        Assert.Equal(Expected(1), UseTimes(Run(d, Warrior(true, "test_sword", "test_slash"), duration: 3.8), "test_slash"));

        // Cap 15%: at 4% per stack the fifth use (4 stacks = 16%) is already capped.
        var capped = Data(Mech("blood_feast", "{\"per_stack\":4}"));
        Assert.Equal(Expected(4), UseTimes(Run(capped, Warrior(true, "test_sword", "test_slash"), duration: 3.4), "test_slash"));
    }
}
