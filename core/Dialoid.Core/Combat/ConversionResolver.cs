namespace Dialoid.Core.Combat;

/// <summary>Step 2: rightward conversion between damage types in enum order (spec 6.3).</summary>
public static class ConversionResolver
{
    public static IReadOnlyList<DamagePortion> Apply(IReadOnlyList<DamagePortion> portions, IReadOnlyList<Conversion> conversions)
    {
        foreach (var c in conversions)
            if (c.To <= c.From)
                throw new ArgumentException($"Conversion {c.From} -> {c.To} is not rightward.", nameof(conversions));

        var current = BaseDamage.Merge(portions).ToList();
        foreach (DamageType from in Enum.GetValues<DamageType>())
        {
            var convs = conversions.Where(c => c.From == from && c.Percent > 0).ToList();
            if (convs.Count == 0) continue;
            var total = convs.Sum(c => c.Percent);
            var scale = total > 100 ? 100 / total : 1;
            var keptShare = 1 - Math.Min(total, 100) / 100;

            var next = new List<DamagePortion>();
            foreach (var p in current)
            {
                if (p.Type != from) { next.Add(p); continue; }
                foreach (var c in convs)
                {
                    var share = c.Percent * scale / 100;
                    next.Add(new DamagePortion(c.To, p.Min * share, p.Max * share, new HashSet<DamageType>(p.Lineage) { c.To }));
                }
                if (keptShare > 1e-12) next.Add(p with { Min = p.Min * keptShare, Max = p.Max * keptShare });
            }
            current = BaseDamage.Merge(next).ToList();
        }
        return current;
    }
}
