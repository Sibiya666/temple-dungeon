using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Stats;
using static Dialoid.Core.Explain.SheetFormat;

namespace Dialoid.Core.Explain;

/// <summary>The character's own numbers with their breakdowns (no skill tags, no conditions).</summary>
public static class CharacterSheet
{
    private static readonly Character.Attribute[] Attributes =
        { Character.Attribute.Str, Character.Attribute.Int, Character.Attribute.Will, Character.Attribute.Dex };

    private static readonly (string Element, string Label)[] Resists =
        { ("fire", "огню"), ("cold", "холоду"), ("lightning", "молнии"), ("poison", "яду") };

    public static IReadOnlyList<NumberLine> Describe(CompiledCharacter ch, GameData data)
    {
        var lines = new List<NumberLine>();
        StatResult Eval(string stat) => ch.Evaluator.Evaluate(stat, TagSet.Empty, NoConditions.Instance);
        NumberLine Stat(string stat, string label)
        {
            var r = Eval(stat);
            var line = new NumberLine(stat, label, r.Value, r);
            lines.Add(line);
            return line;
        }

        foreach (var a in Attributes) Stat(a.StatId(), a.RussianName());

        var life = Stat("life", "Здоровье").Value;
        var regenPct = Stat("life_regen_pct", "Регенерация здоровья, % в секунду").Value;
        lines.Add(new NumberLine("life_regen_per_sec", "Регенерация здоровья в секунду", regenPct / 100 * life, null,
            $"здоровье {Num(life)} × {Num(regenPct)}% в секунду"));

        var res = ch.Class.Resource;
        Stat("resource_max", $"Максимум ресурса ({res.Name})");
        Stat("resource_regen", $"Регенерация ресурса ({res.Name}) в секунду");
        var noDecay = ch.Rules.GetValueOrDefault(CharacterRules.NoResourceDecay) != 0;
        lines.Add(new NumberLine("resource_decay", $"Затухание ресурса ({res.Name}) в секунду", noDecay ? 0 : res.DecayPerSec, null,
            res.DecayPerSec <= 0 ? "у ресурса класса нет затухания"
            : noDecay ? $"затухание {Num(res.DecayPerSec)}/с отключено механикой"
            : $"класс: {Num(res.DecayPerSec)}/с после {Num(res.DecayDelaySec)} с без попаданий"));

        Stat("armor", "Броня");
        foreach (var (element, label) in Resists)
        {
            var r = Eval("resist_" + element);
            var cap = Eval($"resist_{element}_max").Value;
            lines.Add(new NumberLine("resist_" + element, $"Сопротивление {label}: {Num(r.Value)} / кап {Num(cap)}", r.Value, r,
                $"действует {Num(Math.Min(r.Value, cap))}"));
        }
        var dodge = Eval("dodge_chance");
        var dodgeCap = Eval("dodge_chance_max").Value;
        lines.Add(new NumberLine("dodge_chance", $"Шанс уклонения: {Num(dodge.Value)} / кап {Num(dodgeCap)}", dodge.Value, dodge,
            $"действует {Num(Math.Clamp(Math.Min(dodge.Value, dodgeCap), 0, 100))}%"));

        Stat("crit_chance", "Шанс крита (общий), %");
        var critDamage = Stat("crit_damage", "Урон крита (общий), +%").Value;
        lines[^1] = lines[^1] with { Label = $"Урон крита (общий): +{Num(critDamage)}% (×{Num(1 + critDamage / 100)})" };

        var rules = data.AttributeRules;
        var primary = ch.Class.Primary.StatId();
        var primaryName = ch.Class.Primary.RussianName();
        var primaryValue = Eval(primary).Value;
        lines.Add(new NumberLine("primary_damage_more", $"Бонус основного атрибута ({primaryName}) к урону, % «больше»",
            rules.PrimaryDamageMorePerPoint * primaryValue, null,
            $"{primaryName} {Num(primaryValue)} × {Num(rules.PrimaryDamageMorePerPoint)}% за очко"));
        return lines;
    }
}
