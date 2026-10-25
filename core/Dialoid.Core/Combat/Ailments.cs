using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Simulation;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Combat;

/// <summary>Damage-over-time maths (spec 6.7). The DoT is computed once from the ailment base: no double dipping.</summary>
public static class AilmentCalculator
{
    public const double ChillSlowPercent = 15;

    /// <summary>DoT tag context: dot, final type, skill id and the ailment tag only; delivery tags are not inherited.</summary>
    public static TagSet DotContext(AilmentDefinition def, CompiledSkill skill) =>
        TagSet.Of("dot", def.SourceType.ToString().ToLowerInvariant(), $"skill:{skill.SkillId}", def.Tag);

    public static double DotDps(double ailmentBase, AilmentDefinition def, CompiledCharacter ch, CompiledSkill skill, TargetDefense t,
        IConditionContext? conditions = null)
    {
        if (def.DurationSec <= 0) return 0;
        var ctx = DotContext(def, skill);
        var mult = Math.Max(0, ch.Evaluator.Evaluate("damage", ctx, conditions, 1).Value);
        var dps = ailmentBase * def.DamageFraction / def.DurationSec * mult;
        dps *= Defense(def.SourceType, ch, ctx, t, conditions); // resist path only: bleed (physical) ignores armor
        return dps * t.DamageTakenMultiplier;
    }

    /// <summary>Ground zone (burning ground etc.): fraction of the ailment base per second, with the skill's own tags plus dot.</summary>
    public static double GroundZoneDps(double ailmentBase, double fractionPerSec, CompiledCharacter ch, CompiledSkill skill,
        TargetDefense t, DamageType zoneType = DamageType.Fire, IConditionContext? conditions = null)
    {
        var ctx = skill.Tags.With("dot");
        var mult = Math.Max(0, ch.Evaluator.Evaluate("damage", ctx, conditions, 1).Value);
        var dps = ailmentBase * fractionPerSec * mult;
        if (zoneType == DamageType.Physical)
            dps *= 1 - Mitigation.ArmorReduction(t.Armor, dps);
        else
            dps *= Defense(zoneType, ch, ctx, t, conditions);
        return dps * t.DamageTakenMultiplier;
    }

    // Resist (with penetration) path; bleed and other DoTs never use armor.
    private static double Defense(DamageType type, CompiledCharacter ch, TagSet ctx, TargetDefense t, IConditionContext? conditions)
    {
        var pen = ch.Evaluator.Evaluate($"penetration_{type.ToString().ToLowerInvariant()}", ctx, conditions).Value;
        t.Resists.TryGetValue(type, out var resist);
        return Mitigation.ResistMultiplier(resist, pen);
    }

    public static double ShockMagnitude(CompiledCharacter ch, CompiledSkill skill, IConditionContext? conditions = null) =>
        ch.Evaluator.Evaluate("shock_effect", skill.Tags, conditions).Value;

    public static double FreezeDuration(AilmentDefinition def, CompiledCharacter ch, CompiledSkill skill, IConditionContext? conditions = null) =>
        def.DurationSec * ch.Evaluator.Evaluate("freeze_duration", skill.Tags, conditions, 1).Value; // base 1: Increased % scales the duration

    /// <summary>Builds the instance a hit applies. Damaging ailments compute dps from the ailment base; shock and chill carry a magnitude.</summary>
    public static AilmentInstance MakeInstance(AilmentDefinition def, double ailmentBase, CompiledCharacter ch, CompiledSkill skill, TargetDefense t,
        IConditionContext? conditions = null)
    {
        var duration = def.DurationSec;
        double dps = 0, magnitude = 0;
        switch (def.Id)
        {
            case "shock": magnitude = ShockMagnitude(ch, skill, conditions); break;
            case "chill": magnitude = ChillSlowPercent; break;
            case "freeze": duration = FreezeDuration(def, ch, skill, conditions); break;
            default:
                if (def.DamageFraction > 0) dps = DotDps(ailmentBase, def, ch, skill, t, conditions);
                break;
        }
        return new AilmentInstance(def.Id, def.SourceType, dps, duration, skill.SkillId) { Magnitude = magnitude };
    }
}
