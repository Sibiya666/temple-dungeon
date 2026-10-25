using Dialoid.Core.Stats;

namespace Dialoid.Core.Combat;

/// <summary>Spec 6.1: base damage -> conversion -> modifiers -> crit -> defense -> damage taken. Ailment base is taken after modifiers and crit, before defense and damage taken.</summary>
public static class HitCalculator
{
    /// <summary>Context tag present only when the damage of a hit is evaluated (never for DoTs or zones): "+X% to hits" modifiers require it.</summary>
    public const string HitTag = "hit";

    public static HitResult Resolve(HitInput input)
    {
        var ev = input.Character.Evaluator;
        var skill = input.Skill;
        var breakdown = new List<StatResult>();

        var portions = BaseDamage.Compute(skill, input.Character.Build.Level, input.Weapon, ev, skill.Tags);
        portions = ConversionResolver.Apply(portions, skill.Conversions);

        // Crit
        var critBase = skill.Base.WeaponEffectiveness != null && input.Weapon is { } w ? w.CritChance : skill.Base.CritChance;
        var chanceRes = ev.Evaluate("crit_chance", skill.Tags, input.Conditions, critBase);
        var dmgRes = ev.Evaluate("crit_damage", skill.Tags, input.Conditions);
        breakdown.Add(chanceRes);
        breakdown.Add(dmgRes);
        var critMult = 1 + dmgRes.Value / 100;
        var chance = Math.Clamp(chanceRes.Value, 0, 100);
        var critApplied = input.IsCrit switch
        {
            true => critMult,
            false => 1.0,
            null => 1 + chance / 100 * (critMult - 1),
        };

        var final = new SortedDictionary<DamageType, double>();
        var ailment = new SortedDictionary<DamageType, double>();

        foreach (var p in portions)
        {
            if (skill.NullifiedDamageTypes.Contains(p.Type)) continue; // e.g. unconverted physical under avatar_of_fire
            var ctx = skill.Tags.With(HitTag);
            foreach (var t in p.Lineage) ctx = ctx.With(t.ToString().ToLowerInvariant());
            var dmg = ev.Evaluate("damage", ctx, input.Conditions, 1);
            breakdown.Add(dmg);
            var mult = Math.Max(0, dmg.Value);

            var value = input.PickInRange(p.Min, p.Max);
            var ailmentBase = value * critApplied; // after base, conversion and crit; before modifiers and defense (spec 6.1 item 7)
            var amount = ailmentBase * mult;

            if (p.Type == DamageType.Physical)
                amount *= 1 - Mitigation.ArmorReduction(input.Target.Armor, amount);
            else
            {
                var pen = ev.Evaluate($"penetration_{p.Type.ToString().ToLowerInvariant()}", skill.Tags, input.Conditions).Value;
                input.Target.Resists.TryGetValue(p.Type, out var resist);
                amount *= Mitigation.ResistMultiplier(resist, pen);
            }
            amount *= 1 + input.Target.DamageTakenIncreased / 100;

            final[p.Type] = final.GetValueOrDefault(p.Type) + amount;
            ailment[p.Type] = ailment.GetValueOrDefault(p.Type) + ailmentBase;
        }

        return new HitResult(final, ailment, input.IsCrit ?? false, critApplied, breakdown);
    }
}
