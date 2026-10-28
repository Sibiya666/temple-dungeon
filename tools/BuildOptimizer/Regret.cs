using Dialoid.Core.Character;

/// <summary>
/// `regret`: the notable regret table of the ascendancies (Task 10 fix round 1, D3). For every level-60 build that uses an
/// ascendancy (the six references and the parity builds), all 15 quartets of its notables are evaluated with the build's
/// passives, trees and rotation unchanged. For each notable: the best quartet containing it against the best quartet of the
/// build, on the objective √(training × swarm) and on survival against «Атакующий босс» (time to death, cap 180 s).
/// Regret = 1 − best with / best overall; the table keeps, per notable, the build where its regret is smallest.
/// </summary>
public static class Regret
{
    public static void Run()
    {
        var builds = Suite.Main.Concat(new[] { "parity/warrior_l60", "parity/sorcerer_l60" }).Select(n => (n, b: H.LoadBuild(n)))
            .Where(x => x.b.AscendancyId != null).ToList();
        var rows = new Dictionary<(string asc, string notable), (double obj, double surv, string build)>();
        foreach (var (name, b) in builds)
        {
            var notables = H.Data.Ascendancies[b.AscendancyId!].Notables.Select(n => n.Id).ToList();
            var quartets = Combos(notables, 4).ToList();
            var vals = new (double Obj, double Surv)[quartets.Count];
            Parallel.For(0, quartets.Count, i =>
            {
                var c = H.Clone(b); c.AscendancyNodes = quartets[i].ToList();
                vals[i] = H.V(c).Count > 0 ? (double.NegativeInfinity, double.NegativeInfinity)
                    : (Math.Sqrt(H.Run(c, "training").DpsSingleTarget * H.Run(c, "swarm").DpsPack), Math.Min(180, H.Run(c, "attacking_boss", true).TimeToDeath ?? 180));
            });
            var bestObj = vals.Max(v => v.Obj); var bestSurv = vals.Max(v => v.Surv);
            foreach (var n in notables)
            {
                var with = Enumerable.Range(0, quartets.Count).Where(i => quartets[i].Contains(n)).ToList();
                var ro = 1 - with.Max(i => vals[i].Obj) / bestObj;
                var rs = 1 - with.Max(i => vals[i].Surv) / bestSurv;
                var key = (b.AscendancyId!, n);
                if (!rows.TryGetValue(key, out var cur) || Math.Min(ro, rs) < Math.Min(cur.obj, cur.surv)) rows[key] = (ro, rs, name);
            }
        }
        Console.WriteLine("ascendancy | notable | objective regret | survival regret | build");
        foreach (var ((asc, n), v) in rows.OrderBy(r => r.Key.asc).ThenBy(r => r.Key.notable))
            Console.WriteLine($"{asc} | {n} | {100 * v.obj:F1}% | {100 * v.surv:F1}% | {v.build}{(v.obj > 0.25 && v.surv > 0.25 ? "  <-- > 25% on both" : "")}");
    }

    static IEnumerable<List<string>> Combos(List<string> xs, int k)
    {
        if (k == 0) { yield return new(); yield break; }
        for (int i = 0; i <= xs.Count - k; i++)
            foreach (var rest in Combos(xs.Skip(i + 1).ToList(), k - 1)) { rest.Insert(0, xs[i]); yield return rest; }
    }
}
