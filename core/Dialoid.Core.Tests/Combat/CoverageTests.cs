using Dialoid.Core.Combat;

namespace Dialoid.Core.Tests.Combat;

public class CoverageTests
{
    [Fact]
    public void Area_coverage_examples()
    {
        Assert.Equal(1.5625, Coverage.AreaTargets(5, 1.5), 9);
        Assert.Equal(2.5625, Coverage.AreaTargets(5, 2.5), 9);
        Assert.Equal(5, Coverage.AreaTargets(5, 10), 9);
        Assert.Equal(1, Coverage.AreaTargets(1, 3), 9);
    }

    [Fact]
    public void Projectiles_no_shotgun()
    {
        Assert.Equal(3, Coverage.DistinctTargets(5, 3, 0, 0, null), 9);
        Assert.Equal(1, Coverage.DistinctTargets(1, 3, 0, 0, null), 9);
    }

    [Fact]
    public void Projectiles_with_explosion()
        => Assert.Equal(4.6875, Coverage.DistinctTargets(5, 3, 0, 0, 1.5), 9);

    [Fact]
    public void Pierce_and_chain_add_targets_capped_by_pack()
    {
        Assert.Equal(4, Coverage.DistinctTargets(10, 1, 2, 1, null), 9);
        Assert.Equal(5, Coverage.DistinctTargets(5, 2, 3, 4, null), 9);
    }
}
