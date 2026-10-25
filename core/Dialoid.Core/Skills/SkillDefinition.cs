using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Skills;

/// <summary>The numeric base of a skill (spec 8.1). Spell skills use SpellDamage, attacks use WeaponEffectiveness.</summary>
public sealed record SkillBase(IReadOnlyDictionary<DamageType, (double Min, double Max)> SpellDamage, double? WeaponEffectiveness,
    double AddedDamageEffectiveness, double CastTime, double AttackTimeMultiplier, double? Cooldown, double ResourceCost,
    double ResourceGain, double CritChance, int HitsPerUse, IReadOnlyDictionary<string, double> Behavior,
    IReadOnlyDictionary<string, double> AilmentChances, IReadOnlyList<string> WeaponTypes);

public sealed record SkillTreeNode(string Id, string Name, string Description, int MaxPoints,
    IReadOnlyList<(string NodeId, int Points)> Requires, string? ExclusiveGroup, IReadOnlyList<Effect> Effects);

public sealed record SkillDefinition(string Id, string Name, string Description, string? ClassId, int UnlockLevel, TagSet Tags,
    SkillBase Base, IReadOnlyList<SkillTreeNode> Tree, bool IsTriggeredOnly)
{
    /// <summary>An attack scales from the equipped weapon and therefore needs one.</summary>
    public bool IsAttack => Base.WeaponEffectiveness != null;
}

public sealed record WeaponStub(string Id, string Name, string Type, double PhysMin, double PhysMax, double AttacksPerSecond,
    double CritChance, IReadOnlyList<Modifier> Implicits);

public sealed record ItemStub(string Id, string Name, string GrantsSkill, int SkillLevelBonus);

public static class WeaponTypes
{
    public static readonly IReadOnlyList<string> All = new[] { "one_hand", "two_hand", "staff", "wand" };
}
