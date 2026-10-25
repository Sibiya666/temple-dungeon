using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;
using static Dialoid.Core.Effects.MechanicCompileContext;

namespace Dialoid.Core.Mechanics;

// Keystones of spec 7.4. Each is one mechanic id; the numbers are params with the spec values as defaults.
// "Less X%" is More −X; "always/fixed" is an Override with KeystonePriority.

/// <summary>
/// Кровавый договор. No resource: resource max/regen/generation 0. Each skill's cost becomes a cooldown of
/// cooldown_per_10_cost (1.5) s per 10 cost (the larger one if the skill already has a cooldown) and the skill costs
/// nothing; channel skills channel at most channel_max_sec (2) s, then cool down channel_cooldown_sec (3) s.
/// Price: max life More life_more (−30), life regeneration 0.
/// </summary>
public sealed class BloodPact : IMechanic
{
    public string Id => "blood_pact";

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill is not { } skill)
        {
            foreach (var stat in new[] { "resource_max", "resource_regen", "resource_gen", "life_regen_pct" })
                ctx.AddCharacterModifier(ctx.Mod(stat, ModKind.Override, 0, priority: KeystonePriority));
            ctx.AddCharacterModifier(ctx.Mod("life", ModKind.More, ctx.Number("life_more", -30)));
            return;
        }

        if (skill.IsChannel)
            skill.ChannelLimit = new ChannelLimit(ctx.Number("channel_max_sec", 2), ctx.Number("channel_cooldown_sec", 3));
        else
        {
            var cost = Math.Max(0, skill.Evaluate("resource_cost", skill.ResourceCost));
            if (cost > 0) skill.Cooldown = Math.Max(skill.Cooldown ?? 0, cost / 10 * ctx.Number("cooldown_per_10_cost", 1.5));
        }
        skill.ResourceCost = 0;
        skill.ResourceGain = 0;
        skill.AddModifier(ctx.Mod("resource_cost", ModKind.Override, 0, priority: KeystonePriority));
    }
}

/// <summary>Воля титана. The primary attribute's damage More is removed; attribute (will) gives more_per_point (0.5)% More damage per point.</summary>
public sealed class TitanWill : IMechanic
{
    public string Id => "titan_will";

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill != null) return;
        var primary = ctx.Class.Primary.StatId();
        ctx.RemoveCharacterModifiers(m => m.Stat == "damage" && m.Kind == ModKind.More && m.Source.Kind == "attribute" && m.Per?.Stat == primary);
        ctx.AddCharacterModifier(ctx.Mod("damage", ModKind.More, ctx.Number("more_per_point", 0.5),
            new PerStat(ctx.Text("attribute", "will"), 1, null)));
    }
}

/// <summary>
/// Неотвратимость. Crit chance fixed at 0 (so crit damage is unused and on_crit / recently_crit never happen); hits deal
/// hit_more (+18)% More damage (tag "hit": DoTs and zones excluded); ailment chances More ailment_chance_more (−30).
/// Ailments whose chance stat defaults to 100% or more (chill: "always on cold damage") are left alone.
/// </summary>
public sealed class Inevitability : IMechanic
{
    public string Id => "inevitability";

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill != null) return;
        ctx.AddCharacterModifier(ctx.Mod("crit_chance", ModKind.Override, 0, priority: KeystonePriority));
        ctx.AddCharacterModifier(ctx.Mod("damage", ModKind.More, ctx.Number("hit_more", 18), tags: TagSet.Of(HitCalculator.HitTag)));
        var less = ctx.Number("ailment_chance_more", -30);
        foreach (var def in ctx.Data.Ailments.Values.OrderBy(a => a.Id, StringComparer.Ordinal))
            if (ctx.Data.Stats.TryGetValue(def.ChanceStat, out var stat) && stat.Default < 100)
                ctx.AddCharacterModifier(ctx.Mod(def.ChanceStat, ModKind.More, less));
    }
}

/// <summary>
/// Аватар пламени. Every skill converts lightning→fire (100), cold→fire (100), physical→fire (50); unconverted physical
/// deals nothing. Fire penetration +15. Price: ignite_max_stacks fixed at 1 (Override, highest priority), resist_fire_max −15.
/// </summary>
public sealed class AvatarOfFire : IMechanic
{
    public string Id => "avatar_of_fire";

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill is not { } skill)
        {
            ctx.AddCharacterModifier(ctx.Mod("penetration_fire", ModKind.Flat, ctx.Number("fire_penetration", 15)));
            ctx.AddCharacterModifier(ctx.Mod("ignite_max_stacks", ModKind.Override, ctx.Number("ignite_max_stacks", 1), priority: KeystonePriority));
            ctx.AddCharacterModifier(ctx.Mod("resist_fire_max", ModKind.Flat, ctx.Number("fire_resist_max", -15)));
            return;
        }
        skill.AddConversion(DamageType.Physical, DamageType.Fire, ctx.Number("physical_to_fire", 50), ctx.Source);
        skill.AddConversion(DamageType.Lightning, DamageType.Fire, ctx.Number("lightning_to_fire", 100), ctx.Source);
        skill.AddConversion(DamageType.Cold, DamageType.Fire, ctx.Number("cold_to_fire", 100), ctx.Source);
        skill.NullifyFinalDamage(DamageType.Physical);
    }
}

/// <summary>
/// Танец клинков. The general Dex→dodge bonus is replaced by dodge_per_dex (0.15) per point; dodge_chance_max
/// +dodge_max_bonus (+10: the default cap 50 becomes 60, other cap raises still add); armor fixed at 0.
/// </summary>
public sealed class BladeDance : IMechanic
{
    public string Id => "blade_dance";

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill != null) return;
        ctx.RemoveCharacterModifiers(m => m.Stat == "dodge_chance" && m.Source.Kind == "attribute" && m.Per?.Stat == "dex");
        ctx.AddCharacterModifier(ctx.Mod("dodge_chance", ModKind.Flat, ctx.Number("dodge_per_dex", 0.15), new PerStat("dex", 1, null)));
        ctx.AddCharacterModifier(ctx.Mod("dodge_chance_max", ModKind.Flat, ctx.Number("dodge_max_bonus", 10)));
        ctx.AddCharacterModifier(ctx.Mod("armor", ModKind.Override, 0, priority: KeystonePriority));
    }
}

/// <summary>
/// Каменная кожа. Armor also mitigates elemental hits taken, using elemental_armor_fraction (0.5) of the armor in the
/// armor formula. Price: Intelligence gives no resistances (every resist_* modifier scaled per int is removed); dodge fixed at 0.
/// </summary>
public sealed class StoneSkin : IMechanic
{
    public string Id => "stone_skin";

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill != null) return;
        ctx.RemoveCharacterModifiers(m => m.Per?.Stat == "int" && m.Stat.StartsWith("resist_", StringComparison.Ordinal)
                                          && !m.Stat.EndsWith("_max", StringComparison.Ordinal));
        ctx.AddCharacterModifier(ctx.Mod("dodge_chance", ModKind.Override, 0, priority: KeystonePriority));
        ctx.SetRule(CharacterRules.ElementalArmorFraction, ctx.Number("elemental_armor_fraction", 0.5));
    }
}
