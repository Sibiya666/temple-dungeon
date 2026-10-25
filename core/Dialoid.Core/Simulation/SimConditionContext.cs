using Dialoid.Core.Stats;

namespace Dialoid.Core.Simulation;

/// <summary>
/// Evaluates the spec 4.5 condition catalog from simulation state. target_* conditions refer to the target being hit while
/// a hit (or its ailments) is resolved (<see cref="HitTarget"/>), otherwise to the primary target.
/// </summary>
internal sealed class SimConditionContext : IConditionContext
{
    public const double RecentlySec = 4;
    private const double Eps = 1e-9;

    private readonly SimState _s;

    public SimConditionContext(SimState state) => _s = state;

    /// <summary>Index of the target being hit (set by the use resolver around one hit); null: target_* use the primary target.</summary>
    public int? HitTarget { get; set; }

    public bool IsMet(Condition c) => c.Id switch
    {
        "target_ignited" => PrimaryHas("ignite"),
        "target_chilled" => PrimaryHas("chill"),
        "target_shocked" => PrimaryHas("shock"),
        "target_bleeding" => PrimaryHas("bleed"),
        "target_poisoned" => PrimaryHas("poison"),
        "target_frozen" => PrimaryHas("freeze"),
        "resource_above_pct" => _s.ResourceMax > 0 && _s.Resource / _s.ResourceMax * 100 > (c.Param ?? 0),
        "life_below_pct" => _s.LifeMax > 0 && _s.Life / _s.LifeMax * 100 < (c.Param ?? 0),
        "recently_crit" => _s.LastCritTime is { } t && _s.Time - t <= (c.Param ?? RecentlySec) + Eps,
        "recently_used_skill" => RecentlyUsed(c.Ref, c.Param ?? RecentlySec),
        "channelling_for" => _s.ChannelSkillId != null && (c.Ref == null || c.Ref == _s.ChannelSkillId)
                             && _s.Time - _s.ChannelStart >= (c.Param ?? 0) - Eps,
        "buff_active" => c.Ref != null && _s.Buffs.TryGetValue(c.Ref, out var left) && left > 0,
        _ => false, // the validator rejects unknown ids
    };

    private bool PrimaryHas(string ailmentId) =>
        HitTarget is { } i ? _s.Targets[i].HasAilment(ailmentId) : _s.Primary?.HasAilment(ailmentId) ?? false;

    private bool RecentlyUsed(string? skillId, double window)
    {
        if (skillId != null)
            return _s.LastUseTime.TryGetValue(skillId, out var t) && _s.Time - t <= window + Eps;
        foreach (var t in _s.LastUseTime.Values)
            if (_s.Time - t <= window + Eps) return true;
        return false;
    }
}
