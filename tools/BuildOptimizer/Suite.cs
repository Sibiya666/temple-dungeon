using Dialoid.Core.Character;
using Dialoid.Core.Simulation;

/// <summary>
/// `suite`: every balance acceptance metric of core/Dialoid.Core.Tests/Content/BalanceAcceptanceTests.cs in one table, for the
/// tuning loop (same dummies, modes and formulas as the tests; the tests stay the authority).
/// </summary>
public static class Suite
{
    public static readonly string[] Main =
    {
        "warrior_berserker_whirlwind", "warrior_thunderer_crush", "warrior_fire_cleave_crit",
        "sorcerer_pyromancer_fireball", "sorcerer_cryomancer_ice_lance", "sorcerer_spark_hybrid",
    };

    public static readonly string[] Keystones = { "blood_pact", "titan_will", "inevitability", "avatar_of_fire", "blade_dance", "stone_skin" };

    public static double Ttk(Build b) =>
        new Simulator(H.Data, Dialoid.Core.Effects.MechanicRegistry.Default).Run(b, new SimConfig("boss", SimMode.Average, DurationSec: 180)).TimeToKill ?? 180;

    public static double Surv(Build b) => H.Run(b, "attacking_boss", true).TimeToDeath ?? 180;

    /// <summary>Keystone metric: training DPS, or survival for the defensive keystones.</summary>
    public static double KsMetric(string ks, Build b) => ks is "blade_dance" or "stone_skin" ? Surv(b) : H.Run(b, "training").DpsSingleTarget;

    public static void Run(Dictionary<string, string> o)
    {
        var all = o.ContainsKey("all");
        foreach (var level in new[] { 60, 20 })
        {
            var rows = new List<(string n, string cls, double tr, double sw, double sv, double ttk)>();
            foreach (var m in Main)
            {
                var path = level == 60 ? m : $"l20/{m}";
                if (!File.Exists(Path.Combine(H.DataDir, "builds", path + ".json"))) continue;
                var b = H.LoadBuild(path);
                var v = H.V(b);
                if (v.Count > 0) Console.WriteLine($"{path}: ILLEGAL {string.Join("; ", v.Select(x => x.Code))}");
                rows.Add((path, b.ClassId, H.Run(b, "training").DpsSingleTarget, H.Run(b, "swarm").DpsPack, Surv(b), Ttk(b)));
            }
            Console.WriteLine($"--- L{level}");
            foreach (var r in rows) Console.WriteLine($"  {r.n,-40} tr {r.tr,8:F0} sw {r.sw,8:F0} obj {Math.Sqrt(r.tr * r.sw),8:F0} surv {r.sv,6:F1} ttk {r.ttk,6:F1}");
            if (rows.Count == 0) continue;
            Console.WriteLine($"  mean TTK {rows.Average(r => r.ttk):F1} (20..60)  mean surv {rows.Average(r => r.sv):F1} (20..40)  capped {rows.Count(r => r.sv >= 180 - 1e-9)}");
            foreach (var g in rows.GroupBy(r => r.cls))
            {
                Console.WriteLine($"  {g.Key}: training max/min {g.Max(r => r.tr) / g.Min(r => r.tr):F2} (≤2)");
                foreach (var a in g)
                    foreach (var b in g)
                        if (a.n != b.n && b.tr > a.tr && b.sv > a.sv) Console.WriteLine($"    PARETO: {a.n} dominated by {b.n}");
            }
        }

        Console.WriteLine("--- parity (training)");
        foreach (var level in new[] { 1, 20, 60 })
        {
            var w = H.Run(H.LoadBuild($"parity/warrior_l{level}"), "training").DpsSingleTarget;
            var s = H.Run(H.LoadBuild($"parity/sorcerer_l{level}"), "training").DpsSingleTarget;
            Console.WriteLine($"  L{level}: warrior {w:F1} sorcerer {s:F1} ratio {Math.Max(w, s) / Math.Min(w, s):F2} (≤1.5)   surv W {Surv(H.LoadBuild($"parity/warrior_l{level}")):F1} S {Surv(H.LoadBuild($"parity/sorcerer_l{level}")):F1}");
        }

        Console.WriteLine("--- attributes (variant vs source)");
        foreach (var (v, src) in new[] { ("attributes/warrior_no_attribute_nodes", "warrior_thunderer_crush"), ("attributes/sorcerer_no_attribute_nodes", "sorcerer_pyromancer_fireball") })
        {
            var a = H.LoadBuild(src); var b = H.LoadBuild(v);
            Console.WriteLine($"  {v}: dps {100 * (H.Run(b, "training").DpsSingleTarget / H.Run(a, "training").DpsSingleTarget - 1):F1}%  surv {100 * (Surv(b) / Surv(a) - 1):F1}%");
        }

        Console.WriteLine("--- keystones (with / without keystone)");
        foreach (var ks in Keystones)
            foreach (var kind in new[] { "fit", "unfit" })
            {
                var path = $"keystones/{ks}_{kind}";
                if (!File.Exists(Path.Combine(H.DataDir, "builds", path + ".json"))) continue;
                var b = H.LoadBuild(path);
                var v = H.V(b);
                var without = H.Clone(b); without.PassiveNodes.Remove("keystone_" + ks);
                var vw = H.V(without);
                var g = KsMetric(ks, b) / KsMetric(ks, without);
                Console.WriteLine($"  {path,-34} gain {g:F3} {(kind == "fit" ? "(1.10..1.50)" : "(<1)")} {(v.Count + vw.Count > 0 ? "ILLEGAL " + string.Join(",", v.Concat(vw).Select(x => x.Code)) : "")}");
            }

        if (all)
        {
            Console.WriteLine("--- ascendancy notables (obj %, surv s)");
            foreach (var m in Main)
            {
                var b = H.LoadBuild(m);
                var o0 = Math.Sqrt(H.Run(b, "training").DpsSingleTarget * H.Run(b, "swarm").DpsPack); var s0 = Surv(b);
                var parts = new List<string>();
                foreach (var n in b.AscendancyNodes)
                {
                    var c = H.Clone(b); c.AscendancyNodes.Remove(n);
                    var o1 = Math.Sqrt(H.Run(c, "training").DpsSingleTarget * H.Run(c, "swarm").DpsPack);
                    parts.Add($"{n} {100 * (1 - o1 / o0):F1}%/{s0 - Surv(c):F1}s");
                }
                Console.WriteLine($"  {m}: {string.Join(", ", parts)}");
            }
        }
    }
}
