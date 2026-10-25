using Dialoid.Core.Stats;
using Xunit;

namespace Dialoid.Core.Tests.Stats;

public class PerStatTests
{
    private static readonly Dictionary<string, StatDefinition> Defs = new()
    {
        ["str"] = new StatDefinition("str", 0, 0, 1e9, StatDisplay.Number),
        ["damage"] = new StatDefinition("damage", 0, -1e9, 1e9, StatDisplay.Number),
        ["bleed_stacks"] = new StatDefinition("bleed_stacks", 0, 0, 1e9, StatDisplay.Number),
        ["attack_speed"] = new StatDefinition("attack_speed", 0, 0, 1e9, StatDisplay.Number),
        ["a"] = new StatDefinition("a", 0, 0, 1e9, StatDisplay.Number),
        ["b"] = new StatDefinition("b", 0, 0, 1e9, StatDisplay.Number),
    };

    private static SourceRef Src(string id) => new("item", id, id);

    private static Modifier M(string stat, ModKind kind, double value, PerStat? per = null,
        TagSet? tags = null, string src = "s") =>
        new(stat, kind, value, tags ?? TagSet.Empty, Array.Empty<Condition>(), per, Src(src));

    [Fact]
    public void Per_multiplies_value_by_other_stat_over_every()
    {
        var ev = new StatEvaluator(Defs, new[]
        {
            M("str", ModKind.Flat, 133),
            M("damage", ModKind.More, 0.25, new PerStat("str", 1, null)),
        });
        var r = ev.Evaluate("damage", TagSet.Empty, null, 100);
        Assert.Equal(100 * (1 + 0.25 * 133 / 100.0), r.Value, 9);
        Assert.Equal(1.3325, r.Value / 100, 9);
    }

    [Fact]
    public void Per_respects_cap()
    {
        var ev = new StatEvaluator(Defs, new[]
        {
            M("bleed_stacks", ModKind.Flat, 20),
            M("attack_speed", ModKind.Increased, 1, new PerStat("bleed_stacks", 1, 15)),
        });
        Assert.Equal(115, ev.Evaluate("attack_speed", TagSet.Empty, null, 100).Value, 9);
    }

    [Fact]
    public void Per_uses_same_tag_context()
    {
        var ev = new StatEvaluator(Defs, new[]
        {
            M("str", ModKind.Flat, 10),
            M("str", ModKind.Flat, 20, tags: TagSet.Of("fire")),
            M("damage", ModKind.Flat, 1, new PerStat("str", 1, null)),
        });
        Assert.Equal(10, ev.Evaluate("damage", TagSet.Of("spell")).Value, 9);
        Assert.Equal(30, ev.Evaluate("damage", TagSet.Of("spell", "fire")).Value, 9);
    }

    [Fact]
    public void Cycle_is_detected()
    {
        var mods = new[]
        {
            M("a", ModKind.Flat, 1, new PerStat("b", 1, null)),
            M("b", ModKind.Flat, 1, new PerStat("a", 1, null)),
        };
        var cycle = StatGraph.FindCycle(mods);
        Assert.NotNull(cycle);
        Assert.Equal(new[] { "a", "b", "a" }, cycle);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StatEvaluator(Defs, mods).Evaluate("a", TagSet.Empty));
        Assert.Contains("a -> b -> a", ex.Message);
    }

    [Fact]
    public void FindCycle_returns_null_for_acyclic_and_detects_self_loop()
    {
        Assert.Null(StatGraph.FindCycle(new[]
        {
            M("a", ModKind.Flat, 1, new PerStat("b", 1, null)),
            M("damage", ModKind.Flat, 1, new PerStat("b", 1, null)),
            M("b", ModKind.Flat, 1),
        }));
        Assert.Equal(new[] { "a", "a" },
            StatGraph.FindCycle(new[] { M("a", ModKind.Flat, 1, new PerStat("a", 1, null)) }));
    }

    [Fact]
    public void Breakdown_line_shows_resolved_per_value()
    {
        var ev = new StatEvaluator(Defs, new[]
        {
            M("str", ModKind.Flat, 133),
            M("damage", ModKind.More, 0.25, new PerStat("str", 1, null), src: "int_bonus"),
        });
        var line = Assert.Single(ev.Evaluate("damage", TagSet.Empty, null, 100).Lines);
        Assert.Equal("int_bonus", line.Source.Id);
        Assert.Equal(33.25, line.Value, 9);
    }

    [Fact]
    public void Constructor_and_WithExtra_copy_the_modifier_list()
    {
        var list = new List<Modifier> { M("str", ModKind.Flat, 5) };
        var ev = new StatEvaluator(Defs, list);
        list.Add(M("str", ModKind.Flat, 100));
        Assert.Equal(5, ev.Evaluate("str", TagSet.Empty).Value, 9);

        var extra = new List<Modifier> { M("str", ModKind.Flat, 1) };
        var ev2 = ev.WithExtra(extra);
        extra.Add(M("str", ModKind.Flat, 100));
        Assert.Equal(6, ev2.Evaluate("str", TagSet.Empty).Value, 9);
    }

    [Fact]
    public void Per_every_zero_throws_naming_source()
    {
        var ev = new StatEvaluator(Defs, new[]
        {
            M("str", ModKind.Flat, 5),
            M("damage", ModKind.Flat, 1, new PerStat("str", 0, null), src: "bad_item"),
        });
        var ex = Assert.Throws<ArgumentException>(() => ev.Evaluate("damage", TagSet.Empty));
        Assert.Contains("bad_item", ex.Message);
    }
}
