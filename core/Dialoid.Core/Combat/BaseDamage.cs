using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Combat;

/// <summary>Step 1 of the hit pipeline: raw damage per type before modifiers (spec 6.1).</summary>
public static class BaseDamage
{
    public static IReadOnlyList<DamagePortion> Compute(CompiledSkill skill, int characterLevel, WeaponStub? weapon, StatEvaluator ev, TagSet ctx)
    {
        var isAttack = skill.Base.WeaponEffectiveness != null;
        if (isAttack && weapon == null) return Array.Empty<DamagePortion>();

        var raw = new List<DamagePortion>();
        var skillGrowth = Growth.Skill(skill.Level);
        var gear = Growth.Gear(characterLevel);

        foreach (var (type, (min, max)) in skill.Base.SpellDamage)
        {
            var t = skill.DamageTypeOverride ?? type;
            raw.Add(Portion(t, min * skillGrowth * gear, max * skillGrowth * gear));
        }

        if (isAttack)
        {
            var k = skill.Base.WeaponEffectiveness!.Value / 100 * skillGrowth * gear;
            raw.Add(Portion(skill.DamageTypeOverride ?? DamageType.Physical, weapon!.PhysMin * k, weapon.PhysMax * k));
        }

        var eff = skill.Base.AddedDamageEffectiveness / 100;
        foreach (DamageType type in Enum.GetValues<DamageType>())
        {
            var name = type.ToString().ToLowerInvariant();
            var min = ev.Evaluate($"added_{name}_damage_min", ctx).Value * eff;
            var max = ev.Evaluate($"added_{name}_damage_max", ctx).Value * eff;
            if (min != 0 || max != 0) raw.Add(Portion(type, min, max));
        }

        return Merge(raw);
    }

    private static DamagePortion Portion(DamageType t, double min, double max) =>
        new(t, min, max, new HashSet<DamageType> { t });

    internal static IReadOnlyList<DamagePortion> Merge(IEnumerable<DamagePortion> portions)
    {
        var map = new SortedDictionary<(int Type, int Mask), DamagePortion>();
        foreach (var p in portions)
        {
            var key = ((int)p.Type, Mask(p.Lineage));
            map[key] = map.TryGetValue(key, out var e) ? e with { Min = e.Min + p.Min, Max = e.Max + p.Max } : p;
        }
        return map.Values.ToList();
    }

    private static int Mask(IReadOnlySet<DamageType> l)
    {
        var m = 0;
        foreach (var t in l) m |= 1 << (int)t;
        return m;
    }
}
