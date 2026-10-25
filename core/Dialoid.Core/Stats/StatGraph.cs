namespace Dialoid.Core.Stats;

/// <summary>Dependency graph between stats formed by 'per' modifiers (edge: modified stat -> per.Stat).</summary>
public static class StatGraph
{
    /// <summary>Returns a cycle path such as ["a","b","a"], or null when the graph is acyclic.</summary>
    public static IReadOnlyList<string>? FindCycle(IReadOnlyList<Modifier> mods)
    {
        var edges = new Dictionary<string, List<string>>();
        var order = new List<string>();
        foreach (var m in mods)
        {
            if (m.Per is null) continue;
            if (!edges.TryGetValue(m.Stat, out var list))
            {
                edges[m.Stat] = list = new List<string>();
                order.Add(m.Stat);
            }
            if (!list.Contains(m.Per.Stat)) list.Add(m.Per.Stat);
        }

        var state = new Dictionary<string, int>(); // 1 = on path, 2 = done
        var path = new List<string>();
        foreach (var start in order)
        {
            var cycle = Visit(start);
            if (cycle != null) return cycle;
        }
        return null;

        IReadOnlyList<string>? Visit(string node)
        {
            if (state.TryGetValue(node, out var s))
            {
                if (s == 2) return null;
                var i = path.IndexOf(node);
                var cycle = path.GetRange(i, path.Count - i);
                cycle.Add(node);
                return cycle;
            }
            state[node] = 1;
            path.Add(node);
            if (edges.TryGetValue(node, out var next))
                foreach (var n in next)
                {
                    var c = Visit(n);
                    if (c != null) return c;
                }
            path.RemoveAt(path.Count - 1);
            state[node] = 2;
            return null;
        }
    }
}
