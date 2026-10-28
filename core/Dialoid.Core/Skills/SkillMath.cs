using Dialoid.Core.Combat;
using Dialoid.Core.Data;

namespace Dialoid.Core.Skills;

/// <summary>
/// Per-use numbers of a compiled skill shared by the simulator and the explain sheets, so both use one formula.
/// <c>stat(id, baseValue)</c> evaluates a stat for the skill (its tags, conditions and evaluator are the caller's).
/// </summary>
public static class SkillMath
{
    /// <summary>Speed and recovery multipliers never drop below this, so a heavy slow cannot divide by zero.</summary>
    public const double MinMultiplier = 1e-6;

    public static bool IsAttack(CompiledSkill skill) => skill.Base.WeaponEffectiveness != null;

    public static bool IsChannel(CompiledSkill skill) => skill.Tags.Contains("channel");

    /// <summary>Behavior channel_interval when positive, else the cast time.</summary>
    public static double ChannelInterval(CompiledSkill skill) =>
        skill.Behavior.TryGetValue("channel_interval", out var i) && i > 0 ? i : skill.Base.CastTime;

    /// <summary>Spell: CastTime / cast speed; attack: AttackTimeMultiplier / (weapon APS × attack speed); channel: one interval.
    /// An attack without a weapon falls back to the spell formula (it cannot be used anyway).</summary>
    public static double UseTime(CompiledSkill skill, WeaponStub? weapon, Func<string, double, double> stat)
    {
        if (IsChannel(skill)) return ChannelInterval(skill);
        if (IsAttack(skill) && weapon != null)
            return skill.Base.AttackTimeMultiplier / (weapon.AttacksPerSecond * Math.Max(MinMultiplier, stat("attack_speed", 1)));
        return skill.Base.CastTime / Math.Max(MinMultiplier, stat("cast_speed", 1));
    }

    /// <summary>Base of the crit_chance stat: the weapon's crit chance for an attack with a weapon, else the skill's own.</summary>
    public static double CritChanceBase(CompiledSkill skill, WeaponStub? weapon) =>
        IsAttack(skill) && weapon != null ? weapon.CritChance : skill.Base.CritChance;

    /// <summary>Base cooldown / cooldown recovery; 0 for a skill without a cooldown.</summary>
    public static double Cooldown(CompiledSkill skill, Func<string, double, double> stat) =>
        skill.Base.Cooldown is > 0 and var cd ? cd / Math.Max(MinMultiplier, stat("cooldown_recovery", 1)) : 0;

    /// <summary>Radius multiplier of the area stat: area scales the covered surface, so radii grow with its square root.</summary>
    public static double RadiusScale(double area) => Math.Sqrt(Math.Max(0, area));

    /// <summary>Base of an ailment's chance stat for this skill: the skill's own chance on top of the stat default,
    /// so Increased/More/Less scale it too.</summary>
    public static double AilmentChanceBase(GameData data, AilmentDefinition ailment, CompiledSkill skill) =>
        skill.Base.AilmentChances.GetValueOrDefault(ailment.Id) + data.Stats[ailment.ChanceStat].Default;

    /// <summary>Ailment base a hit gives this ailment: its source type's share; poison also takes the physical share (spec 6.7).
    /// The hit can apply the ailment only when this is positive.</summary>
    public static double AilmentBase(IReadOnlyDictionary<DamageType, double> hitAilmentBase, AilmentDefinition ailment)
    {
        var b = hitAilmentBase.GetValueOrDefault(ailment.SourceType);
        if (ailment.Id == "poison" && ailment.SourceType != DamageType.Physical)
            b += hitAilmentBase.GetValueOrDefault(DamageType.Physical);
        return b;
    }
}
