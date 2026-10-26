using Dialoid.Core.Builds;
using Dialoid.Core.Passives;

public static class AttrVar
{
    public static void Run(Dictionary<string, string> o)
    {
        Opt.G = new Goal(o.GetValueOrDefault("skill"), o.GetValueOrDefault("type"));
        Opt.Ban = o.GetValueOrDefault("ban", "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        var tree = H.Data.Passives;
        var b = H.LoadBuild(o["in"]);
        var start = H.Data.Classes[b.ClassId].PassiveStartNodeId;
        int removed = 0;
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var n in b.PassiveNodes.Where(n => tree.Nodes[n].Kind == PassiveKind.Attribute).ToList())
            {
                var alloc = b.PassiveNodes.Append(start).ToHashSet();
                if (!tree.CanDeallocate(alloc, n, start)) continue;
                b.PassiveNodes.Remove(n); b.AttributeChoices.Remove(n); removed++; changed = true;
            }
        }
        Console.WriteLine($"removed {removed} attribute nodes");
        for (int i = 0; i < removed; i++)
        {
            var alloc = b.PassiveNodes.Append(start).ToHashSet();
            var cands = tree.Nodes.Values.Where(n => n.Kind == PassiveKind.Small && tree.CanAllocate(alloc, n.Id)).Select(n =>
            {
                var c = H.Clone(b); c.PassiveNodes.Add(n.Id); return c;
            }).ToList();
            var vals = cands.Select(Opt.Eval).ToList();
            var max = vals.Max();
            var near = Enumerable.Range(0, cands.Count).Where(i => vals[i] >= max * 0.999).ToList();
            var bi = near.MaxBy(i => (H.Run(cands[i], "attacking_boss", true).TimeToDeath ?? 999, vals[i]));
            Console.WriteLine($"  + {cands[bi].PassiveNodes[^1]} {vals[bi]:F0}");
            b = cands[bi];
        }
        File.WriteAllText(o["out"], BuildCodec.ToJson(b));
        H.Report(o["out"], b);
    }
}
