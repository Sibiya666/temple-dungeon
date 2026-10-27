using Dialoid.Core.Builds;
using Dialoid.Core.Character;
using Dialoid.Core.Passives;
using Attr = Dialoid.Core.Character.Attribute;

/// <summary>
/// `keystone`: a keystone test build (spec 15.6) from a reference build. The keystone and the shortest path to it are added;
/// the points come from the passive leaves whose removal costs the reference objective √(training × swarm) least. The keystone
/// stays a leaf, so the test can remove it alone. --attr X sets every attribute choice (old and new) to X first. --ban p1|p2 first
/// strips the banned nodes from the build (as `opt` does) and keeps them off the path, so a later `opt` with the same ban keeps
/// the keystone connected.
/// </summary>
public static class KeystoneBuild
{
    public static void Run(Dictionary<string, string> o)
    {
        var ks = "keystone_" + o["ks"];
        var tree = H.Data.Passives;
        var b = H.LoadBuild(o["in"]);
        if (o.TryGetValue("ban", out var ban))
        {
            Opt.Ban = ban.Split('|', StringSplitOptions.RemoveEmptyEntries);
            b = Opt.Strip(b);
        }
        var start = H.Data.Classes[b.ClassId].PassiveStartNodeId;
        Attr? attr = o.TryGetValue("attr", out var a) ? Enum.Parse<Attr>(a, true) : null;
        if (attr != null) foreach (var k in b.AttributeChoices.Keys.ToList()) b.AttributeChoices[k] = attr.Value;
        double Obj(Build x) => H.V(x).Count > 0 ? double.NegativeInfinity : Math.Sqrt(H.Run(x, "training").DpsSingleTarget * H.Run(x, "swarm").DpsPack);
        while (true)
        {
            var path = PathTo(b, ks, start);
            var free = b.Level - 1 - b.PassiveNodes.Count;
            if (path.Count <= free)
            {
                foreach (var n in path)
                {
                    b.PassiveNodes.Add(n);
                    if (tree.Nodes[n].Kind == PassiveKind.Attribute) b.AttributeChoices[n] = attr ?? (b.ClassId == "warrior" ? Attr.Str : Attr.Int);
                }
                break;
            }
            var alloc = b.PassiveNodes.Append(start).ToHashSet();
            var cands = b.PassiveNodes.Where(n => tree.CanDeallocate(alloc, n, start)).Select(n =>
            {
                var c = H.Clone(b); c.PassiveNodes.Remove(n); c.AttributeChoices.Remove(n); return (n, c);
            }).ToList();
            var vals = new double[cands.Count];
            Parallel.For(0, cands.Count, i => vals[i] = Obj(cands[i].c));
            var best = Enumerable.Range(0, cands.Count).MaxBy(i => vals[i]);
            Console.WriteLine($"  - {cands[best].n} ({vals[best]:F0})");
            b = cands[best].c;
        }
        var v = H.V(b);
        if (v.Count > 0) throw new Exception(string.Join("; ", v.Select(x => x.Message)));
        File.WriteAllText(o["out"], BuildCodec.ToJson(b));
        var without = H.Clone(b); without.PassiveNodes.Remove(ks);
        H.Report(o["out"], b);
        H.Report("  without keystone", without);
    }

    /// <summary>Shortest list of unallocated nodes from the allocated set to the target (target last).</summary>
    static List<string> PathTo(Build b, string target, string start)
    {
        var tree = H.Data.Passives;
        var alloc = b.PassiveNodes.Append(start).ToHashSet();
        var parent = new Dictionary<string, string?>();
        var q = new Queue<string>();
        foreach (var x in alloc) { parent[x] = null; q.Enqueue(x); }
        while (q.Count > 0)
        {
            var x = q.Dequeue();
            if (x == target) break;
            foreach (var y in tree.Edges[x])
            {
                if (parent.ContainsKey(y) || tree.Nodes[y].Kind == PassiveKind.Start || Opt.Banned(y)) continue;
                if (tree.Nodes[y].Kind == PassiveKind.Keystone && y != target) continue;
                parent[y] = x; q.Enqueue(y);
            }
        }
        var path = new List<string>();
        for (string? c = target; c != null && !alloc.Contains(c); c = parent[c]) path.Add(c);
        path.Reverse();
        return path;
    }
}
