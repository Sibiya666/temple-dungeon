using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Mechanics;

// Pyromancer (Пироман) ascendancy (spec 10.4). The numbers are params with the spec values as defaults. Пожар (start,
// ignite_max_stacks +2 for spells) and Метеоритный дождь (damage More -50 plus two on_use casts of the meteor) are plain effects.

/// <summary>
/// Распространение, sandbox rule (spec 10.4): on a pack, the ignite a use lands counts as applied to every living pack
/// target delay (1) s later. Ignites that landed at the same moment from the same skill form one group: the strongest of
/// them spreads, delay seconds older and at the same damage per second, to the living targets the group missed (so a use
/// that already ignited the whole pack adds nothing). Copies land by the target's stacking rule and never spread again.
/// ("A dying ignited target passes its ignite to its neighbours" is the game rule; the sandbox has no positions.)
/// </summary>
public sealed class SpreadIgnite : IMechanic
{
    private sealed record Pending(double LandedAt, int Source, AilmentInstance Ailment);

    public string Id => "spread_ignite";

    public void OnAilmentApplied(MechanicSimContext ctx, AilmentInstance ailment)
    {
        if (ailment.AilmentId != ctx.Text("ailment", "ignite") || ctx.TargetCount < 2) return;
        ctx.State<List<Pending>>("pending").Add(new Pending(ctx.Time, ctx.TargetIndex, ailment));
    }

    public void OnTick(MechanicSimContext ctx, double dt)
    {
        var pending = ctx.State<List<Pending>>("pending");
        if (pending.Count == 0) return;
        var now = ctx.Time + dt; // end of this step: the ailments already ticked
        var due = pending.Where(p => now >= p.LandedAt + ctx.Number("delay", 1) - 1e-9).ToList();
        foreach (var p in due) pending.Remove(p);
        foreach (var group in due.GroupBy(p => (p.LandedAt, p.Ailment.SourceSkillId)))
        {
            var best = group.MaxBy(p => p.Ailment.DamagePerSec)!;
            var copy = best.Ailment with { Remaining = best.Ailment.Remaining - (now - best.LandedAt) };
            if (copy.Remaining <= 1e-9) continue;
            var reached = group.Select(p => p.Source).ToHashSet();
            for (var i = 0; i < ctx.TargetCount; i++)
                if (!reached.Contains(i) && ctx.IsAlive(i))
                    ctx.LandAilment(i, copy);
        }
    }
}

/// <summary>
/// Пепел. Hits on ignited targets deal more_per_stack (+1)% More damage per ignite stack on the whole pack, at most cap (25)%.
/// An endless buff (damage More, tag hit, condition target_ignited: the hit target) updated on each use and step, so a hit
/// sees the stacks as of its use.
/// </summary>
public sealed class Ashes : IMechanic
{
    public string Id => "ashes";

    public void OnUse(MechanicSimContext ctx) => Update(ctx);
    public void OnTick(MechanicSimContext ctx, double dt) => Update(ctx);

    private static void Update(MechanicSimContext ctx)
    {
        var ailment = ctx.Text("ailment", "ignite");
        var stacks = 0;
        for (var i = 0; i < ctx.TargetCount; i++)
            if (ctx.IsAlive(i)) stacks += ctx.AilmentStacks(i, ailment);
        var value = Math.Min(ctx.Number("cap", 25), stacks * ctx.Number("more_per_stack", 1));
        if (Math.Abs(value - ctx.Counter("buff")) < 1e-9) return;
        ctx.SetCounter("buff", value);
        var id = "mechanic:" + ctx.Effect.MechanicId;
        if (value == 0) { ctx.RemoveBuff(id); return; }
        var mod = new Modifier("damage", ModKind.More, value, TagSet.Of(HitCalculator.HitTag),
            new[] { new Condition(ctx.Text("condition", "target_ignited")) }, null, ctx.Source);
        ctx.ApplyBuff(id, double.PositiveInfinity, new[] { mod });
    }
}

/// <summary>Вечный огонь. Ignites last duration_mult (x1.5) as long at the same damage per second (more in total).</summary>
public sealed class EternalFire : IMechanic
{
    public string Id => "eternal_fire";

    public AilmentInstance OnAilmentApplying(MechanicSimContext ctx, AilmentInstance ailment) =>
        ailment.AilmentId == ctx.Text("ailment", "ignite") ? ailment with { Remaining = ailment.Remaining * ctx.Number("duration_mult", 1.5) } : ailment;
}

/// <summary>
/// Жар души. A use of one of <c>skills</c> (fireball, meteor) restores per_target (1)% of max mana for each ignited target it
/// hit, at most max (5)% per use. A target counts once per use: when it was ignited before the hit, or when this use's
/// ignite landed on it. Triggered copies restore nothing (they are not uses, spec 8.4).
/// </summary>
public sealed class SoulHeat : IMechanic
{
    public string Id => "soul_heat";

    public void OnUse(MechanicSimContext ctx)
    {
        if (!Listed(ctx)) return;
        ctx.SetCounter("use", ctx.Counter("use") + 1);
        ctx.SetCounter("gained", 0);
    }

    public void OnHit(MechanicSimContext ctx)
    {
        if (ctx.HasAilment(ctx.TargetIndex, ctx.Text("ailment", "ignite"))) Credit(ctx);
    }

    public void OnAilmentApplied(MechanicSimContext ctx, AilmentInstance ailment)
    {
        if (ailment.AilmentId == ctx.Text("ailment", "ignite")) Credit(ctx);
    }

    private bool Listed(MechanicSimContext ctx) =>
        !ctx.IsTriggeredCopy && ctx.SkillId != null && ctx.Texts("skills", "fireball", "meteor").Contains(ctx.SkillId);

    private void Credit(MechanicSimContext ctx)
    {
        if (!Listed(ctx)) return;
        var use = ctx.Counter("use");
        var key = "target" + ctx.TargetIndex;
        if (ctx.Counter(key) == use) return; // already counted in this use
        ctx.SetCounter(key, use);
        var pct = Math.Min(ctx.Number("per_target", 1), ctx.Number("max", 5) - ctx.Counter("gained"));
        if (pct <= 1e-9) return;
        ctx.SetCounter("gained", ctx.Counter("gained") + pct);
        ctx.AddResource(pct / 100 * ctx.ResourceMax);
    }
}

/// <summary>
/// Огненная броня, sandbox rule (spec 10.4): when a slotted skill makes burning ground, the character gets armor_increased
/// (+30)% Increased armor and resist_fire_max +resist_cap (10). Burning ground: behavior ground_duration and ground_fraction
/// above 0 and a fire zone (the skill's ground type, else a fire skill: zones take the hit's largest share). Checked on the
/// skills as compiled before the mechanic skill phase.
/// </summary>
public sealed class FireArmor : IMechanic
{
    public string Id => "fire_armor";

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill != null || !ctx.SlottedSkills.Any(MakesBurningGround)) return;
        ctx.AddCharacterModifier(ctx.Mod("armor", ModKind.Increased, ctx.Number("armor_increased", 30)));
        ctx.AddCharacterModifier(ctx.Mod("resist_fire_max", ModKind.Flat, ctx.Number("resist_cap", 10)));
    }

    private static bool MakesBurningGround(Skills.CompiledSkill s) =>
        s.Behavior.GetValueOrDefault("ground_duration") > 0 && s.Behavior.GetValueOrDefault("ground_fraction") > 0
        && (s.GroundDamageType is { } t ? t == DamageType.Fire : s.Tags.Contains("fire"));
}
