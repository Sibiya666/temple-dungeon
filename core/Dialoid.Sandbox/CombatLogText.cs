using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Simulation;

namespace Dialoid.Sandbox;

/// <summary>
/// Russian combat log lines of the training room (spec 7): time, skill, what happened, number, joined with « · », e.g.
/// «12,4 с · Огненный шар · попадание · 812 огнем (крит)», «12,4 с · Огненный шар · поджог»,
/// «13,0 с · Метеор · отказ: перезарядка 2,1 с». Numbers through <see cref="BreakdownText"/> (Russian comma, Unicode minus);
/// skill names from the data. The core's event texts are machine-readable English; they are parsed here and never shown as is.
/// </summary>
public sealed class CombatLogText
{
    private readonly GameData _data;

    public CombatLogText(GameData data) => _data = data;

    /// <summary>One hit or DoT tick: the final damage per type («500 физическим + 312 огнем»), «(крит)» for a rolled crit.</summary>
    public string Line(DamageEvent e)
    {
        var what = e.IsDot
            ? (e.Target == 0 ? Texts.LogDot : Texts.LogDotOnTarget(e.Target))
            : (e.Target == 0 ? Texts.LogHit : Texts.LogHitOnTarget(e.Target));
        var dealt = e.Parts.Where(p => p.Final > 0).Select(p => $"{BreakdownText.Whole(p.Final)} {Texts.DamageTypeInstrumental(p.Type)}").ToList();
        var amount = dealt.Count == 0 ? BreakdownText.Whole(0) : string.Join(" + ", dealt);
        if (e.IsCrit) amount += " " + Texts.LogCritMark;
        return Join(e.Time, SkillName(e.SkillId), what, amount);
    }

    /// <summary>
    /// One core log event. Kinds the room shows: use, ailment, proc, mechanic, kill; hits and crits come from <see cref="Line(DamageEvent)"/>
    /// (their SimEvent lines exist but carry no damage type). Any other kind gives its Russian kind name only.
    /// </summary>
    public string Line(SimEvent e)
    {
        switch (e.Kind)
        {
            case "use":
                return Join(e.Time, SkillName(e.Text), Texts.EventKindName("use"));
            case "hit" when Split(e.Text, " -> #") is (var skill, var rest):
                var amount = rest.IndexOf(": ", StringComparison.Ordinal) is var at and >= 0
                    && double.TryParse(rest[(at + 2)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                    ? BreakdownText.Whole(v) : null;
                return amount == null ? Join(e.Time, SkillName(skill), Texts.LogHit) : Join(e.Time, SkillName(skill), Texts.LogHit, amount);
            case "crit" when Split(e.Text, " -> #") is (var skill, _):
                return Join(e.Time, SkillName(skill), Texts.LogCrit);
            case "ailment" when Split(e.Text, " -> #") is (var ailment, var rest):
                // "ignite -> #0 (fireball)" or "ignite -> #1 (fireball, spread)"
                var open = rest.IndexOf('(');
                var source = open < 0 ? null : rest[(open + 1)..].TrimEnd(')').Split(',')[0].Trim();
                return source == null
                    ? Join(e.Time, Texts.AilmentName(ailment))
                    : Join(e.Time, SkillName(source), Texts.AilmentName(ailment));
            case "proc" when Split(e.Text, ": ") is (var procSource, var rest):
                // "fireball: on_hit -> cast_skill fireball_explosion"
                var arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
                var detail = arrow < 0 ? null : ProcAction(rest[(arrow + 4)..]);
                return detail == null
                    ? Join(e.Time, SkillName(procSource), Texts.EventKindName("proc"))
                    : Join(e.Time, SkillName(procSource), Texts.EventKindName("proc"), detail);
            case "mechanic" when Split(e.Text, ": ") is (_, var text) && Texts.MechanicText(text) is { } ru:
                return Join(e.Time, Texts.LogMechanic, ru);
            default:
                return Join(e.Time, Texts.EventKindName(e.Kind));
        }
    }

    /// <summary>A refused key press: «13,0 с · Метеор · отказ: перезарядка 2,1 с»; an empty slot is named by its number.</summary>
    public string Refusal(double time, int slot, string? skillId, PressResult r)
    {
        var cooldown = r.CooldownLeft is { } left ? BreakdownText.Seconds(left) : null;
        var who = skillId == null ? Texts.LogSlot(slot) : SkillName(skillId);
        return Join(time, who, Texts.LogRefusal(Texts.RefusalReason(r.Refusal, cooldown)));
    }

    /// <summary>Russian name of a meter mode.</summary>
    public static string MeterModeName(MeterMode mode) => Texts.MeterModeName(mode);

    /// <summary>The skill's name from the data; an unknown id is returned as is.</summary>
    public string SkillName(string id) => _data.Skills.TryGetValue(id, out var s) ? s.Name : id;

    private string? ProcAction(string action)
    {
        if (Split(action, " ") is not (var verb, var arg)) return null;
        return verb switch
        {
            "cast_skill" => SkillName(arg),
            "apply_ailment" => Texts.AilmentName(arg),
            "apply_buff" => Texts.LogBuff,
            _ => null,
        };
    }

    private static (string Head, string Tail)? Split(string text, string separator)
    {
        var i = text.IndexOf(separator, StringComparison.Ordinal);
        return i < 0 ? null : (text[..i], text[(i + separator.Length)..]);
    }

    private static string Join(double time, params string[] parts) =>
        Texts.LogTime(BreakdownText.Seconds(time)) + Texts.LogSeparator + string.Join(Texts.LogSeparator, parts);
}
