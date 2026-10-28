using System.Globalization;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Explain;

/// <summary>
/// One explained number of a sheet. <paramref name="Key"/> is a stable id (life, armor, resist_fire, hit_avg_fire, uses_per_sec...),
/// <paramref name="Label"/> a Russian caption. <paramref name="Breakdown"/> is the stat evaluation behind the number; it is null only
/// for a derived number, whose <paramref name="Note"/> lists the parts it is made of. A line with a breakdown has
/// Value == Breakdown.Value, except the hit damage lines of <see cref="SkillSheet"/>, whose breakdown is the damage multiplier.
/// </summary>
public sealed record NumberLine(string Key, string Label, double Value, StatResult? Breakdown, string? Note = null);

internal static class SheetFormat
{
    /// <summary>Invariant number with at most two decimals (45, 0.5, 12.34).</summary>
    public static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static string TypeName(Combat.DamageType t) => t switch
    {
        Combat.DamageType.Physical => "физический",
        Combat.DamageType.Fire => "огонь",
        Combat.DamageType.Cold => "холод",
        Combat.DamageType.Lightning => "молния",
        Combat.DamageType.Poison => "яд",
        _ => t.ToString(),
    };

    public static string AilmentName(string id) => id switch
    {
        "bleed" => "кровотечение",
        "ignite" => "поджог",
        "poison" => "отравление",
        "shock" => "шок",
        "chill" => "охлаждение",
        "freeze" => "заморозка",
        _ => id,
    };
}
