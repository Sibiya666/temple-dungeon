using Dialoid.Core.Combat;

namespace Dialoid.Core.Tests.Combat;

public class MitigationTests
{
    [Fact]
    public void Armor_formula_and_cap()
    {
        Assert.Equal(0.5, Mitigation.ArmorReduction(1000, 100), 9);
        Assert.Equal(0.85, Mitigation.ArmorReduction(1e6, 100), 9);
        Assert.Equal(0, Mitigation.ArmorReduction(0, 100), 9);
        Assert.Equal(0, Mitigation.ArmorReduction(1000, 0), 9);
    }

    [Fact]
    public void Big_hits_penetrate_armor_better()
    {
        Assert.Equal(0.5, Mitigation.ArmorReduction(1000, 100), 9);
        Assert.Equal(1000.0 / 11000, Mitigation.ArmorReduction(1000, 1000), 9);
        Assert.InRange(Mitigation.ArmorReduction(1000, 1000), 0.0909, 0.0910);
    }

    [Fact]
    public void Resist_minus_penetration_floor_minus_100()
    {
        Assert.Equal(2.0, Mitigation.ResistMultiplier(30, 200), 9);
        Assert.Equal(0.6, Mitigation.ResistMultiplier(40, 0), 9);
        Assert.Equal(0.8, Mitigation.ResistMultiplier(40, 20), 9);
        Assert.Equal(0.0, Mitigation.ResistMultiplier(100, 0), 9);
    }
}
