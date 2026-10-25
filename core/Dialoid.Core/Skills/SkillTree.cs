namespace Dialoid.Core.Skills;

/// <summary>Pure helpers over a skill's tree: 1 point per skill level, requirements, exclusive groups.</summary>
public static class SkillTree
{
    public static int PointsForLevel(int skillLevel) => Math.Max(0, skillLevel);

    public static int PointsSpent(IReadOnlyDictionary<string, int> nodePoints) => nodePoints.Values.Where(p => p > 0).Sum();

    /// <summary>True when every requirement of the node is met by the given point allocation.</summary>
    public static bool RequirementsMet(SkillTreeNode node, IReadOnlyDictionary<string, int> nodePoints) =>
        node.Requires.All(r => nodePoints.TryGetValue(r.NodeId, out var p) && p >= r.Points);

    /// <summary>The exclusive groups (with their nodes) that hold points in more than one node.</summary>
    public static IEnumerable<(string Group, IReadOnlyList<string> Nodes)> ViolatedGroups(SkillDefinition skill, IReadOnlyDictionary<string, int> nodePoints) =>
        skill.Tree.Where(n => n.ExclusiveGroup != null && nodePoints.TryGetValue(n.Id, out var p) && p > 0)
            .GroupBy(n => n.ExclusiveGroup!)
            .Where(g => g.Count() > 1)
            .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(n => n.Id).ToList()));

    /// <summary>Returns a requirement cycle as a node path (first == last), or null when the requirements are acyclic.</summary>
    public static IReadOnlyList<string>? FindRequirementCycle(IReadOnlyList<SkillTreeNode> tree)
    {
        var byId = tree.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        var state = new Dictionary<string, int>(); // 1 = on stack, 2 = done
        var path = new List<string>();

        IReadOnlyList<string>? Visit(string id)
        {
            state[id] = 1;
            path.Add(id);
            foreach (var (req, _) in byId[id].Requires)
            {
                if (!byId.ContainsKey(req)) continue;
                if (state.TryGetValue(req, out var s))
                {
                    if (s == 1)
                    {
                        var cycle = path.Skip(path.IndexOf(req)).ToList();
                        cycle.Add(req);
                        return cycle;
                    }
                    continue;
                }
                if (Visit(req) is { } found) return found;
            }
            path.RemoveAt(path.Count - 1);
            state[id] = 2;
            return null;
        }

        foreach (var id in byId.Keys)
            if (!state.ContainsKey(id) && Visit(id) is { } cycle) return cycle;
        return null;
    }
}

/// <summary>Experience tables (spec 5.3 / 8.2). Thresholds are cumulative XP: index i is the XP needed for level i + 1.</summary>
public sealed record ProgressionTable(IReadOnlyList<double> CharacterLevels, IReadOnlyList<double> SkillLevels)
{
    public static ProgressionTable Empty { get; } = new(Array.Empty<double>(), Array.Empty<double>());
}

public static class SkillProgression
{
    /// <summary>Level reached with the given XP: the number of thresholds passed, at least 1 and at most cap.</summary>
    public static int LevelForXp(double xp, IReadOnlyList<double> thresholds, int cap)
    {
        int level = 1;
        for (int i = 1; i < thresholds.Count && level < cap; i++)
        {
            if (xp >= thresholds[i]) level = i + 1;
            else break;
        }
        return Math.Max(1, Math.Min(level, cap));
    }
}
