using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;

namespace Dialoid.Sandbox;

/// <summary>
/// Whether a skill can be used at the dummy from where the hero stands (spec 6.3). The core knows no coordinates, so the game layer
/// decides by the tags of the <b>compiled</b> skill (tree nodes add and remove tags). Owner's rule: no casting at a target off the
/// screen, but a projectile may fly off the screen.
/// </summary>
public static class ReachRules
{
    /// <summary>How far (in tiles, from the dummy) a melee skill reaches.</summary>
    public const double MeleeTiles = 2;

    private const double Eps = 1e-9;

    /// <summary>
    /// <see cref="PressRefusal.None"/> or <see cref="PressRefusal.OutOfReach"/>:
    /// <list type="bullet">
    /// <item><c>projectile</c> — always (it flies straight and may leave the screen); wins over <c>melee</c>
    /// (Meteor with «Звездопад», Crush with «Ударная волна»);</item>
    /// <item><c>melee</c> — within <see cref="MeleeTiles"/>;</item>
    /// <item>shout or self buff (<c>shout</c>, <c>buff</c>) — always;</item>
    /// <item>anything else (a spell aimed at the target or the ground: Meteor, Poison fog, Blink) — only while the dummy is on the
    /// screen.</item>
    /// </list>
    /// </summary>
    public static PressRefusal Check(TagSet compiledTags, double distanceTiles, bool targetOnScreen)
    {
        if (compiledTags.Contains("projectile")) return PressRefusal.None;
        if (compiledTags.Contains("melee")) return distanceTiles <= MeleeTiles + Eps ? PressRefusal.None : PressRefusal.OutOfReach;
        if (compiledTags.Contains("shout") || compiledTags.Contains("buff")) return PressRefusal.None;
        return targetOnScreen ? PressRefusal.None : PressRefusal.OutOfReach;
    }
}
