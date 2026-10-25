using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Character;

public sealed record CompiledCharacter(Build Build, ClassDefinition Class, StatEvaluator Evaluator,
    IReadOnlyList<Modifier> GlobalModifiers, IReadOnlyList<CompiledSkill> Skills, IReadOnlyList<MechanicEffect> Mechanics);
