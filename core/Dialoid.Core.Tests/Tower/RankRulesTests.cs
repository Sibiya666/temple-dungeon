using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

public class RankRulesTests
{
    private static readonly TowerRules R = TowerDataLoader.Load(FixturePaths.TowerFixture).Data!.Rules;

    [Theory]
    [InlineData(2, 5, 12)]
    [InlineData(2, 18, 18)]
    [InlineData(2, 30, 24)]
    [InlineData(1, 1, 1)]
    [InlineData(5, 60, 60)]
    [InlineData(6, 60, 61)]
    [InlineData(9, 60, 64)]
    [InlineData(15, 60, 70)]
    public void Zone_level(int rank, int level, int expected) => Assert.Equal(expected, RankRules.ZoneLevel(R, rank, level));

    [Theory]
    [InlineData(2, 24, 1.0)]
    [InlineData(2, 25, 0.9)]
    [InlineData(2, 26, 0.8)]
    [InlineData(2, 40, 0.5)]
    [InlineData(2, 10, 1.0)]
    [InlineData(5, 60, 1.0)]
    [InlineData(7, 60, 1.0)]
    [InlineData(15, 60, 1.0)]
    public void Xp_multiplier(int rank, int level, double expected) => Assert.Equal(expected, RankRules.XpMultiplier(R, rank, level), 9);

    [Theory]
    [InlineData(2, ModKind.FloorType, 1, false)]
    [InlineData(3, ModKind.FloorType, 2, true)]
    [InlineData(3, ModKind.General, 3, false)]
    [InlineData(3, ModKind.Tower, 1, false)]
    [InlineData(4, ModKind.Tower, 1, true)]
    [InlineData(1, ModKind.General, 1, true)]
    [InlineData(1, ModKind.General, 0, false)]
    [InlineData(15, ModKind.Tower, 5, true)]
    [InlineData(11, ModKind.General, 5, false)]
    public void Can_apply(int rank, ModKind kind, int tier, bool expected) => Assert.Equal(expected, RankRules.CanApply(R, rank, kind, tier));

    [Theory]
    [InlineData(3, 23, false)]
    [InlineData(3, 24, true)]
    [InlineData(1, 1, true)]
    [InlineData(6, 59, false)]
    [InlineData(6, 60, true)]
    public void Level_allows(int rank, int level, bool expected) => Assert.Equal(expected, RankRules.LevelAllows(R, rank, level));

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void Unknown_rank_throws(int rank) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RankRules.ZoneLevel(R, rank, 30));

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(15, true)]
    public void Hard_counter_allowed_from_rank_5(int rank, bool expected) => Assert.Equal(expected, RankRules.AllowsHardCounter(R, rank));
}
