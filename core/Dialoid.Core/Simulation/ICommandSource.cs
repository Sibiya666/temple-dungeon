using Dialoid.Core.Character;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Simulation;

/// <summary>What the character does when it is free to act: the offline <see cref="Rotation"/>, or live player commands.</summary>
internal interface ICommandSource
{
    /// <summary>The skill to use now, or null to stay idle this step.</summary>
    SkillRuntime? Next(SimState s, IConditionContext c, WeaponStub? weapon);

    /// <summary>True when a null from <see cref="Next"/> means the source wanted to act but nothing was usable, so the engine logs
    /// "no_resource" once per idle stretch. A player who simply does not press anything is not starved.</summary>
    bool WantsToAct => false;
}
