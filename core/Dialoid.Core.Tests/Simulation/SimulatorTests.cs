using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Simulation;

public class SimulatorTests
{
    private static readonly GameData Tiny = DataLoader.Load(FixturePaths.Tiny).Data!;

    /// <summary>Tiny fixture with test_bolt costing nothing and a 5.68% base crit, so that with
    /// the mage's 8 Dex (×0.04) the spell crit chance is exactly 6% (hand calculation of the brief).</summary>
    private static readonly GameData FreeBolt = Variant(s =>
    {
        var bolt = Node(s, "test_bolt");
        bolt["resource_cost"] = 0;
        bolt["crit_chance"] = 5.68;
    });

    private static GameData Variant(Action<JsonArray> mutateSkills)
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, "skills.json");
        var arr = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        mutateSkills(arr);
        File.WriteAllText(path, arr.ToJsonString());
        var r = DataLoader.Load(dir);
        Assert.Empty(r.Errors);
        return r.Data!;
    }

    private static JsonNode Node(JsonArray a, string id) => a.First(n => n!["id"]!.GetValue<string>() == id)!;

    private static Build Make(string cls, int level, string? weapon, params string[] skills)
    {
        var b = new Build { ClassId = cls, Level = level, WeaponId = weapon };
        foreach (var s in skills) b.Slots.Add(new SkillSlot { SkillId = s, Level = 1 });
        b.Rotation.AddRange(skills);
        return b;
    }

    private static Build Mage(int level, params string[] skills) => Make("test_mage", level, null, skills);

    private static SimResult Run(GameData d, Build b, string dummy, double duration = 60, int? pack = null,
        Action<SimState>? observe = null, SimMode mode = SimMode.Average)
    {
        var sim = new Simulator(d, MechanicRegistry.Default);
        if (observe != null) sim.StepObserver = observe;
        return sim.Run(b, new SimConfig(dummy, mode, DurationSec: duration, PackSizeOverride: pack));
    }

    private static int Count(SimResult r, string kind, string? prefix = null) =>
        r.Log.Count(e => e.Kind == kind && (prefix == null || e.Text.StartsWith(prefix, StringComparison.Ordinal)));

    [Fact]
    public void Average_mode_is_bit_identical_across_runs()
    {
        var builds = new Func<Build>[]
        {
            () => Make("test_warrior", 1, "test_sword", "test_slash"),
            () => Mage(4, "test_bolt", "test_spark"),
        };
        foreach (var make in builds)
        {
            var a = Run(Tiny, make(), "test_swarm");
            var b = Run(Tiny, make(), "test_swarm");
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.DpsSingleTarget), BitConverter.DoubleToInt64Bits(b.DpsSingleTarget));
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.DpsPack), BitConverter.DoubleToInt64Bits(b.DpsPack));
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.HitDamage), BitConverter.DoubleToInt64Bits(b.HitDamage));
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.DotDamage), BitConverter.DoubleToInt64Bits(b.DotDamage));
            Assert.Equal(a.DamageBySkill.ToList(), b.DamageBySkill.ToList());
            Assert.Equal(a.DamageByType.ToList(), b.DamageByType.ToList());
            Assert.Equal(a.Log, b.Log);
            Assert.True(a.DpsPack > 0);
        }
    }

    [Fact]
    public void Single_spell_dps_matches_hand_calculation()
    {
        // (14+20)/2 × expected crit (6%, ×1.5 → 1.03) × Int More (15 × 0.25% → 1.0375) / 0.75 s
        var r = Run(FreeBolt, Mage(1, "test_bolt"), "test_training");
        Assert.Empty(r.Violations);
        Assert.Equal(17 * 1.03 * 1.0375 / 0.75, r.DpsSingleTarget, 0.01);
        Assert.Equal(24.22, r.DpsSingleTarget, 0.01);
        Assert.Equal(r.DpsSingleTarget, r.DpsPack, 9);
        Assert.Equal(80, Count(r, "use"));
    }

    [Fact]
    public void Attack_rate_follows_weapon_speed_without_step_drift()
    {
        // Sword 1.4 APS → 0.714 s per slash (not a multiple of the 0.05 s step): exactly 84 uses in 60 s.
        var r = Run(Tiny, Make("test_warrior", 1, "test_sword", "test_slash"), "test_training");
        Assert.Equal(84, Count(r, "use"));
    }

    [Fact]
    public void Generator_used_when_spender_lacks_resource()
    {
        // Spark (generator) is listed first, yet bolt goes first while mana lasts.
        var b = Mage(4, "test_spark", "test_bolt");
        var sparkUseWithMana = false;
        double before = double.NaN;
        var r = Run(Tiny, b, "test_training", observe: s =>
        {
            if (s.LastUseTime.TryGetValue("test_spark", out var t) && Math.Abs(t - s.Time) < 1e-9 && before >= 12)
                sparkUseWithMana = true;
            before = s.Resource;
        });
        var uses = r.Log.Where(e => e.Kind == "use").ToList();
        Assert.StartsWith("test_bolt", uses[0].Text);
        Assert.Contains(uses, e => e.Text.StartsWith("test_spark"));
        Assert.True(uses.TakeWhile(e => e.Text.StartsWith("test_bolt")).Count() >= 8); // 100+ mana / 12
        Assert.False(sparkUseWithMana);
    }

    [Fact]
    public void Mana_regen_limits_spender_rate()
    {
        // Max mana 100 + 15 Int / 2 = 107.5; regen 5 × (1 + 2%) = 5.1/s; bolt costs 12.
        var r = Run(Tiny, Mage(1, "test_bolt"), "test_training");
        var uses = Count(r, "use");
        Assert.InRange(uses, 33, 36); // (107.5 + 60 × 5.1) / 12 ≈ 34.5
        var free = Run(FreeBolt, Mage(1, "test_bolt"), "test_training");
        Assert.True(r.DpsSingleTarget < free.DpsSingleTarget * 0.5);
        Assert.Contains(r.Log, e => e.Kind == "no_resource");
    }

    [Fact]
    public void Fury_decays_after_three_seconds_without_hits()
    {
        var d = Variant(s => Node(s, "test_slash")["cooldown"] = 100);
        var seen = new Dictionary<int, double>();
        Run(d, Make("test_warrior", 1, "test_sword", "test_slash"), "test_training", duration: 10,
            observe: s => seen[(int)Math.Round(s.Time * 100)] = s.Resource);
        var gain = 12 * 1.04; // will 10: class +2% and attribute +2% resource_gen
        Assert.Equal(gain, seen[0], 9);
        Assert.Equal(gain, seen[300], 9);
        Assert.Equal(gain - 5, seen[400], 9);
        Assert.Equal(0, seen[600], 9);
    }

    [Fact]
    public void Fury_starts_empty_and_mana_starts_full()
    {
        var fury = new List<double>();
        Run(Tiny, Make("test_warrior", 4, "test_greataxe", "test_slam"), "test_training", duration: 0.1,
            observe: s => fury.Add(s.Resource));
        Assert.All(fury, v => Assert.Equal(0, v));
        var mana = new List<double>();
        Run(Tiny, Mage(1), "test_training", duration: 0.05, observe: s => mana.Add(s.Resource));
        Assert.Equal(107.5, mana[0], 9);
    }

    [Fact]
    public void Resource_gain_counted_per_use_not_per_target()
    {
        var d = Variant(s =>
        {
            var slash = Node(s, "test_slash");
            slash["tags"]!.AsArray().Add("area");
            slash["behavior"] = new JsonObject { ["area_radius"] = 4 };
        });
        double afterFirst = double.NaN;
        var r = Run(d, Make("test_warrior", 1, "test_sword", "test_slash"), "test_swarm", duration: 0.05,
            observe: s => afterFirst = s.Resource);
        Assert.Equal(12 * 1.04, afterFirst, 9); // +12 (×1.04), not +60
        Assert.Equal(5, r.DpsPack / r.DpsSingleTarget, 9);
    }

    [Fact]
    public void Pack_dps_counts_coverage_targets()
    {
        // Chain 2 → 3 distinct targets.
        var spark = Run(Tiny, Mage(1, "test_spark"), "test_training", pack: 5);
        Assert.Equal(3, spark.DpsPack / spark.DpsSingleTarget, 9);

        // Explosion r = 3 on a pack of 5 (R = 4): 1 + 4 × 0.5625 = 3.25 targets on average.
        var d = Variant(s =>
        {
            var bolt = Node(s, "test_bolt");
            bolt["resource_cost"] = 0;
            bolt["tags"]!.AsArray().Add("area");
            bolt["behavior"] = new JsonObject { ["projectile_count"] = 1, ["area_radius"] = 3 };
        });
        var boom = Run(d, Mage(1, "test_bolt"), "test_training", pack: 5);
        Assert.Equal(3.25, boom.DpsPack / boom.DpsSingleTarget, 0.03);
    }

    [Fact]
    public void Respawning_targets_trigger_kills_and_continue()
    {
        var r = Run(FreeBolt, Mage(1, "test_bolt"), "test_swarm");
        Assert.True(Count(r, "kill") >= 10); // 100 life, ~18.2 per bolt → a kill every 6 casts
        Assert.Null(r.TimeToKill);
        Assert.True(r.Log.Last(e => e.Kind == "use").Time > 59);
        Assert.Equal(24.22, r.DpsSingleTarget, 0.01);
    }

    [Fact]
    public void Boss_without_respawn_gives_time_to_kill()
    {
        var perHit = 17 * 1.03 * 1.0375; // fire ignores armor
        var casts = (int)Math.Ceiling(1000 / perHit);
        var r = Run(FreeBolt, Mage(1, "test_bolt"), "test_boss");
        Assert.NotNull(r.TimeToKill);
        Assert.Equal((casts - 1) * 0.75, r.TimeToKill!.Value, 0.051);
        Assert.Equal(1, Count(r, "kill"));
        Assert.Equal(casts * perHit / r.TimeToKill.Value, r.DpsSingleTarget, 0.05);
    }

    [Fact]
    public void Armor_reduces_physical_hits_on_boss()
    {
        var w = Make("test_warrior", 1, "test_sword", "test_slash");
        var training = Run(Tiny, w, "test_training");
        var boss = Run(Tiny, Make("test_warrior", 1, "test_sword", "test_slash"), "test_boss");
        Assert.True(boss.HitDamage / boss.TimeToKill.GetValueOrDefault(60) < training.HitDamage / 60);
    }

    [Fact]
    public void Empty_or_non_damaging_rotation_returns_zero_without_hanging()
    {
        var empty = Run(Tiny, Mage(1), "test_training");
        Assert.Empty(empty.Violations);
        Assert.Equal(0, empty.DpsSingleTarget);
        Assert.Equal(0, empty.DpsPack);
        Assert.Null(empty.TimeToKill);

        var d = Variant(s =>
        {
            var bolt = Node(s, "test_bolt");
            bolt["spell_damage"] = new JsonObject();
            bolt["resource_cost"] = 0;
        });
        var none = Run(d, Mage(1, "test_bolt"), "test_boss");
        Assert.Equal(0, none.DpsPack);
        Assert.Null(none.TimeToKill);
        Assert.True(Count(none, "use") > 0);
    }

    [Fact]
    public void Never_enough_resource_returns_zero_without_hanging()
    {
        var r = Run(Tiny, Make("test_warrior", 4, "test_greataxe", "test_slam"), "test_training");
        Assert.Empty(r.Violations);
        Assert.Equal(0, r.DpsPack);
        Assert.Equal(1, Count(r, "no_resource")); // once per idle streak
        Assert.Equal(0, Count(r, "use"));
    }

    [Fact]
    public void Illegal_build_returns_violations_without_exception()
    {
        var b = Mage(1, "no_such_skill");
        var r = Run(Tiny, b, "test_training");
        Assert.NotEmpty(r.Violations);
        Assert.Equal(0, r.DpsPack);
        Assert.Empty(r.Log);
        Assert.Empty(r.DamageBySkill);

        var unknownDummy = Run(Tiny, Mage(1, "test_bolt"), "no_such_dummy");
        Assert.Contains(unknownDummy.Violations, v => v.Code == "unknown_dummy");
    }

    [Fact]
    public void Damage_by_skill_and_type_sum_to_total()
    {
        var d = Variant(s => Node(s, "test_slash")["ailment_chances"] = new JsonObject { ["bleed"] = 50 });
        var r = Run(d, Make("test_warrior", 1, "test_sword", "test_slash"), "test_swarm");
        Assert.True(r.DotDamage > 0);
        Assert.True(r.HitDamage > 0);
        var total = r.HitDamage + r.DotDamage;
        Assert.Equal(total, r.DamageBySkill.Values.Sum(), 6);
        Assert.Equal(total, r.DamageByType.Values.Sum(), 6);
        Assert.Equal(total, r.DpsPack * 60, 6);
        Assert.Contains(r.Log, e => e.Kind == "ailment");
    }

    [Fact]
    public void Roll_mode_is_reproducible_with_seed_and_logs_crits()
    {
        var b = Mage(1, "test_bolt");
        var d = Variant(s =>
        {
            var bolt = Node(s, "test_bolt");
            bolt["resource_cost"] = 0;
            bolt["crit_chance"] = 50;
        });
        var a = Run(d, b, "test_training", mode: SimMode.Roll);
        var c = Run(d, Mage(1, "test_bolt"), "test_training", mode: SimMode.Roll);
        Assert.Equal(a.DpsSingleTarget, c.DpsSingleTarget);
        Assert.Contains(a.Log, e => e.Kind == "crit");
    }

    public static IEnumerable<object[]> ConditionIds() => ConditionCatalog.Ids.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(ConditionIds))]
    public void Each_condition_evaluates_from_sim_state(string conditionId)
    {
        var s = new SimState(1, double.PositiveInfinity) { ResourceMax = 100, Resource = 40, LifeMax = 100, Life = 100, Time = 5.5 };
        var ctx = new SimConditionContext(s);
        bool Met(double? p = null, string? r = null) => ctx.IsMet(new Condition(conditionId, p, r));

        switch (conditionId)
        {
            case "target_ignited": AilmentCase("ignite", DamageType.Fire); break;
            case "target_chilled": AilmentCase("chill", DamageType.Cold); break;
            case "target_shocked": AilmentCase("shock", DamageType.Lightning); break;
            case "target_bleeding": AilmentCase("bleed", DamageType.Physical); break;
            case "target_poisoned": AilmentCase("poison", DamageType.Poison); break;
            case "target_frozen":
                Assert.False(Met());
                s.Targets[0].FreezeRemaining = 1;
                Assert.True(Met());
                break;
            case "resource_above_pct":
                Assert.False(Met(50));
                s.Resource = 60;
                Assert.True(Met(50));
                break;
            case "life_below_pct":
                Assert.False(Met(50));
                s.Life = 40;
                Assert.True(Met(50));
                break;
            case "recently_crit":
                Assert.False(Met());
                s.LastCritTime = 1.0; // 4.5 s ago
                Assert.False(Met());
                s.LastCritTime = 2.0; // 3.5 s ago
                Assert.True(Met());
                break;
            case "recently_used_skill":
                Assert.False(Met(null, "test_bolt"));
                s.LastUseTime["test_bolt"] = 3.0;
                Assert.True(Met(null, "test_bolt"));
                Assert.False(Met(null, "test_spark"));
                Assert.False(Met(1, "test_bolt")); // explicit window of 1 s
                break;
            case "channelling_for":
                s.ChannelSkillId = "test_beam";
                s.ChannelStart = 4.0;
                Assert.False(Met(2));
                s.ChannelStart = 3.5;
                Assert.True(Met(2));
                s.ChannelSkillId = null;
                Assert.False(Met(2));
                break;
            case "buff_active":
                Assert.False(Met(null, "haste"));
                s.Buffs["haste"] = 2;
                Assert.True(Met(null, "haste"));
                Assert.False(Met(null, "other"));
                break;
            default:
                Assert.Fail($"No test case for condition '{conditionId}'");
                break;
        }

        void AilmentCase(string ailment, DamageType type)
        {
            Assert.False(Met());
            s.Targets[0].Apply(new AilmentInstance(ailment, type, 1, 2, "x") { Magnitude = 10 }, 8, StackRule.StrongestOnly);
            Assert.True(Met());
        }
    }

    [Fact]
    public void Condition_catalog_matches_brief()
    {
        Assert.Equal(new[]
        {
            "target_ignited", "target_chilled", "target_shocked", "target_bleeding", "target_poisoned", "target_frozen",
            "resource_above_pct", "life_below_pct", "recently_crit", "recently_used_skill", "channelling_for", "buff_active",
        }, ConditionCatalog.Ids);
    }

    [Fact]
    public void Channel_condition_grows_with_channel_time()
    {
        var b = Mage(1, "test_beam");
        b.Slots[0].NodePoints["beam_focus"] = 1;
        var at = new Dictionary<int, bool>();
        var r = Run(Tiny, b, "test_training", duration: 3, observe: s =>
            at[(int)Math.Round(s.Time * 100)] = new SimConditionContext(s).IsMet(new Condition("channelling_for", 2)));
        Assert.False(at[100]);
        Assert.True(at[200]);
        // 4 ticks per second; the node doubles tick damage from 2 s on.
        var hits = r.Log.Where(e => e.Kind == "use").ToList();
        Assert.Equal(12, hits.Count);
    }

    [Fact]
    public void Channel_costs_resource_per_second()
    {
        // Beam: 10 mana per second, a tick every 0.25 s → 2.5 per tick; regen 5.1/s.
        var mana = new Dictionary<int, double>();
        Run(Tiny, Mage(1, "test_beam"), "test_training", duration: 1.05, observe: s => mana[(int)Math.Round(s.Time * 100)] = s.Resource);
        // At t = 1.0: 5 ticks paid (0, .25, .5, .75, 1.0), 1 s of regen.
        Assert.Equal(107.5 - 5 * 2.5 + 5.1, mana[100], 6);
    }
}
