using Dialoid.Core.Passives;
public static class Check
{
    static (double o, double s) M(Dialoid.Core.Character.Build b)
    {
        if (H.V(b).Count > 0) return (double.NaN, double.NaN);
        var t = H.Run(b, "training").DpsSingleTarget; var w = H.Run(b, "swarm").DpsPack; var d = H.Run(b, "attacking_boss", true).TimeToDeath ?? 999;
        return (Math.Sqrt(t * w), d);
    }
    public static void Run(string file)
    {
        var b = H.LoadBuild(file);
        var (o0, s0) = M(b);
        Console.WriteLine($"base obj {o0:F0} surv {s0:F1}");
        var asc = H.Data.Ascendancies[b.AscendancyId!];
        foreach (var n in b.AscendancyNodes)
        {
            var c = H.Clone(b); c.AscendancyNodes.Remove(n);
            var (o, s) = M(c);
            Console.WriteLine($"  asc -{n}: obj {100 * (o / o0 - 1):F1}% surv {s - s0:F1}");
        }
        var tree = H.Data.Passives; var start = H.Data.Classes[b.ClassId].PassiveStartNodeId;
        var alloc = b.PassiveNodes.Append(start).ToHashSet();
        var lst = new List<string>();
        foreach (var n in b.PassiveNodes.Where(n => tree.CanDeallocate(alloc, n, start)))
        {
            var c = H.Clone(b); c.PassiveNodes.Remove(n); c.AttributeChoices.Remove(n);
            var (o, s) = M(c);
            lst.Add($"  leaf -{n}: obj {100 * (o / o0 - 1):F2}% surv {s - s0:F1}");
        }
        foreach (var l in lst) Console.WriteLine(l);
        for (int i = 0; i < b.Slots.Count; i++)
            foreach (var k in b.Slots[i].NodePoints.Keys.ToList())
            {
                var c = H.Clone(b); var p = c.Slots[i].NodePoints; p[k]--; if (p[k] == 0) p.Remove(k);
                var (o, s) = M(c);
                if (double.IsNaN(o)) continue;
                if (Math.Abs(o / o0 - 1) < 0.001 && Math.Abs(s - s0) < 0.01) Console.WriteLine($"  ZERO skill node {b.Slots[i].SkillId}/{k}");
            }
    }
}
