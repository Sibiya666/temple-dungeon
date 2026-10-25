using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Character;

public sealed record CompiledCharacter(Build Build, ClassDefinition Class, StatEvaluator Evaluator,
    IReadOnlyList<Modifier> GlobalModifiers, IReadOnlyList<CompiledSkill> Skills, IReadOnlyList<MechanicEffect> Mechanics,
    IReadOnlyList<SourcedEffect> DeferredEffects)
{
    /// <summary>Global-scope mechanics with their sources, in hook order (keystones last). Simulator hooks fire these at character level.</summary>
    public IReadOnlyList<SourcedEffect> GlobalMechanics { get; init; } = Array.Empty<SourcedEffect>();

    /// <summary>Numeric rules set by mechanics (keys in <see cref="CharacterRules"/>); a missing key means the default rule.</summary>
    public IReadOnlyDictionary<string, double> Rules { get; init; } = new Dictionary<string, double>();
}

/// <summary>An effect that is not a global modifier (skill/tag scoped, conversions, procs...), kept for skill compilation.</summary>
public sealed record SourcedEffect(Effect Effect, SourceRef Source);

/// <summary>Keys of <see cref="CompiledCharacter.Rules"/>: rule changes that are not stats (no breakdown needed).</summary>
public static class CharacterRules
{
    /// <summary>Share of armor that also mitigates elemental hits taken, with the armor formula (stone_skin: 0.5).</summary>
    public const string ElementalArmorFraction = "elemental_armor_fraction";
}
