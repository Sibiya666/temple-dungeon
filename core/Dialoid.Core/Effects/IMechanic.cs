using Dialoid.Core.Simulation;

namespace Dialoid.Core.Effects;

/// <summary>
/// A code-driven mechanic referenced by id from data (spec 4.6): <c>{"type":"mechanic","id":"...","params":{...}}</c>.
/// A mechanic implements only the hooks it needs; each call gets the effect's params through the context.
/// <para>Who calls what:</para>
/// <list type="bullet">
/// <item><see cref="OnCompile"/>: the character compiler. A Global-scope mechanic gets one character-phase call
/// (<c>ctx.Skill == null</c>: change character modifiers and rules) and then one skill-phase call per compiled skill
/// (<c>ctx.Skill != null</c>: change that skill's cost, cooldown, channel limit, conversions, modifiers). A skill/tags-scoped
/// mechanic gets skill-phase calls only, for the skills it landed on. Order: non-keystone sources first, keystones last.</item>
/// <item><see cref="OnUse"/>, <see cref="OnHit"/>, <see cref="OnKill"/>, <see cref="OnTick"/>, <see cref="OnAilmentApplying"/>,
/// <see cref="OnAilmentApplied"/>: the simulator. Global-scope
/// mechanics fire for every skill; scoped ones only for the skill that carries them (never both, no double firing).</item>
/// </list>
/// </summary>
public interface IMechanic
{
    string Id { get; }

    /// <summary>Compile time: modifiers (incl. Override with priority), costs, cooldowns, channel limits, conversions, rules.</summary>
    void OnCompile(MechanicCompileContext ctx) { }

    /// <summary>A rotation skill was used (a channel tick counts as one), after its cost was paid and before its hits.</summary>
    void OnUse(MechanicSimContext ctx) { }

    /// <summary>One hit on one target (triggered copies too: see <see cref="MechanicSimContext.IsTriggeredCopy"/>).</summary>
    void OnHit(MechanicSimContext ctx) { }

    /// <summary>A target died, before its ailments are cleared. The skill is the last damage source (may be null).</summary>
    void OnKill(MechanicSimContext ctx) { }

    /// <summary>Once per simulation step, after DoTs, resource and buffs ticked.</summary>
    void OnTick(MechanicSimContext ctx, double dt) { }

    /// <summary>
    /// An ailment instance of the event skill is about to land on <see cref="MechanicSimContext.TargetIndex"/> (from a hit's
    /// roll, a proc or <see cref="MechanicSimContext.ApplyAilment"/>; triggered copies too). Return the instance to apply:
    /// unchanged, or with another magnitude/duration/dps. Mechanics run in hook order, each seeing the previous result.
    /// </summary>
    AilmentInstance OnAilmentApplying(MechanicSimContext ctx, AilmentInstance ailment) => ailment;

    /// <summary>
    /// An ailment instance of the event skill actually landed on <see cref="MechanicSimContext.TargetIndex"/>: the target's
    /// stacking rule accepted it (a freeze on a frozen or freeze-immune target, or an ignite weaker than every stack, did not
    /// land). <paramref name="ailment"/> is the instance after every <see cref="OnAilmentApplying"/>. Copies landed by
    /// <see cref="MechanicSimContext.LandAilment"/> do not fire it.
    /// </summary>
    void OnAilmentApplied(MechanicSimContext ctx, AilmentInstance ailment) { }
}
