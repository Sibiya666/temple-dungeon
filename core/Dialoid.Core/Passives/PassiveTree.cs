namespace Dialoid.Core.Passives;

/// <summary>
/// The shared passive graph (undirected). Graph primitives only; full build legality lives in BuildRules.
/// The "allocated" sets passed to <see cref="CanAllocate"/> and <see cref="CanDeallocate"/> include the class start node.
/// </summary>
public sealed class PassiveTree
{
    public const int AttributeNodeBonus = 5;

    public static PassiveTree Empty { get; } = new(Array.Empty<PassiveNode>(), new Dictionary<string, IReadOnlyList<string>>());

    public IReadOnlyDictionary<string, PassiveNode> Nodes { get; }

    /// <summary>Adjacency lists. Links are symmetric between known nodes; a link to an unknown id is kept (the validator reports it).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Edges { get; }

    internal PassiveTree(IEnumerable<PassiveNode> nodes, IReadOnlyDictionary<string, IReadOnlyList<string>> links)
    {
        var nodeMap = new Dictionary<string, PassiveNode>();
        foreach (var n in nodes) nodeMap[n.Id] = n;
        var adj = new Dictionary<string, List<string>>();
        foreach (var id in nodeMap.Keys) adj[id] = new List<string>();
        foreach (var (from, targets) in links)
            foreach (var to in targets)
            {
                if (!adj[from].Contains(to)) adj[from].Add(to);
                if (adj.TryGetValue(to, out var back) && !back.Contains(from)) back.Add(from);
            }
        Nodes = nodeMap;
        Edges = adj.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    /// <summary>Passive points: 1 per level from level 2 (L1 → 0, L60 → 59).</summary>
    public static int PointsForLevel(int level) => Math.Max(0, level - 1);

    public PassiveNode? StartForClass(string classId) =>
        Nodes.Values.FirstOrDefault(n => n.StartForClassId == classId);

    /// <summary>True when every allocated node is reachable from the start through allocated nodes only.</summary>
    public bool IsConnectedToStart(IEnumerable<string> allocated, string startId)
    {
        var set = new HashSet<string>(allocated);
        set.Remove(startId);
        if (!Nodes.ContainsKey(startId)) return set.Count == 0;
        if (set.Any(id => !Nodes.ContainsKey(id))) return false;

        var seen = new HashSet<string> { startId };
        var queue = new Queue<string>();
        queue.Enqueue(startId);
        while (queue.Count > 0)
            foreach (var next in Edges[queue.Dequeue()])
                if (Nodes.ContainsKey(next) && set.Contains(next) && seen.Add(next)) queue.Enqueue(next);
        return seen.Count == set.Count + 1;
    }

    /// <summary>A node can be taken when it exists, is not a start node, is not taken yet and touches an allocated node.</summary>
    public bool CanAllocate(IReadOnlySet<string> allocated, string nodeId)
    {
        if (!Nodes.TryGetValue(nodeId, out var node) || node.Kind == PassiveKind.Start) return false;
        if (allocated.Contains(nodeId)) return false;
        return Edges[nodeId].Any(allocated.Contains);
    }

    /// <summary>A node can be dropped when it is allocated, is not the start and the rest stays connected to the start.</summary>
    public bool CanDeallocate(IReadOnlySet<string> allocated, string nodeId, string startId)
    {
        if (nodeId == startId || !allocated.Contains(nodeId)) return false;
        return IsConnectedToStart(allocated.Where(id => id != nodeId), startId);
    }
}
