using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Explain;
using Dialoid.Core.Simulation;

namespace Dialoid.Sandbox;

/// <summary>
/// Russian combat log lines of the training room (spec 7): time, skill, (on «Рой» the target), what happened, number, joined with
/// « · », e.g. «12,4 с · Огненный шар · попадание · 812 огнем (крит)», «12,4 с · Огненный шар · поджог»,
/// «13,0 с · Метеор · отказ: перезарядка 2,1 с». Damage numbers have a space between digit groups («12 345»), seconds a decimal
/// comma; skill names come from the data. Every line is built from the events' structured fields (<see cref="SimEvent.SkillId"/>,
/// <see cref="SimEvent.AilmentId"/>, <see cref="SimEvent.MechanicKey"/>...): the core's English event text is never read.
/// </summary>
public sealed class CombatLogText
{
    private readonly GameData _data;

    public CombatLogText(GameData data) => _data = data;

    /// <summary>A hit at its own time (see <see cref="Hit"/>).</summary>
    public string Line(DamageEvent e, bool showTarget = false) => Hit(e.Time, e, showTarget);

    /// <summary>One hit: the final damage per type («500 физическим + 812 огнем»), «(крит)» for a rolled crit; the target number
    /// when <paramref name="showTarget"/> (a pack).</summary>
    public string Hit(double time, DamageEvent e, bool showTarget)
    {
        var amount = Amount(e.Parts.Select(p => (p.Type, p.Final)));
        if (e.IsCrit) amount += " " + Texts.LogCritMark;
        return Join(time, Who(SkillName(e.SkillId), e.Target, showTarget), Texts.LogHit, amount);
    }

    /// <summary>The DoT ticks of one ailment or ground zone of a skill on a target, summed over a while:
    /// «12,0 с · Огненный шар · урон поджога · 1 234 огнем».</summary>
    public string DotTicks(double time, string skillId, string? ailmentId, int target, bool showTarget,
        IEnumerable<(DamageType Type, double Final)> finalByType) =>
        Join(time, Who(SkillName(skillId), target, showTarget), Texts.LogDotTick(ailmentId), Amount(finalByType));

    /// <summary>A core event at its own time (see <see cref="Line(double, SimEvent, bool)"/>).</summary>
    public string Line(SimEvent e, bool showTarget = false) => Line(e.Time, e, showTarget);

    /// <summary>
    /// One core log event the room shows: use, ailment, proc, mechanic, kill; any other kind gives its Russian kind name only.
    /// Hits are shown from their <see cref="DamageEvent"/> (<see cref="Hit"/>), which has the damage types. A mechanic without a
    /// Russian name keeps its key, so a missing name shows up in the log tests.
    /// </summary>
    public string Line(double time, SimEvent e, bool showTarget)
    {
        switch (e.Kind)
        {
            case "use":
                return Join(time, SkillName(e.SkillId ?? ""), Texts.EventKindName("use"));
            case "ailment":
                return Join(time, Who(SkillName(e.SkillId ?? ""), e.Target, showTarget), Texts.AilmentName(e.AilmentId ?? ""));
            case "proc":
            {
                var source = e.SourceSkillId ?? e.SkillId ?? "";
                var detail = e.SkillId != null && e.SkillId != source ? SkillName(e.SkillId)
                    : e.AilmentId != null ? Texts.AilmentName(e.AilmentId)
                    : Texts.LogBuff;
                return Join(time, SkillName(source), Texts.EventKindName("proc"), detail);
            }
            case "mechanic":
            {
                var key = e.MechanicKey ?? "";
                if (Texts.MechanicEvent(key) is not { } mechanic) return Join(time, Texts.EventKindName("mechanic"), key);
                var (name, what) = mechanic;
                var parts = new List<string> { Who(name, e.Target, showTarget), what };
                if (e.Value is { } value) parts.Add(BreakdownText.Value(value));
                return Join(time, parts.ToArray());
            }
            case "kill" when e.Target is { } target:
                return Join(time, Texts.LogTarget(target), Texts.EventKindName("kill"));
            default:
                return Join(time, Texts.EventKindName(e.Kind));
        }
    }

    /// <summary>A refused key press: «13,0 с · Метеор · отказ: перезарядка 2,1 с»; an empty slot is named by its number. Several
    /// identical refusals in a row (<paramref name="repeat"/> &gt; 1) drop the seconds and end with the count:
    /// «отказ: перезарядка ×6».</summary>
    public string Refusal(double time, int slot, string? skillId, PressResult r, int repeat = 1)
    {
        var cooldown = repeat == 1 && r.CooldownLeft is { } left ? BreakdownText.Seconds(left) : null;
        var who = skillId == null ? Texts.LogSlot(slot) : SkillName(skillId);
        var line = Join(time, who, Texts.LogRefusal(Texts.RefusalReason(r.Refusal, cooldown)));
        return repeat == 1 ? line : Texts.LogRepeat(line, repeat);
    }

    /// <summary>The refusal's short text over the hero: «Перезарядка 2,1 с», «Не хватает маны», «Цель не видна».</summary>
    public static string RefusalPopup(PressResult r, string? resourceId) =>
        Texts.RefusalPopup(r.Refusal, r.CooldownLeft is { } left ? BreakdownText.Seconds(left) : null, resourceId);

    /// <summary>Russian name of a meter mode.</summary>
    public static string MeterModeName(MeterMode mode) => Texts.MeterModeName(mode);

    /// <summary>The note under a meter mode (Потеря на защите: the percentages do not add up to 100); null for the others.</summary>
    public static string? MeterModeHint(MeterMode mode) => Texts.MeterModeHint(mode);

    /// <summary>«DPS +12%» for <see cref="FightSegment.DpsChangeVsPrevious"/> = 0,12.</summary>
    public static string DpsChange(double change) => Texts.DpsChange(RuNumber.Format(change * 100, "+0;-0;0"));

    /// <summary>Russian name of a shown mechanic stack or target debuff (<see cref="TimedEffect.Id"/>); null for an unknown one.</summary>
    public static string? MechanicStackName(string id) => Texts.MechanicStackName(id);

    /// <summary>The skill's name from the data; an unknown id is returned as is.</summary>
    public string SkillName(string id) => _data.Skills.TryGetValue(id, out var s) ? s.Name : id;

    /// <summary>The dummy preset's name from the data; an unknown id is returned as is.</summary>
    public string DummyName(string id) => Texts.DummyName(_data, id);

    private static string Who(string name, int? target, bool showTarget) =>
        showTarget && target is { } t ? name + Texts.LogSeparator + Texts.LogTarget(t) : name;

    private static string Amount(IEnumerable<(DamageType Type, double Final)> parts)
    {
        var dealt = parts.Where(p => p.Final > 0).Select(p => $"{RuNumber.Grouped(p.Final)} {Texts.DamageTypeInstrumental(p.Type)}").ToList();
        return dealt.Count == 0 ? RuNumber.Grouped(0) : string.Join(" + ", dealt);
    }

    private static string Join(double time, params string[] parts) =>
        Texts.LogTime(BreakdownText.Seconds(time)) + Texts.LogSeparator + string.Join(Texts.LogSeparator, parts);
}
