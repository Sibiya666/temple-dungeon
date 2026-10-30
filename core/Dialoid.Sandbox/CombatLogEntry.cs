namespace Dialoid.Sandbox;

/// <summary>What a combat log line is about (spec 7): the log window's filter by type.</summary>
public enum LogKind { Use, Hit, Crit, Ailment, DotTick, Proc, Mechanic, Refusal, System }

/// <summary>
/// One line of the training room's combat log (spec 7).
/// </summary>
/// <param name="Segment">The <see cref="FightSegment.Index"/> the line belongs to; null for a line outside any segment (a use or a
/// refusal before the first hit, anything after the segment closed, restart lines).</param>
/// <param name="Time">Seconds from the start of <see cref="Segment"/>; fight time for a line outside a segment.</param>
/// <param name="Kind">The line's type, for the filter. A rolled crit is <see cref="LogKind.Crit"/>, not <see cref="LogKind.Hit"/>.</param>
/// <param name="SkillId">The skill the line is about (a proc: the skill whose proc fired), for the filter by skill; null for system
/// lines, a refused empty slot and mechanic events without a skill.</param>
/// <param name="Target">The target index (0 is the first pack member) for hits, DoT lines, ailments and mechanic events on a
/// target; null otherwise.</param>
/// <param name="Amount">Exact final damage of a hit or of an aggregated DoT line (to check against the meter); null otherwise.</param>
/// <param name="Text">The ready Russian line: «12,4 с · Огненный шар · попадание · 812 огнем (крит)»; on «Рой» with the target
/// number («цель 3»); a collapsed refusal ends with its count («отказ: перезарядка ×6»).</param>
/// <param name="Repeat">How many identical refusals in a row this line stands for; 1 for every other line.</param>
public sealed record CombatLogEntry(int? Segment, double Time, LogKind Kind, string? SkillId, int? Target, double? Amount, string Text,
    int Repeat);
