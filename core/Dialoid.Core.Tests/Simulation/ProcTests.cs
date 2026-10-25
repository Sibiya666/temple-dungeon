using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Simulation;

public class ProcTests
{
    private const string Noop = "{\"apply_buff\":{\"id\":\"noop\",\"duration\":1}}";

    private static JsonNode Node(JsonArray a, string id) => a.First(n => n!["id"]!.GetValue<string>() == id)!;

    private static GameData Variant(Action<JsonArray> skills, Action<JsonArray>? classes = null)
    {
        var dir = FixturePaths.CopyTiny();
        void Edit(string file, Action<JsonArray> mutate)
        {
            var path = Path.Combine(dir, file);
            var arr = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
            mutate(arr);
            File.WriteAllText(path, arr.ToJsonString());
        }
        Edit("skills.json", skills);
        Edit("tags.json", AddTags);
        if (classes != null) Edit("classes.json", classes);
        var r = DataLoader.Load(dir);
        Assert.Empty(r.Errors);
        return r.Data!;
    }

    private static LoadResult TryLoad(Action<JsonArray> skills)
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, "skills.json");
        var arr = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        skills(arr);
        File.WriteAllText(path, arr.ToJsonString());
        var tagsPath = Path.Combine(dir, "tags.json");
        var tags = JsonNode.Parse(File.ReadAllText(tagsPath))!.AsArray();
        AddTags(tags);
        File.WriteAllText(tagsPath, tags.ToJsonString());
        return DataLoader.Load(dir);
    }

    private static void AddTags(JsonArray tags)
    {
        foreach (var id in new[] { "buff", "triggered" })
            if (!tags.Any(t => t!["id"]!.GetValue<string>() == id))
                tags.Add(JsonNode.Parse($"{{\"id\":\"{id}\",\"name\":\"{id}\"}}"));
    }

    /// <summary>Adds a tree node granting one proc (own-skill scope) to a skill.</summary>
    private static void AddProc(JsonArray skills, string skillId, string nodeId, string trigger, string action)
    {
        var skill = Node(skills, skillId);
        skill["tree"] ??= new JsonArray();
        skill["tree"]!.AsArray().Add(JsonNode.Parse(
            $"{{\"id\":\"{nodeId}\",\"name\":\"Тест\",\"description\":\"\",\"max_points\":1,\"effects\":[{{\"type\":\"grant_proc\",\"trigger\":{trigger},\"action\":{action}}}]}}"));
    }

    private static void AddDamageNode(JsonArray skills, string skillId, string nodeId, double increased)
    {
        var skill = Node(skills, skillId);
        skill["tree"] ??= new JsonArray();
        skill["tree"]!.AsArray().Add(JsonNode.Parse(
            $"{{\"id\":\"{nodeId}\",\"name\":\"Тест\",\"description\":\"\",\"max_points\":1,\"effects\":[{{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"increased\",\"value\":{increased}}}]}}"));
    }

    private static void Free(JsonArray skills, params string[] ids)
    {
        foreach (var id in ids) Node(skills, id)["resource_cost"] = 0;
    }

    private static Build MageBuild(int level, string[] rotation, params (string Skill, int Level, string[] Nodes)[] slots)
    {
        var b = new Build { ClassId = "test_mage", Level = level };
        foreach (var (skill, lvl, nodes) in slots)
        {
            var slot = new SkillSlot { SkillId = skill, Level = lvl };
            foreach (var n in nodes) slot.NodePoints[n] = 1;
            b.Slots.Add(slot);
        }
        b.Rotation.AddRange(rotation);
        return b;
    }

    private static SimResult Run(GameData d, Build b, string dummy = "test_training", double duration = 6, int? pack = null,
        Action<SimState>? observe = null, SimMode mode = SimMode.Average)
    {
        var sim = new Simulator(d, MechanicRegistry.Default);
        if (observe != null) sim.StepObserver = observe;
        var r = sim.Run(b, new SimConfig(dummy, mode, DurationSec: duration, PackSizeOverride: pack));
        Assert.Empty(r.Violations);
        return r;
    }

    private static int Count(SimResult r, string kind, string? contains = null) =>
        r.Log.Count(e => e.Kind == kind && (contains == null || e.Text.Contains(contains, StringComparison.Ordinal)));

    private static string CastSkill(string id, double percent) =>
        $"{{\"cast_skill\":{{\"id\":\"{id}\",\"damage_percent\":{percent.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}";

    [Fact]
    public void On_hit_and_on_crit_fire_at_most_once_per_use()
    {
        var d = Variant(s =>
        {
            Free(s, "test_spark");
            Node(s, "test_spark")["crit_chance"] = 100; // every use crits
            AddProc(s, "test_spark", "hit_proc", "\"on_hit\"", Noop);
            AddProc(s, "test_spark", "crit_proc", "\"on_crit\"", Noop);
        });
        var b = MageBuild(4, new[] { "test_spark" }, ("test_spark", 2, new[] { "hit_proc", "crit_proc" }));
        var r = Run(d, b, pack: 5, duration: 3); // chain 2 → 3 hits per use
        var uses = Count(r, "use");
        Assert.True(uses >= 4);
        Assert.Equal(uses, Count(r, "proc", "on_hit"));
        Assert.Equal(uses, Count(r, "proc", "on_crit"));
        Assert.True(Count(r, "hit") >= 3 * uses);
    }

    [Fact]
    public void Every_n_uses_counts_uses()
    {
        var d = Variant(s =>
        {
            Free(s, "test_bolt");
            AddProc(s, "test_bolt", "n4", "{\"every_n_uses\":4}", Noop);
        });
        var r = Run(d, MageBuild(1, new[] { "test_bolt" }, ("test_bolt", 1, new[] { "n4" })), duration: 6);
        Assert.Equal(8, Count(r, "use"));
        Assert.Equal(2, Count(r, "proc", "every_n_uses"));
    }

    [Fact]
    public void Every_n_hits_counts_hits()
    {
        var d = Variant(s =>
        {
            Free(s, "test_spark");
            Node(s, "test_spark")["behavior"]!["chain_count"] = 4; // 5 targets in a pack of 5
            AddProc(s, "test_spark", "n10", "{\"every_n_hits\":10}", Noop);
        });
        var r = Run(d, MageBuild(1, new[] { "test_spark" }, ("test_spark", 1, new[] { "n10" })), pack: 5, duration: 1.2);
        Assert.Equal(2, Count(r, "use"));
        Assert.Equal(10, Count(r, "hit"));
        Assert.Equal(1, Count(r, "proc", "every_n_hits"));
    }

    [Fact]
    public void On_crit_in_average_mode_uses_accumulator()
    {
        var d = Variant(s =>
        {
            Free(s, "test_bolt");
            Node(s, "test_bolt")["crit_chance"] = 24.68; // + 8 Dex × 0.04 = 25%
            AddProc(s, "test_bolt", "crit_proc", "\"on_crit\"", Noop);
        });
        var r = Run(d, MageBuild(1, new[] { "test_bolt" }, ("test_bolt", 1, new[] { "crit_proc" })), duration: 6);
        Assert.Equal(8, Count(r, "use"));
        Assert.Equal(2, Count(r, "proc", "on_crit"));
        // The accumulator fires on the 4th and the 8th use.
        var times = r.Log.Where(e => e.Kind == "use").Select(e => e.Time).ToList();
        var procTimes = r.Log.Where(e => e.Kind == "proc").Select(e => e.Time).ToList();
        Assert.Equal(new[] { times[3], times[7] }, procTimes);
    }

    private static GameData EchoData() => Variant(s =>
    {
        Free(s, "test_spark", "test_bolt");
        AddDamageNode(s, "test_bolt", "bolt_dmg", 50);
        AddProc(s, "test_spark", "echo", "\"on_hit\"", CastSkill("test_bolt", 100));
    });

    private static double BoltDamage(GameData d, Build b, double duration = 3) =>
        Run(d, b, duration: duration).DamageBySkill.GetValueOrDefault("test_bolt");

    [Fact]
    public void Triggered_copy_uses_slotted_tree_and_level()
    {
        var d = EchoData();
        var plain = MageBuild(8, new[] { "test_spark" },
            ("test_spark", 5, new[] { "echo" }), ("test_bolt", 5, Array.Empty<string>()));
        var treed = MageBuild(8, new[] { "test_spark" },
            ("test_spark", 5, new[] { "echo" }), ("test_bolt", 5, new[] { "bolt_dmg" }));
        var low = MageBuild(8, new[] { "test_spark" },
            ("test_spark", 5, new[] { "echo" }), ("test_bolt", 1, Array.Empty<string>()));
        var basePlain = BoltDamage(d, plain);
        Assert.True(basePlain > 0);
        Assert.Equal(1.5, BoltDamage(d, treed) / basePlain, 6);       // slotted tree node +50%
        Assert.Equal(1.2, basePlain / BoltDamage(d, low), 6);         // slotted level 5 vs 1: 1 + 0.05 × 4
    }

    [Fact]
    public void Triggered_copy_of_unslotted_skill_uses_source_level_no_tree()
    {
        var d = EchoData();
        var slotted = MageBuild(8, new[] { "test_spark" },
            ("test_spark", 5, new[] { "echo" }), ("test_bolt", 5, Array.Empty<string>()));
        var unslotted = MageBuild(8, new[] { "test_spark" }, ("test_spark", 5, new[] { "echo" }));
        var expected = BoltDamage(d, slotted);
        Assert.True(expected > 0);
        Assert.Equal(expected, BoltDamage(d, unslotted), 6);          // source level 5, no tree points
        var lowSource = MageBuild(8, new[] { "test_spark" }, ("test_spark", 1, new[] { "echo" }));
        Assert.Equal(1.2, expected / BoltDamage(d, lowSource), 6);
    }

    [Fact]
    public void Triggered_copy_scales_damage_by_percent()
    {
        var full = EchoData();
        var half = Variant(s =>
        {
            Free(s, "test_spark", "test_bolt");
            AddProc(s, "test_spark", "echo", "\"on_hit\"", CastSkill("test_bolt", 50));
        });
        var b = MageBuild(8, new[] { "test_spark" }, ("test_spark", 5, new[] { "echo" }));
        Assert.Equal(0.5, BoltDamage(half, b) / BoltDamage(full, b), 6);
    }

    [Fact]
    public void Triggered_copy_costs_nothing_and_generates_nothing()
    {
        // Mana trace must be identical with and without the proc: the copy of the 12-mana bolt costs 0,
        // and the copy of the generator spark (+3) adds nothing.
        List<double> Trace(GameData d, Build b)
        {
            var t = new List<double>();
            Run(d, b, observe: s => t.Add(s.Resource), duration: 5);
            return t;
        }

        var costData = Variant(s => { Free(s, "test_spark"); AddProc(s, "test_spark", "echo", "\"on_hit\"", CastSkill("test_bolt", 100)); });
        var withCost = Trace(costData, MageBuild(1, new[] { "test_spark" }, ("test_spark", 1, new[] { "echo" })));
        var withoutCost = Trace(costData, MageBuild(1, new[] { "test_spark" }, ("test_spark", 1, Array.Empty<string>())));
        Assert.Equal(withoutCost, withCost);

        var genData = Variant(s => AddProc(s, "test_bolt", "echo", "\"on_hit\"", CastSkill("test_spark", 100)));
        var withGen = Trace(genData, MageBuild(1, new[] { "test_bolt" }, ("test_bolt", 1, new[] { "echo" })));
        var withoutGen = Trace(genData, MageBuild(1, new[] { "test_bolt" }, ("test_bolt", 1, Array.Empty<string>())));
        Assert.Equal(withoutGen, withGen);
        Assert.Contains(withGen, v => v < 100); // the bolt itself did cost mana
    }

    [Fact]
    public void Triggered_copy_does_not_count_for_counters()
    {
        // Each bolt use casts a spark copy (3 hits in a pack of 5). The bolt's own counters see only the bolt's uses/hits.
        var d = Variant(s =>
        {
            Free(s, "test_bolt");
            AddProc(s, "test_bolt", "echo", "\"on_hit\"", CastSkill("test_spark", 100));
            AddProc(s, "test_bolt", "n3hits", "{\"every_n_hits\":3}", Noop);
            AddProc(s, "test_bolt", "n2uses", "{\"every_n_uses\":2}", Noop);
        });
        var r = Run(d, MageBuild(4, new[] { "test_bolt" }, ("test_bolt", 3, new[] { "echo", "n3hits", "n2uses" })),
            pack: 5, duration: 6);
        var uses = Count(r, "use");
        Assert.Equal(8, uses);
        Assert.Equal(uses / 3, Count(r, "proc", "every_n_hits"));
        Assert.Equal(uses / 2, Count(r, "proc", "every_n_uses"));
        Assert.Equal(uses, Count(r, "proc", "cast_skill"));
    }

    [Fact]
    public void Triggered_skill_never_fires_procs()
    {
        // A casts B on hit, B casts A on hit: B's copy must not launch A again.
        var d = Variant(s =>
        {
            Free(s, "test_spark", "test_bolt");
            AddProc(s, "test_spark", "to_b", "\"on_hit\"", CastSkill("test_bolt", 100));
            AddProc(s, "test_bolt", "to_a", "\"on_hit\"", CastSkill("test_spark", 100));
        });
        var b = MageBuild(8, new[] { "test_spark" },
            ("test_spark", 1, new[] { "to_b" }), ("test_bolt", 1, new[] { "to_a" }));
        var r = Run(d, b, duration: 3);
        var uses = Count(r, "use");
        Assert.True(uses > 0);
        Assert.Equal(uses, Count(r, "proc", "test_spark: on_hit"));   // A's own proc, once per use
        Assert.Equal(0, Count(r, "proc", "test_bolt:"));               // B's proc never fires on the copy
        Assert.Equal(uses, Count(r, "proc_blocked"));                 // runtime guard logged each time
        Assert.Equal(uses, Count(r, "hit", "test_bolt"));             // exactly one copy per use, no chain
    }

    [Fact]
    public void Triggered_copy_kill_does_not_fire_procs()
    {
        // Pack of weak targets: spark's copy of bolt kills; bolt's on_kill would grant a buff but copies fire nothing.
        var d = Variant(s =>
        {
            Free(s, "test_spark", "test_bolt");
            AddProc(s, "test_spark", "to_b", "\"on_use\"", CastSkill("test_bolt", 100));
            AddProc(s, "test_bolt", "kill", "\"on_kill\"", Noop);
        });
        var b = MageBuild(8, new[] { "test_spark" },
            ("test_spark", 1, new[] { "to_b" }), ("test_bolt", 1, new[] { "kill" }));
        var r = Run(d, b, dummy: "test_swarm", duration: 3);
        Assert.True(Count(r, "kill") > 0);
        Assert.Equal(0, Count(r, "proc", "on_kill"));
    }

    [Fact]
    public void On_kill_fires_for_the_skill_that_dealt_the_killing_hit()
    {
        var d = Variant(s =>
        {
            Free(s, "test_bolt");
            AddProc(s, "test_bolt", "kill", "\"on_kill\"", Noop);
        });
        var r = Run(d, MageBuild(1, new[] { "test_bolt" }, ("test_bolt", 1, new[] { "kill" })), dummy: "test_swarm", duration: 6);
        var kills = Count(r, "kill");
        Assert.True(kills > 0);
        Assert.Equal(kills, Count(r, "proc", "on_kill"));
    }

    [Fact]
    public void Dot_and_zone_kills_by_a_triggered_copy_do_not_fire_on_kill()
    {
        // The rotation skill (spark) only echoes bolt copies; all real damage is the copies' hits and ignite DoT.
        // Bolt is slotted with on_kill -> cast bolt, which must never fire for kills the copies cause.
        var d = Variant(s =>
        {
            Free(s, "test_spark", "test_bolt");
            Node(s, "test_bolt")["ailment_chances"] = new JsonObject { ["ignite"] = 100 };
            Node(s, "test_bolt")["behavior"]!["ground_duration"] = 3;
            Node(s, "test_bolt")["behavior"]!["ground_fraction"] = 40;
            Node(s, "test_bolt")["spell_damage"] = JsonNode.Parse("{\"fire\":[1,1]}");
            AddProc(s, "test_spark", "echo", "\"on_hit\"", CastSkill("test_bolt", 100));
            AddProc(s, "test_bolt", "chain", "\"on_kill\"", CastSkill("test_bolt", 100));
        });
        var b = MageBuild(8, new[] { "test_spark" },
            ("test_spark", 1, new[] { "echo" }), ("test_bolt", 1, new[] { "chain" }));
        var r = Run(d, b, dummy: "test_swarm", duration: 30);
        Assert.True(Count(r, "kill") > 0);
        Assert.Equal(0, Count(r, "proc", "on_kill"));
    }

    [Fact]
    public void Copy_does_not_advance_the_slotted_skills_accumulators()
    {
        List<double> CritProcTimes(bool withCopy)
        {
            var d = Variant(s =>
            {
                Free(s, "test_bolt");
                Node(s, "test_bolt")["crit_chance"] = 24.68; // 25%
                AddProc(s, "test_bolt", "crit_proc", "\"on_crit\"", Noop);
                AddProc(s, "test_bolt", "self", "\"on_use\"", CastSkill("test_bolt", 100));
            });
            var nodes = withCopy ? new[] { "crit_proc", "self" } : new[] { "crit_proc" };
            var r = Run(d, MageBuild(1 + 3, new[] { "test_bolt" }, ("test_bolt", 3, nodes)), duration: 6);
            Assert.Equal(8, Count(r, "use"));
            return r.Log.Where(e => e.Kind == "proc" && e.Text.Contains("on_crit")).Select(e => e.Time).ToList();
        }
        Assert.Equal(CritProcTimes(false), CritProcTimes(true));
        Assert.Equal(2, CritProcTimes(true).Count);
    }

    [Fact]
    public void Apply_ailment_proc_uses_the_triggering_hit_base()
    {
        var d = Variant(s =>
        {
            Free(s, "test_bolt");
            AddProc(s, "test_bolt", "burn", "\"on_hit\"", "{\"apply_ailment\":\"ignite\"}");
        });
        var r = Run(d, MageBuild(1, new[] { "test_bolt" }, ("test_bolt", 1, new[] { "burn" })), duration: 3);
        var uses = Count(r, "use");
        Assert.Equal(uses, Count(r, "ailment", "ignite -> #0 (test_bolt, proc)"));
        Assert.True(r.DotDamage > 0);
    }

    [Fact]
    public void Validator_rejects_proc_on_triggered_only_skill()
    {
        var r = TryLoad(s =>
        {
            var echo = JsonNode.Parse(Node(s, "test_bolt").ToJsonString())!;
            echo["id"] = "test_echo";
            echo["class_id"] = null;
            echo["is_triggered_only"] = true;
            s.Add(echo);
            AddProc(s, "test_echo", "bad", "\"on_hit\"", Noop);
        });
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "skills.json" && e.Id == "test_echo" && e.Message.Contains("triggered"));
    }

    [Fact]
    public void Validator_rejects_cast_of_unknown_skill()
    {
        var r = TryLoad(s => AddProc(s, "test_bolt", "bad", "\"on_hit\"", CastSkill("no_such_skill", 100)));
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "skills.json" && e.Id == "test_bolt" && e.Message.Contains("no_such_skill"));
    }

    // ---- Buffs ----

    private static GameData CryData(double cooldown) => Variant(s =>
    {
        Free(s, "test_bolt");
        s.Add(JsonNode.Parse(
            "{\"id\":\"test_cry\",\"name\":\"Тестовый клич\",\"description\":\"\",\"class_id\":\"test_mage\",\"unlock_level\":1," +
            "\"tags\":[\"spell\",\"buff\"],\"cast_time\":0.5," + (cooldown > 0 ? $"\"cooldown\":{cooldown}," : "") +
            "\"tree\":[{\"id\":\"cry_buff\",\"name\":\"Клич\",\"description\":\"\",\"max_points\":1,\"effects\":[" +
            "{\"type\":\"grant_proc\",\"trigger\":\"on_use\",\"action\":{\"apply_buff\":{\"id\":\"cry_power\",\"duration\":8," +
            "\"modifiers\":[{\"stat\":\"damage\",\"kind\":\"more\",\"value\":20}]}}}]}]}"));
    }, c => ((JsonArray)Node(c, "test_mage")["skills"]!).Add("test_cry"));

    private static Build CryBuild() => MageBuild(4, new[] { "test_cry", "test_bolt" },
        ("test_cry", 1, new[] { "cry_buff" }), ("test_bolt", 1, Array.Empty<string>()));

    [Fact]
    public void Buff_skill_applies_buff_on_use_and_expires()
    {
        var r = Run(CryData(20), CryBuild(), duration: 30);
        double Hit(SimEvent e) => double.Parse(e.Text[(e.Text.IndexOf(": ", StringComparison.Ordinal) + 2)..],
            System.Globalization.CultureInfo.InvariantCulture);
        var bolts = r.Log.Where(e => e.Kind == "hit" && e.Text.StartsWith("test_bolt")).ToList();
        var buffed = bolts.Where(e => e.Time < 8 - 1e-9).Select(Hit).ToList();
        var plain = bolts.Where(e => e.Time > 8 + 1e-9 && e.Time < 20 - 1e-9).Select(Hit).ToList();
        Assert.NotEmpty(buffed);
        Assert.NotEmpty(plain);
        Assert.Equal(1.2, buffed[1] / plain[0], 2);                   // damage ×1.2 for the first 8 s only
        Assert.Equal(buffed[1], buffed[^1], 6);
        Assert.Equal(plain[0], plain[^1], 6);
        var cryUses = r.Log.Where(e => e.Kind == "use" && e.Text == "test_cry").Select(e => e.Time).ToList();
        Assert.Equal(2, cryUses.Count);                               // t = 0 and the cooldown of 20 s
        Assert.True(cryUses[1] >= 20 - 1e-9);
    }

    [Fact]
    public void Rotation_casts_buff_only_when_not_active()
    {
        var r = Run(CryData(0), CryBuild(), duration: 60);
        var cryUses = r.Log.Where(e => e.Kind == "use" && e.Text == "test_cry").Select(e => e.Time).ToList();
        Assert.True(cryUses.Count >= 6);
        for (var i = 1; i < cryUses.Count; i++)
            Assert.True(cryUses[i] - cryUses[i - 1] >= 8 - 1e-6, $"cry recast after {cryUses[i] - cryUses[i - 1]} s while buff was active");
        Assert.Contains(r.Log, e => e.Kind == "use" && e.Text == "test_bolt");
    }

    [Fact]
    public void Buff_is_visible_to_buff_active_condition_and_reapply_refreshes()
    {
        var seen = new List<bool>();
        Run(CryData(20), CryBuild(), duration: 15, observe: s =>
        {
            var active = s.Buffs.TryGetValue("cry_power", out var left) && left > 0;
            seen.Add(active);
            Assert.True(!active || left <= 8 + 1e-9);               // no stacking beyond one duration
        });
        Assert.Contains(true, seen);
        Assert.Contains(false, seen);
    }
}
