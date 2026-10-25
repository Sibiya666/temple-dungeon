using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Simulation;

public class SurvivalTests
{
    private const string Attacker = "test_attacker";

    private static void Edit(string dir, string file, Action<JsonNode> mutate)
    {
        var path = Path.Combine(dir, file);
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        mutate(node);
        File.WriteAllText(path, node.ToJsonString());
    }

    private static JsonNode Find(JsonNode arr, string id) => arr.AsArray().First(n => n!["id"]!.GetValue<string>() == id)!;

    /// <summary>Tiny fixture without attribute bonuses; the warrior gets exact flat defences from class bonuses.</summary>
    private static GameData Defence(double armor = 0, double fire = 0, double dodge = 0, double? damage = null, double? interval = null,
        double? coldCap = null)
    {
        var dir = FixturePaths.CopyTiny();
        Edit(dir, "attributes.json", n => n["bonuses"] = new JsonArray());
        Edit(dir, "classes.json", n =>
        {
            var bonuses = Find(n, "test_warrior")["class_bonuses"]!.AsArray();
            foreach (var (stat, v) in new[] { ("armor", armor), ("resist_fire", fire), ("dodge_chance", dodge) })
                if (v != 0) bonuses.Add(JsonNode.Parse($"{{\"stat\":\"{stat}\",\"kind\":\"flat\",\"value\":{v}}}"));
        });
        Edit(dir, "dummies.json", n =>
        {
            var atk = Find(n, Attacker)["attack"]!;
            if (damage != null) atk["damage_per_hit"] = JsonNode.Parse($"{{\"physical\":{damage}}}");
            if (interval != null) atk["interval_sec"] = interval;
        });
        var r = DataLoader.Load(dir);
        Assert.Empty(r.Errors);
        return r.Data!;
    }

    private static Build Warrior(int level) => new() { ClassId = "test_warrior", Level = level };

    private static SimResult Run(GameData d, Build b, double cap = 180, string dummy = Attacker, bool attacks = true,
        Action<SimState>? observe = null)
    {
        var sim = new Simulator(d, MechanicRegistry.Default);
        if (observe != null) sim.StepObserver = observe;
        return sim.Run(b, new SimConfig(dummy, SimMode.Average, DurationSec: 5, DummyAttacks: attacks, SurvivalCapSec: cap));
    }

    private static int Count(SimResult r, string kind) => r.Log.Count(e => e.Kind == kind);

    [Fact]
    public void Dummy_attack_mitigated_by_armor_resist_and_dodge()
    {
        // Level 1: G = 1. Physical 14 vs armor 140: 140 / (140 + 140) = 50% -> 7. Fire 9 at 10% resist -> 8.1. One attack (t = 3.5).
        var r = Run(Defence(armor: 140, fire: 10), Warrior(1), cap: 4);
        Assert.Empty(r.Violations);
        Assert.Equal(7.0, r.DamageTaken![DamageType.Physical], 9);
        Assert.Equal(8.1, r.DamageTaken[DamageType.Fire], 9);
        Assert.Equal(1, Count(r, "dummy_attack"));
        Assert.Equal(4.0, r.TimeToDeath);
    }

    [Fact]
    public void Resist_is_clamped_by_its_max_stat()
    {
        // resist_fire 90 but resist_fire_max 75 -> 9 * 0.25
        var r = Run(Defence(fire: 90), Warrior(1), cap: 4);
        Assert.Equal(2.25, r.DamageTaken![DamageType.Fire], 9);
    }

    [Fact]
    public void Damage_scales_with_gear_growth()
    {
        // Level 11: G = 1.6 -> 22.4 physical, no armor; fire 14.4
        var r = Run(Defence(), Warrior(11), cap: 4);
        Assert.Equal(22.4, r.DamageTaken![DamageType.Physical], 9);
        Assert.Equal(14.4, r.DamageTaken[DamageType.Fire], 9);
    }

    [Fact]
    public void Dodge_uses_accumulator_in_average_mode()
    {
        // 25% dodge, an attack every second, 16 attacks in 16 s -> exactly 4 dodged (every 4th), the rest hit.
        var r = Run(Defence(dodge: 25, damage: 1, interval: 1), Warrior(1), cap: 16.5);
        Assert.Equal(16, Count(r, "dummy_attack"));
        Assert.Equal(4, Count(r, "dodge"));
        Assert.Equal(12.0, r.DamageTaken![DamageType.Physical], 9);
        Assert.Equal(new[] { 4, 8, 12, 16 }, r.Log.Where(e => e.Kind == "dummy_attack").Select((e, i) => (e, i))
            .Where(x => r.Log.Any(d => d.Kind == "dodge" && d.Time == x.e.Time)).Select(x => x.i + 1));
    }

    [Fact]
    public void Dodge_is_capped_by_dodge_chance_max()
    {
        // 80% dodge, default max 50 -> every 2nd attack dodged
        var r = Run(Defence(dodge: 80, damage: 1, interval: 1), Warrior(60), cap: 10.5);
        Assert.Equal(10, Count(r, "dummy_attack"));
        Assert.Equal(5, Count(r, "dodge"));
    }

    [Fact]
    public void Life_regen_heals_per_second()
    {
        // Warrior level 60: life 912, regen 0.5% = 4.56/s. The dummy attacks every 1000 s, i.e. never.
        var data = Defence(interval: 1000);
        var seen = new Dictionary<int, double>();
        var r = Run(data, Warrior(60), cap: 10, observe: s =>
        {
            Assert.Equal(912.0, s.LifeMax, 9);
            if (s.Time == 0)
            {
                Assert.Equal(912.0, s.Life, 9); // starts at full life
                s.Life = 100;
            }
            seen[(int)Math.Round(s.Time * 20)] = s.Life;
        });
        Assert.Equal(104.56, seen[20], 6);
        Assert.Equal(100 + 4.56 * 5, seen[100], 6);
        Assert.Equal(10.0, r.TimeToDeath);
    }

    [Fact]
    public void Life_regen_is_capped_at_max_life()
    {
        var seen = 0.0;
        Run(Defence(interval: 1000), Warrior(60), cap: 5, observe: s => seen = Math.Max(seen, s.Life));
        Assert.Equal(912.0, seen, 9);
    }

    [Fact]
    public void Character_death_stops_sim_and_reports_time()
    {
        var r = Run(Defence(damage: 100000, interval: 2), Warrior(1));
        Assert.Equal(2.0, r.TimeToDeath!.Value, 6);
        Assert.Equal(1, Count(r, "death"));
        Assert.Equal(2.0, r.Log.Single(e => e.Kind == "death").Time, 6);
    }

    [Fact]
    public void Survival_capped_at_180()
    {
        var r = Run(Defence(damage: 1, interval: 3.5), Warrior(60), cap: 180);
        Assert.Equal(180.0, r.TimeToDeath);
        Assert.Equal(51, Count(r, "dummy_attack")); // t = 3.5 .. 178.5
        Assert.Equal(0, Count(r, "death"));

        var d = new SimConfig(Attacker, SimMode.Average, DummyAttacks: true);
        Assert.Equal(180.0, d.SurvivalCapSec);
    }

    [Fact]
    public void Frozen_dummy_skips_attacks()
    {
        var r = Run(Defence(), Warrior(1), cap: 8, observe: s =>
        {
            if (s.Time == 0) s.Targets[0].FreezeRemaining = 100;
        });
        Assert.Equal(0, Count(r, "dummy_attack"));
        Assert.True(r.DamageTaken == null || r.DamageTaken.Values.Sum() == 0);
    }

    [Fact]
    public void Self_damage_never_below_one_life()
    {
        var s = new SimState(1, 100) { Life = 50, LifeMax = 100 };
        s.DamageSelf(20);
        Assert.Equal(30.0, s.Life, 9);
        s.DamageSelf(1000);
        Assert.Equal(1.0, s.Life, 9);
        s.DamageSelf(5);
        Assert.Equal(1.0, s.Life, 9);
    }

    [Fact]
    public void Life_payment_refused_when_life_not_above_cost()
    {
        var s = new SimState(1, 100) { Life = 50, LifeMax = 100 };
        Assert.False(s.TryPayLife(50));
        Assert.Equal(50.0, s.Life, 9);
        Assert.False(s.TryPayLife(80));
        Assert.Equal(50.0, s.Life, 9);
        Assert.True(s.TryPayLife(20));
        Assert.Equal(30.0, s.Life, 9);
    }

    [Fact]
    public void Character_cannot_die_when_dummy_does_not_attack()
    {
        var d = Defence();
        // dummy without an attack, defensive mode requested
        var r = Run(d, Warrior(1), dummy: "test_training", attacks: true);
        Assert.Null(r.TimeToDeath);
        Assert.Equal(0, Count(r, "dummy_attack"));
        // attacking dummy, but defensive mode off
        var r2 = Run(d, Warrior(1), attacks: false);
        Assert.Null(r2.TimeToDeath);
        Assert.Equal(0, Count(r2, "dummy_attack"));
    }
}
