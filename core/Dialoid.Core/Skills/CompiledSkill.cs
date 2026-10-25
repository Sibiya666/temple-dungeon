using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Skills;

/// <summary>A slotted skill after the tree, passives, ascendancy and keystones were applied (spec 4.6, 8.4).</summary>
public sealed record CompiledSkill(string SkillId, int Level, bool IsLoot, TagSet Tags, DamageType? DamageTypeOverride,
    SkillBase Base, IReadOnlyDictionary<string, double> Behavior, IReadOnlyList<Conversion> Conversions,
    double ResourceCost, double ResourceGain, IReadOnlyList<Modifier> Modifiers,
    IReadOnlyList<GrantProcEffect> Procs, IReadOnlyList<MechanicEffect> Mechanics,
    IReadOnlyDictionary<string, int> EffectiveNodePoints)
{
    /// <summary>Skill level minus points actually paid; a node granted by an ascendancy is free and refunds the nodes it displaces.</summary>
    public int FreePoints { get; init; }
}
