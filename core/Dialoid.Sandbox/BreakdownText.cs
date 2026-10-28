using System.Globalization;
using Dialoid.Core.Stats;

namespace Dialoid.Sandbox;

/// <summary>
/// Russian breakdown of one stat evaluation, in the formula order (base + Σflat) × (1 + Σincreased/100) × Π(1 + more/100).
/// Numbers use the Russian decimal comma; percents and multipliers have one decimal.
/// </summary>
public static class BreakdownText
{
    private static readonly CultureInfo Fmt = new("ru-RU");

    /// <summary>Russian title of a stat id; unknown ids are returned as is.</summary>
    public static string Title(string statId) => Texts.StatTitle(statId);

    /// <summary>Russian title of a NumberLine key; unknown keys are returned as is.</summary>
    public static string KeyTitle(string key) => Texts.NumberKeyTitle(key);

    /// <summary>Russian name of a simulation mode.</summary>
    public static string ModeName(Dialoid.Core.Simulation.SimMode mode) => Texts.ModeName(mode);

    /// <summary>Russian name of a damage type.</summary>
    public static string DamageTypeName(Dialoid.Core.Combat.DamageType t) => Texts.DamageTypeName(t);

    /// <summary>The dummy's name from the data; unknown ids are returned as is.</summary>
    public static string DummyName(Dialoid.Core.Data.GameData data, string id) => Texts.DummyName(data, id);

    /// <summary>One decimal, Russian comma (0,5; 12,0).</summary>
    public static string Number(double v) => FormatFixed(v, 1);

    /// <summary>A damage or DPS figure: an integer.</summary>
    public static string Whole(double v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", Fmt);

    /// <summary>Seconds with one decimal.</summary>
    public static string Seconds(double v) => FormatFixed(v, 1);

    private static string FormatFixed(double v, int decimals) =>
        v.ToString("0." + new string('0', decimals), CultureInfo.InvariantCulture).Replace('.', ',');

    public static IReadOnlyList<string> Lines(StatResult r)
    {
        var lines = new List<string> { $"{Texts.Base}: {Number(r.Base)}" };

        var flat = r.Lines.Where(l => l.Kind == ModKind.Flat).ToList();
        if (flat.Count > 0)
        {
            lines.Add($"{Texts.FlatHeader}: {Signed(flat.Sum(l => l.Value))}");
            lines.AddRange(flat.Select(l => $"  {Signed(l.Value)} — {l.Source.Name}"));
        }

        var inc = r.Lines.Where(l => l.Kind == ModKind.Increased).ToList();
        if (inc.Count > 0)
        {
            lines.Add(Texts.IncreasedHeader(Signed(inc.Sum(l => l.Value))));
            lines.AddRange(inc.Select(l => $"  {Signed(l.Value)}% — {l.Source.Name}"));
        }

        foreach (var l in r.Lines.Where(l => l.Kind == ModKind.More))
        {
            var mult = Number(1 + l.Value / 100);
            lines.Add(l.Value < 0 ? Texts.LessLine(mult, l.Source.Name) : Texts.MoreLine(mult, l.Source.Name));
        }

        if (r.AppliedOverride is { } o) lines.Add(Texts.OverrideLine(Number(o.Value), o.Source.Name));

        lines.Add(Texts.Total(Number(r.Uncapped), Number(r.Value)));
        return lines;
    }

    private static string Signed(double v) => (v >= 0 ? "+" : "−") + Number(Math.Abs(v));
}
