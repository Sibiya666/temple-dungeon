using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Simulation;

/// <summary>
/// The player's commands for <see cref="CombatEngine"/>: one remembered press (a newer press replaces it) and one held key.
/// When the character is free the press goes first, then the held skill (a channel, or any skill repeated while its key is held).
/// Both are checked again at that moment with <see cref="Rotation.Refusal"/>; a press that became unusable meanwhile is dropped.
/// </summary>
internal sealed class PlayerCommands : ICommandSource
{
    public SkillRuntime? Pressed { get; private set; }
    public SkillRuntime? Held { get; private set; }

    public void Press(SkillRuntime skill) => Pressed = skill;
    public void Hold(SkillRuntime skill) => Held = skill;
    public void Release() => Held = null;

    /// <summary>A held key that cannot be used (no resource) is a starved player, logged as "no_resource" like a starved
    /// rotation; not pressing anything is not.</summary>
    public bool WantsToAct => Held != null;

    public SkillRuntime? Next(SimState s, IConditionContext c, WeaponStub? weapon)
    {
        var pressed = Pressed;
        Pressed = null;
        if (pressed != null && Rotation.Refusal(pressed, s, c, weapon, s.Time) == PressRefusal.None) return pressed;
        if (Held != null && Rotation.Refusal(Held, s, c, weapon, s.Time) == PressRefusal.None) return Held;
        return null;
    }
}
