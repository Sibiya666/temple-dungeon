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

    /// <summary>Channel skills: ticks speed up with whole seconds of continuous channelling (set by mechanics, e.g. whirl_rage).</summary>
    public ChannelRamp? ChannelRamp { get; init; }

    /// <summary>The node that granted each entry of <see cref="Mechanics"/> (same order). Missing entries fall back to kind "mechanic".</summary>
    public IReadOnlyList<SourceRef> MechanicSources { get; init; } = Array.Empty<SourceRef>();

    /// <summary>Scoped mechanics with the node that granted them.</summary>
    public IEnumerable<(MechanicEffect Effect, SourceRef Source)> SourcedMechanics() =>
        Mechanics.Select((m, i) => (m, i < MechanicSources.Count ? MechanicSources[i] : new SourceRef("mechanic", m.MechanicId, m.MechanicId)));
}

/// <summary>A channel lasts at most <paramref name="MaxSec"/>, then the skill goes on cooldown for <paramref name="CooldownSec"/>.</summary>
public sealed record ChannelLimit(double MaxSec, double CooldownSec);

/// <summary>Channel tick rate +<paramref name="PercentPerSec"/>% per whole second of continuous channelling, at most +<paramref name="CapPercent"/>%.
/// A faster tick also costs proportionally less, so the cost per second is unchanged.</summary>
public sealed record ChannelRamp(double PercentPerSec, double CapPercent)
{
    public double Multiplier(double channelSec) =>
        1 + Math.Min(CapPercent, PercentPerSec * Math.Floor(Math.Max(0, channelSec) + 1e-9)) / 100;
}
