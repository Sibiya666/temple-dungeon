using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;
using static Dialoid.Core.Explain.SheetFormat;

namespace Dialoid.Core.Explain;

/// <summary>
/// The numbers of one slotted skill: hit damage per final type (no target defense, no crit, no conditions), crit, tempo,
/// cooldown, resource, ailment chances and the behavior numbers. Stats are evaluated like the simulator does: the character
/// evaluator plus the skill's own modifiers, with the skill's tags.
/// </summary>
public static class SkillSheet
{
    private static readonly TargetDefense NoDefense = new(0, new Dictionary<DamageType, double>(), 0);

    private static readonly (string Key, string Label)[] Counts =
        { ("projectile_count", "Снарядов"), ("pierce_count", "Пробиваний"), ("chain_count", "Цепей") };

    private static readonly (string Key, string Label)[] Radii =
        { ("area_radius", "Радиус области"), ("explosion_radius", "Радиус взрыва") };

    private static readonly (string Key, string Label)[] RawBehavior =
    {
        ("duration", "Длительность, с"), ("ground_duration", "Длительность зоны, с"), ("ground_fraction", "Доля урона зоны в секунду"),
        ("channel_interval", "Интервал канала, с"),
    };

    public static IReadOnlyList<NumberLine> Describe(CompiledCharacter ch, CompiledSkill skill, GameData data, WeaponStub? weapon)
    {
        var skillCh = ch with { Evaluator = ch.Evaluator.WithExtra(skill.Modifiers) };
        var ev = skillCh.Evaluator;
        var c = NoConditions.Instance;
        var lines = new List<NumberLine>();
        StatResult Eval(string stat, double? baseValue = null) => ev.Evaluate(stat, skill.Tags, c, baseValue);
        double StatValue(string stat, double baseValue) =>
            data.Stats.ContainsKey(stat) ? Eval(stat, baseValue).Value : baseValue; // as the simulator: a missing stat keeps its base
        NumberLine Stat(string key, string label, string stat, double? baseValue = null, string? note = null)
        {
            var r = Eval(stat, baseValue);
            var line = new NumberLine(key, label, r.Value, r, note);
            lines.Add(line);
            return line;
        }

        var isAttack = SkillMath.IsAttack(skill);
        AddHits(lines, skillCh, skill, weapon, isAttack);

        // Crit
        var chance = Math.Clamp(Stat("crit_chance", "Шанс крита, %", "crit_chance", SkillMath.CritChanceBase(skill, weapon)).Value, 0, 100);
        var critDamage = Stat("crit_damage", "Урон крита, +%", "crit_damage").Value;
        var critMult = 1 + critDamage / 100;
        lines[^1] = lines[^1] with { Label = $"Урон крита: +{Num(critDamage)}% (×{Num(critMult)})" };
        var expected = HitCalculator.Resolve(new HitInput(skillCh, skill, NoDefense, c, null, (min, max) => (min + max) / 2, weapon));
        var noCrit = lines.Single(l => l.Key == "hit_avg_total").Value;
        lines.Add(new NumberLine("hit_avg_with_crit", "Среднее попадание с учетом крита", expected.Final.Values.Sum(), null,
            $"без крита {Num(noCrit)}; крит ×{Num(critMult)} с шансом {Num(chance)}%" +
            (skill.Base.HitsPerUse > 1 ? $"; попаданий за применение: {skill.Base.HitsPerUse}" : "")));

        // Tempo and cooldown
        var useTime = SkillMath.UseTime(skill, weapon, StatValue);
        var cooldown = SkillMath.Cooldown(skill, StatValue);
        var period = Math.Max(useTime, cooldown);
        string tempo;
        if (SkillMath.IsChannel(skill)) tempo = $"канал: тик раз в {Num(useTime)} с";
        else if (isAttack && weapon != null)
        {
            Stat("attack_speed", "Скорость атаки (множитель)", "attack_speed", 1);
            tempo = $"атака: {Num(skill.Base.AttackTimeMultiplier)} / ({Num(weapon.AttacksPerSecond)} уд/с оружия × скорость атаки {Num(StatValue("attack_speed", 1))}) = {Num(useTime)} с";
        }
        else
        {
            Stat("cast_speed", "Скорость сотворения (множитель)", "cast_speed", 1);
            tempo = $"заклинание: {Num(skill.Base.CastTime)} с / скорость сотворения {Num(StatValue("cast_speed", 1))} = {Num(useTime)} с" +
                    (isAttack ? " (атака без оружия не применяется)" : "");
        }
        if (cooldown > 0) tempo += $"; перезарядка {Num(cooldown)} с";
        lines.Add(new NumberLine("uses_per_sec", "Применений в секунду", period > 0 ? 1 / period : 0, null,
            tempo + (skill.ChannelRamp != null ? "; без разгона канала" : "") +
            "; в симуляторе применение длится не меньше одного шага (по умолчанию 0.05 с)"));
        if (skill.Base.Cooldown is > 0 and var baseCd)
            lines.Add(new NumberLine("cooldown", "Перезарядка, с", cooldown, null,
                $"база {Num(baseCd)} с / восстановление {Num(StatValue("cooldown_recovery", 1))}"));

        // Resource
        var resName = ch.Class.Resource.Name;
        Stat("resource_cost", SkillMath.IsChannel(skill) ? $"Стоимость ({resName}) в секунду канала" : $"Стоимость ({resName})",
            "resource_cost", skill.ResourceCost);
        Stat("resource_gen", $"Прирост ресурса ({resName}) за применение с попаданием", "resource_gen", skill.ResourceGain);

        // Ailment chances, for the ailments this hit can apply
        foreach (var def in data.Ailments.Values.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            if (SkillMath.AilmentBase(expected.AilmentBase, def) <= 0) continue;
            var r = Eval(def.ChanceStat, SkillMath.AilmentChanceBase(data, def, skill));
            if (r.Value > 0) lines.Add(new NumberLine("ailment_chance_" + def.Id, $"Шанс недуга «{AilmentName(def.Id)}», %", r.Value, r));
        }

        // Behavior
        var b = skill.Behavior;
        foreach (var (key, label) in Counts)
            if (b.TryGetValue(key, out var v)) Stat(key, label, key, v, "в симуляторе округляется вниз");
        var radiusScale = SkillMath.RadiusScale(StatValue("area", 1));
        foreach (var (key, label) in Radii)
            if (b.TryGetValue(key, out var r))
                lines.Add(new NumberLine(key, label, r * radiusScale, null,
                    $"база {Num(r)} × √(область {Num(StatValue("area", 1))})"));
        foreach (var (key, label) in RawBehavior)
            if (b.TryGetValue(key, out var v)) lines.Add(new NumberLine(key, label, v, null, "значение поведения скилла"));
        return lines;
    }

    /// <summary>
    /// Min/max/avg per final damage type, the no-crit total, one damage multiplier line per portion and the penetration of each
    /// final type. The hit lines are derived: value = Σ over the type's portions of base × multiplier × penetration factor
    /// (no crit, no target defense), and the note lists those parts.
    /// </summary>
    private static void AddHits(List<NumberLine> lines, CompiledCharacter skillCh, CompiledSkill skill, WeaponStub? weapon, bool isAttack)
    {
        HitResult Hit(Func<double, double, double> pick) =>
            HitCalculator.Resolve(new HitInput(skillCh, skill, NoDefense, NoConditions.Instance, false, pick, weapon));
        var min = Hit((lo, _) => lo);
        var max = Hit((_, hi) => hi);
        var avg = Hit((lo, hi) => (lo + hi) / 2);

        // HitCalculator adds crit_chance and crit_damage first, then one "damage" result per non-nullified portion in order
        // (IsCrit = false adds no crit-only results).
        var portions = ConversionResolver.Apply(
                BaseDamage.Compute(skill, skillCh.Build.Level, weapon, skillCh.Evaluator, skill.Tags), skill.Conversions)
            .Where(p => !skill.NullifiedDamageTypes.Contains(p.Type)).ToList();
        var damage = avg.Breakdown.Skip(2).ToList();
        if (damage.Count != portions.Count || damage.Any(d => d.Stat != "damage"))
            throw new InvalidOperationException("Hit breakdown does not match the damage portions.");

        foreach (var (type, value) in avg.Final)
        {
            var name = type.ToString().ToLowerInvariant();
            var mine = portions.Select((p, i) => (p, d: damage[i])).Where(x => x.p.Type == type).ToList();
            if (mine.Count == 0) continue;

            // The target has no armor and no resists, so only penetration changes the hit: physical hits go through armor only.
            var penFactor = 1.0;
            string penNote = "";
            if (type != DamageType.Physical)
            {
                var pen = skillCh.Evaluator.Evaluate($"penetration_{name}", skill.Tags, NoConditions.Instance);
                if (pen.Value != 0)
                {
                    penFactor = Mitigation.ResistMultiplier(0, pen.Value);
                    lines.Add(new NumberLine($"penetration_{name}", $"Пробивание сопротивления ({TypeName(type)}), %", pen.Value, pen));
                    penNote = $" × пробивание {Num(penFactor)}";
                }
            }

            var labels = new List<string>();
            foreach (var (p, d) in mine)
            {
                var origins = p.Lineage.Where(t => t != type).OrderBy(t => t).ToList();
                var key = origins.Count == 0
                    ? $"hit_mult_{name}"
                    : $"hit_mult_{name}_from_{string.Join("_", origins.Select(t => t.ToString().ToLowerInvariant()))}";
                var portionName = origins.Count == 0
                    ? TypeName(type)
                    : $"{TypeName(type)} из: {string.Join(", ", origins.Select(TypeName))}";
                lines.Add(new NumberLine(key, $"Множитель урона попадания ({portionName})", d.Value, d));
                labels.Add(portionName);
            }
            string Note(Func<double, double, double> pick) => string.Join(" + ", mine.Select((x, i) =>
                $"{labels[i]}: база {Num(pick(x.p.Min, x.p.Max))} × множитель {Num(Math.Max(0, x.d.Value))}{penNote}"));
            lines.Add(new NumberLine($"hit_min_{name}", $"Попадание, минимум ({TypeName(type)})", min.Final.GetValueOrDefault(type), null,
                Note((lo, _) => lo)));
            lines.Add(new NumberLine($"hit_max_{name}", $"Попадание, максимум ({TypeName(type)})", max.Final.GetValueOrDefault(type), null,
                Note((_, hi) => hi)));
            lines.Add(new NumberLine($"hit_avg_{name}", $"Попадание, среднее ({TypeName(type)})", value, null,
                Note((lo, hi) => (lo + hi) / 2)));
        }
        lines.Add(new NumberLine("hit_avg_total", "Среднее попадание без крита", avg.Final.Values.Sum(), null,
            avg.Final.Count == 0
                ? isAttack && weapon == null ? "атаке нужно оружие" : "у скилла нет урона попадания"
                : string.Join(" + ", avg.Final.Select(kv => $"{TypeName(kv.Key)} {Num(kv.Value)}"))));
    }
}
