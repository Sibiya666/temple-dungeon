using Dialoid.Core.Character;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Simulation;

/// <summary>
/// Priority rotation (spec 11.3): Build.Rotation order (fallback: slot order), all non-generators first, then generators,
/// so a generator is used only when every spender is blocked. Triggered-only skills never enter the rotation.
/// </summary>
internal sealed class Rotation : ICommandSource
{
    private const double Eps = 1e-9;

    public IReadOnlyList<SkillRuntime> Order { get; }

    public Rotation(Build build, IReadOnlyList<SkillRuntime> skills)
    {
        var byId = skills.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var ids = build.Rotation.Count > 0 ? build.Rotation.Distinct() : build.Slots.Select(s => s.SkillId);
        var ordered = ids.Where(byId.ContainsKey).Select(id => byId[id]).Where(s => !s.IsTriggeredOnly).ToList();
        Order = ordered.Where(s => !s.IsGenerator).Concat(ordered.Where(s => s.IsGenerator)).ToList();
    }

    public bool IsEmpty => Order.Count == 0;

    /// <summary>A non-empty rotation with nothing usable is starved (logged as no_resource).</summary>
    public bool WantsToAct => !IsEmpty;

    public SkillRuntime? Next(SimState s, IConditionContext c, WeaponStub? weapon) => Pick(s, c, weapon);

    /// <summary>First skill whose cooldown is ready, whose cost is affordable and whose weapon requirement is met.</summary>
    public SkillRuntime? Pick(SimState s, IConditionContext c, WeaponStub? weapon)
    {
        foreach (var skill in Order)
            if (Refusal(skill, s, c, weapon, s.Time) == PressRefusal.None) return skill;
        return null;
    }

    /// <summary>
    /// Why <paramref name="skill"/> cannot be used now (None when it can): the checks of <see cref="Pick"/>, shared with live
    /// key presses. Order: weapon, own buff still running, cooldown (ready by <paramref name="readyBy"/>, the time the use would
    /// start), resource.
    /// </summary>
    public static PressRefusal Refusal(SkillRuntime skill, SimState s, IConditionContext c, WeaponStub? weapon, double readyBy)
    {
        if (!skill.WeaponFits(weapon)) return PressRefusal.WeaponMismatch;
        if (skill.OwnBuffIds.Any(id => s.Buffs.TryGetValue(id, out var left) && left > 0)) return PressRefusal.BuffActive;
        if (s.CooldownReadyAt.TryGetValue(skill.Id, out var ready) && ready > readyBy + Eps) return PressRefusal.Cooldown;
        if (skill.Cost(c) / skill.TickSpeed(s, s.Time) > s.Resource + Eps) return PressRefusal.NotEnoughResource;
        return PressRefusal.None;
    }
}
