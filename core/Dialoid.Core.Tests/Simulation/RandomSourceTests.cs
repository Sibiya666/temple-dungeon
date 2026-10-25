using Dialoid.Core.Simulation;

namespace Dialoid.Core.Tests.Simulation;

public class RandomSourceTests
{
    [Fact]
    public void Average_accumulator_fires_every_fourth_at_25_percent()
    {
        var r = RandomSources.Create(SimMode.Average, 1);
        var fired = Enumerable.Range(1, 8).Select(_ => r.Chance("ignite:test_bolt", 0.25)).ToArray();
        Assert.Equal(new[] { false, false, false, true, false, false, false, true }, fired);
    }

    [Fact]
    public void Average_channels_are_independent()
    {
        var r = RandomSources.Create(SimMode.Average, 1);
        for (var i = 0; i < 3; i++) Assert.False(r.Chance("a", 0.25));
        Assert.False(r.Chance("b", 0.25));
        Assert.True(r.Chance("a", 0.25));
    }

    [Fact]
    public void Roll_is_deterministic_for_same_seed()
    {
        var a = RandomSources.Create(SimMode.Roll, 42);
        var b = RandomSources.Create(SimMode.Roll, 42);
        for (var i = 0; i < 50; i++)
        {
            var x = a.InRange(10, 20);
            Assert.Equal(x, b.InRange(10, 20));
            Assert.InRange(x, 10, 20);
            Assert.Equal(a.Chance("c", 0.5), b.Chance("c", 0.5));
            Assert.Equal(a.CritOrExpected("k", 30), b.CritOrExpected("k", 30));
        }
    }

    [Fact]
    public void Roll_chance_edges_and_crit_is_not_null()
    {
        var r = RandomSources.Create(SimMode.Roll, 7);
        Assert.True(r.Chance("x", 1.0));
        Assert.False(r.Chance("x", 0.0));
        Assert.NotNull(r.CritOrExpected("k", 50));
        Assert.True(r.CritOrExpected("k", 100));
        Assert.False(r.CritOrExpected("k", 0));
    }

    [Fact]
    public void Average_returns_midpoint_and_null_crit()
    {
        var r = RandomSources.Create(SimMode.Average, 1);
        Assert.Equal(15, r.InRange(10, 20));
        Assert.Null(r.CritOrExpected("crit:test_bolt", 40));
    }
}
