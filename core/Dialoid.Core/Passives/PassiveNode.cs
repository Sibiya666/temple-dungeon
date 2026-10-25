using Dialoid.Core.Effects;

namespace Dialoid.Core.Passives;

public enum PassiveKind { Start, Small, Attribute, Notable, Keystone }

/// <summary>
/// One node of the shared passive tree. Attribute nodes carry no effects of their own: they give
/// <see cref="PassiveTree.AttributeNodeBonus"/> to the attribute chosen in the build.
/// </summary>
public sealed record PassiveNode(string Id, string Name, string Description, PassiveKind Kind, string Region, double X, double Y,
    IReadOnlyList<Effect> Effects, string? StartForClassId);
