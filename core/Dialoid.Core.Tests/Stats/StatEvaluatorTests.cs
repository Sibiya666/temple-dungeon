using Dialoid.Core.Stats;
using Xunit;

namespace Dialoid.Core.Tests.Stats;

public class StatEvaluatorTests
{
    private static readonly Dictionary<string, StatDefinition> Defs = new()
    {
        ["life"] = new StatDefinition("life", 0, 0, 1e9, StatDisplay.Number),
        ["resist_fire"] = new StatDefinition("resist_fire", 0, -100, 75, StatDisplay.Percent),
    };

    private static SourceRef Src(string id) => new("item", id, id);

    private static Modifier M(ModKind kind, double value, string stat = "life", TagSet? tags = null,
        IReadOnlyList<Condition>? conds = null, int prio = 0, string src = "s") =>
        new(stat, kind, value, tags ?? TagSet.Empty, conds ?? Array.Empty<Condition>(), null, Src(src), prio);

    private sealed class Ctx(bool met) : IConditionContext
    {
        public bool IsMet(Condition condition) => met;
    }

    private static StatEvaluator Ev(params Modifier[] mods) => new(Defs, mods);

    [Fact]
    public void Flat_increased_more_follow_formula()
    {
        var ev = Ev(M(ModKind.Flat, 20), M(ModKind.Increased, 30), M(ModKind.Increased, 20),
            M(ModKind.More, 10), M(ModKind.More, 50));
        var r = ev.Evaluate("life", TagSet.Empty, null, 100);
        Assert.Equal(297, r.Value, 9);
    }

    [Fact]
    public void Required_tags_filter_modifiers()
    {
        var ev = Ev(M(ModKind.Increased, 50, tags: TagSet.Of("fire")));
        Assert.Equal(100, ev.Evaluate("life", TagSet.Of("spell"), null, 100).Value, 9);
        Assert.Equal(150, ev.Evaluate("life", TagSet.Of("spell", "fire"), null, 100).Value, 9);
    }

    [Fact]
    public void Override_highest_priority_wins_then_highest_value()
    {
        var ev = Ev(M(ModKind.Override, 1, prio: 5), M(ModKind.Override, 3, prio: 5),
            M(ModKind.Override, 9, prio: 1), M(ModKind.Flat, 100));
        var r = ev.Evaluate("life", TagSet.Empty);
        Assert.Equal(3, r.Value);
        Assert.Equal(3, r.AppliedOverride!.Value);
        Assert.Empty(r.Lines);
    }

    [Fact]
    public void Result_is_capped_and_uncapped_kept()
    {
        var r = Ev(M(ModKind.Flat, 90, "resist_fire")).Evaluate("resist_fire", TagSet.Empty);
        Assert.Equal(75, r.Value);
        Assert.Equal(90, r.Uncapped);
    }

    [Fact]
    public void More_minus_100_zeroes_result_never_negative()
    {
        var ev = Ev(M(ModKind.More, -100), M(ModKind.More, 50));
        Assert.Equal(0, ev.Evaluate("life", TagSet.Empty, null, 100).Value);
    }

    [Fact]
    public void Breakdown_lists_every_applied_modifier_with_source()
    {
        var ev = Ev(M(ModKind.Flat, 5, src: "a"), M(ModKind.Increased, 10, src: "b"), M(ModKind.More, 20, src: "c"));
        var r = ev.Evaluate("life", TagSet.Empty, null, 10);
        Assert.Equal(3, r.Lines.Count);
        Assert.Equal(new[] { "a", "b", "c" }, r.Lines.Select(l => l.Source.Id));
    }

    [Fact]
    public void Unmet_condition_excludes_modifier()
    {
        var ev = Ev(M(ModKind.Flat, 50, conds: new[] { new Condition("target_ignited") }));
        Assert.Equal(0, ev.Evaluate("life", TagSet.Empty, new Ctx(false)).Value);
        Assert.Equal(50, ev.Evaluate("life", TagSet.Empty, new Ctx(true)).Value);
    }
}
