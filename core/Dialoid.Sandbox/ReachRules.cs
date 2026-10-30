using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;

namespace Dialoid.Sandbox;

/// <summary>
/// Whether a skill can be used at the dummy from where the hero stands (spec 6.3). The core knows no coordinates: the game layer
/// measures and passes numbers, and the rule decides by the tags of the <b>compiled</b> skill (tree nodes add and remove tags).
/// Owner's rule: no casting at a target off the screen, but a projectile may fly off the screen.
/// <para>The distance is in meters from the edge of the collision of the <b>nearest</b> dummy figure (the boss has its own radius;
/// on «Рой» the nearest of the five). "On the screen" means at least one figure is inside the camera's frame (windows ignored).</para>
/// </summary>
public static class ReachRules
{
    /// <summary>How far (meters, from the edge of the nearest dummy figure) a melee skill reaches.</summary>
    public const double MeleeReachMeters = 2;

    private const double Eps = 1e-9;

    /// <summary>
    /// <see cref="PressRefusal.None"/>, <see cref="PressRefusal.OutOfReach"/> or <see cref="PressRefusal.TargetNotVisible"/>:
    /// <list type="bullet">
    /// <item><c>projectile</c> — always (it flies straight and may leave the screen); wins over <c>melee</c>
    /// (Meteor with «Звездопад», Crush with «Ударная волна»);</item>
    /// <item><c>melee</c> — within <see cref="MeleeReachMeters"/>, else <see cref="PressRefusal.OutOfReach"/> (on the screen or not);</item>
    /// <item>shout or self buff (<c>shout</c>, <c>buff</c>) — always;</item>
    /// <item>anything else (a spell aimed at the target or the ground: Meteor, Poison fog, Blink) — only while the dummy is on the
    /// screen, else <see cref="PressRefusal.TargetNotVisible"/>; no distance limit.</item>
    /// </list>
    /// </summary>
    public static PressRefusal Check(TagSet compiledTags, double distanceMeters, bool targetOnScreen)
    {
        if (compiledTags.Contains("projectile")) return PressRefusal.None;
        if (compiledTags.Contains("melee"))
            return distanceMeters <= MeleeReachMeters + Eps ? PressRefusal.None : PressRefusal.OutOfReach;
        if (compiledTags.Contains("shout") || compiledTags.Contains("buff")) return PressRefusal.None;
        return targetOnScreen ? PressRefusal.None : PressRefusal.TargetNotVisible;
    }
}
