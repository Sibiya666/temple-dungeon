using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Character;

public sealed record CompiledCharacter(Build Build, ClassDefinition Class, StatEvaluator Evaluator,
    IReadOnlyList<Modifier> GlobalModifiers, IReadOnlyList<CompiledSkill> Skills, IReadOnlyList<MechanicEffect> Mechanics,
    IReadOnlyList<SourcedEffect> DeferredEffects);

/// <summary>An effect that is not a global modifier (skill/tag scoped, conversions, procs...), kept for skill compilation.</summary>
public sealed record SourcedEffect(Effect Effect, SourceRef Source);
