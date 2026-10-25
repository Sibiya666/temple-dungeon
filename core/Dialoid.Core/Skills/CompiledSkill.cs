using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Skills;

/// <summary>A slotted skill after the tree, passives, ascendancy, keystones and mechanics were applied (spec 4.6, 8.4).</summary>
public sealed record CompiledSkill(string SkillId, int Level, bool IsLoot, TagSet Tags, DamageType? DamageTypeOverride,
    SkillBase Base, IReadOnlyDictionary<string, double> Behavior, IReadOnlyList<Conversion> Conversions,
    double ResourceCost, double ResourceGain, IReadOnlyList<Modifier> Modifiers,
    IReadOnlyList<GrantProcEffect> Procs, IReadOnlyList<MechanicEffect> Mechanics,
    IReadOnlyDictionary<string, int> EffectiveNodePoints)
{
    private static readonly IReadOnlySet<DamageType> NoTypes = new HashSet<DamageType>();

    /// <summary>Skill level minus points actually paid; a node granted by an ascendancy is free and refunds the nodes it displaces.</summary>
    public int FreePoints { get; init; }

    /// <summary>Channel skills: stop after MaxSec of continuous channelling, then cooldown (set by mechanics, e.g. blood_pact).</summary>
    public ChannelLimit? ChannelLimit { get; init; }

    /// <summary>Final damage types that deal nothing (e.g. avatar_of_fire: unconverted physical).</summary>
    public IReadOnlySet<DamageType> NullifiedDamageTypes { get; init; } = NoTypes;
}

/// <summary>A channel lasts at most <paramref name="MaxSec"/>, then the skill goes on cooldown for <paramref name="CooldownSec"/>.</summary>
public sealed record ChannelLimit(double MaxSec, double CooldownSec);
