using Dialoid.Core.Stats;

namespace Dialoid.Core.Character;

public sealed record ResourceDefinition(string Id, string Name, double BaseMax, double RegenPerSec, double DecayPerSec,
    double DecayDelaySec, PerStat? MaxPer);

public sealed record ClassDefinition(string Id, string Name, Attribute Primary, IReadOnlyDictionary<Attribute, int> BaseAttributes,
    double LifeMultiplier, ResourceDefinition Resource, IReadOnlyList<Modifier> ClassBonuses, string PassiveStartNodeId,
    IReadOnlyList<string> SkillIds, IReadOnlyList<string> AscendancyIds);

/// <summary>One general attribute bonus: ValuePerPoint of Stat (of the given kind) for each point of Attribute.</summary>
public sealed record AttributeBonus(Attribute Attribute, string Stat, ModKind Kind, double ValuePerPoint);

/// <summary>Tunable attribute rules from attributes.json.</summary>
public sealed record AttributeRules(IReadOnlyList<AttributeBonus> Bonuses, double PrimaryDamageMorePerPoint)
{
    public static AttributeRules Empty { get; } = new(Array.Empty<AttributeBonus>(), 0);
}
