using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Stats;
using Attr = Dialoid.Core.Character.Attribute;

namespace Dialoid.Core.Tests.Content;

public class CommonDataTests
{
    [Fact]
    public void Real_data_has_attribute_rules_matching_spec()
    {
        var r = ContentHarness.Data.AttributeRules;
        Assert.Equal(0.25, r.PrimaryDamageMorePerPoint);
        Assert.Equal(8, r.Bonuses.Count);
        void Has(Attr a, string stat, ModKind k, double v) =>
            Assert.Contains(r.Bonuses, b => b.Attribute == a && b.Stat == stat && b.Kind == k && b.ValuePerPoint == v);
        Has(Attr.Str, "armor", ModKind.Flat, 2);
        foreach (var s in new[] { "resist_fire", "resist_cold", "resist_lightning", "resist_poison" })
            Has(Attr.Int, s, ModKind.Flat, 0.1);
        Has(Attr.Will, "resource_gen", ModKind.Increased, 0.2);
        Has(Attr.Will, "life_regen_pct", ModKind.Increased, 0.2);
        Has(Attr.Dex, "dodge_chance", ModKind.Flat, 0.05);
    }

    [Fact]
    public void Four_weapons_one_per_type()
    {
        var w = ContentHarness.Data.Weapons;
        Assert.Equal(4, w.Count);
        Assert.Equal(new[] { "one_hand", "staff", "two_hand", "wand" }, w.Values.Select(x => x.Type).OrderBy(x => x).ToArray());

        var sword = w["sword"];
        Assert.Equal("one_hand", sword.Type);
        Assert.Equal((10, 15, 1.4, 5), (sword.PhysMin, sword.PhysMax, sword.AttacksPerSecond, sword.CritChance));
        Assert.Empty(sword.Implicits);

        var axe = w["greataxe"];
        Assert.Equal("two_hand", axe.Type);
        Assert.Equal((22, 33, 0.9, 6), (axe.PhysMin, axe.PhysMax, axe.AttacksPerSecond, axe.CritChance));
        Assert.Empty(axe.Implicits);

        var staff = w["staff"];
        Assert.Equal("staff", staff.Type);
        Assert.Equal((8, 12, 1.1, 4), (staff.PhysMin, staff.PhysMax, staff.AttacksPerSecond, staff.CritChance));
        Assert.Equal(2, staff.Implicits.Count);
        Assert.Contains(staff.Implicits, m => m.Stat == "cast_speed" && m.Kind == ModKind.Increased && m.Value == 10 && m.RequiredTags.Items.Count == 0);
        Assert.Contains(staff.Implicits, m => m.Stat == "damage" && m.Kind == ModKind.Increased && m.Value == 15 && m.RequiredTags.Contains("spell"));

        var wand = w["wand"];
        Assert.Equal("wand", wand.Type);
        Assert.Equal((5, 8, 1.5, 7), (wand.PhysMin, wand.PhysMax, wand.AttacksPerSecond, wand.CritChance));
        var wi = Assert.Single(wand.Implicits);
        Assert.True(wi.Stat == "damage" && wi.Kind == ModKind.Increased && wi.Value == 20 && wi.RequiredTags.Contains("spell"));
    }

    /// <summary>
    /// Spec 11.1 presets. The «Босс» life and the «Атакующий босс» blow are the spec's starting numbers calibrated in Task 10
    /// (spec 11.1 «[Билдеру] подстроить»: boss time to kill 20–60 s, survival 20–40 s at levels 20 and 60; BALANCE_NOTES):
    /// life 8000 → <see cref="BossLife"/> × G, blow physical 14 → 25 and fire 9 → 16 × G.
    /// </summary>
    private const double BossLife = 38500;

    [Fact]
    public void Six_dummy_presets_match_spec_11_1()
    {
        var d = ContentHarness.Data.Dummies;
        Assert.Equal(6, d.Count);

        var t = d["training"];
        Assert.True(t.Immortal && t.PackSize == 1 && t.Armor == 0 && t.Attack == null);
        Assert.All(t.Resists.Values, v => Assert.Equal(0, v));

        var a = d["armored"];
        Assert.True(a.Immortal && a.PackSize == 1 && a.Armor == 2000);

        var e = d["elemental"];
        Assert.True(e.Immortal && e.PackSize == 1 && e.Armor == 0);
        foreach (var ty in new[] { DamageType.Lightning, DamageType.Cold, DamageType.Fire, DamageType.Poison })
            Assert.Equal(40, e.Resists[ty]);

        var s = d["swarm"];
        Assert.Equal(5, s.PackSize);
        Assert.Equal(200, s.Armor);
        Assert.Equal(150, s.LifePerTarget);
        Assert.True(s.Respawn);
        foreach (var ty in new[] { DamageType.Lightning, DamageType.Cold, DamageType.Fire, DamageType.Poison })
            Assert.Equal(10, s.Resists[ty]);

        var b = d["boss"];
        Assert.Equal((1, 1000.0, BossLife, false), (b.PackSize, b.Armor, b.LifePerTarget, b.Respawn));
        foreach (var ty in new[] { DamageType.Lightning, DamageType.Cold, DamageType.Fire, DamageType.Poison })
            Assert.Equal(30, b.Resists[ty]);

        var ab = d["attacking_boss"];
        Assert.True(ab.Immortal);
        Assert.Equal(1000, ab.Armor);
        foreach (var ty in new[] { DamageType.Lightning, DamageType.Cold, DamageType.Fire, DamageType.Poison })
            Assert.Equal(30, ab.Resists[ty]);
        Assert.NotNull(ab.Attack);
        Assert.Equal(3.5, ab.Attack!.IntervalSec);
        Assert.Equal(25, ab.Attack.DamagePerHit[DamageType.Physical]);
        Assert.Equal(16, ab.Attack.DamagePerHit[DamageType.Fire]);
    }

    [Fact]
    public void Bosses_halve_freeze_duration()
    {
        var d = ContentHarness.Data.Dummies;
        Assert.Equal(0.5, d["boss"].FreezeDurationMultiplier);
        Assert.Equal(0.5, d["attacking_boss"].FreezeDurationMultiplier);
        Assert.Equal(1, d["training"].FreezeDurationMultiplier);
    }
}
