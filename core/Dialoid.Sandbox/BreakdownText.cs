using Dialoid.Core.Stats;

namespace Dialoid.Sandbox;

/// <summary>
/// Russian breakdown of one stat evaluation, in the formula order (base + Σflat) × (1 + Σincreased/100) × Π(1 + more/100).
/// Numbers use the Russian decimal comma and the Unicode minus: contributions (base, flat, increased) up to two decimals and
/// More/Less factors up to three, trailing zeros trimmed (<see cref="Part"/>, <see cref="Factor"/>); totals one decimal
/// (<see cref="Number"/>), shown only with the cap when the cap changed the value; damage and DPS whole (<see cref="Whole"/>);
/// shown values whole when integral, else one decimal (<see cref="Value"/>).
/// </summary>
public static class BreakdownText
{
    /// <summary>Russian title of a stat id; unknown ids are returned as is.</summary>
    public static string Title(string statId) => Texts.StatTitle(statId);

    /// <summary>Russian title of a NumberLine key; unknown keys are returned as is.</summary>
    public static string KeyTitle(string key) => Texts.NumberKeyTitle(key);

    /// <summary>Russian name of a simulation mode.</summary>
    public static string ModeName(Dialoid.Core.Simulation.SimMode mode) => Texts.ModeName(mode);

    /// <summary>Russian name of a damage type.</summary>
    public static string DamageTypeName(Dialoid.Core.Combat.DamageType t) => Texts.DamageTypeName(t);

    /// <summary>Russian name of a simulation log event kind (SimEvent.Kind); an unknown kind is returned as is.</summary>
    public static string EventKindName(string kind) => Texts.EventKindName(kind);

    /// <summary>
    /// Why a run has no time to death: the dummy was not asked to attack («манекен не бьет»), it was asked but has no attack
    /// so no defense was simulated (<see cref="Dialoid.Core.Simulation.SimResult.Defense"/> is null: «этот манекен не
    /// атакует»), or the character survived.
    /// </summary>
    public static string NoTimeToDeath(bool dummyAttacksRequested, Dialoid.Core.Simulation.SimResult result) =>
        !dummyAttacksRequested ? Texts.TimeToDeathNotAttacking
        : result.Defense == null ? Texts.TimeToDeathDummyHasNoAttack
        : Texts.TimeToDeathSurvived;

    /// <summary>The dummy's name from the data; unknown ids are returned as is.</summary>
    public static string DummyName(Dialoid.Core.Data.GameData data, string id) => Texts.DummyName(data, id);

    /// <summary>One decimal, Russian comma (0,5; 12,0); a negative value uses the Unicode minus.</summary>
    public static string Number(double v) => Format(v, "0.0");

    /// <summary>
    /// A shown value (NumberLabel): a whole number when the value is integral (attributes, level, life 912), else one decimal
    /// (<see cref="Number"/>). The rule is by value, not by key, so any integral number reads without «,0».
    /// </summary>
    public static string Value(double v) => v == Math.Round(v) ? Format(v, "0") : Number(v);

    /// <summary>A damage or DPS figure: an integer.</summary>
    public static string Whole(double v) => Format(Math.Round(v, MidpointRounding.AwayFromZero), "0");

    /// <summary>Seconds with one decimal.</summary>
    public static string Seconds(double v) => Format(v, "0.0");

    /// <summary>Breakdown contribution: up to two decimals, trailing zeros trimmed (0,25; 12; 1,5).</summary>
    public static string Part(double v) => Format(v, "0.##");

    /// <summary>More/Less factor: up to three decimals, trailing zeros trimmed (1,05; 0,9).</summary>
    public static string Factor(double v) => Format(v, "0.###");

    private static string Format(double v, string pattern) => Dialoid.Core.Explain.RuNumber.Format(v, pattern);

    public static IReadOnlyList<string> Lines(StatResult r)
    {
        var lines = new List<string> { $"{Texts.Base}: {Part(r.Base)}" };

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
            var mult = $"{Factor(1 + l.Value / 100)} ({Signed(l.Value)}%)";
            lines.Add(l.Value < 0 ? Texts.LessLine(mult, l.Source.Name) : Texts.MoreLine(mult, l.Source.Name));
        }

        if (r.AppliedOverride is { } o) lines.Add(Texts.OverrideLine(Part(o.Value), o.Source.Name));

        // The cap part only when the cap (or a clamp) actually changed the value.
        lines.Add(r.Uncapped == r.Value ? Texts.Total(Number(r.Value)) : Texts.TotalCapped(Number(r.Uncapped), Number(r.Value)));
        return lines;
    }

    private static string Signed(double v)
    {
        var t = Part(v);
        return t.StartsWith('\u2212') || t == "0" ? t : "+" + t;
    }
}
