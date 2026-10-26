using System.Collections.Concurrent;
using Dialoid.Core.Builds;
using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Passives;
using Dialoid.Core.Skills;
using Attr = Dialoid.Core.Character.Attribute;

public sealed record Goal(string? Skill, string? Type, double Margin = 1.25, bool TrainOnly = false, double MinTypeShare = 0.6);

public static class Opt
{
    static readonly ConcurrentDictionary<string, double> Cache = new();
    public static Goal G = new(null, null);

    public static double Eval(Build b)
    {
        var key = BuildCodec.ToJson(b);
        if (Cache.TryGetValue(key, out var v)) return v;
        v = EvalRaw(b);
        Cache[key] = v;
        return v;
    }

    public static string[] Ban = Array.Empty<string>();
    /// <summary>Survival weight w: the objective is multiplied by (min(TimeToDeath, 180) / 180)^w against «Атакующий босс».</summary>
    public static double SurvivalWeight;
    static bool Banned(string id) => Ban.Any(p => p.Length > 0 && id.StartsWith(p));
    static double EvalRaw(Build b)
    {
        if (b.PassiveNodes.Any(Banned) || b.Slots.Any(s => s.NodePoints.Keys.Any(Banned) || Banned("slot:" + s.SkillId))) return double.NegativeInfinity;
        if (H.V(b).Count > 0) return double.NegativeInfinity;
        var t = H.Run(b, "training");
        double obj;
        if (G.TrainOnly) obj = t.DpsSingleTarget;
        else { var s = H.Run(b, "swarm"); obj = Math.Sqrt(Math.Max(0, t.DpsSingleTarget) * Math.Max(0, s.DpsPack)); }
        if (SurvivalWeight > 0)
        {
            var ttd = H.Run(b, "attacking_boss", true).TimeToDeath ?? 180;
            obj *= Math.Pow(Math.Min(ttd, 180) / 180, SurvivalWeight);
        }
        double pen = 1;
        var used = t.Log.Where(e => e.Kind == "use").Select(e => e.Text).ToHashSet();
        int unused = b.Rotation.Count(r => !used.Contains(r));
        if (unused > 0) pen *= Math.Pow(0.3, unused);
        if (G.Skill != null && G.Skill.Contains(">="))
        {
            var parts = G.Skill.Split(">=");
            var tot = t.DamageBySkill.Values.Sum();
            var share = tot > 0 ? t.DamageBySkill.GetValueOrDefault(parts[0]) / tot : 0;
            var min = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            if (share < min) pen *= 0.3 * Math.Pow(share / min, 2);
        }
        else if (G.Skill != null)
        {
            var tot = t.DamageBySkill.Values.Sum();
            var me = t.DamageBySkill.GetValueOrDefault(G.Skill);
            var other = t.DamageBySkill.Where(kv => kv.Key != G.Skill).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
            if (me < G.Margin * other) pen *= 0.3 * Math.Pow(Math.Min(1, me / (G.Margin * other + 1e-9)), 2);
        }
        if (G.Type != null)
        {
            var tot = t.DamageByType.Values.Sum();
            var me = t.DamageByType.Where(kv => kv.Key.ToString().Equals(G.Type, StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
            if (tot > 0 && me / tot < G.MinTypeShare) pen *= 0.3 * Math.Pow((me / tot) / G.MinTypeShare, 2);
        }
        return obj * pen;
    }

    static (Build B, double V) Best(IEnumerable<Build> cands)
    {
        var list = cands.ToList();
        var vals = new double[list.Count];
        Parallel.For(0, list.Count, new ParallelOptions { MaxDegreeOfParallelism = 12 }, i => vals[i] = Eval(list[i]));
        int bi = -1; double bv = double.NegativeInfinity;
        for (int i = 0; i < list.Count; i++) if (vals[i] > bv) { bv = vals[i]; bi = i; }
        return bi < 0 ? (list.FirstOrDefault()!, double.NegativeInfinity) : (list[bi], bv);
    }

    static double[] EvalAll(List<Build> list)
    {
        var vals = new double[list.Count];
        Parallel.For(0, list.Count, new ParallelOptions { MaxDegreeOfParallelism = 12 }, i => vals[i] = Eval(list[i]));
        return vals;
    }

    // ---------- skill trees ----------
    static Dictionary<string, int> WithNode(SkillDefinition def, Dictionary<string, int> pts, string node)
    {
        var r = new Dictionary<string, int>(pts);
        void Need(string id, int p)
        {
            if (r.GetValueOrDefault(id) >= p) return;
            foreach (var (req, rp) in def.Tree.Single(n => n.Id == id).Requires) Need(req, rp);
            r[id] = p;
        }
        Need(node, r.GetValueOrDefault(node) + 1);
        return r;
    }

    public static Build TreeGreedy(Build b, int slot, bool fromScratch)
    {
        var def = H.Data.Skills[b.Slots[slot].SkillId];
        var cur = H.Clone(b);
        if (fromScratch) cur.Slots[slot].NodePoints = new();
        int level = cur.Slots[slot].Level;
        while (cur.Slots[slot].NodePoints.Values.Sum() < level)
        {
            var baseV = Eval(cur);
            var spent = cur.Slots[slot].NodePoints.Values.Sum();
            var cands = new List<(Build, int)>();
            foreach (var n in def.Tree)
            {
                var np = WithNode(def, cur.Slots[slot].NodePoints, n.Id);
                var cost = np.Values.Sum() - spent;
                if (np.Values.Sum() > level || np[n.Id] > n.MaxPoints) continue;
                var c = H.Clone(cur); c.Slots[slot].NodePoints = np;
                cands.Add((c, cost));
            }
            var vals = EvalAll(cands.Select(x => x.Item1).ToList());
            int bi = -1; double br = double.NegativeInfinity;
            for (int i = 0; i < cands.Count; i++)
            {
                if (double.IsNegativeInfinity(vals[i])) continue;
                var r = (vals[i] - baseV) / cands[i].Item2;
                if (r > br) { br = r; bi = i; }
            }
            if (bi < 0) break;
            cur = cands[bi].Item1;
        }
        return TreeRefine(cur, slot);
    }

    public static Build TreeRefine(Build cur, int slot)
    {
        var def = H.Data.Skills[cur.Slots[slot].SkillId];
        while (true)
        {
            var baseV = Eval(cur);
            var cands = new List<Build>();
            var pts = cur.Slots[slot].NodePoints;
            foreach (var a in pts.Keys.ToList())
            {
                var less = new Dictionary<string, int>(pts); less[a]--; if (less[a] == 0) less.Remove(a);
                foreach (var n in def.Tree)
                {
                    if (n.Id == a) continue;
                    var np = WithNode(def, less, n.Id);
                    var extra = np.Values.Sum() - less.Values.Sum();
                    if (extra != 1 || np[n.Id] > n.MaxPoints) { 
                        // allow 2-for-2: remove another point too
                        if (extra == 2 && np[n.Id] <= n.MaxPoints)
                            foreach (var a2 in less.Keys.ToList())
                            {
                                var l2 = new Dictionary<string, int>(np); if (!l2.ContainsKey(a2) || a2 == n.Id) continue;
                                l2[a2]--; if (l2[a2] == 0) l2.Remove(a2);
                                var c2 = H.Clone(cur); c2.Slots[slot].NodePoints = l2; cands.Add(c2);
                            }
                        continue; }
                    var c = H.Clone(cur); c.Slots[slot].NodePoints = np; cands.Add(c);
                }
            }
            var (bb, bv) = Best(cands);
            if (bv <= baseV * (1 + 1e-6)) return cur;
            cur = bb;
        }
    }

    // ---------- passives ----------
    static string Start(Build b) => H.Data.Classes[b.ClassId].PassiveStartNodeId;
    static Attr Primary(Build b) => b.ClassId == "warrior" ? Attr.Str : Attr.Int;

    static Dictionary<string, List<string>> Paths(Build b, int maxLen)
    {
        var tree = H.Data.Passives;
        var alloc = b.PassiveNodes.Append(Start(b)).ToHashSet();
        var parent = new Dictionary<string, string?>();
        var dist = new Dictionary<string, int>();
        var q = new Queue<string>();
        foreach (var a in alloc) { dist[a] = 0; parent[a] = null; q.Enqueue(a); }
        while (q.Count > 0)
        {
            var x = q.Dequeue();
            if (dist[x] >= maxLen) continue;
            foreach (var y in tree.Edges[x])
            {
                if (dist.ContainsKey(y) || !tree.Nodes.ContainsKey(y) || tree.Nodes[y].Kind == PassiveKind.Start) continue;
                dist[y] = dist[x] + 1; parent[y] = x; q.Enqueue(y);
            }
        }
        var res = new Dictionary<string, List<string>>();
        foreach (var (n, d) in dist)
        {
            if (d == 0) continue;
            var path = new List<string>(); string? c = n;
            while (c != null && !alloc.Contains(c)) { path.Add(c); c = parent[c]; }
            path.Reverse(); res[n] = path;
        }
        return res;
    }

    static Build AddNodes(Build b, IEnumerable<string> nodes, Attr? attr = null)
    {
        var c = H.Clone(b);
        foreach (var n in nodes)
        {
            c.PassiveNodes.Add(n);
            if (H.Data.Passives.Nodes[n].Kind == PassiveKind.Attribute) c.AttributeChoices[n] = attr ?? Primary(b);
        }
        return c;
    }

    static Build RemoveNode(Build b, string n)
    {
        var c = H.Clone(b); c.PassiveNodes.Remove(n); c.AttributeChoices.Remove(n); return c;
    }

    public static Build PassiveGreedy(Build b, int maxPath = 3)
    {
        var cur = H.Clone(b);
        int budget = b.Level - 1;
        while (cur.PassiveNodes.Count < budget)
        {
            var baseV = Eval(cur);
            var paths = Paths(cur, Math.Min(maxPath, budget - cur.PassiveNodes.Count));
            var cands = paths.Values.Where(p => p.Count + cur.PassiveNodes.Count <= budget)
                .Where(p => H.Data.Passives.Nodes[p[^1]].Kind != PassiveKind.Keystone || AllowKeystones).Select(p => (AddNodes(cur, p), p.Count)).ToList();
            var vals = EvalAll(cands.Select(x => x.Item1).ToList());
            int bi = -1; double br = double.NegativeInfinity;
            for (int i = 0; i < cands.Count; i++)
            {
                if (double.IsNegativeInfinity(vals[i])) continue;
                var r = (vals[i] - baseV) / cands[i].Item2;
                if (r > br) { br = r; bi = i; }
            }
            if (bi < 0) break;
            cur = cands[bi].Item1;
        }
        return cur;
    }

    public static bool AllowKeystones;

    public static Build PassiveRefine(Build cur, int k = 6)
    {
        var tree = H.Data.Passives;
        while (true)
        {
            var baseV = Eval(cur);
            var alloc = cur.PassiveNodes.Append(Start(cur)).ToHashSet();
            var removable = cur.PassiveNodes.Where(n => tree.CanDeallocate(alloc, n, Start(cur))).ToList();
            var rem = removable.Select(n => RemoveNode(cur, n)).ToList();
            var rv = EvalAll(rem);
            var order = Enumerable.Range(0, rem.Count).OrderByDescending(i => rv[i]).Take(k).ToList();
            var cands = new List<Build>();
            foreach (var i in order)
            {
                var r = rem[i];
                foreach (var p in Paths(r, 1).Values)
                {
                    if (p.Count != 1 || p[0] == removable[i]) continue;
                    if (tree.Nodes[p[0]].Kind == PassiveKind.Keystone && !AllowKeystones) continue;
                    cands.Add(AddNodes(r, p, cur.AttributeChoices.Values.FirstOrDefault(Primary(cur))));
                }
            }
            // 2-for-2: remove two cheapest (sequentially removable), add a 2-path
            var two = new List<Build>();
            for (int x = 0; x < Math.Min(4, order.Count); x++)
            {
                var r1 = rem[order[x]];
                var a1 = r1.PassiveNodes.Append(Start(r1)).ToHashSet();
                var rem2 = r1.PassiveNodes.Where(n => tree.CanDeallocate(a1, n, Start(r1))).Select(n => RemoveNode(r1, n)).ToList();
                var rv2 = EvalAll(rem2);
                foreach (var j in Enumerable.Range(0, rem2.Count).OrderByDescending(j => rv2[j]).Take(3))
                    foreach (var p in Paths(rem2[j], 2).Values.Where(p => p.Count == 2))
                    {
                        if (tree.Nodes[p[^1]].Kind == PassiveKind.Keystone && !AllowKeystones) continue;
                        two.Add(AddNodes(rem2[j], p));
                    }
            }
            cands.AddRange(two);
            var (bb, bv) = Best(cands);
            if (bv <= baseV * (1 + 1e-6)) return cur;
            Console.WriteLine($"    passive swap {baseV:F0} -> {bv:F0}: -[{string.Join(",", cur.PassiveNodes.Except(bb.PassiveNodes))}] +[{string.Join(",", bb.PassiveNodes.Except(cur.PassiveNodes))}]");
            cur = bb;
        }
    }

    public static Build AttrRefine(Build cur)
    {
        while (true)
        {
            var baseV = Eval(cur);
            var cands = new List<Build>();
            // change one node of attribute X to Y (only distinct X choices matter)
            foreach (var g in cur.AttributeChoices.GroupBy(kv => kv.Value))
            {
                var node = g.First().Key;
                foreach (var a in Enum.GetValues<Attr>())
                {
                    if (a == g.Key) continue;
                    var c = H.Clone(cur); c.AttributeChoices[node] = a; cands.Add(c);
                }
            }
            if (cands.Count == 0) return cur;
            var (bb, bv) = Best(cands);
            if (bv <= baseV * (1 + 1e-6)) return cur;
            cur = bb;
        }
    }

    public static Build AscBest(Build cur, string? fixedAsc)
    {
        var ascs = H.Data.Classes[cur.ClassId].AscendancyIds.Where(a => fixedAsc == null || a == fixedAsc);
        var cands = new List<Build>();
        foreach (var asc in ascs)
        {
            var nodes = H.Data.Ascendancies[asc].Notables.Select(n => n.Id).Where(n => !Banned("asc:" + n)).ToList();
            foreach (var combo in Combos(nodes, Ascendancy.PointsForLevel(cur.Level) / 2))
            {
                var c = H.Clone(cur); c.AscendancyId = asc; c.AscendancyNodes = combo.ToList(); cands.Add(c);
            }
        }
        return Best(cands).B;
    }

    static IEnumerable<List<string>> Combos(List<string> xs, int k)
    {
        if (k == 0) { yield return new(); yield break; }
        for (int i = 0; i <= xs.Count - k; i++)
            foreach (var rest in Combos(xs.Skip(i + 1).ToList(), k - 1)) { rest.Insert(0, xs[i]); yield return rest; }
    }

    public static Build RotationBest(Build cur)
    {
        var ids = cur.Slots.Select(s => s.SkillId).ToList();
        var cands = new List<Build>();
        foreach (var p in Perms(ids))
        {
            var c = H.Clone(cur); c.Rotation = p; cands.Add(c);
        }
        return Best(cands).B;
    }

    static IEnumerable<List<string>> Perms(List<string> xs)
    {
        if (xs.Count <= 1) { yield return new(xs); yield break; }
        foreach (var x in xs)
            foreach (var p in Perms(xs.Where(y => y != x).ToList())) { p.Insert(0, x); yield return p; }
    }

    public static Build SlotSwap(Build cur)
    {
        var all = H.Data.Classes[cur.ClassId].SkillIds.Where(s => H.Data.Skills[s].UnlockLevel <= cur.Level).ToList();
        var unused = all.Except(cur.Slots.Select(s => s.SkillId)).ToList();
        var best = cur; var bv = Eval(cur);
        for (int i = 0; i < cur.Slots.Count; i++)
            foreach (var u in unused)
            {
                var c = H.Clone(cur);
                var old = c.Slots[i].SkillId;
                c.Slots[i].SkillId = u; c.Slots[i].NodePoints = new();
                c.Rotation = c.Rotation.Select(r => r == old ? u : r).ToList();
                c = TreeGreedy(c, i, true);
                var v = Eval(c);
                Console.WriteLine($"    slot {old}->{u}: {v:F0} (cur {bv:F0})");
                if (v > bv) { best = c; bv = v; }
            }
        return best;
    }


    static readonly Random Rng = new(12345);

    public static Build Strip(Build b)
    {
        var tree = H.Data.Passives; var start = Start(b);
        var c = H.Clone(b);
        c.PassiveNodes.RemoveAll(Banned);
        // drop disconnected
        var seen = new HashSet<string> { start }; var q = new Queue<string>(); q.Enqueue(start);
        var set = c.PassiveNodes.ToHashSet();
        while (q.Count > 0) foreach (var y in tree.Edges[q.Dequeue()]) if (set.Contains(y) && seen.Add(y)) q.Enqueue(y);
        c.PassiveNodes.RemoveAll(n => !seen.Contains(n));
        foreach (var k in c.AttributeChoices.Keys.ToList()) if (!c.PassiveNodes.Contains(k)) c.AttributeChoices.Remove(k);
        foreach (var sl in c.Slots) foreach (var k in sl.NodePoints.Keys.ToList()) if (Banned(k)) sl.NodePoints.Remove(k);
        return c;
    }

    public static Build PassiveLns(Build cur, int iters)
    {
        var tree = H.Data.Passives;
        var bestV = Eval(cur);
        for (int it = 0; it < iters; it++)
        {
            int r = Rng.Next(2, 7);
            var c = H.Clone(cur);
            for (int j = 0; j < r; j++)
            {
                var alloc = c.PassiveNodes.Append(Start(c)).ToHashSet();
                var removable = c.PassiveNodes.Where(n => tree.CanDeallocate(alloc, n, Start(c))).ToList();
                if (removable.Count == 0) break;
                var n = removable[Rng.Next(removable.Count)];
                c = RemoveNode(c, n);
            }
            c = PassiveGreedy(c, 4);
            var v = Eval(c);
            if (v > bestV * (1 + 1e-6))
            {
                Console.WriteLine($"    LNS passive {bestV:F0} -> {v:F0}: -[{string.Join(",", cur.PassiveNodes.Except(c.PassiveNodes))}] +[{string.Join(",", c.PassiveNodes.Except(cur.PassiveNodes))}]");
                cur = c; bestV = v;
            }
        }
        return cur;
    }

    public static Build TreeLns(Build cur, int slot, int iters)
    {
        var bestV = Eval(cur);
        var def = H.Data.Skills[cur.Slots[slot].SkillId];
        for (int it = 0; it < iters; it++)
        {
            var c = H.Clone(cur);
            int r = Rng.Next(2, 6);
            for (int j = 0; j < r; j++)
            {
                var pts = c.Slots[slot].NodePoints;
                // removable: nodes whose decrement keeps requirements of others
                var cand = pts.Keys.Where(k =>
                {
                    var t = new Dictionary<string, int>(pts); t[k]--; if (t[k] == 0) t.Remove(k);
                    return def.Tree.Where(n => t.GetValueOrDefault(n.Id) > 0).All(n => SkillTree.RequirementsMet(n, t));
                }).ToList();
                if (cand.Count == 0) break;
                var k = cand[Rng.Next(cand.Count)];
                pts[k]--; if (pts[k] == 0) pts.Remove(k);
            }
            c = TreeGreedy(c, slot, false);
            var v = Eval(c);
            if (v > bestV * (1 + 1e-6)) { Console.WriteLine($"    LNS tree {def.Id} {bestV:F0} -> {v:F0}"); cur = c; bestV = v; }
        }
        return cur;
    }

    public static void Main2(Dictionary<string, string> o)
    {
        var input = o["in"]; var output = o["out"];
        G = new Goal(o.GetValueOrDefault("skill"), o.GetValueOrDefault("type"), TrainOnly: o.ContainsKey("train-only"));
        if (G.Skill != null && G.Skill.Contains('^')) { var sp = G.Skill.Split('^'); G = G with { Skill = sp[0], Margin = Num(sp[1]) }; }
        if (G.Type != null && G.Type.Contains(">=")) { var tp = G.Type.Split(">="); G = G with { Type = tp[0], MinTypeShare = Num(tp[1]) }; }
        var asc = o.GetValueOrDefault("asc");
        int rounds = int.Parse(o.GetValueOrDefault("rounds", "2"));
        AllowKeystones = o.ContainsKey("allow-keystones");
        SurvivalWeight = Num(o.GetValueOrDefault("survival-weight", "0"));
        Ban = o.GetValueOrDefault("ban", "").Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Concat(AllowKeystones ? Array.Empty<string>() : new[] { "keystone" }).ToArray();
        var b = H.LoadBuild(input);
        bool scratchTrees = o.ContainsKey("scratch-trees");
        if (o.ContainsKey("scratch-passives")) { b.PassiveNodes.Clear(); b.AttributeChoices.Clear(); }
        if (scratchTrees) foreach (var s in b.Slots) s.NodePoints.Clear();
        b = Strip(b);
        foreach (var sl in b.Slots.Where(x => Banned("slot:" + x.SkillId)).ToList())
        {
            var repl = H.Data.Classes[b.ClassId].SkillIds.First(x => !Banned("slot:" + x) && b.Slots.All(y => y.SkillId != x));
            b.Rotation = b.Rotation.Select(r => r == sl.SkillId ? repl : r).ToList();
            sl.SkillId = repl; sl.NodePoints = new();
        }
        int lns = o.ContainsKey("lns") ? 25 : 0;
        bool slots = o.ContainsKey("slots");
        void Save(Build x) => File.WriteAllText(output, BuildCodec.ToJson(x));
        for (int r = 0; r < rounds; r++)
        {
            Console.WriteLine($"round {r}: {Eval(b):F0}");
            if (b.Level >= 20) { b = AscBest(b, asc); Console.WriteLine($"  asc {b.AscendancyId} {string.Join(",", b.AscendancyNodes)}: {Eval(b):F0}"); }
            if (b.PassiveNodes.Count < b.Level - 1) { b = PassiveGreedy(b); Console.WriteLine($"  passive greedy: {Eval(b):F0}"); }
            for (int i = 0; i < b.Slots.Count; i++) { b = (r == 0 && scratchTrees) || b.Slots[i].NodePoints.Values.Sum() < b.Slots[i].Level ? TreeGreedy(b, i, true) : TreeRefine(b, i); Console.WriteLine($"  tree {b.Slots[i].SkillId}: {Eval(b):F0}"); }
            Save(b);
            if (b.PassiveNodes.Count > 0) { b = PassiveRefine(b); Console.WriteLine($"  passive refine: {Eval(b):F0}"); }
            if (lns > 0 && b.PassiveNodes.Count > 0) { b = PassiveLns(b, lns); Console.WriteLine($"  passive LNS: {Eval(b):F0}"); Save(b); }
            if (lns > 0) for (int i = 0; i < b.Slots.Count; i++) b = TreeLns(b, i, 8);
            b = AttrRefine(b); Console.WriteLine($"  attr: {Eval(b):F0} {string.Join(",", b.AttributeChoices.GroupBy(x => x.Value).Select(g => g.Key + "=" + g.Count()))}");
            b = RotationBest(b); Console.WriteLine($"  rotation {string.Join(",", b.Rotation)}: {Eval(b):F0}");
            if (slots) { b = SlotSwap(b); Console.WriteLine($"  slots: {Eval(b):F0}"); }
            Save(b);
        }
        H.Report(output, b);
    }

    static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
}
