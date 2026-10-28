using Dialoid.Core.Character;

/// <summary>
/// `regret`: the notable regret table of the ascendancies (Task 10 fix round 1, D3). For every level-60 build that uses an
/// ascendancy (the six references and the parity builds), all 15 quartets of its notables are evaluated with the build's
/// passives, trees and rotation unchanged. For each notable: the best quartet containing it against the best quartet of the
/// build, on the objective √(training × swarm) and on survival as the inverse of the damage taken from «Атакующий босс» in
/// 20 s (continuous; time to death moves in 3.5 s steps and ties most quartets).
/// Regret = 1 − best with / best overall; per notable the table keeps the smallest objective regret and the smallest survival
/// regret over the builds (each with its build): a notable is dead only if it is far behind on both in every build.
/// </summary>
public static class Regret
{
    public static void Run()
    {
        var builds = Suite.Main.Concat(new[] { "parity/warrior_l60", "parity/sorcerer_l60" }).Select(n => (n, b: H.LoadBuild(n)))
            .Where(x => x.b.AscendancyId != null).ToList();
        var rows = new Dictionary<(string asc, string notable), (double obj, string objBuild, double surv, string survBuild)>();
        foreach (var (name, b) in builds)
        {
            var notables = H.Data.Ascendancies[b.AscendancyId!].Notables.Select(n => n.Id).ToList();
            var quartets = Combos(notables, 4).ToList();
            var vals = new (double Obj, double Surv)[quartets.Count];
            Parallel.For(0, quartets.Count, i =>
            {
                var c = H.Clone(b); c.AscendancyNodes = quartets[i].ToList();
                vals[i] = H.V(c).Count > 0 ? (double.NegativeInfinity, double.NegativeInfinity)
                    : (Math.Sqrt(H.Run(c, "training").DpsSingleTarget * H.Run(c, "swarm").DpsPack), 1 / H.TakenIn20(c));
            });
            var bestObj = vals.Max(v => v.Obj); var bestSurv = vals.Max(v => v.Surv);
            foreach (var n in notables)
            {
                var with = Enumerable.Range(0, quartets.Count).Where(i => quartets[i].Contains(n)).ToList();
                var ro = 1 - with.Max(i => vals[i].Obj) / bestObj;
                var rs = 1 - with.Max(i => vals[i].Surv) / bestSurv;
                var key = (b.AscendancyId!, n);
                var cur = rows.TryGetValue(key, out var c0) ? c0 : (double.PositiveInfinity, "", double.PositiveInfinity, "");
                if (ro < cur.Item1) cur = (ro, name, cur.Item3, cur.Item4);
                if (rs < cur.Item3) cur = (cur.Item1, cur.Item2, rs, name);
                rows[key] = cur;
            }
        }
        Console.WriteLine("ascendancy | notable | objective regret (build) | survival regret (build)");
        foreach (var ((asc, n), v) in rows.OrderBy(r => r.Key.asc).ThenBy(r => r.Key.notable))
            Console.WriteLine($"{asc} | {n} | {100 * v.obj:F1}% ({v.objBuild}) | {100 * v.surv:F1}% ({v.survBuild}){(v.obj > 0.25 && v.surv > 0.25 ? "  <-- > 25% on both" : "")}");
    }

    static IEnumerable<List<string>> Combos(List<string> xs, int k)
    {
        if (k == 0) { yield return new(); yield break; }
        for (int i = 0; i <= xs.Count - k; i++)
            foreach (var rest in Combos(xs.Skip(i + 1).ToList(), k - 1)) { rest.Insert(0, xs[i]); yield return rest; }
    }
}
