using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Tests.Combat;

public class BaseDamageTests
{
    private static readonly Dictionary<string, StatDefinition> Defs = Enum.GetValues<DamageType>()
        .SelectMany(t => new[] { "min", "max" }.Select(m => $"added_{t.ToString().ToLowerInvariant()}_damage_{m}"))
        .ToDictionary(id => id, id => new StatDefinition(id, 0, 0, 1e9, StatDisplay.Number));

    private static Modifier Flat(string stat, double v) =>
        new(stat, ModKind.Flat, v, TagSet.Empty, Array.Empty<Condition>(), null, new SourceRef("item", "i", "i"));

    private static StatEvaluator Ev(params Modifier[] m) => new(Defs, m);

    internal static CompiledSkill Skill(Dictionary<DamageType, (double, double)>? spell = null, double? weaponEff = null,
        int level = 1, double addedEff = 100, DamageType? over = null)
    {
        var b = new SkillBase(spell ?? new Dictionary<DamageType, (double, double)>(), weaponEff, addedEff, 1, 1, null, 0, 0, 5, 1,
            new Dictionary<string, double>(), new Dictionary<string, double>(), Array.Empty<string>());
        return new CompiledSkill("s", level, false, TagSet.Empty, over, b, new Dictionary<string, double>(), Array.Empty<Conversion>(),
            0, 0, Array.Empty<Modifier>(), Array.Empty<GrantProcEffect>(), Array.Empty<MechanicEffect>(), new Dictionary<string, int>());
    }

    private static readonly WeaponStub Sword = new("sword", "Sword", "one_hand", 10, 15, 1, 5, Array.Empty<Modifier>());

    [Fact]
    public void Skill_growth_is_linear_5_percent()
    {
        Assert.Equal(1.0, Growth.Skill(1), 9);
        Assert.Equal(1.45, Growth.Skill(10), 9);
        Assert.Equal(1.95, Growth.Skill(20), 9);
    }

    [Fact]
    public void Gear_growth_is_linear_6_percent()
    {
        Assert.Equal(1.0, Growth.Gear(1), 9);
        Assert.Equal(4.54, Growth.Gear(60), 9);
    }

    [Fact]
    public void Spell_base_damage()
    {
        var s = Skill(new() { [DamageType.Fire] = (14, 20) }, level: 20);
        var p = Assert.Single(BaseDamage.Compute(s, 60, null, Ev(), TagSet.Empty));
        Assert.Equal(DamageType.Fire, p.Type);
        Assert.Equal(14 * 1.95 * 4.54, p.Min, 9);
        Assert.Equal(20 * 1.95 * 4.54, p.Max, 9);
        Assert.Equal(new[] { DamageType.Fire }, p.Lineage);
    }

    [Fact]
    public void Attack_base_damage_from_weapon()
    {
        var p = Assert.Single(BaseDamage.Compute(Skill(weaponEff: 110), 1, Sword, Ev(), TagSet.Empty));
        Assert.Equal(DamageType.Physical, p.Type);
        Assert.Equal(11, p.Min, 9);
        Assert.Equal(16.5, p.Max, 9);
    }

    [Fact]
    public void Attack_without_weapon_returns_empty() =>
        Assert.Empty(BaseDamage.Compute(Skill(weaponEff: 110), 1, null, Ev(), TagSet.Empty));

    [Fact]
    public void Added_flat_damage_scaled_by_added_effectiveness()
    {
        var s = Skill(new() { [DamageType.Fire] = (10, 20) }, level: 20, addedEff: 120);
        var ev = Ev(Flat("added_fire_damage_min", 10), Flat("added_fire_damage_max", 10));
        var p = Assert.Single(BaseDamage.Compute(s, 60, null, ev, TagSet.Empty));
        // merged with the spell fire portion (same type and lineage); growth applies only to the table
        Assert.Equal(10 * 1.95 * 4.54 + 12, p.Min, 9);
        Assert.Equal(20 * 1.95 * 4.54 + 12, p.Max, 9);
    }

    [Fact]
    public void Added_flat_applies_to_attacks_and_keeps_its_type()
    {
        var ev = Ev(Flat("added_cold_damage_min", 5), Flat("added_cold_damage_max", 8));
        var r = BaseDamage.Compute(Skill(weaponEff: 100, addedEff: 50), 1, Sword, ev, TagSet.Empty);
        Assert.Equal(2, r.Count);
        var cold = r.Single(x => x.Type == DamageType.Cold);
        Assert.Equal(2.5, cold.Min, 9);
        Assert.Equal(4, cold.Max, 9);
    }

    [Fact]
    public void DamageTypeOverride_converts_base_before_conversion()
    {
        var s = Skill(new() { [DamageType.Fire] = (10, 20) }, weaponEff: 100, over: DamageType.Cold);
        var ev = Ev(Flat("added_poison_damage_min", 3), Flat("added_poison_damage_max", 3));
        var r = BaseDamage.Compute(s, 1, Sword, ev, TagSet.Empty);
        var cold = r.Single(x => x.Type == DamageType.Cold);
        Assert.Equal(20, cold.Min, 9); // 10 spell + 10 weapon
        Assert.Equal(35, cold.Max, 9);
        Assert.Equal(new[] { DamageType.Cold }, cold.Lineage);
        Assert.Equal(DamageType.Poison, r.Single(x => x.Type != DamageType.Cold).Type);
    }
}
