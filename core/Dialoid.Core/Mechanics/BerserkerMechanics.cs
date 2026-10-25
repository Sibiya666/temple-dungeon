using Dialoid.Core.Character;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;
using static Dialoid.Core.Effects.MechanicCompileContext;

namespace Dialoid.Core.Mechanics;

// Berserker ascendancy (spec 10.4). Each node is one mechanic id; the numbers are params with the spec values as defaults.
// "At 100 Fury" means the resource is full: condition resource_above_pct(fury_pct − 1e-6), i.e. at fury_pct% or more.

internal static class BerserkerRules
{
    public static Condition AtResourcePct(double pct) => new("resource_above_pct", pct - 1e-6);

    /// <summary>Sets (or with 0 removes) an endless buff carrying one modifier; re-applied only when the value changes.</summary>
    public static void SetValueBuff(MechanicSimContext ctx, string stat, ModKind kind, double value)
    {
        if (Math.Abs(value - ctx.Counter("buff")) < 1e-9) return;
        ctx.SetCounter("buff", value);
        var id = "mechanic:" + ctx.Effect.MechanicId;
        if (value == 0) ctx.RemoveBuff(id);
        else ctx.ApplyBuff(id, double.PositiveInfinity, new[] { ctx.Mod(stat, kind, value) });
    }
}

/// <summary>
/// Кровавое безумие (start). Fury never decays. At fury_pct (100)% Fury the character is in frenzy: attacks deal
/// attack_more (+20)% More damage and the character takes damage_taken (+10) more damage (the damage_taken stat).
/// </summary>
public sealed class BerserkFrenzy : IMechanic
{
    public string Id => "berserk_frenzy";

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill != null) return;
        ctx.SetRule(CharacterRules.NoResourceDecay, 1);
        var frenzy = BerserkerRules.AtResourcePct(ctx.Number("fury_pct", 100));
        ctx.AddCharacterModifier(ctx.Mod("damage", ModKind.More, ctx.Number("attack_more", 20), tags: TagSet.Of("attack"), conditions: frenzy));
        ctx.AddCharacterModifier(ctx.Mod("damage_taken", ModKind.Flat, ctx.Number("damage_taken", 10), conditions: frenzy));
    }
}

/// <summary>
/// Неутолимый. An attack that pays resource while Fury is at fury_pct (100)% pays life instead: life_pct_per_10 (2)% of max
/// life per 10 resource of cost (spec 5.4: refused when life is not above the cost). Paid life refunds the resource, so
/// Fury stays full; when life is short the resource payment stands (the rotation never stalls).
/// </summary>
public sealed class Insatiable : IMechanic
{
    public string Id => "insatiable";

    public void OnUse(MechanicSimContext ctx)
    {
        if (ctx.ResourcePaid <= 0 || !ctx.SkillTags.Contains("attack") || ctx.ResourceMax <= 0) return;
        if ((ctx.Resource + ctx.ResourcePaid) / ctx.ResourceMax * 100 < ctx.Number("fury_pct", 100) - 1e-6) return;
        var life = ctx.Number("life_pct_per_10", 2) / 100 * ctx.LifeMax * ctx.ResourcePaid / 10;
        if (!ctx.TryPayLife(life)) return;
        ctx.AddResource(ctx.ResourcePaid);
    }
}

/// <summary>
/// Без оглядки. Resistances fixed at 0 (Override at ascendancy priority, below keystones). Damage More +more_per_pct (1)%
/// per 1% of missing life, at most cap (40)%: an endless buff refreshed on each use and step when the value changes
/// (a life payment in another mechanic's OnUse shows up from the next hook call at the latest).
/// </summary>
public sealed class Reckless : IMechanic
{
    private static readonly string[] Resists = { "resist_fire", "resist_cold", "resist_lightning", "resist_poison" };

    public string Id => "reckless";

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill != null) return;
        foreach (var stat in Resists)
            if (ctx.Data.Stats.ContainsKey(stat))
                ctx.AddCharacterModifier(ctx.Mod(stat, ModKind.Override, 0, priority: AscendancyPriority));
    }

    public void OnUse(MechanicSimContext ctx) => Update(ctx);
    public void OnTick(MechanicSimContext ctx, double dt) => Update(ctx);

    private static void Update(MechanicSimContext ctx)
    {
        if (ctx.LifeMax <= 0) return;
        var missing = Math.Max(0, (1 - ctx.Life / ctx.LifeMax) * 100);
        BerserkerRules.SetValueBuff(ctx, "damage", ModKind.More, Math.Min(ctx.Number("cap", 40), missing * ctx.Number("more_per_pct", 1)));
    }
}

/// <summary>
/// Вихрь ярости. The channel skill <c>skill</c> (whirlwind) costs nothing while the resource is above free_above_pct (50)%
/// (a conditional resource_cost Override 0) and its ticks come speed_per_sec (10)% faster per whole second of continuous
/// channelling, at most speed_cap (+50)% (<see cref="ChannelRamp"/>; a faster tick costs proportionally less).
/// </summary>
public sealed class WhirlRage : IMechanic
{
    public string Id => "whirl_rage";

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill is not { } skill || skill.SkillId != ctx.Text("skill", "whirlwind")) return;
        skill.AddModifier(ctx.Mod("resource_cost", ModKind.Override, 0, priority: AscendancyPriority,
            conditions: new Condition("resource_above_pct", ctx.Number("free_above_pct", 50))));
        if (skill.IsChannel) skill.ChannelRamp = new ChannelRamp(ctx.Number("speed_per_sec", 10), ctx.Number("speed_cap", 50));
    }
}

/// <summary>
/// Рассечение плоти. A crit on a target that is already bleeding (before this hit's own ailments) applies extra_stacks (1)
/// more bleed stack with this hit's ailment base. Crits: see <see cref="MechanicSimContext.IsCritEvent"/>.
/// </summary>
public sealed class FleshRend : IMechanic
{
    public string Id => "flesh_rend";

    public void OnHit(MechanicSimContext ctx)
    {
        var ailment = ctx.Text("ailment", "bleed");
        if (!ctx.HasAilment(ctx.TargetIndex, ailment) || !ctx.IsCritEvent()) return;
        for (var i = 0; i < (int)ctx.Number("extra_stacks", 1); i++) ctx.ApplyAilment(ctx.TargetIndex, ailment);
    }
}

/// <summary>
/// Боевой транс. While the cry buff <c>buff</c> (battle_cry) is active, attacks generate fury_per_attack (+5) resource per
/// use (a conditional resource_gen Flat, so resource generation bonuses scale it). The cry keeps its buff (id, duration,
/// other modifiers) but the buff loses its modifiers on suppress_stat (damage): no damage buff.
/// </summary>
public sealed class BattleTrance : IMechanic
{
    public string Id => "battle_trance";

    public void OnCompile(MechanicCompileContext ctx)
    {
        var buff = ctx.Text("buff", "battle_cry");
        if (ctx.Skill is not { } skill)
        {
            ctx.AddCharacterModifier(ctx.Mod("resource_gen", ModKind.Flat, ctx.Number("fury_per_attack", 5), tags: TagSet.Of("attack"),
                conditions: new Condition("buff_active", null, buff)));
            return;
        }
        var stat = ctx.Text("suppress_stat", "damage");
        skill.Procs = skill.Procs.Select(p => p.Action is ProcAction.ApplyBuff b && b.BuffId == buff
            ? p with { Action = b with { Mods = b.Mods.Where(m => m.Stat != stat).ToList() } }
            : p).ToList();
    }
}

/// <summary>
/// Кровавый пир (its stat part, bleed_max_stacks +7, is a plain modifier). Each stack of <c>ailment</c> (bleed) on the
/// primary target gives per_stack (1)% Increased <c>stat</c> (attack_speed), at most cap (15)%; an endless buff updated
/// every step, so a use sees the stacks as of the previous step.
/// </summary>
public sealed class BloodFeast : IMechanic
{
    public string Id => "blood_feast";

    public void OnTick(MechanicSimContext ctx, double dt)
    {
        var primary = ctx.PrimaryTarget;
        var stacks = primary < 0 ? 0 : ctx.AilmentStacks(primary, ctx.Text("ailment", "bleed"));
        BerserkerRules.SetValueBuff(ctx, ctx.Text("stat", "attack_speed"), ModKind.Increased,
            Math.Min(ctx.Number("cap", 15), stacks * ctx.Number("per_stack", 1)));
    }
}
