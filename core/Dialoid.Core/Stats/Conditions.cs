namespace Dialoid.Core.Stats;

public interface IConditionContext
{
    bool IsMet(Condition condition);
}

/// <summary>The fixed set of condition ids the simulation can evaluate (spec 4.5). The validator rejects any other id.</summary>
public static class ConditionCatalog
{
    public static IReadOnlyList<string> Ids { get; } = new[]
    {
        "target_ignited", "target_chilled", "target_shocked", "target_bleeding", "target_poisoned", "target_frozen",
        "resource_above_pct", "life_below_pct", "recently_crit", "recently_used_skill", "channelling_for", "buff_active",
    };

    public static bool IsKnown(string id) => Ids.Contains(id);
}
