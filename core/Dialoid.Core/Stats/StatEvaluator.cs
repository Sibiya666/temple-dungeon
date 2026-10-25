namespace Dialoid.Core.Stats;

public sealed class StatEvaluator
{
    private readonly IReadOnlyDictionary<string, StatDefinition> _defs;
    private readonly IReadOnlyList<Modifier> _modifiers;

    public StatEvaluator(IReadOnlyDictionary<string, StatDefinition> defs, IReadOnlyList<Modifier> modifiers)
    {
        _defs = defs;
        _modifiers = modifiers.ToArray();
    }

    public StatEvaluator WithExtra(IReadOnlyList<Modifier> extra) =>
        new(_defs, _modifiers.Concat(extra).ToArray());

    public StatResult Evaluate(string stat, TagSet context, IConditionContext? conditions = null, double? baseValue = null) =>
        Evaluate(stat, context, conditions, baseValue, new List<string>(), new Dictionary<string, StatResult>());

    private StatResult Evaluate(string stat, TagSet context, IConditionContext? conditions, double? baseValue,
        List<string> visiting, Dictionary<string, StatResult> memo)
    {
        if (!_defs.TryGetValue(stat, out var def))
            throw new KeyNotFoundException($"Unknown stat '{stat}'.");
        var at = visiting.IndexOf(stat);
        if (at >= 0)
        {
            var path = visiting.GetRange(at, visiting.Count - at);
            path.Add(stat);
            throw new InvalidOperationException($"Cyclic 'per' dependency: {string.Join(" -> ", path)}.");
        }
        visiting.Add(stat);
        try
        {
            var baseV = baseValue ?? def.Default;
            var lines = new List<BreakdownLine>();
            var applied = new List<(Modifier Mod, double Value)>();

            foreach (var m in _modifiers)
            {
                if (m.Stat != stat || !context.ContainsAll(m.RequiredTags)) continue;
                if (m.Conditions.Count > 0)
                {
                    if (conditions is null) continue;
                    var ok = true;
                    foreach (var c in m.Conditions)
                        if (!conditions.IsMet(c)) { ok = false; break; }
                    if (!ok) continue;
                }
                var v = m.Value;
                if (m.Per is { } per)
                {
                    if (per.Every == 0)
                        throw new ArgumentException(
                            $"Modifier from '{m.Source.Id}' on stat '{m.Stat}' has per.Every == 0.");
                    if (!memo.TryGetValue(per.Stat, out var perRes))
                        memo[per.Stat] = perRes = Evaluate(per.Stat, context, conditions, null, visiting, memo);
                    var perVal = perRes.Value;
                    v *= perVal / per.Every;
                    if (per.Cap is { } cap) v = m.Value >= 0 ? Math.Min(v, cap) : Math.Max(v, -cap);
                }
                applied.Add((m, v));
            }

            var overrides = applied.Where(a => a.Mod.Kind == ModKind.Override).ToList();
            if (overrides.Count > 0)
            {
                var best = overrides.OrderByDescending(o => o.Mod.Priority).ThenByDescending(o => o.Value).First();
                var line = new BreakdownLine(best.Mod.Source, ModKind.Override, best.Value);
                return new StatResult(stat, baseV, best.Value, Clamp(best.Value, def), lines, line);
            }

            double flat = 0, inc = 0, more = 1;
            foreach (var (m, v) in applied)
            {
                lines.Add(new BreakdownLine(m.Source, m.Kind, v));
                switch (m.Kind)
                {
                    case ModKind.Flat: flat += v; break;
                    case ModKind.Increased: inc += v; break;
                    case ModKind.More: more *= 1 + v / 100.0; break;
                }
            }
            var uncapped = (baseV + flat) * (1 + inc / 100.0) * more;
            return new StatResult(stat, baseV, uncapped, Clamp(uncapped, def), lines, null);
        }
        finally
        {
            visiting.RemoveAt(visiting.Count - 1);
        }
    }

    private static double Clamp(double v, StatDefinition d) => Math.Min(Math.Max(v, d.Min), d.Max);
}
