using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Explain;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Tests.Combat;

public class HitPipelineTests
{
    private static readonly Dictionary<string, StatDefinition> Defs = BuildDefs();

    private static Dictionary<string, StatDefinition> BuildDefs()
    {
        var d = new Dictionary<string, StatDefinition>();
        void Add(string id, double def, double min, double max) => d[id] = new StatDefinition(id, def, min, max, StatDisplay.Number);
        Add("damage", 0, 0, 1e9);
        Add("crit_chance", 0, 0, 100);
        Add("crit_damage", 50, 0, 1e9);
        foreach (var t in Enum.GetValues<DamageType>())
        {
            var n = t.ToString().ToLowerInvariant();
            Add($"penetration_{n}", 0, 0, 1e9);
            Add($"added_{n}_damage_min", 0, 0, 1e9);
            Add($"added_{n}_damage_max", 0, 0, 1e9);
        }
        return d;
    }

    private static Modifier Mod(string stat, ModKind k, double v, params string[] tags) =>
        new(stat, k, v, TagSet.Of(tags), Array.Empty<Condition>(), null, new SourceRef("test", "t", "t"));

    private static Conversion Conv(DamageType f, DamageType t) => new(f, t, 100, new SourceRef("test", "t", "t"));

    private static CompiledCharacter Char(params Modifier[] mods) =>
        new(new Build { Level = 1 }, null!, new StatEvaluator(Defs, mods), mods, Array.Empty<CompiledSkill>(),
            Array.Empty<MechanicEffect>(), Array.Empty<SourcedEffect>());

    private static CompiledSkill Skill(Dictionary<DamageType, (double, double)>? spell = null, double? weaponEff = null,
        double critChance = 0, Conversion[]? conv = null, params string[] tags)
    {
        var b = new SkillBase(spell ?? new Dictionary<DamageType, (double, double)>(), weaponEff, 100, 1, 1, null, 0, 0, critChance, 1,
            new Dictionary<string, double>(), new Dictionary<string, double>(), Array.Empty<string>());
        return new CompiledSkill("s", 1, false, TagSet.Of(tags), null, b, new Dictionary<string, double>(),
            conv ?? Array.Empty<Conversion>(), 0, 0, Array.Empty<Modifier>(), Array.Empty<GrantProcEffect>(),
            Array.Empty<MechanicEffect>(), new Dictionary<string, int>());
    }

    private static CompiledSkill Fire100(double crit = 0, params string[] tags) =>
        Skill(new() { [DamageType.Fire] = (100, 100) }, null, crit, null, tags);

    private static TargetDefense Target(double armor = 0, double fire = 0, double taken = 0) =>
        new(armor, new Dictionary<DamageType, double> { [DamageType.Fire] = fire }, taken);

    private static HitResult Hit(CompiledCharacter c, CompiledSkill s, TargetDefense t, bool? crit = false, WeaponStub? w = null) =>
        HitCalculator.Resolve(new HitInput(c, s, t, NoConditions.Instance, crit, (min, max) => (min + max) / 2, w));

    [Fact]
    public void Portion_gets_modifiers_of_every_lineage_type()
    {
        var conv = new[] { Conv(DamageType.Physical, DamageType.Fire) };
        var s = Skill(null, 100, 0, conv);
        var w = new WeaponStub("w", "w", "one_hand", 100, 100, 1, 0, Array.Empty<Modifier>());
        var c = Char(Mod("damage", ModKind.Increased, 50, "physical"), Mod("damage", ModKind.Increased, 100, "fire"));
        var r = Hit(c, s, Target(), false, w);
        Assert.Equal(100 * (1 + 1.5), r.Final[DamageType.Fire], 9);
        Assert.False(r.Final.ContainsKey(DamageType.Physical));
    }

    [Fact]
    public void Damage_taken_below_minus_100_deals_nothing_instead_of_healing()
    {
        Assert.Equal(0, Hit(Char(), Fire100(), Target(taken: -150)).Final[DamageType.Fire], 9);
        Assert.Equal(50, Hit(Char(), Fire100(), Target(taken: -50)).Final[DamageType.Fire], 9);
    }

    [Fact]
    public void Primary_attribute_more_applies_to_hits()
    {
        var c = Char(Mod("damage", ModKind.More, 25));
        Assert.Equal(125, Hit(c, Fire100(), Target()).Final[DamageType.Fire], 9);
    }

    [Fact]
    public void Crit_chance_uses_general_formula_and_caps_at_100()
    {
        var c = Char(Mod("crit_chance", ModKind.Flat, 2), Mod("crit_chance", ModKind.Increased, 100));
        Assert.Equal(14, Hit(c, Fire100(5), Target()).Breakdown.Single(b => b.Stat == "crit_chance").Value, 9);
        var capped = Char(Mod("crit_chance", ModKind.Flat, 80), Mod("crit_chance", ModKind.Increased, 100));
        Assert.Equal(100, Hit(capped, Fire100(5), Target()).Breakdown.Single(b => b.Stat == "crit_chance").Value, 9);
    }

    [Fact]
    public void Attack_with_weapon_uses_weapon_crit_chance()
    {
        var w = new WeaponStub("w", "w", "one_hand", 10, 10, 1, 7, Array.Empty<Modifier>());
        var r = Hit(Char(), Skill(null, 100, 5), Target(), null, w);
        Assert.Equal(7, r.Breakdown.Single(b => b.Stat == "crit_chance").Value, 9);
    }

    [Fact]
    public void Crit_damage_is_flat_points()
    {
        var c = Char(Mod("crit_damage", ModKind.Flat, 15));
        var r = Hit(c, Fire100(), Target(), true);
        Assert.Equal(1.65, r.CritMultiplierApplied, 9);
        Assert.True(r.IsCrit);
        Assert.Equal(165, r.Final[DamageType.Fire], 9);
    }

    [Fact]
    public void Expected_crit_multiplier()
    {
        var r = Hit(Char(), Fire100(20), Target(), null);
        Assert.Equal(1.10, r.CritMultiplierApplied, 9);
        Assert.Equal(110, r.Final[DamageType.Fire], 9);
        Assert.Equal(1.0, Hit(Char(), Fire100(20), Target(), false).CritMultiplierApplied, 9);
    }

    [Fact]
    public void Armor_applies_only_to_physical()
    {
        var w = new WeaponStub("w", "w", "one_hand", 100, 100, 1, 0, Array.Empty<Modifier>());
        var phys = Hit(Char(), Skill(null, 100), Target(armor: 1000), false, w);
        Assert.Equal(50, phys.Final[DamageType.Physical], 9);
        var big = Hit(Char(Mod("damage", ModKind.More, 900)), Skill(null, 100), Target(armor: 1000), false, w);
        Assert.Equal(1000 * (1 - 1000.0 / 11000), big.Final[DamageType.Physical], 9);
        Assert.Equal(100, Hit(Char(), Fire100(), Target(armor: 1000)).Final[DamageType.Fire], 9);
    }

    [Fact]
    public void Resist_and_penetration_apply_to_non_physical()
    {
        Assert.Equal(60, Hit(Char(), Fire100(), Target(fire: 40)).Final[DamageType.Fire], 9);
        var c = Char(Mod("penetration_fire", ModKind.Flat, 200));
        Assert.Equal(200, Hit(c, Fire100(), Target(fire: 30)).Final[DamageType.Fire], 9);
    }

    [Fact]
    public void Damage_taken_bucket_is_additive()
    {
        Assert.Equal(135, Hit(Char(), Fire100(), Target(taken: 35)).Final[DamageType.Fire], 9);
    }

    [Fact]
    public void Ailment_base_excludes_mods_and_defense()
    {
        var c = Char(Mod("damage", ModKind.Increased, 100));
        var r = Hit(c, Fire100(), Target(fire: 40));
        Assert.Equal(100, r.AilmentBase[DamageType.Fire], 9);
        Assert.Equal(120, r.Final[DamageType.Fire], 9);
    }

    [Fact]
    public void Ailment_base_includes_crit()
    {
        var r = Hit(Char(), Fire100(), Target(fire: 40), true);
        Assert.Equal(150, r.AilmentBase[DamageType.Fire], 9);
        Assert.Equal(90, r.Final[DamageType.Fire], 9);
    }

    [Fact]
    public void More_minus_100_zeroes_damage()
    {
        var c = Char(Mod("damage", ModKind.More, -100));
        Assert.Equal(0, Hit(c, Fire100(), Target()).Final[DamageType.Fire], 9);
    }

    [Fact]
    public void Same_final_type_portions_are_summed()
    {
        var s = Skill(new() { [DamageType.Fire] = (100, 100), [DamageType.Cold] = (50, 50) }, null, 0,
            new[] { Conv(DamageType.Cold, DamageType.Fire) });
        var r = Hit(Char(), s, Target());
        Assert.Equal(150, r.Final[DamageType.Fire], 9);
        Assert.Equal(150, r.AilmentBase[DamageType.Fire], 9);
    }

    [Fact]
    public void Pick_in_range_is_used()
    {
        var s = Skill(new() { [DamageType.Fire] = (10, 20) });
        var r = HitCalculator.Resolve(new HitInput(Char(), s, Target(), NoConditions.Instance, false, (min, max) => max));
        Assert.Equal(20, r.Final[DamageType.Fire], 9);
    }

    [Fact]
    public void Breakdown_contains_damage_and_crit_stats()
    {
        var r = Hit(Char(), Fire100(), Target());
        Assert.Contains(r.Breakdown, b => b.Stat == "damage");
        Assert.Contains(r.Breakdown, b => b.Stat == "crit_chance");
        Assert.Contains(r.Breakdown, b => b.Stat == "crit_damage");
    }
}
